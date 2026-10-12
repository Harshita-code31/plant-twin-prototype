using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Minimal results panel: shown once PassChecklistTracker reports an attempt complete.
/// Displays a checklist (one line per PASS step), the overall PASS/FAIL and score for
/// this scenario, and Retry/Back-to-menu buttons. On a campaign with more than one
/// scenario left to train, a NEXT button also appears — clicking it moves straight on to
/// the next scenario (see ARPlacementController.AdvanceToNextScenario) instead of ending
/// the session; NEXT only disappears once every scenario has been trained, at which point
/// Retry/Back are the session's final actions. Before those final actions are revealed, the
/// fire safety quiz (finalQuiz) runs once as the campaign's closing step.
///
/// The final mark follows <see cref="TrainingScoring"/>: the practice score (the average of each
/// scenario's latest score) counts 40% and the quiz 60%, and the training passes at 80/100. The
/// certificate has no button of its own -- it opens automatically once that total is reached
/// (see RevealPanel). Below it, the final result is FAIL with no certificate; retrying the last
/// scenario brings the quiz back unless the best quiz score already reaches the pass mark.
/// </summary>
public class TrainingResultsUI : MonoBehaviour
{
    [SerializeField] private FireResponseCoordinator coordinator;
    [SerializeField] private GameObject panelRoot;

    [Tooltip("The terse GREAT JOB modal that fronts this panel. When set, this panel stays " +
        "hidden (its content is still populated) until that modal is dismissed, so the two " +
        "never show at once -- see FireResultsModal.OnDismissed. Leave unset to fall back to " +
        "showing immediately, e.g. for a scene that doesn't use the modal.")]
    [SerializeField] private FireResultsModal resultsModal;

    [SerializeField] private TMP_Text alarmLine;
    [SerializeField] private TMP_Text pullLine;
    [SerializeField] private TMP_Text aimLine;
    [SerializeField] private TMP_Text squeezeLine;
    [SerializeField] private TMP_Text sweepLine;
    [SerializeField] private TMP_Text extinguisherLine;
    [SerializeField] private TMP_Text overallResultText;

    [SerializeField] private Button retryButton;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private MenuManager menuManager;

    [Tooltip("Moves on to the next scenario in the campaign. Shown only while a scenario is still left to train.")]
    [SerializeField] private Button nextButton;

    [Tooltip("QR certificate panel, opened automatically once the whole training (final scenario + quiz) is passed.")]
    [SerializeField] private CertificateUI certificateUI;

    [Tooltip("Quiz run after the campaign's final scenario, before this panel reveals its final " +
        "Retry/Certificate/Menu actions. Passing it is required for the certificate. Skipped once it " +
        "has been passed this session. Leave unset to skip the quiz (certificate then follows the scenario result alone).")]
    [SerializeField] private TrainingQuizUI finalQuiz;

    [Tooltip("Quiz outcome line on the final result (passed with score / not passed, no certificate). Hidden mid-campaign.")]
    [SerializeField] private TMP_Text quizStatusText;

    private FireResponseResult lastResult;
    private bool lastHasNext;
    private string lastScenarioTag = string.Empty;
    private bool quizPending;

    // Latest score per campaign scenario number; a retried scenario overwrites its entry.
    private readonly Dictionary<int, int> scenarioScores = new Dictionary<int, int>();

    private const string Pass = "✓";
    private const string Fail = "✗";
    private static readonly Color PassColor = new Color32(0x2E, 0x7D, 0x32, 0xFF);
    private static readonly Color FailColor = new Color32(0xC6, 0x28, 0x28, 0xFF);

    private void Awake()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (coordinator != null)
        {
            coordinator.OnModuleComplete += ShowResult;
        }
        if (resultsModal != null)
        {
            resultsModal.OnDismissed += RevealPanel;
        }
        retryButton?.onClick.AddListener(HandleRetry);
        backToMenuButton?.onClick.AddListener(HandleBackToMenu);
        nextButton?.onClick.AddListener(HandleNext);
    }

    /// <summary>
    /// Wires the FireResponseCoordinator this results panel listens to. Called at
    /// runtime by ARPlacementController once the extinguisher content has been placed —
    /// this HUD lives on the always-present ARRig, so it can't be wired to the
    /// coordinator at design time.
    /// </summary>
    public void SetCoordinator(FireResponseCoordinator newCoordinator)
    {
        if (coordinator != null)
        {
            coordinator.OnModuleComplete -= ShowResult;
        }

        coordinator = newCoordinator;

        if (coordinator != null)
        {
            coordinator.OnModuleComplete += ShowResult;
        }
    }

    private void OnDisable()
    {
        if (coordinator != null)
        {
            coordinator.OnModuleComplete -= ShowResult;
        }
        if (resultsModal != null)
        {
            resultsModal.OnDismissed -= RevealPanel;
        }
        retryButton?.onClick.RemoveListener(HandleRetry);
        backToMenuButton?.onClick.RemoveListener(HandleBackToMenu);
        nextButton?.onClick.RemoveListener(HandleNext);
        if (finalQuiz != null)
        {
            finalQuiz.OnClosed -= HandleQuizClosed;
        }
    }

    private void ShowResult(FireResponseResult result)
    {
        lastResult = result;

        var placementController = FindFirstObjectByType<ARPlacementController>();
        bool hasNext = placementController != null && placementController.HasNextScenario;
        scenarioScores[placementController != null ? placementController.ScenarioNumber : 0] = result.score;

        // Last scenario of the campaign just finished: every fire-safety module is done, so
        // the quiz runs before this panel's final actions (see RevealPanel) -- unless the best
        // quiz score this session already brings the total to the pass mark.
        quizPending = !hasNext && finalQuiz != null && !TrainingScoring.IsPass(PracticePercent, finalQuiz.BestPercent);

        // Content is populated immediately either way; only the reveal is deferred when a
        // fronting modal is wired up, so this panel never overlaps it (see resultsModal doc).
        if (resultsModal == null)
        {
            RevealPanel();
        }

        SetLine(alarmLine, LocalizationManager.Get("line_alarm"), result.alarmActivated);
        SetLine(pullLine, LocalizationManager.Get("pass_pull_pin"), result.pinPulled);
        SetLine(aimLine, LocalizationManager.Get("line_aim_short"), result.aimedAtBase);
        SetLine(squeezeLine, LocalizationManager.Get("line_squeeze"), result.squeezed);
        SetLine(sweepLine, LocalizationManager.Get("line_sweep_short"), result.swept);
        SetLine(extinguisherLine,
            result.forcedFailure ? LocalizationManager.Get("line_extinguisher_procedure") : LocalizationManager.Get("line_extinguisher_correct"),
            result.forcedFailure ? false : !result.wrongExtinguisherUsed);

        lastHasNext = hasNext;
        lastScenarioTag = placementController != null
            ? LocalizationManager.Get("scenario_tag_format", placementController.ScenarioNumber, placementController.TotalScenarios)
            : string.Empty;

        if (nextButton != null)
        {
            nextButton.gameObject.SetActive(hasNext);
        }

        RefreshOutcome();
    }

    /// <summary>Practice part of the final mark: the average of each scenario's latest score.</summary>
    private int PracticePercent
    {
        get
        {
            if (scenarioScores.Count == 0)
            {
                return 0;
            }
            int sum = 0;
            foreach (int s in scenarioScores.Values)
            {
                sum += s;
            }
            return Mathf.RoundToInt((float)sum / scenarioScores.Count);
        }
    }

    /// <summary>Weighted practice + quiz total (practice alone when no quiz is wired up).</summary>
    private int TotalScore => finalQuiz != null
        ? TrainingScoring.Total(PracticePercent, finalQuiz.BestPercent)
        : PracticePercent;

    /// <summary>True once the whole campaign is done and the total reaches the pass mark. Gates the certificate.</summary>
    private bool TrainingPassed => !lastHasNext && TotalScore >= TrainingScoring.PassMark;

    /// <summary>
    /// Overall PASS/FAIL line and quiz status line. Run when the scenario result arrives and
    /// again after the quiz closes, since on the final scenario both depend on the quiz outcome.
    /// </summary>
    private void RefreshOutcome()
    {
        bool quizRequired = finalQuiz != null;
        bool finalScenario = !lastHasNext;

        // Mid-campaign results stay per-scenario; the final result is the weighted training outcome.
        bool passed = finalScenario ? TrainingPassed : lastResult.passed;
        int shownScore = finalScenario ? TotalScore : lastResult.score;

        if (overallResultText != null)
        {
            overallResultText.text = passed
                ? LocalizationManager.Get("overall_pass_format", lastResult.elapsedSeconds.ToString("0.0"), shownScore, lastScenarioTag)
                : LocalizationManager.Get("overall_fail_format", shownScore, lastScenarioTag);
            overallResultText.color = passed ? PassColor : FailColor;
        }

        if (quizStatusText != null)
        {
            bool show = quizRequired && finalScenario;
            quizStatusText.gameObject.SetActive(show);
            if (show)
            {
                string glyph = passed ? $"<color=#2E7D32>{Pass}</color> " : $"<color=#C62828>{Fail}</color> ";
                quizStatusText.text = glyph + LocalizationManager.Get("score_breakdown_format",
                    PracticePercent, finalQuiz.BestPercent, TotalScore, TrainingScoring.PassMark);
            }
        }
    }

    private static void SetLine(TMP_Text label, string text, bool ok)
    {
        if (label == null)
        {
            return;
        }
        string glyph = ok ? Pass : Fail;
        string hex = ok ? "#2E7D32" : "#C62828";
        label.text = $"<color={hex}>{glyph}</color> {text}";
    }

    /// <summary>
    /// Activates panelRoot. Called either immediately from ShowResult (no fronting modal
    /// wired up) or from resultsModal.OnDismissed once the player closes the GREAT JOB modal.
    /// After the campaign's final scenario, runs the quiz first and reveals the panel when
    /// the quiz is closed; if the training is then passed, the certificate opens on top.
    /// </summary>
    private void RevealPanel()
    {
        if (quizPending)
        {
            quizPending = false;
            finalQuiz.OnClosed -= HandleQuizClosed;
            finalQuiz.OnClosed += HandleQuizClosed;
            finalQuiz.SetPracticePercent(PracticePercent);
            finalQuiz.Show();
            return;
        }

        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }

        if (TrainingPassed)
        {
            CertificateUI.ShowForTraining("fire_safety",
                LocalizationManager.Get("module_fire_safety_training"), TotalScore);
        }
    }

    private void HandleQuizClosed()
    {
        finalQuiz.OnClosed -= HandleQuizClosed;
        RefreshOutcome();
        RevealPanel();
    }

    private void HandleRetry()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        // Reset in place rather than reloading the scene — reloading destroys and
        // recreates the AR Session/XR Origin, which made the wall/floor planes the
        // player already scanned stop being detected after a retry. Resetting keeps
        // the AR session (and its already-tracked planes) alive.
        var placementController = FindFirstObjectByType<ARPlacementController>();
        if (placementController != null)
        {
            placementController.ResetTraining();
        }
    }

    private void HandleNext()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        var placementController = FindFirstObjectByType<ARPlacementController>();
        placementController?.AdvanceToNextScenario();
    }

    private void HandleBackToMenu()
    {
        menuManager?.LoadMainMenu();
    }
}
