using TMPro;
using UnityEngine;

/// <summary>
/// Drop-in replacement for a static TMP_Text caption: attach to any label whose content is
/// always the same fixed string (button captions, panel titles, placeholders) and it keeps
/// itself in sync with LocalizationManager. No font-swapping needed here -- Devanagari and
/// Ol Chiki font assets are registered as fallbacks on the project's main TMP font asset
/// (see the font-import step), so TextMeshPro automatically pulls glyphs it doesn't have
/// from them regardless of which script a given string uses, including strings that mix
/// scripts (an English module name inside an otherwise-Hindi certificate line, say).
///
/// Not used for dynamically-built lines (the per-frame instruction text, results checklists,
/// certificate details, etc.) -- those already call LocalizationManager.Get directly from
/// the C# that assembles them, since a generic component can't supply the runtime values
/// (step numbers, scores, names) those templates need.
/// </summary>
[RequireComponent(typeof(TMP_Text))]
public class LocalizedText : MonoBehaviour
{
    [Tooltip("Key looked up in Resources/Localization/{lang}.json.")]
    [SerializeField] private string key;

    private TMP_Text label;

    private void Awake()
    {
        label = GetComponent<TMP_Text>();
    }

    private void OnEnable()
    {
        if (label == null)
        {
            label = GetComponent<TMP_Text>();
        }
        LocalizationManager.OnLanguageChanged += Refresh;
        Refresh();
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= Refresh;
    }

    private void Refresh()
    {
        if (label == null || string.IsNullOrEmpty(key))
        {
            return;
        }
        label.text = LocalizationManager.Get(key);
    }
}
