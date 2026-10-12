using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Opening alert modal matching the reference AR training app: frames whichever scenario
/// actually got placed ("A fire just started in a trash can! What should you do?") the
/// moment the fire ignites, dismissed with a START button. Shown on demand (see Show)
/// rather than automatically on scene load — showing it at Awake, before any scenario has
/// even been chosen, is how it used to always say "trash can" regardless of which of the
/// 4 scenarios actually got randomly placed. Purely narrative framing — it doesn't gate or
/// interact with the existing Stage-driven placement logic (ARPlacementController), which
/// already handles its own tap-by-tap sequencing independently.
/// </summary>
public class FireAlertIntro : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private TMP_Text bodyText;
    [SerializeField] private Button startButton;

    private NarrationPlayer narrationPlayer;
    private string currentScenarioKey;

    private void Awake()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        startButton?.onClick.AddListener(Dismiss);
        if (narrationPlayer == null)
        {
            narrationPlayer = FindFirstObjectByType<NarrationPlayer>();
        }
        LocalizationManager.OnLanguageChanged += RefreshText;
    }

    private void OnDisable()
    {
        startButton?.onClick.RemoveListener(Dismiss);
        LocalizationManager.OnLanguageChanged -= RefreshText;
    }

    /// <summary>
    /// Shows the modal for this scenario and plays its matching narration clip. Called by
    /// ARPlacementController right after the fire is placed, passing the newly-placed
    /// FireSource's own scenarioNarrationId — one key drives both the localized body text
    /// (see LocalizationManager, e.g. "scenario_trashcan") and the spoken narration clip
    /// (see NarrationPlayer), so both always match whichever of the 4 scenarios actually got
    /// randomly picked this attempt, in whichever language is currently active.
    /// </summary>
    public void Show(string scenarioKey)
    {
        currentScenarioKey = scenarioKey;

        if (modalRoot != null)
        {
            modalRoot.SetActive(true);
        }
        RefreshText();
        narrationPlayer?.Play(scenarioKey);
    }

    private void RefreshText()
    {
        if (bodyText != null && !string.IsNullOrEmpty(currentScenarioKey))
        {
            bodyText.text = LocalizationManager.Get(currentScenarioKey);
        }
    }

    private void Dismiss()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
    }
}
