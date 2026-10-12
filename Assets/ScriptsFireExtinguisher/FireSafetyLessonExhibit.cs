using UnityEngine;

/// <summary>
/// One exhibit in the auto-playing AR "Learn" stage. Owned by FireSafetyLessonController,
/// which highlights it while its content is being explained (Highlight()) and dims it
/// once its turn has passed (MarkViewed()). No tap interaction — the module presents
/// itself in the trainee's real space rather than waiting to be poked at.
/// </summary>
public class FireSafetyLessonExhibit : MonoBehaviour
{
    [SerializeField] private string exhibitId = "exhibit";
    [SerializeField] private string title = "Fire Safety Concept";
    [SerializeField, TextArea] private string content = "Explain the concept.";

    public string ExhibitId => exhibitId;
    public string Title => title;
    public string Content => content;
    public bool Viewed { get; private set; }

    private static readonly Color HighlightColor = new Color(1f, 0.85f, 0.15f); // bright amber while being explained
    private static readonly Color ViewedColor = new Color(0.16f, 0.75f, 0.32f); // dim green once its turn has passed

    private Renderer[] renderers;

    private void Awake()
    {
        CacheRenderers();
    }

    private void CacheRenderers()
    {
        renderers = GetComponentsInChildren<Renderer>();
    }

    /// <summary>Called by FireSafetyLessonController the moment this exhibit becomes the current one.</summary>
    public void Highlight()
    {
        if (renderers == null)
        {
            CacheRenderers();
        }
        MaterialTintUtility.Tint(renderers, HighlightColor);
    }

    /// <summary>Called by FireSafetyLessonController once this exhibit's turn has passed.</summary>
    public void MarkViewed()
    {
        Viewed = true;
        MaterialTintUtility.Tint(renderers, ViewedColor);
    }
}
