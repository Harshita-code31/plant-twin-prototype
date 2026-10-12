using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Activate stage of the drill — the first of the reference material's "Three A's"
/// (Activate, Assist, Attempt): a single manual call point prop, auto-placed alongside
/// everything else on the single placement tap (see ARPlacementController.PlaceEverything).
/// Tapping it raises the alarm and plays the siren; TrainingInstructionsUI guides the
/// player to do this before the extinguisher steps by surfacing this stage's prompt
/// ahead of any fire/extinguisher instruction as long as it's unactivated. Same
/// tap-raycast pattern as ExitIdentificationController/FireHazardInspectionController,
/// simplified to one target — there's nothing to mis-tap about a call point once you've
/// found it.
/// </summary>
public class ManualCallPointController : MonoBehaviour
{
    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("Siren clip played once the alarm is activated. Empty until a clip is generated (blocked on the fal.ai key) or assigned manually.")]
    [SerializeField] private AudioClip sirenClip;

    [Tooltip("Raised once, the moment the call point is tapped.")]
    public UnityEvent OnActivated;

    public bool IsActivated { get; private set; }

    /// <summary>Re-arms the alarm for the next scenario in a campaign (see ARPlacementController.AdvanceToNextScenario) -- the call point itself stays mounted between scenarios, so unlike a fresh placement's OnEnable reset, this has to be triggered explicitly. Also stops the looping siren from the previous scenario -- otherwise it just keeps blaring into the next attempt, which reads as "the alarm is already on" even though IsActivated has been reset.</summary>
    public void ResetActivation()
    {
        IsActivated = false;
        if (audioSource != null)
        {
            audioSource.Stop();
        }
    }

    private Collider callPointCollider;
    private AudioSource audioSource;

    private void Awake()
    {
        callPointCollider = GetComponentInChildren<Collider>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f; // 3D -- the siren should sound like it's coming from the alarm's position.
    }

    private void OnEnable()
    {
        // Re-scan in case this was instantiated after Awake (ARPlacementController),
        // and reset for a fresh attempt.
        callPointCollider = GetComponentInChildren<Collider>();
        IsActivated = false;
    }

    private void Update()
    {
        if (IsActivated || callPointCollider == null)
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
        if (callPointCollider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity))
        {
            IsActivated = true;
            if (sirenClip != null)
            {
                audioSource.clip = sirenClip;
                audioSource.loop = true;
                audioSource.Play();
            }
            OnActivated?.Invoke();
        }
    }
}
