using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Manual mode's jog control -- runs the belt only while held, releasing stops it
/// immediately. Lives on the same GameObject/Collider as the Start ConveyorControlButton
/// (StartButtonHotspot): the two coexist without conflict because ConveyorMotionController
/// self-guards each entry point by Mode -- a tap calls Run() (no-ops in Manual), a hold
/// calls BeginJog()/EndJog() (no-ops in Auto). Same tap-and-hold pattern as
/// ExtinguisherTrigger.cs (ArTouchInput.TryGetHeldPosition + Collider.Raycast), minus its
/// pin/pickup preconditions -- there's nothing to unlock first here.
/// </summary>
public class ConveyorJogButton : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [SerializeField] private ConveyorMotionController motionController;

    [Tooltip("Raised on the frame a hold begins (before ConveyorMotionController's own guards -- may be a no-op in Auto mode).")]
    public UnityEvent OnJogHeld;

    [Tooltip("Raised on the frame a hold ends.")]
    public UnityEvent OnJogReleased;

    private Collider hotspotCollider;
    private bool heldLastFrame;

    private void Awake()
    {
        hotspotCollider = GetComponent<Collider>();
    }

    private void Update()
    {
        bool held = IsPointerHeldOnHotspot();

        if (held && !heldLastFrame)
        {
            if (motionController != null)
            {
                motionController.BeginJog();
            }
            OnJogHeld?.Invoke();
        }
        else if (!held && heldLastFrame)
        {
            if (motionController != null)
            {
                motionController.EndJog();
            }
            OnJogReleased?.Invoke();
        }

        heldLastFrame = held;
    }

    private void OnDisable()
    {
        // Don't leave the belt stuck running if this component goes inactive mid-hold.
        if (heldLastFrame)
        {
            if (motionController != null)
            {
                motionController.EndJog();
            }
            heldLastFrame = false;
        }
    }

    private bool IsPointerHeldOnHotspot()
    {
        if (hotspotCollider == null)
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
        return hotspotCollider.Raycast(ray, out _, float.PositiveInfinity);
    }
}
