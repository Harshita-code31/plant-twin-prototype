using UnityEngine;

/// <summary>
/// A trapped/injured person along the evacuation route — the "Assist" of the reference
/// material's Three A's (Activate, Assist, Attempt). Owned and raycast-tapped by
/// EvacuationSequenceController alongside its ordered waypoints, rather than being a
/// waypoint itself: just walking up to them isn't enough, the trainee has to actually
/// tap to help before evacuation can complete.
/// </summary>
[RequireComponent(typeof(Collider))]
public class AssistTarget : MonoBehaviour
{
    public bool IsAssisted { get; private set; }

    private Collider targetCollider;
    public Collider Collider => targetCollider != null ? targetCollider : (targetCollider = GetComponent<Collider>());

    public void MarkAssisted()
    {
        IsAssisted = true;
    }
}
