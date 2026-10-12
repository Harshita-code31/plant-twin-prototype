using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Result of a full Fire Safety training attempt: the Activate (alarm) step plus the
/// PASS-technique extinguisher checklist. Surfaced to TrainingResultsUI/FireResultsModal
/// in place of the extinguisher-only TrainingResult.
/// </summary>
[Serializable]
public struct FireResponseResult
{
    public bool alarmActivated;
    public bool pinPulled;
    public bool aimedAtBase;
    public bool squeezed;
    public bool swept;
    public bool wrongExtinguisherUsed;
    public bool fireFullyOut;
    public bool forcedFailure;
    public bool passed;
    public int score;
    public float elapsedSeconds;
}

/// <summary>
/// Sequences and scores the Fire Safety module: sound the alarm (Activate stage) followed
/// by extinguisher use (PassChecklistTracker's PASS-technique checklist), plus any
/// scenario-specific forced-failure branch (e.g. the gas furnace exploding).
/// </summary>
public class FireResponseCoordinator : MonoBehaviour
{
    [SerializeField] private FireSafetyLessonController lessonController;
    [SerializeField] private ManualCallPointController manualCallPoint;
    [SerializeField] private PassChecklistTracker extinguisherTracker;

    public event Action<FireResponseResult> OnModuleComplete;

    [Tooltip("Raised whenever the extinguisher tracker flags a wrong-extinguisher pickup, or a player attempts to pick up an extinguisher not rated for the current fire — wire a HUD warning to this.")]
    public UnityEvent OnWrongExtinguisherWarning;

    [Tooltip("Raised whenever a player attempts to pick up an extinguisher before the alarm has been activated — wire a HUD warning to this.")]
    public UnityEvent OnPickupBlockedAlarmNotActive;

    public bool ExtinguisherStageDone { get; private set; }
    public bool ActivateStageDone { get; private set; }

    /// <summary>Live elapsed time since this attempt started — for the AR toolbar's timer pill (see ArHudToolbar).</summary>
    public float ElapsedSeconds => Time.time - startTime;

    private float startTime;
    private TrainingResult extinguisherResult;
    private bool moduleCompleted;
    private bool forcedFailure;

    private void Awake()
    {
        startTime = Time.time;
    }

    private void OnEnable()
    {
        Subscribe();
    }

    private void OnDisable()
    {
        Unsubscribe();
    }

    /// <summary>
    /// Wires this coordinator to a newly picked-up extinguisher's tracker. Called at
    /// runtime by ARPlacementController the moment the player commits to an extinguisher
    /// — mirrors PassChecklistTracker.SetTargetFire's runtime-wiring pattern, since none
    /// of this content exists at design time.
    /// </summary>
    public void ConfigureExtinguisher(PassChecklistTracker tracker)
    {
        if (extinguisherTracker != null)
        {
            extinguisherTracker.OnAttemptComplete -= HandleExtinguisherComplete;
            extinguisherTracker.OnWrongExtinguisherChosen.RemoveListener(HandleWrongExtinguisherChosen);
        }

        extinguisherTracker = tracker;
        ExtinguisherStageDone = false;
        moduleCompleted = false;
        forcedFailure = false;

        if (extinguisherTracker != null)
        {
            extinguisherTracker.OnAttemptComplete += HandleExtinguisherComplete;
            extinguisherTracker.OnWrongExtinguisherChosen.AddListener(HandleWrongExtinguisherChosen);
        }
    }

    /// <summary>
    /// Wires this coordinator to the Activate-stage manual call point. Called at
    /// runtime by ARPlacementController as soon as the call point is auto-placed —
    /// resolves before the fire (and therefore before ConfigureExtinguisher) even exists.
    /// </summary>
    public void ConfigureActivateStage(ManualCallPointController callPoint)
    {
        if (manualCallPoint != null)
        {
            manualCallPoint.OnActivated.RemoveListener(HandleActivateStageComplete);
        }

        manualCallPoint = callPoint;
        ActivateStageDone = false;

        if (manualCallPoint != null)
        {
            manualCallPoint.OnActivated.AddListener(HandleActivateStageComplete);
        }
    }

    private void HandleActivateStageComplete()
    {
        ActivateStageDone = true;
    }

    /// <summary>
    /// Wires this coordinator to the AR "Learn" stage. Purely informational (the lesson
    /// isn't scored) — kept here only so a future results screen could show it was
    /// completed; ARPlacementController doesn't currently call this since the lesson
    /// stage gates its own progression directly rather than through the coordinator.
    /// </summary>
    public void ConfigureLessonStage(FireSafetyLessonController controller)
    {
        lessonController = controller;
    }

    /// <summary>
    /// Called by scenario-specific consequence scripts (e.g. FurnaceExplosionController)
    /// when the fire got extinguished but the attempt should still fail for a reason the
    /// PASS-technique checklist alone doesn't capture — the gas furnace scenario's "you
    /// forgot to shut off the gas supply, and it exploded" branch.
    /// </summary>
    public void MarkForcedFailure()
    {
        forcedFailure = true;
    }

    private void Subscribe()
    {
        if (extinguisherTracker != null)
        {
            extinguisherTracker.OnAttemptComplete += HandleExtinguisherComplete;
            extinguisherTracker.OnWrongExtinguisherChosen.AddListener(HandleWrongExtinguisherChosen);
        }
    }

    private void Unsubscribe()
    {
        if (extinguisherTracker != null)
        {
            extinguisherTracker.OnAttemptComplete -= HandleExtinguisherComplete;
            extinguisherTracker.OnWrongExtinguisherChosen.RemoveListener(HandleWrongExtinguisherChosen);
        }
    }

    private void HandleWrongExtinguisherChosen()
    {
        OnWrongExtinguisherWarning?.Invoke();
    }

    private void HandleExtinguisherComplete(TrainingResult result)
    {
        extinguisherResult = result;
        ExtinguisherStageDone = true;
        TryComplete();
    }

    private void TryComplete()
    {
        bool activateStageOk = manualCallPoint == null || ActivateStageDone;
        if (moduleCompleted || !activateStageOk || !ExtinguisherStageDone)
        {
            return;
        }
        moduleCompleted = true;

        var result = new FireResponseResult
        {
            // Credited only when the call point was actually tapped. A scene with no call point gets
            // no alarm credit (it used to get the points for free).
            alarmActivated = manualCallPoint != null && manualCallPoint.IsActivated,
            pinPulled = extinguisherResult.pinPulled,
            aimedAtBase = extinguisherResult.aimedAtBase,
            squeezed = extinguisherResult.squeezed,
            swept = extinguisherResult.swept,
            wrongExtinguisherUsed = extinguisherResult.wrongExtinguisherUsed,
            fireFullyOut = extinguisherResult.fireFullyOut,
            forcedFailure = forcedFailure,
            elapsedSeconds = extinguisherResult.elapsedSeconds,
        };
        result.score = ComputeScore(result);
        // A run that scores at least the pass mark is a pass even if one PASS step was imperfect;
        // it must never be shown (or stored) as a fail. A forced failure (score 0) still fails.
        result.passed = !forcedFailure && (extinguisherResult.passed || result.score >= TrainingScoring.PassMark);

        RecordProgress(result);
        OnModuleComplete?.Invoke(result);
    }

    /// <summary>
    /// Scenario tag is "&lt;scene&gt;/&lt;fire class&gt;/&lt;scenario&gt;", e.g. "FireTraining/BC/furnace" --
    /// three scenarios share class BC, so the class alone can't tell the admin dashboard which
    /// one was trained. (Older records have only "&lt;scene&gt;/&lt;fire class&gt;"; the dashboard reads both.)
    /// </summary>
    private void RecordProgress(FireResponseResult result)
    {
        string scenario = gameObject.scene.name;
        FireSource fire = extinguisherTracker != null ? extinguisherTracker.TargetFire : null;
        if (fire != null)
        {
            scenario += "/" + fire.fireClass;
            const string prefix = "scenario_";
            string id = fire.scenarioNarrationId ?? string.Empty;
            if (id.StartsWith(prefix))
            {
                scenario += "/" + id.Substring(prefix.Length);
            }
        }
        ProgressStore.Record("fire_safety", scenario, result.passed, result.score, result.elapsedSeconds, JsonUtility.ToJson(result));
    }

    /// <summary>
    /// Deterministic points for the actions actually performed this attempt, normalized
    /// to a 0-100 scale, clamped at 0 so a run with more penalties than credits doesn't
    /// report a negative score. A forced failure (e.g. the furnace exploding) zeroes the
    /// score outright regardless of how much of the PASS technique was otherwise correct.
    /// </summary>
    private static int ComputeScore(FireResponseResult result)
    {
        if (result.forcedFailure)
        {
            return 0;
        }

        const int maxRawScore = 250; // Activate (50) + pull/aim/squeeze/sweep (25 each = 100) + fire out (100)

        int raw = 0;
        raw += result.alarmActivated ? 50 : 0;
        raw += result.wrongExtinguisherUsed ? -50 : 0;
        raw += result.pinPulled ? 25 : 0;
        raw += result.aimedAtBase ? 25 : 0;
        raw += result.squeezed ? 25 : 0;
        raw += result.swept ? 25 : 0;
        raw += result.fireFullyOut ? 100 : 0;

        return Mathf.Clamp(Mathf.RoundToInt(raw / (float)maxRawScore * 100f), 0, 100);
    }
}
