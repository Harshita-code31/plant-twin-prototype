using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Result of one PASS-technique training attempt, surfaced to TrainingResultsUI.
/// </summary>
[Serializable]
public struct TrainingResult
{
    public bool pinPulled;
    public bool aimedAtBase;
    public bool squeezed;
    public bool swept;
    public bool wrongExtinguisherUsed;
    public bool fireFullyOut;
    public bool passed;
    public float elapsedSeconds;
}

/// <summary>
/// Tracks completion of the four PASS-technique steps (Pull, Aim, Squeeze, Sweep) for
/// one training attempt, plus whether the correct extinguisher was used and whether
/// the fire was actually put out, and reports a pass/fail result. Wire the serialized
/// references in the Inspector to the extinguisher and fire being trained on for this
/// attempt (Editor-only wiring step).
/// </summary>
public class PassChecklistTracker : MonoBehaviour
{
    [SerializeField] private ExtinguisherPin pin;
    [SerializeField] private ExtinguisherAimController aim;
    [SerializeField] private ExtinguisherTrigger trigger;
    [SerializeField] private FireExtinguisherController extinguisherController;
    [SerializeField] private FireSource targetFire;

    public event Action<TrainingResult> OnAttemptComplete;

    [Tooltip("Raised once, immediately if the player picks up an extinguisher that can't put out this fire — see MarkWrongExtinguisherChosen.")]
    public UnityEvent OnWrongExtinguisherChosen;

    /// <summary>
    /// Wires the fire this attempt is being scored against. Called at runtime by
    /// ARPlacementController once both the extinguisher and the fire have been
    /// placed independently (they're no longer part of the same prefab).
    /// </summary>
    public void SetTargetFire(FireSource fire)
    {
        targetFire = fire;
        if (aim != null)
        {
            aim.SweepTarget = fire != null ? fire.transform : null;
        }
    }

    /// <summary>
    /// Called at pickup time (not spray time) by ARPlacementController when the
    /// player commits to an extinguisher whose ExtinguisherIdentity.CanExtinguish
    /// says no against the target fire's class — lets the mistake be flagged (and a
    /// warning shown) the instant it happens, instead of only once/if they actually
    /// spray it. Sets the same wrongExtinguisherUsed flag the spray-time safety-
    /// violation check already sets, so scoring only ever penalizes this once.
    /// </summary>
    public void MarkWrongExtinguisherChosen()
    {
        if (wrongExtinguisherUsed)
        {
            return;
        }

        wrongExtinguisherUsed = true;
        OnWrongExtinguisherChosen?.Invoke();
    }

    // Read-only progress flags — used by TrainingInstructionsUI to show a live
    // "what to do next" prompt without duplicating this tracker's step logic.
    public bool PinPulled => pinPulled;
    public bool Squeezed => squeezed;
    public bool Swept => swept;
    public bool FireFullyOut => fireFullyOut;
    public FireSource TargetFire => targetFire;

    private bool pinPulled;
    private bool aimedAtBase;
    private bool squeezed;
    private bool swept;
    private bool wrongExtinguisherUsed;
    private bool fireFullyOut;
    private bool attemptCompleted;
    private float startTime;

    private void OnEnable()
    {
        startTime = Time.time;

        if (pin != null) pin.OnPinPulled.AddListener(HandlePinPulled);
        if (aim != null) aim.OnSweepDetected.AddListener(HandleSweepDetected);
        if (trigger != null) trigger.OnSqueezeStart.AddListener(HandleSqueezeStart);
        if (extinguisherController != null)
        {
            extinguisherController.OnFireExtinguished.AddListener(HandleFireExtinguished);
            extinguisherController.OnSafetyViolation.AddListener(HandleSafetyViolation);
        }
    }

    private void OnDisable()
    {
        if (pin != null) pin.OnPinPulled.RemoveListener(HandlePinPulled);
        if (aim != null) aim.OnSweepDetected.RemoveListener(HandleSweepDetected);
        if (trigger != null) trigger.OnSqueezeStart.RemoveListener(HandleSqueezeStart);
        if (extinguisherController != null)
        {
            extinguisherController.OnFireExtinguished.RemoveListener(HandleFireExtinguished);
            extinguisherController.OnSafetyViolation.RemoveListener(HandleSafetyViolation);
        }
    }

    private void HandlePinPulled()
    {
        pinPulled = true;
    }

    private void Update()
    {
        // "Aim at the base" is earned by actually spraying with the nozzle pointed at the fire's
        // base, not just by pointing at it: only credited while the lever is squeezed. (Checking
        // only at the instant of squeeze missed trainees who squeezed first and aimed after.)
        if (attemptCompleted || aimedAtBase || trigger == null || !trigger.IsSqueezed
            || aim == null || targetFire == null)
        {
            return;
        }

        if (aim.IsAimedAtFireBase(targetFire.transform))
        {
            aimedAtBase = true;
        }
    }

    private void HandleSqueezeStart()
    {
        squeezed = true;

        if (aim != null && targetFire != null && aim.IsAimedAtFireBase(targetFire.transform))
        {
            aimedAtBase = true;
        }
    }

    private void HandleSweepDetected()
    {
        swept = true;
    }

    private void HandleSafetyViolation(FireSource fire)
    {
        if (targetFire == null || fire == targetFire)
        {
            wrongExtinguisherUsed = true;
        }
    }

    private void HandleFireExtinguished(FireSource fire)
    {
        if (targetFire != null && fire != targetFire)
        {
            return;
        }

        fireFullyOut = true;
        CompleteAttempt();
    }

    /// <summary>
    /// Ends the attempt early (e.g. a "Done"/"Give up" button) and reports whatever
    /// state has been reached so far.
    /// </summary>
    public void FinishAttempt()
    {
        CompleteAttempt();
    }

    /// <summary>
    /// Clears every PASS-step flag back to a fresh attempt -- called by
    /// ARPlacementController.AdvanceToNextScenario when a scenario's extinguishers are
    /// fixed wall mounts reused across the whole campaign rather than fresh instances.
    /// Without this, attemptCompleted stays true from the previous scenario forever,
    /// so CompleteAttempt's own guard silently swallows every later attempt's
    /// OnAttemptComplete -- putting a later fire out never finishes the training.
    /// </summary>
    public void ResetForNewAttempt()
    {
        pinPulled = false;
        aimedAtBase = false;
        squeezed = false;
        swept = false;
        wrongExtinguisherUsed = false;
        fireFullyOut = false;
        attemptCompleted = false;
        startTime = Time.time;

        // The extinguisher is reused, so put its pin back too: otherwise it stays pulled from the
        // last scenario -- the lever would work without pulling it and the step could never be earned.
        if (pin != null)
        {
            pin.ResetPin();
        }
    }

    private void CompleteAttempt()
    {
        if (attemptCompleted)
        {
            return;
        }
        attemptCompleted = true;

        var result = new TrainingResult
        {
            pinPulled = pinPulled,
            aimedAtBase = aimedAtBase,
            squeezed = squeezed,
            swept = swept,
            wrongExtinguisherUsed = wrongExtinguisherUsed,
            fireFullyOut = fireFullyOut,
            elapsedSeconds = Time.time - startTime,
        };
        result.passed = result.pinPulled && result.aimedAtBase && result.squeezed && result.swept
                         && !result.wrongExtinguisherUsed && result.fireFullyOut;

        OnAttemptComplete?.Invoke(result);
    }
}
