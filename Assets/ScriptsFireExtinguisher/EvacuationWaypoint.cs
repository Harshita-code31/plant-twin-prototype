using UnityEngine;

/// <summary>
/// One ordered stop on an evacuation route. Plain data, in the same style as ExitSign —
/// the logic lives in EvacuationSequenceController, which owns a group of these.
/// </summary>
public class EvacuationWaypoint : MonoBehaviour
{
    [Tooltip("Position of this stop in the route, lowest first. Must be unique within one route.")]
    [SerializeField] private int order;

    [Tooltip("Whether this is the final assembly/muster point that completes the drill.")]
    [SerializeField] private bool isAssemblyPoint;

    public int Order => order;
    public bool IsAssemblyPoint => isAssemblyPoint;
}
