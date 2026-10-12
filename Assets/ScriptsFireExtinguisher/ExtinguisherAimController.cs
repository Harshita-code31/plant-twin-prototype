using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Steps 2 and 4 of the PASS technique: aiming at the base of the fire, and sweeping
/// side to side while spraying. Aiming is physical: this transform is a fixed part
/// of the extinguisher, which itself becomes a child of the AR camera once picked up
/// (see ExtinguisherPickup) — so pointing the phone points the nozzle, no on-screen
/// drag input needed. Attach at the extinguisher's nozzle tip.
/// </summary>
public class ExtinguisherAimController : MonoBehaviour
{
    [Tooltip("Max distance to a fire's base for it to be considered in effective spray range.")]
    [SerializeField] private float maxSprayRange = 3f;

    [Tooltip("Max angle (degrees) between the nozzle's forward and the direction to the fire base to count as \"aimed at\".")]
    [SerializeField] private float aimAngleThreshold = 20f;

    [Tooltip("Cumulative aim-direction change (degrees), tracked while spraying, required to count as a sweep.")]
    [SerializeField] private float sweepArcThreshold = 15f;

    [Tooltip("Raised once per spray when the sweep arc threshold is exceeded.")]
    public UnityEvent OnSweepDetected;

    [Tooltip("A sweep only counts while the nozzle stays within this multiple of the aim angle of the target fire's base -- sweeping the ceiling or a wall earns nothing.")]
    [SerializeField] private float sweepAimToleranceScale = 2f;

    /// <summary>Set by ExtinguisherTrigger while the lever is held, to gate sweep tracking.</summary>
    public bool IsSpraying { get; set; }

    /// <summary>
    /// The fire being trained on (set by PassChecklistTracker). Sweep motion is only accumulated
    /// while the spray is directed at it; with no target set, no sweep is ever credited.
    /// </summary>
    public Transform SweepTarget { get; set; }

    public Transform AimPoint => transform;

    private Quaternion lastRotation;
    private bool hasLastRotation;
    private float sweepAccumulated;
    private bool sweepFiredThisSpray;

    private void Update()
    {
        TrackSweep();
    }

    private void TrackSweep()
    {
        if (!IsSpraying)
        {
            // Reset for the next spray attempt once the trigger is released.
            sweepAccumulated = 0f;
            sweepFiredThisSpray = false;
            hasLastRotation = false;
            return;
        }

        if (!hasLastRotation)
        {
            lastRotation = transform.rotation;
            hasLastRotation = true;
            return;
        }

        float delta = Quaternion.Angle(lastRotation, transform.rotation);
        lastRotation = transform.rotation;

        if (sweepFiredThisSpray)
        {
            return;
        }

        // Spraying elsewhere (ceiling, wall, away from the fire) is not a sweep of the fire.
        if (SweepTarget == null || !IsAimedAtFireBase(SweepTarget, sweepAimToleranceScale))
        {
            return;
        }

        sweepAccumulated += delta;
        if (sweepAccumulated >= sweepArcThreshold)
        {
            sweepFiredThisSpray = true;
            OnSweepDetected?.Invoke();
        }
    }

    /// <summary>
    /// Whether the nozzle is currently pointed at the given fire's base, within the
    /// configured angle/distance thresholds.
    /// </summary>
    public bool IsAimedAtFireBase(Transform fire)
    {
        return IsAimedAtFireBase(fire, 1f);
    }

    /// <summary>Same check with the aim-angle threshold scaled by <paramref name="angleScale"/> (e.g. 2 for a looser sweep test).</summary>
    public bool IsAimedAtFireBase(Transform fire, float angleScale)
    {
        if (fire == null)
        {
            return false;
        }

        Vector3 toFire = fire.position - AimPoint.position;
        float distance = toFire.magnitude;
        if (distance > maxSprayRange)
        {
            return false;
        }

        float angle = Vector3.Angle(AimPoint.forward, toFire);
        return angle <= aimAngleThreshold * angleScale;
    }
}
