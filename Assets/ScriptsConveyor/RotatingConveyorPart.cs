using UnityEngine;

/// <summary>
/// Spins one of the conveyor's end-drum meshes (a separate CAD sub-object, unlike the belt
/// surface itself) in sync with the belt's linear speed, so the drum doesn't visibly "slip"
/// against the belt it's driving. Only worth adding to the 2 visible main drums, not every
/// roller/bearing in the 341-mesh assembly.
/// </summary>
public class RotatingConveyorPart : MonoBehaviour
{
    [Tooltip("Spin axis, expressed in axisReference's local space -- NOT this part's own local space (a CAD assembly's nested nodes commonly carry their own baked rotation, so this part's own local axes don't line up with its visible shaft direction) and NOT raw world space either (once the conveyor is placed in AR, the whole rig is rotated to match the tapped floor/anchor pose, so a hardcoded world direction stops matching the shaft the instant the prefab isn't sitting at identity rotation). Resolving it through axisReference each frame keeps it correct regardless of how the conveyor is placed.")]
    [SerializeField] private Vector3 axisInReferenceSpace = Vector3.right;

    [Tooltip("Stable transform the spin axis is expressed relative to -- normally the conveyor's root, since that's exactly what AR placement rotates as a rigid whole. Falls back to world space if left empty.")]
    [SerializeField] private Transform axisReference;

    [Tooltip("Approximate real-world radius of this drum, used to convert the belt's linear speed into a matching angular speed.")]
    [SerializeField] private float rollerRadiusMeters = 0.05f;

    [Tooltip("The belt whose speed this drum's spin is derived from -- keeps every rotating part locked to one source of truth even if belt speed is retuned later.")]
    [SerializeField] private ConveyorBeltAnimator syncSource;

    private bool isRunning;
    private Vector3 localPivotOffset;

    private void Awake()
    {
        // This part's Transform.position is the CAD assembly's shared origin, not this mesh's
        // own visual center (typical of a SolidWorks export, where every part keeps the whole
        // assembly's pivot). transform.Rotate spins around transform.position, so rotating
        // in place requires explicitly pivoting around the mesh's own bounds center instead --
        // otherwise the drum swings/orbits around that far-off shared origin like a pendulum.
        var rend = GetComponent<Renderer>();
        localPivotOffset = rend != null ? transform.InverseTransformPoint(rend.bounds.center) : Vector3.zero;
    }

    public void SetRunning(bool running)
    {
        isRunning = running;
    }

    private void Update()
    {
        if (!isRunning || syncSource == null || rollerRadiusMeters <= 0f)
        {
            return;
        }

        float linearSpeed = syncSource.CurrentSpeedMetersPerSecond;
        float angularSpeedDegPerSec = (linearSpeed / (2f * Mathf.PI * rollerRadiusMeters)) * 360f;
        Vector3 worldPivot = transform.TransformPoint(localPivotOffset);
        Vector3 worldAxis = axisReference != null ? axisReference.TransformDirection(axisInReferenceSpace) : axisInReferenceSpace;
        transform.RotateAround(worldPivot, worldAxis, angularSpeedDegPerSec * Time.deltaTime);
    }
}
