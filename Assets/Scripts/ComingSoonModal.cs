using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Small acknowledgement modal for MainMenu buttons whose destination content doesn't
/// exist yet (e.g. Options) — so tapping them visibly does something honest instead of
/// silently no-opping, which reads to a player as "the button isn't wired."
/// </summary>
public class ComingSoonModal : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
    [SerializeField] private TMP_Text messageText;
    [SerializeField] private Button closeButton;

    private bool showing;

    private void Awake()
    {
        // This script lives on modalRoot, which is saved inactive, so Awake first runs during
        // Show()'s SetActive(true) -- hiding unconditionally here undid the first tap.
        if (modalRoot != null && !showing)
        {
            modalRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        closeButton?.onClick.AddListener(Hide);
    }

    private void OnDisable()
    {
        closeButton?.onClick.RemoveListener(Hide);
    }

    public void Show(string message)
    {
        if (modalRoot != null)
        {
            showing = true;
            modalRoot.SetActive(true);
            showing = false;
        }
        if (messageText != null)
        {
            messageText.text = message;
        }
    }

    /// <summary>Parameterless overload for Inspector OnClick() wiring, which can't pass a string here.</summary>
    public void ShowOptionsComingSoon()
    {
        Show(LocalizationManager.Get("coming_soon_options"));
    }

    public void Hide()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
    }
}
