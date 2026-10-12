using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Gas-furnace scenario's extra safety step: a single tap-target valve handle the trainee
/// must shut off in addition to extinguishing the fire itself. Same single-target tap
/// pattern as ManualCallPointController. FurnaceExplosionController checks IsShutOff once
/// the fire reports extinguished and decides between a normal "fire out" result and the
/// furnace-explosion forced-failure branch.
/// </summary>
public class GasShutoffValve : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("The valve handle's visual transform — rotated a quarter turn when shut off so the action reads clearly. Defaults to this GameObject's first child.")]
    [SerializeField] private Transform handleVisual;

    [Tooltip("Raised once, the moment the valve is tapped and shut off.")]
    public UnityEvent OnShutOff;

    public bool IsShutOff { get; private set; }

    private Collider valveCollider;

    private void Awake()
    {
        valveCollider = GetComponentInChildren<Collider>();
        if (handleVisual == null && transform.childCount > 0)
        {
            handleVisual = transform.GetChild(0);
        }
    }

    private void OnEnable()
    {
        // Re-scan in case this was instantiated after Awake, and reset for a fresh attempt.
        valveCollider = GetComponentInChildren<Collider>();
        IsShutOff = false;
    }

    private void Update()
    {
        if (IsShutOff || valveCollider == null)
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
        if (valveCollider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity))
        {
            IsShutOff = true;
            if (handleVisual != null)
            {
                handleVisual.localRotation *= Quaternion.Euler(0f, 90f, 0f);
            }
            OnShutOff?.Invoke();
        }
    }
}
