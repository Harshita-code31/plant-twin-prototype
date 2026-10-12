using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Pre-fire hazard-identification stage: owns a group of FireHazardMarker children,
/// scores taps against a HazardInspectionSession built from their specs (reused as-is
/// from ScriptsChemicalSafety — it's plain C# and not chemical-specific), and applies
/// the same post-tap visual feedback convention as ChemicalInspectionController (green =
/// correctly identified hazard, amber = safe object mis-tapped). Placed and activated
/// first by ARPlacementController, before the fire itself is auto-placed — see
/// FireResponseCoordinator's hazard-stage gating.
/// </summary>
public class FireHazardInspectionController : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    private static readonly Color CorrectHazardColor = new Color(0.16f, 0.75f, 0.32f);
    private static readonly Color SafeObjectMistapColor = new Color(1f, 0.66f, 0.1f);

    [Tooltip("Raised once, when every real hazard has been correctly identified.")]
    public UnityEvent OnInspectionComplete;

    public int HazardsFound => session?.HazardsFound ?? 0;
    public int HazardsRequired => session?.HazardsRequired ?? 0;
    public int FalsePositives => session?.FalsePositives ?? 0;
    public int Score => session?.Score ?? 0;
    public bool IsComplete => session != null && session.IsComplete;

    private FireHazardMarker[] markers;
    private HazardInspectionSession session;

    private void Awake()
    {
        BuildSession();
    }

    private void OnEnable()
    {
        // Re-scan/rebuild in case markers were added after Awake (e.g. instantiated by
        // ARPlacementController), and reset state for a fresh attempt.
        BuildSession();
    }

    private void BuildSession()
    {
        markers = GetComponentsInChildren<FireHazardMarker>(true);

        var specs = new List<InspectionTargetSpec>(markers.Length);
        foreach (var marker in markers)
        {
            specs.Add(new InspectionTargetSpec(marker.TargetId, marker.IsHazard));
        }

        session = specs.Count > 0 ? new HazardInspectionSession(specs) : null;
    }

    private void Update()
    {
        if (session == null || session.IsComplete)
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
        FireHazardMarker tapped = RaycastMarkers(ray);
        if (tapped == null)
        {
            return;
        }

        var result = session.Inspect(tapped.TargetId);
        switch (result.Outcome)
        {
            case InspectionOutcome.CorrectHazard:
                tapped.SetFeedbackColor(CorrectHazardColor);
                break;
            case InspectionOutcome.SafeObjectSelected:
                tapped.SetFeedbackColor(SafeObjectMistapColor);
                break;
            default:
                return;
        }

        if (result.IsComplete)
        {
            OnInspectionComplete?.Invoke();
        }
    }

    private FireHazardMarker RaycastMarkers(Ray ray)
    {
        FireHazardMarker closest = null;
        float closestDistance = float.PositiveInfinity;

        foreach (var marker in markers)
        {
            if (marker == null || marker.Collider == null)
            {
                continue;
            }

            if (marker.Collider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity) && hit.distance < closestDistance)
            {
                closest = marker;
                closestDistance = hit.distance;
            }
        }

        return closest;
    }
}
