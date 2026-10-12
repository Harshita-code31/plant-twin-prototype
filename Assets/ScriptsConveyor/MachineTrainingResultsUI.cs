using System;
using System.Collections;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// End of the Machine Training (conveyor) module, mirroring the Fire Safety flow: once
/// ConveyorTrainingTracker reports every control practised, the belt is stopped, the practice
/// is recorded, and the machine-safety quiz runs. When the quiz closes, this results panel
/// shows the practice checklist and the training outcome.
///
/// The final mark follows <see cref="TrainingScoring"/>: the practice (share of controls
/// practised) counts 40% and the quiz 60%, and the training passes at 80/100 -- only then does
/// the certificate open (automatically; there is no separate certificate button). Below it, the
/// result is NOT PASSED with no certificate; RETRY restarts the practice, after which the quiz
/// runs again unless the best quiz score this session already reaches the pass mark.
/// </summary>
public class MachineTrainingResultsUI : MonoBehaviour
{
    [SerializeField] private ConveyorTrainingTracker tracker;
    [SerializeField] private ConveyorPlacementController placementController;

    [Tooltip("Machine-safety quiz run once the practice is complete. Leave unset to skip the quiz.")]
    [SerializeField] private TrainingQuizUI quiz;

    [Tooltip("Seconds to wait after the last practice step is done before the quiz (or the results panel) appears, so the trainee sees the training finish instead of being cut straight to the quiz.")]
    [SerializeField] private float quizDelaySeconds = 3f;

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_Text checklistText;
    [SerializeField] private TMP_Text overallResultText;
    [SerializeField] private TMP_Text quizStatusText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button backToMenuButton;
    [SerializeField] private MenuManager menuManager;

    [Tooltip("QR certificate panel, opened automatically once the training (practice + quiz) is passed.")]
    [SerializeField] private CertificateUI certificateUI;

    private const string ProgressModule = "machine_training";
    private Coroutine pendingReveal;
    private static readonly Color PassColor = new Color32(0x2E, 0x7D, 0x32, 0xFF);
    private static readonly Color FailColor = new Color32(0xC6, 0x28, 0x28, 0xFF);

    [Serializable]
    private class PracticeDetails
    {
        public int tasksCompleted;
    }

    /// <summary>Practice part of the final mark: the share of practice tasks completed.</summary>
    private int PracticePercent
    {
        get
        {
            if (tracker == null || tracker.TaskCount == 0)
            {
                return 0;
            }
            int done = 0;
            foreach (ConveyorTrainingTracker.Task task in Enum.GetValues(typeof(ConveyorTrainingTracker.Task)))
            {
                if (tracker.IsDone(task))
                {
                    done++;
                }
            }
            return TrainingScoring.Percent(done, tracker.TaskCount);
        }
    }

    /// <summary>Weighted practice + quiz total (practice alone when no quiz is wired up). Also the certificate score.</summary>
    private int TotalScore => quiz != null ? TrainingScoring.Total(PracticePercent, quiz.BestPercent) : PracticePercent;

    private bool TrainingPassed => tracker != null && tracker.IsComplete && TotalScore >= TrainingScoring.PassMark;

    private void Awake()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        if (tracker != null)
        {
            tracker.OnCompleted += HandlePracticeComplete;
        }
        retryButton?.onClick.AddListener(HandleRetry);
        backToMenuButton?.onClick.AddListener(HandleBackToMenu);
        LocalizationManager.OnLanguageChanged += Refresh;
    }

    private void OnDisable()
    {
        if (tracker != null)
        {
            tracker.OnCompleted -= HandlePracticeComplete;
        }
        if (quiz != null)
        {
            quiz.OnClosed -= HandleQuizClosed;
        }
        CancelPendingReveal();
        retryButton?.onClick.RemoveListener(HandleRetry);
        backToMenuButton?.onClick.RemoveListener(HandleBackToMenu);
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    private void HandlePracticeComplete()
    {
        // Don't leave the belt running (and humming) behind the quiz.
        placementController?.MotionController?.Stop();

        ProgressStore.Record(ProgressModule, gameObject.scene.name + "/Practice", true, 100,
            tracker.CompletedElapsedSeconds, JsonUtility.ToJson(new PracticeDetails { tasksCompleted = tracker.TaskCount }));

        // Give the finished practice a moment to register before switching screens.
        CancelPendingReveal();
        pendingReveal = StartCoroutine(RevealAfterDelay());
    }

    private IEnumerator RevealAfterDelay()
    {
        yield return new WaitForSecondsRealtime(Mathf.Max(0f, quizDelaySeconds));
        pendingReveal = null;

        // The trainee may have reset the practice during the wait: then there is nothing to reveal.
        if (tracker == null || !tracker.IsComplete)
        {
            yield break;
        }

        if (quiz != null && !TrainingScoring.IsPass(PracticePercent, quiz.BestPercent))
        {
            quiz.OnClosed -= HandleQuizClosed;
            quiz.OnClosed += HandleQuizClosed;
            quiz.SetPracticePercent(PracticePercent);
            quiz.Show();
            yield break;
        }

        ShowPanel();
    }

    private void CancelPendingReveal()
    {
        if (pendingReveal != null)
        {
            StopCoroutine(pendingReveal);
            pendingReveal = null;
        }
    }

    private void HandleQuizClosed()
    {
        quiz.OnClosed -= HandleQuizClosed;
        ShowPanel();
    }

    private void ShowPanel()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(true);
        }
        Refresh();

        FindAnyObjectByType<NarrationPlayer>()?.Play(TrainingPassed ? "machine_result_pass" : "machine_result_fail");

        if (TrainingPassed)
        {
            CertificateUI.ShowForTraining("machine_training",
                LocalizationManager.Get("module_machine_safety_training"), TotalScore);
        }
    }

    private void Refresh()
    {
        if (panelRoot == null || !panelRoot.activeSelf || tracker == null)
        {
            return;
        }

        if (checklistText != null)
        {
            var sb = new StringBuilder();
            foreach (ConveyorTrainingTracker.Task task in Enum.GetValues(typeof(ConveyorTrainingTracker.Task)))
            {
                bool ok = tracker.IsDone(task);
                sb.AppendLine((ok ? "<color=#2E7D32>✓</color> " : "<color=#C62828>✗</color> ") + ConveyorTrainingTracker.DoneLabel(task));
            }
            checklistText.text = sb.ToString().TrimEnd();
        }

        bool passed = TrainingPassed;
        if (overallResultText != null)
        {
            overallResultText.text = LocalizationManager.Get(passed ? "machine_result_pass" : "machine_result_fail");
            overallResultText.color = passed ? PassColor : FailColor;
        }

        if (quizStatusText != null)
        {
            quizStatusText.gameObject.SetActive(quiz != null);
            if (quiz != null)
            {
                quizStatusText.text = (passed ? "<color=#2E7D32>✓</color> " : "<color=#C62828>✗</color> ")
                    + LocalizationManager.Get("score_breakdown_format", PracticePercent, quiz.BestPercent, TotalScore, TrainingScoring.PassMark);
            }
        }
    }

    private void HandleRetry()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }

        // Restart the practice from scratch: clear progress and let the trainee place the
        // conveyor again (same in-place reset as the toolbar's Reposition -- no scene reload).
        tracker?.ResetProgress();
        placementController?.ResetTraining();
    }

    private void HandleBackToMenu()
    {
        menuManager?.LoadMainMenu();
    }
}
