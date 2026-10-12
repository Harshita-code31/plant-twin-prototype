using System;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;
using Random = UnityEngine.Random;

/// <summary>
/// Full-screen multiple-choice quiz run as the final step of a training module, with the
/// question set picked by <see cref="bank"/>: Fire Safety (TrainingResultsUI opens it after
/// the campaign's last scenario) and Machine Training (MachineTrainingResultsUI opens it once
/// every conveyor control has been practised). The owner reveals its own final results panel
/// when the quiz raises OnClosed. This script sits on the full-screen backdrop GameObject,
/// which is also what gets toggled active.
///
/// Flow per question: tap an option -> all options lock, the correct one turns green (and a
/// wrong pick turns red), a feedback line + explanation appears, NEXT advances. After the last
/// question a results view shows the score with RETRY / CLOSE. Question order and each
/// question's option order are reshuffled on every attempt so answers can't be memorised by
/// position. Content lives in the QuizBanks classes; all text is localized.
///
/// The quiz is 60% of the module's final mark (see <see cref="TrainingScoring"/>): the owner passes
/// in the practice score via <see cref="SetPracticePercent"/> before <see cref="Show"/>, and this
/// quiz's pass mark is the fewest correct answers that bring the combined total to the pass mark.
/// Every attempt is recorded to ProgressStore as module <see cref="progressModule"/>, scenario
/// "&lt;scene&gt;/Quiz" -- a finished attempt with its score, and one closed before the end as a
/// failed, incomplete attempt.
/// </summary>
public class TrainingQuizUI : MonoBehaviour
{
    [Tooltip("Which module's question set this quiz runs.")]
    [SerializeField] private QuizBank bank = QuizBank.FireSafety;

    [Tooltip("ProgressStore module name quiz attempts are recorded under (matches the module's own records).")]
    [SerializeField] private string progressModule = "fire_safety";

    [SerializeField] private GameObject modalRoot;
    [SerializeField] private Button closeButton;

    [Header("Question view")]
    [SerializeField] private GameObject questionView;
    [SerializeField] private TMP_Text progressText;
    [SerializeField] private GameObject imageFrame;
    [SerializeField] private Image questionImage;
    [SerializeField] private TMP_Text questionText;
    [SerializeField] private Button[] optionButtons = new Button[4];
    [SerializeField] private TMP_Text[] optionLabels = new TMP_Text[4];
    [SerializeField] private TMP_Text feedbackText;
    [SerializeField] private Button nextButton;
    [SerializeField] private TMP_Text nextButtonLabel;

    [Header("Result view")]
    [SerializeField] private GameObject resultView;
    [SerializeField] private TMP_Text resultTitleText;
    [SerializeField] private TMP_Text resultScoreText;
    [SerializeField] private Button retryButton;
    [SerializeField] private Button resultCloseButton;

    [Header("Colors")]
    [SerializeField] private Color optionNormalColor = new Color(0.94f, 0.94f, 0.96f, 1f);
    [SerializeField] private Color optionCorrectColor = new Color(0.30f, 0.69f, 0.31f, 1f);
    [SerializeField] private Color optionWrongColor = new Color(0.90f, 0.22f, 0.21f, 1f);
    [SerializeField] private Color optionLabelColor = new Color(0.13f, 0.13f, 0.16f, 1f);
    [SerializeField] private Color optionLabelOnColor = Color.white;
    // Light tints: the feedback line sits on the quiz page's dark background.
    [SerializeField] private Color feedbackCorrectColor = new Color(0.55f, 0.90f, 0.55f, 1f);
    [SerializeField] private Color feedbackWrongColor = new Color(1f, 0.55f, 0.52f, 1f);

    /// <summary>Raised whenever the quiz is closed (result screen's close button or the top-bar X).</summary>
    public event Action OnClosed;

    /// <summary>True once any attempt this session reached the pass mark.</summary>
    public bool HasPassed { get; private set; }

    /// <summary>Highest number of correct answers in any finished attempt this session.</summary>
    public int BestScore { get; private set; }

    public int QuestionCount => QuizBanks.Get(bank).Length;

    /// <summary><see cref="BestScore"/> as a percentage of <see cref="QuestionCount"/>; 0 before any finished attempt.</summary>
    public int BestPercent => TrainingScoring.Percent(BestScore, QuestionCount);

    private int practicePercent = 100;

    /// <summary>Practice score (0-100) this quiz's pass mark is computed against. Set before <see cref="Show"/>.</summary>
    public void SetPracticePercent(int percent)
    {
        practicePercent = Mathf.Clamp(percent, 0, 100);
    }

    [Serializable]
    private class QuizAttemptDetails
    {
        public int correct;
        public int answered;
        public int total;
        public int passMark;
        public bool completed;
    }

    private readonly List<QuizQuestion> order = new List<QuizQuestion>();
    private readonly int[] optionMap = new int[4]; // display slot -> authored option index
    private int currentIndex;
    private int score;
    private int answeredCount;
    private int answeredSlot = -1; // -1 while the current question is unanswered
    private float attemptStartTime;
    private bool attemptRecorded;

    // No Awake() hiding modalRoot here (unlike ComingSoonModal/LanguageSelectModal): this script
    // lives on modalRoot itself, which is saved inactive in the scene, so Awake only ever runs on
    // the first Show() -- hiding in Awake would immediately undo that first open.

    private void OnEnable()
    {
        closeButton?.onClick.AddListener(Hide);
        resultCloseButton?.onClick.AddListener(Hide);
        retryButton?.onClick.AddListener(StartQuiz);
        nextButton?.onClick.AddListener(Next);
        for (int i = 0; i < optionButtons.Length; i++)
        {
            int slot = i; // capture per-iteration copy for the closure
            optionButtons[i]?.onClick.AddListener(() => Answer(slot));
        }
        LocalizationManager.OnLanguageChanged += RefreshTexts;
    }

    private void OnDisable()
    {
        closeButton?.onClick.RemoveListener(Hide);
        resultCloseButton?.onClick.RemoveListener(Hide);
        retryButton?.onClick.RemoveListener(StartQuiz);
        nextButton?.onClick.RemoveListener(Next);
        foreach (var b in optionButtons)
        {
            b?.onClick.RemoveAllListeners();
        }
        LocalizationManager.OnLanguageChanged -= RefreshTexts;
    }

    /// <summary>Opens the quiz and starts a fresh attempt.</summary>
    public void Show()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(true);
        }
        StartQuiz();
    }

    public void Hide()
    {
        bool wasOpen = modalRoot != null && modalRoot.activeSelf;
        if (wasOpen)
        {
            RecordAttempt(completed: false); // no-op if this attempt already reached its results
        }
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
        if (wasOpen)
        {
            OnClosed?.Invoke();
        }
    }

    private void StartQuiz()
    {
        // RETRY from the results screen starts a new attempt; an unfinished one can't be
        // left behind here since RETRY only exists on the results screen.
        order.Clear();
        order.AddRange(QuizBanks.Get(bank));
        Shuffle(order);
        currentIndex = 0;
        score = 0;
        answeredCount = 0;
        attemptStartTime = Time.realtimeSinceStartup;
        attemptRecorded = false;
        ShowQuestion();
    }

    private void ShowQuestion()
    {
        questionView.SetActive(true);
        resultView.SetActive(false);

        answeredSlot = -1;
        for (int i = 0; i < optionMap.Length; i++)
        {
            optionMap[i] = i;
        }
        Shuffle(optionMap);

        var q = order[currentIndex];
        Sprite sprite = string.IsNullOrEmpty(q.imagePath) ? null : Resources.Load<Sprite>(q.imagePath);
        imageFrame.SetActive(sprite != null);
        questionImage.sprite = sprite;

        for (int slot = 0; slot < optionButtons.Length; slot++)
        {
            optionButtons[slot].interactable = true;
            SetOptionColors(slot, optionNormalColor, optionLabelColor);
        }

        RefreshTexts();
    }

    private void Answer(int slot)
    {
        if (answeredSlot >= 0)
        {
            return;
        }
        answeredSlot = slot;
        answeredCount++;

        var q = order[currentIndex];
        bool correct = optionMap[slot] == q.correctIndex;
        if (correct)
        {
            score++;
        }

        for (int i = 0; i < optionButtons.Length; i++)
        {
            optionButtons[i].interactable = false;
            if (optionMap[i] == q.correctIndex)
            {
                SetOptionColors(i, optionCorrectColor, optionLabelOnColor);
            }
            else if (i == slot)
            {
                SetOptionColors(i, optionWrongColor, optionLabelOnColor);
            }
        }

        RefreshTexts();
    }

    private void Next()
    {
        if (answeredSlot < 0)
        {
            return;
        }

        currentIndex++;
        if (currentIndex < order.Count)
        {
            ShowQuestion();
        }
        else
        {
            ShowResult();
        }
    }

    private void ShowResult()
    {
        questionView.SetActive(false);
        resultView.SetActive(true);
        if (score >= PassMark)
        {
            HasPassed = true;
        }
        BestScore = Mathf.Max(BestScore, score);
        RecordAttempt(completed: true);
        RefreshTexts();
    }

    private void RecordAttempt(bool completed)
    {
        if (attemptRecorded || order.Count == 0)
        {
            return;
        }
        attemptRecorded = true;

        bool passed = completed && score >= PassMark;
        int percent = Mathf.RoundToInt(score * 100f / order.Count);
        var details = new QuizAttemptDetails
        {
            correct = score,
            answered = answeredCount,
            total = order.Count,
            passMark = PassMark,
            completed = completed,
        };
        ProgressStore.Record(progressModule, gameObject.scene.name + "/Quiz", passed, percent,
            Time.realtimeSinceStartup - attemptStartTime, JsonUtility.ToJson(details));
    }

    /// <summary>
    /// Fewest correct answers that bring the combined practice + quiz total to the pass mark.
    /// order.Count + 1 when even a perfect quiz can't (the practice score is too low).
    /// </summary>
    private int PassMark
    {
        get
        {
            for (int correct = 0; correct <= order.Count; correct++)
            {
                if (TrainingScoring.IsPass(practicePercent, TrainingScoring.Percent(correct, order.Count)))
                {
                    return correct;
                }
            }
            return order.Count + 1;
        }
    }

    /// <summary>Re-fetches every visible string -- called on each state change and whenever the language switches mid-quiz.</summary>
    private void RefreshTexts()
    {
        if (order.Count == 0)
        {
            return;
        }

        if (resultView.activeSelf)
        {
            bool passed = score >= PassMark;
            resultTitleText.text = LocalizationManager.Get(passed ? "quiz_result_pass_title" : "quiz_result_fail_title");
            if (passed)
            {
                resultScoreText.text = LocalizationManager.Get("quiz_result_pass_format", score, order.Count);
            }
            else if (PassMark > order.Count)
            {
                resultScoreText.text = LocalizationManager.Get("quiz_result_fail_practice_format", score, order.Count);
            }
            else
            {
                resultScoreText.text = LocalizationManager.Get("quiz_result_fail_format", score, order.Count, PassMark);
            }
            return;
        }

        var q = order[currentIndex];
        progressText.text = LocalizationManager.Get("quiz_progress_format", currentIndex + 1, order.Count);
        questionText.text = LocalizationManager.Get(q.questionKey);
        for (int slot = 0; slot < optionLabels.Length; slot++)
        {
            optionLabels[slot].text = LocalizationManager.Get(q.optionKeys[optionMap[slot]]);
        }

        bool answered = answeredSlot >= 0;
        nextButton.gameObject.SetActive(answered);
        feedbackText.gameObject.SetActive(answered);
        if (answered)
        {
            bool correct = optionMap[answeredSlot] == q.correctIndex;
            string verdict = correct
                ? LocalizationManager.Get("quiz_correct")
                : LocalizationManager.Get("quiz_incorrect_format", LocalizationManager.Get(q.optionKeys[q.correctIndex]));
            feedbackText.text = verdict + "\n" + LocalizationManager.Get(q.explanationKey);
            feedbackText.color = correct ? feedbackCorrectColor : feedbackWrongColor;

            bool last = currentIndex == order.Count - 1;
            nextButtonLabel.text = LocalizationManager.Get(last ? "quiz_see_results" : "next_button");
        }
    }

    private void SetOptionColors(int slot, Color background, Color label)
    {
        // Set the Image directly and mirror it into disabledColor, since locked (non-interactable)
        // buttons otherwise get the ColorBlock's grey tint and the green/red reveal would be lost.
        var img = optionButtons[slot].targetGraphic as Image;
        if (img != null)
        {
            img.color = background;
        }
        var colors = optionButtons[slot].colors;
        colors.normalColor = Color.white;
        colors.disabledColor = Color.white;
        optionButtons[slot].colors = colors;
        optionLabels[slot].color = label;
    }

    private static void Shuffle<T>(IList<T> list)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
