using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Tap-to-pick-up for an extinguisher mounted on the fire safety wall scenario (see
/// ARPlacementController, which places that whole prefab with one floor tap). Before
/// pickup the extinguisher sits still where it was mounted; tapping its body (this
/// object's Collider) re-parents it to the AR camera at a fixed held-position offset,
/// like a first-person held prop — pointing the phone then naturally aims the nozzle,
/// no separate aim input needed (see ExtinguisherAimController). A tap can be rejected
/// outright before any of that happens -- see SetRequiredCallPoint/SetTargetFire.
/// </summary>
public class ExtinguisherPickup : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps, and that the extinguisher is held relative to. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("Local position (relative to the camera) the extinguisher snaps to once held. Keep the handle (and the rest of the model) within the camera's FOV cone on a real device's narrower aspect ratio, not just in the Editor's Game View -- Unity's FOV is vertical, so a taller/narrower phone screen has a noticeably tighter horizontal FOV than a wider test window, and a held-prop offset that looks fine in the Editor can clip on-device. See the -- now corrected -- default here: the previous (0.12, -0.32, 0.55) put the handle's edge ~28 degrees off camera-forward against a real device's ~19-degree horizontal half-FOV budget, clipping it off-screen.")]
    [SerializeField] private Vector3 heldLocalPosition = new Vector3(0.05f, -0.22f, 0.85f);

    [Tooltip("Only used when nozzleReference is unset (see PickUp): local rotation (euler, relative to the camera) the extinguisher snaps to once held. When nozzleReference IS set, only the Y component is used, and it means something different -- see PickUp's doc comment: it spins the already-upright, already-aimed held pose around its own vertical axis to bring the hose and nozzle out from behind the body's silhouette (negative swings it to the left, toward the screen centre); the nozzle pivot is then re-aimed along the camera's forward, so this angle does not change where the spray goes.")]
    [SerializeField] private Vector3 heldLocalEulerAngles = new Vector3(0f, 0f, 0f);

    [Tooltip("The nozzle transform (see ExtinguisherAimController) — its rest-pose direction rarely points straight out of the model, so pickup auto-corrects rotation to make it point exactly where the camera looks once held. Optional.")]
    [SerializeField] private Transform nozzleReference;

    [Tooltip("Raised once, the moment the extinguisher is picked up.")]
    public UnityEvent OnPickedUp;

    [Tooltip("Raised when a tap is rejected because the alarm hasn't been activated yet (see SetRequiredCallPoint) -- no pickup, no snap-into-hand.")]
    public UnityEvent OnPickupBlockedAlarmNotActive;

    [Tooltip("Raised when a tap is rejected because this extinguisher isn't rated for the current fire (see SetTargetFire) -- no pickup, no snap-into-hand.")]
    public UnityEvent OnPickupBlockedWrongExtinguisher;

    [Tooltip("Sound played the instant the extinguisher is picked up.")]
    [SerializeField] private AudioClip pickupClip;

    public bool IsHeld { get; private set; }

    /// <summary>
    /// The anchor this extinguisher was originally mounted under (see ARPlacementController.
    /// CreateAnchor) — stays correct even once held, unlike transform.parent, which PickUp
    /// changes to the AR camera. ARPlacementController's Retry/Next-scenario cleanup needs
    /// this: destroying transform.parent.gameObject after this extinguisher had been picked
    /// up used to destroy the AR camera itself (it briefly IS this transform's parent while
    /// held), taking raycasting/rendering/UI down with it.
    /// </summary>
    public Transform HomeAnchor => originalParent;

    private Collider bodyCollider;
    private Transform originalParent;
    private Vector3 originalLocalPosition;
    private Quaternion originalLocalRotation;
    private ManualCallPointController requiredCallPoint;
    private FireSource targetFire;
    private ExtinguisherIdentity identity;
    private Quaternion nozzleRestLocalRotation = Quaternion.identity;

    private void Awake()
    {
        // The nozzle pivot is re-aimed while held (see PickUp) and put back in UndoPickup, so the
        // authored rotation is remembered here rather than read back from a possibly-modified pivot.
        if (nozzleReference != null)
        {
            nozzleRestLocalRotation = nozzleReference.localRotation;
        }

        bodyCollider = GetComponent<Collider>();
        if (bodyCollider == null)
        {
            Debug.LogWarning($"{name}: ExtinguisherPickup has no Collider, it can never be picked up.", this);
        }

        // Always a descendant, never on this same GameObject (see FE_Red.prefab's
        // hierarchy) -- resolved once here rather than requiring external wiring, since
        // it never changes for a given prefab instance.
        identity = GetComponentInChildren<ExtinguisherIdentity>();

        // Best-effort fallback only -- ARPlacementController calls CaptureRestPose() right
        // after it finishes positioning a freshly-placed extinguisher, which is what
        // actually matters (see CaptureRestPose's own comment for why Awake's snapshot
        // can't be trusted for this).
        CaptureRestPose();
    }

    /// <summary>Wired at runtime by ARPlacementController right after the wall scenario is placed -- until the alarm is activated, taps on this extinguisher are rejected outright (see Update()), not picked-up-then-undone.</summary>
    public void SetRequiredCallPoint(ManualCallPointController callPoint)
    {
        requiredCallPoint = callPoint;
    }

    /// <summary>Wired at runtime once the current fire exists (mirrors PassChecklistTracker.SetTargetFire) -- lets Update() reject a wrong-class pickup before it ever happens, instead of ARPlacementController.HandleExtinguisherChosen undoing it after the fact.</summary>
    public void SetTargetFire(FireSource fire)
    {
        targetFire = fire;
    }

    /// <summary>
    /// Snapshots the current parent/local pose as "home" -- where UndoPickup puts this
    /// extinguisher back when it's rejected as the wrong choice for the fire. Must be
    /// called once placement is actually finished, not from Awake(): ARPlacementController
    /// instantiates each option via Instantiate(prefab, worldPos, worldRot, anchor), and
    /// with a parent argument those position/rotation values are WORLD space, not local --
    /// so at the moment Awake() runs (mid-Instantiate, before the caller's next lines get a
    /// chance to run), transform.localPosition is whatever local offset happens to map to
    /// world (0,0,0) under the anchor, not the anchor's own position. Caching that in Awake
    /// used to send a rejected extinguisher flying off toward world origin (visibly "down
    /// to the floor") instead of back to where it was actually mounted on the wall.
    /// </summary>
    public void CaptureRestPose()
    {
        originalParent = transform.parent;
        originalLocalPosition = transform.localPosition;
        originalLocalRotation = transform.localRotation;
    }

    private void Update()
    {
        if (IsHeld || bodyCollider == null)
        {
            return;
        }

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
        {
            return;
        }

        if (ArTouchInput.TryGetTapPosition(out Vector2 screenPos))
        {
            Ray ray = cam.ScreenPointToRay(screenPos);
            if (bodyCollider.Raycast(ray, out _, float.PositiveInfinity))
            {
                // Both checks happen BEFORE PickUp -- rejecting here means the extinguisher
                // never reparents/snaps into the player's hand at all, unlike the old
                // pick-up-then-UndoPickup flow. requiredCallPoint/targetFire are null until
                // ARPlacementController wires them right after placement, so an unwired
                // pickup (e.g. a stray test instance) is never blocked by a gate it was never
                // told to enforce.
                if (requiredCallPoint != null && !requiredCallPoint.IsActivated)
                {
                    OnPickupBlockedAlarmNotActive?.Invoke();
                    return;
                }

                if (identity != null && targetFire != null && !identity.CanExtinguish(targetFire.fireClass))
                {
                    OnPickupBlockedWrongExtinguisher?.Invoke();
                    return;
                }

                PickUp(cam);
            }
        }
    }

    private void PickUp(Camera cam)
    {
        IsHeld = true;
        if (pickupClip != null)
        {
            AudioSource.PlayClipAtPoint(pickupClip, transform.position);
        }
        transform.SetParent(cam.transform, worldPositionStays: false);
        transform.localPosition = heldLocalPosition;

        if (nozzleReference != null)
        {
            // Aim the nozzle's forward exactly at the camera's forward (required for
            // ExtinguisherAimController's aim-detection to line up with where the player is
            // actually looking) while ALSO keeping the extinguisher's own long axis as close
            // to upright (world up) as that aim constraint allows. The old approach
            // (FromToRotation(nozzleReference.forward, cam.forward) applied on top of
            // heldLocalEulerAngles) fixed the aim direction correctly but left roll around
            // that axis essentially arbitrary -- in practice it could tip the whole canister
            // onto its side to get the nozzle "out from behind" the body, which read as
            // tilted far more than a person would ever actually hold one.
            //
            // nozzleReference is a direct child of this transform, so its localRotation
            // (parent-independent) gives the nozzle's forward direction in THIS transform's
            // own local space regardless of any rotation applied to this transform itself.
            Vector3 nozzleLocalForward = nozzleRestLocalRotation * Vector3.forward;
            Quaternion zAxisToNozzle = Quaternion.FromToRotation(Vector3.forward, nozzleLocalForward);
            Quaternion aimedAndLevel = Quaternion.LookRotation(cam.transform.forward, Vector3.up) * Quaternion.Inverse(zAxisToNozzle);

            // heldLocalEulerAngles.y spins the already-level, already-aimed pose around its
            // own (now-vertical) axis -- e.g. to rotate the nozzle out from directly behind
            // the body's silhouette -- without re-introducing the old tilt. This does nudge
            // the nozzle's true aim slightly off camera-forward, same as a real held
            // extinguisher is never held in mathematically perfect alignment; keep it well
            // inside ExtinguisherAimController's aimAngleThreshold.
            transform.rotation = Quaternion.AngleAxis(heldLocalEulerAngles.y, Vector3.up) * aimedAndLevel;

            // The yaw above swings the hose end out to the side so the nozzle (and the smoke that
            // starts there) is seen beside the cylinder instead of hidden behind it. That must not
            // change where the spray goes: re-aim the nozzle pivot -- which the spray particles and
            // ExtinguisherAimController's aim/sweep checks are attached to -- straight along the
            // camera's forward. Its position stays at the visible hose end.
            nozzleReference.rotation = Quaternion.LookRotation(cam.transform.forward, Vector3.up);
        }
        else
        {
            transform.localRotation = Quaternion.Euler(heldLocalEulerAngles);
        }

        // This project has Physics.autoSyncTransforms disabled (Project Settings >
        // Physics), so colliders don't automatically pick up transform changes made
        // from script. Without this, every collider on the extinguisher (pin,
        // handle, body) stays physically stuck at its old wall-mounted position —
        // raycasts against them (tap-to-pull-pin, tap-to-squeeze) would keep testing
        // the wrong spot even though the model visibly moved to the held pose.
        Physics.SyncTransforms();

        OnPickedUp?.Invoke();
    }

    /// <summary>
    /// Puts the extinguisher back where it was mounted and lets it be picked up again —
    /// called by ARPlacementController when the player grabs an extinguisher that isn't
    /// rated for the current fire, instead of letting them commit to a losing choice.
    /// </summary>
    public void UndoPickup()
    {
        IsHeld = false;
        transform.SetParent(originalParent, worldPositionStays: false);
        transform.localPosition = originalLocalPosition;
        transform.localRotation = originalLocalRotation;
        if (nozzleReference != null)
        {
            nozzleReference.localRotation = nozzleRestLocalRotation;
        }
        Physics.SyncTransforms();
    }
}
