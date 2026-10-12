using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Full-screen language picker opened by the Language button on MainMenu, replacing the
/// old TMP_Dropdown (a native dropdown read as a small, easy-to-miss control here; a full
/// list makes language switching an obvious, deliberate action). Mirrors ComingSoonModal's
/// own show/hide pattern: this script sits on the backdrop GameObject itself, which is
/// also the thing toggled active/inactive.
/// </summary>
public class LanguageSelectModal : MonoBehaviour
{
    [SerializeField] private GameObject modalRoot;
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

    public void Show()
    {
        if (modalRoot != null)
        {
            showing = true;
            modalRoot.SetActive(true);
            showing = false;
        }
    }

    public void Hide()
    {
        if (modalRoot != null)
        {
            modalRoot.SetActive(false);
        }
    }

    // One parameterless wrapper per shipped language (see LocalizationManager.AvailableLanguages)
    // so each row's OnClick() can wire directly to it -- a persistent UnityEvent can't target
    // a parameterized lookup into that array from the Inspector.
    public void SelectEnglish() => Select(LocalizationManager.English);
    public void SelectHindi() => Select(LocalizationManager.Hindi);
    public void SelectSantali() => Select(LocalizationManager.Santali);

    private void Select(string code)
    {
        LocalizationManager.SetLanguage(code);
        Hide();
    }
}
