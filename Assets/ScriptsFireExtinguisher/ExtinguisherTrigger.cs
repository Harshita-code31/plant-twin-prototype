using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Step 3 of the PASS technique: squeezing the lever. Attach directly to the
/// extinguisher's handle/lever 3D object (with a Collider) — tap-and-hold it on
/// screen, the same plain touch-raycast approach as ExtinguisherPin, so it works
/// identically once the extinguisher is held in front of the camera. No UI button
/// or interaction package required.
/// </summary>
public class ExtinguisherTrigger : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [SerializeField] private ExtinguisherPin pin;
    [SerializeField] private ParticleSystem spray;
    [SerializeField] private ExtinguisherAimController aimController;

    [Tooltip("Optional — if set, the lever can only be squeezed once the extinguisher has been picked up.")]
    [SerializeField] private ExtinguisherPickup pickup;

    [Tooltip("Raised when the lever is squeezed (pin must already be pulled).")]
    public UnityEvent OnSqueezeStart;

    [Tooltip("Raised when the lever is released.")]
    public UnityEvent OnSqueezeEnd;

    public bool IsSqueezed { get; private set; }

    private Collider handleCollider;
    private bool heldLastFrame;

    private void Awake()
    {
        handleCollider = GetComponent<Collider>();
        if (handleCollider == null)
        {
            Debug.LogWarning($"{name}: ExtinguisherTrigger has no Collider, it can never be squeezed.", this);
        }
    }

    private void Update()
    {
        if (pickup != null && !pickup.IsHeld)
        {
            return;
        }

        bool held = IsPointerHeldOnHandle();

        if (held && !heldLastFrame)
        {
            TrySqueezeStart();
        }
        else if (!held && heldLastFrame)
        {
            SqueezeEnd();
        }

        heldLastFrame = held;
    }

    private void OnDisable()
    {
        // Make sure a held spray doesn't keep emitting if this gets disabled mid-squeeze.
        if (IsSqueezed)
        {
            SqueezeEnd();
        }
    }

    private bool IsPointerHeldOnHandle()
    {
        if (handleCollider == null)
        {
            return false;
        }

        Camera cam = raycastCamera != null ? raycastCamera : Camera.main;
        if (cam == null)
        {
            return false;
        }

        if (!ArTouchInput.TryGetHeldPosition(out Vector2 screenPos))
        {
            return false;
        }

        Ray ray = cam.ScreenPointToRay(screenPos);
        return handleCollider.Raycast(ray, out _, float.PositiveInfinity);
    }

    private void TrySqueezeStart()
    {
        if (pin != null && !pin.IsPulled)
        {
            // Real PASS-technique order: can't spray before the pin is pulled.
            return;
        }

        if (IsSqueezed)
        {
            return;
        }

        IsSqueezed = true;
        spray?.Play();
        if (aimController != null)
        {
            aimController.IsSpraying = true;
        }
        OnSqueezeStart?.Invoke();
    }

    private void SqueezeEnd()
    {
        if (!IsSqueezed)
        {
            return;
        }

        IsSqueezed = false;
        spray?.Stop();
        if (aimController != null)
        {
            aimController.IsSpraying = false;
        }
        OnSqueezeEnd?.Invoke();
    }
}
