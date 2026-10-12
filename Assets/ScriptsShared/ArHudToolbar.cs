using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shared in-AR toolbar chrome, styled after the reference SENAR/NCCER AR training app: top-left
/// Close/Reposition/Help icon buttons, top-right Timer/Pause, and a bottom instruction card. This
/// wraps whatever instruction text a module's own *InstructionsUI script already computes every
/// frame — it only supplies the visual container and the icon actions, never any training logic
/// itself, and stays deliberately module-agnostic (no reference to FireResponseCoordinator/
/// CprResponseCoordinator etc.) so the same prefab is reused across every module: each module's
/// own InstructionsUI script wires ElapsedSecondsProvider/OnReposition to its own coordinator in
/// OnEnable.
/// </summary>
public class ArHudToolbar : MonoBehaviour
{
    [SerializeField] private Button closeButton;
    [SerializeField] private Button repositionButton;
    [SerializeField] private Button helpButton;
    [SerializeField] private Button pauseButton;
    [SerializeField] private TMP_Text pauseButtonLabel;
    [SerializeField] private TMP_Text timerText;
    [SerializeField] private RectTransform bottomCard;

    [Tooltip("How much larger the bottom instruction card gets while the Help toggle is on.")]
    [SerializeField] private float helpExpandScale = 1.12f;

    /// <summary>Wired by the module's own InstructionsUI script — returns seconds elapsed in the current attempt.</summary>
    public System.Func<float> ElapsedSecondsProvider;

    /// <summary>Wired by the module's own InstructionsUI script — the closest existing equivalent to the reference app's "reposition" action: re-scan and re-place (module ResetTraining()).</summary>
    public System.Action OnReposition;

    private MenuManager menuManager;
    private bool paused;
    private bool helpExpanded;

    private void Awake()
    {
        menuManager = FindFirstObjectByType<MenuManager>();
    }

    private void OnEnable()
    {
        closeButton?.onClick.AddListener(HandleClose);
        repositionButton?.onClick.AddListener(HandleReposition);
        helpButton?.onClick.AddListener(HandleHelp);
        pauseButton?.onClick.AddListener(HandlePause);
    }

    private void OnDisable()
    {
        closeButton?.onClick.RemoveListener(HandleClose);
        repositionButton?.onClick.RemoveListener(HandleReposition);
        helpButton?.onClick.RemoveListener(HandleHelp);
        pauseButton?.onClick.RemoveListener(HandlePause);

        // Never leave the game paused behind a scene change.
        if (paused)
        {
            Time.timeScale = 1f;
            paused = false;
        }
    }

    private void Update()
    {
        if (timerText == null || ElapsedSecondsProvider == null)
        {
            return;
        }

        float t = Mathf.Max(0f, ElapsedSecondsProvider());
        int minutes = Mathf.FloorToInt(t / 60f);
        int seconds = Mathf.FloorToInt(t % 60f);
        timerText.text = $"{minutes:00}:{seconds:00}";
    }

    private void HandleClose()
    {
        if (menuManager == null)
        {
            menuManager = FindFirstObjectByType<MenuManager>();
        }
        menuManager?.LoadMainMenu();
    }

    private void HandleReposition()
    {
        OnReposition?.Invoke();
    }

    private void HandleHelp()
    {
        helpExpanded = !helpExpanded;
        if (bottomCard != null)
        {
            bottomCard.localScale = helpExpanded ? Vector3.one * helpExpandScale : Vector3.one;
        }
    }

    private void HandlePause()
    {
        paused = !paused;
        Time.timeScale = paused ? 0f : 1f;
        if (pauseButtonLabel != null)
        {
            pauseButtonLabel.text = paused ? "RESUME" : "PAUSE";
        }
    }
}
