using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Evacuation-sequencing stage of the drill: owns an ordered set of EvacuationWaypoint
/// children and guides the trainee through them in order by physically walking there
/// with the phone. Only the current target waypoint's visual is active — earlier ones
/// are already passed, later ones are hidden — so the route reads as turn-by-turn
/// guidance and skipping ahead is impossible by construction, rather than needing
/// separate out-of-order detection. Reaching the final waypoint flagged
/// EvacuationWaypoint.IsAssemblyPoint completes the drill.
/// </summary>
public class EvacuationSequenceController : MonoBehaviour
{
    [Tooltip("Distance (meters) from a waypoint the AR camera must be within to count as arrived.")]
    [SerializeField] private float arrivalRadius = 0.6f;

    [Tooltip("Raised once, when the final assembly-point waypoint is reached.")]
    public UnityEvent OnEvacuationComplete;

    /// <summary>Raised each time a waypoint is reached, with its Order.</summary>
    public event Action<int> OnWaypointReached;

    public bool IsComplete { get; private set; }

    /// <summary>True if this route has a trapped/injured person to help (the "Assist"
    /// of the Three A's). False on a route with none configured — nothing to gate on.</summary>
    public bool HasAssistTarget => assistTarget != null;

    /// <summary>True once the assist target has been tapped, or if there is none.</summary>
    public bool IsAssisted => assistTarget == null || assistTarget.IsAssisted;

    private EvacuationWaypoint[] orderedWaypoints;
    private AssistTarget assistTarget;
    private int currentIndex;

    private void Awake()
    {
        CacheWaypoints();
    }

    private void OnEnable()
    {
        // Re-scan in case waypoints were added/replaced after Awake, and reset for a
        // fresh attempt.
        CacheWaypoints();
        currentIndex = 0;
        IsComplete = false;
        RefreshVisibility();
    }

    private void CacheWaypoints()
    {
        orderedWaypoints = GetComponentsInChildren<EvacuationWaypoint>(true)
            .OrderBy(w => w.Order)
            .ToArray();
        assistTarget = GetComponentInChildren<AssistTarget>(true);
    }

    private void Update()
    {
        if (IsComplete)
        {
            return;
        }

        Camera cam = Camera.main;
        if (cam == null)
        {
            return;
        }

        // The assist target can be tapped at any point during the walk (it's a
        // "help along the way" action, not tied to a specific waypoint), so it's
        // checked every frame independently of the current waypoint index.
        if (assistTarget != null && !assistTarget.IsAssisted && ArTouchInput.TryGetTapPosition(out Vector2 screenPos))
        {
            Ray tapRay = cam.ScreenPointToRay(screenPos);
            if (assistTarget.Collider.Raycast(tapRay, out RaycastHit assistHit, float.PositiveInfinity))
            {
                assistTarget.MarkAssisted();
            }
        }

        if (orderedWaypoints == null || currentIndex >= orderedWaypoints.Length)
        {
            return;
        }

        EvacuationWaypoint target = orderedWaypoints[currentIndex];
        if (target == null)
        {
            currentIndex++;
            return;
        }

        float distance = Vector3.Distance(cam.transform.position, target.transform.position);
        if (distance <= arrivalRadius)
        {
            Arrive(target);
        }
    }

    private void Arrive(EvacuationWaypoint target)
    {
        if (target.IsAssemblyPoint)
        {
            if (assistTarget != null && !assistTarget.IsAssisted)
            {
                // Can't finish evacuating without helping the person along the way —
                // leave the assembly point active so the trainee can come back to it.
                return;
            }

            target.gameObject.SetActive(false);
            OnWaypointReached?.Invoke(target.Order);
            IsComplete = true;
            OnEvacuationComplete?.Invoke();
            return;
        }

        target.gameObject.SetActive(false);
        OnWaypointReached?.Invoke(target.Order);
        currentIndex++;
        RefreshVisibility();
    }

    private void RefreshVisibility()
    {
        if (orderedWaypoints == null)
        {
            return;
        }

        for (int i = 0; i < orderedWaypoints.Length; i++)
        {
            if (orderedWaypoints[i] == null)
            {
                continue;
            }

            // Only the current target is visible/active; already-passed waypoints were
            // deactivated on arrival, future ones stay hidden until it's their turn.
            orderedWaypoints[i].gameObject.SetActive(i == currentIndex);
        }
    }
}
