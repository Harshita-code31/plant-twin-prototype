using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// AR-native "Learn" stage: starts automatically the moment its exhibits are placed in
/// the trainee's real room, and walks through them itself — highlighting whichever one
/// is currently being explained and showing its content — with no tap required. The
/// module presents itself; the trainee just watches and looks around.
/// </summary>
public class FireSafetyLessonController : MonoBehaviour
{
    [Tooltip("Seconds each exhibit stays highlighted/explained before advancing to the next.")]
    [SerializeField] private float secondsPerExhibit = 6f;

    [Tooltip("Raised once, when every exhibit has been shown.")]
    public UnityEvent OnLessonComplete;

    /// <summary>Raised when an exhibit becomes the current/highlighted one, with its title and content — wire a detail panel to this.</summary>
    public event System.Action<string, string> OnExhibitViewed;

    public int ViewedCount { get; private set; }
    public int ExhibitCount { get; private set; }
    public bool IsComplete { get; private set; }
    public FireSafetyLessonExhibit CurrentExhibit { get; private set; }

    private FireSafetyLessonExhibit[] exhibits;
    private Coroutine playRoutine;

    private void Awake()
    {
        CacheExhibits();
    }

    private void OnEnable()
    {
        CacheExhibits();
        ViewedCount = 0;
        IsComplete = false;
        CurrentExhibit = null;
        playRoutine = StartCoroutine(PlayLesson());
    }

    private void OnDisable()
    {
        if (playRoutine != null)
        {
            StopCoroutine(playRoutine);
            playRoutine = null;
        }
    }

    private void CacheExhibits()
    {
        exhibits = GetComponentsInChildren<FireSafetyLessonExhibit>(true);
        ExhibitCount = exhibits.Length;
    }

    private IEnumerator PlayLesson()
    {
        if (exhibits == null || exhibits.Length == 0)
        {
            Complete();
            yield break;
        }

        foreach (var exhibit in exhibits)
        {
            if (exhibit == null)
            {
                continue;
            }

            CurrentExhibit = exhibit;
            exhibit.Highlight();
            OnExhibitViewed?.Invoke(exhibit.Title, exhibit.Content);

            yield return new WaitForSeconds(secondsPerExhibit);

            exhibit.MarkViewed();
            ViewedCount++;
        }

        CurrentExhibit = null;
        Complete();
    }

    private void Complete()
    {
        IsComplete = true;
        OnLessonComplete?.Invoke();
    }
}
