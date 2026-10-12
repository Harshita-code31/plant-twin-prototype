using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Step 1 of the PASS technique: pulling the safety pin. Plain touch/mouse raycast —
/// no interaction package required. Attach to the pin child object of an extinguisher
/// prefab, with a Collider sized to the pin so it's easy to tap on a phone screen.
/// </summary>
public class ExtinguisherPin : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("Optional — if set, the pin can only be pulled once the extinguisher has been picked up.")]
    [SerializeField] private ExtinguisherPickup pickup;

    [Tooltip("Click played the instant the pin is pulled. Played via PlayClipAtPoint (not a normal AudioSource) since this GameObject deactivates itself immediately after, which would otherwise cut the sound off.")]
    [SerializeField] private AudioClip pullClip;

    [Tooltip("Raised once, the moment the pin is pulled.")]
    public UnityEvent OnPinPulled;

    public bool IsPulled { get; private set; }

    private Collider pinCollider;

    private void Awake()
    {
        pinCollider = GetComponent<Collider>();
        if (pinCollider == null)
        {
            Debug.LogWarning($"{name}: ExtinguisherPin has no Collider, it can never be tapped.", this);
        }
    }

    private void Update()
    {
        if (IsPulled || pinCollider == null)
        {
            return;
        }

        if (pickup != null && !pickup.IsHeld)
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
            if (pinCollider.Raycast(ray, out _, float.PositiveInfinity))
            {
                Pull();
            }
        }
    }

    /// <summary>
    /// Puts the pin back for a new attempt. Wall-mounted extinguishers are reused across a whole
    /// campaign (see ARPlacementController.AdvanceToNextScenario); without this the pin stays pulled
    /// and hidden after the first scenario, so the lever would work without the pin and the
    /// "pull the pin" step could never be earned again.
    /// </summary>
    public void ResetPin()
    {
        IsPulled = false;
        gameObject.SetActive(true);
    }

    private void Pull()
    {
        IsPulled = true;
        if (pullClip != null)
        {
            AudioSource.PlayClipAtPoint(pullClip, transform.position);
        }
        gameObject.SetActive(false);
        OnPinPulled?.Invoke();
    }
}
