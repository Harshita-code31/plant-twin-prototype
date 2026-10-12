using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Exit-identification stage of the evacuation drill: owns a small group of ExitSign
/// children (one correct, one or two decoys) and resolves taps against them. Tapping
/// the wrong sign is recorded as a real mistake (matches the training requirement that
/// a wrong choice counts against the trainee) but does not end the stage — like this
/// project's pin-before-squeeze gating, the trainee must still find the correct exit
/// to move on, since in reality you can't evacuate through a blocked exit just because
/// you picked it.
/// </summary>
public class ExitIdentificationController : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("Raised the first time (and every time) the wrong sign is tapped.")]
    public UnityEvent OnWrongExitSelected;

    [Tooltip("Raised once, the moment the correct sign is tapped.")]
    public UnityEvent OnCorrectExitSelected;

    /// <summary>True once a wrong sign has ever been tapped during this stage.</summary>
    public bool WrongExitAttempted { get; private set; }

    /// <summary>True once the correct sign has been tapped.</summary>
    public bool IsResolved { get; private set; }

    private ExitSign[] signs;

    private void Awake()
    {
        signs = GetComponentsInChildren<ExitSign>(true);
    }

    private void OnEnable()
    {
        // Re-scan in case signs were added/replaced after Awake (e.g. instantiated by
        // ARPlacementController), and reset stage state for a fresh attempt.
        signs = GetComponentsInChildren<ExitSign>(true);
        WrongExitAttempted = false;
        IsResolved = false;
    }

    private void Update()
    {
        if (IsResolved || signs == null || signs.Length == 0)
        {
            return;
        }

        if (!ArTouchInput.TryGetTapPosition(out Vector2 screenPos))
        {
            return;
        }

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        Ray ray = cam.ScreenPointToRay(screenPos);
        ExitSign tapped = RaycastSigns(ray);
        if (tapped == null)
        {
            return;
        }

        if (tapped.IsCorrectExit)
        {
            IsResolved = true;
            OnCorrectExitSelected?.Invoke();
        }
        else
        {
            WrongExitAttempted = true;
            OnWrongExitSelected?.Invoke();
        }
    }

    private ExitSign RaycastSigns(Ray ray)
    {
        ExitSign closest = null;
        float closestDistance = float.PositiveInfinity;

        foreach (var sign in signs)
        {
            if (sign == null || sign.Collider == null)
            {
                continue;
            }

            if (sign.Collider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity) && hit.distance < closestDistance)
            {
                closest = sign;
                closestDistance = hit.distance;
            }
        }

        return closest;
    }
}
