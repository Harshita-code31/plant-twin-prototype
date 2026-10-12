using System.Collections;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// A tappable hotspot standing in for a physical Start/Stop button that doesn't exist as its
/// own sub-object in the source CAD assembly -- the control box's button cluster is baked
/// into one fused mesh (Control Box 200x300x155 Rittal), so this component lives on a small
/// invisible child GameObject with its own BoxCollider positioned over the button's visual
/// location instead. Same Collider.Raycast + ArTouchInput tap pattern as
/// ManualCallPointController, except (unlike a one-shot pin/call point) a button must stay
/// re-pressable for the whole attempt.
/// </summary>
public class ConveyorControlButton : MonoBehaviour
{
    public enum Role { Start, Stop, EStop, ModeToggle, SpeedDial }

    [SerializeField] private Role role;

    [Tooltip("Camera used to raycast from screen taps. Defaults to Camera.main if left empty.")]
    [SerializeField] private Camera raycastCamera;

    [Tooltip("Optional visual (e.g. a small highlight quad) flashed briefly on press, to make an otherwise invisible hotspot discoverable.")]
    [SerializeField] private GameObject affordanceVisual;

    [SerializeField] private float affordanceFlashSeconds = 0.15f;

    [SerializeField] private AudioClip pressClip;

    public UnityEvent OnPressed;

    public Role ButtonRole => role;

    private Collider hotspotCollider;
    private AudioSource audioSource;
    private Coroutine flashRoutine;

    private void Awake()
    {
        hotspotCollider = GetComponent<Collider>();

        audioSource = GetComponent<AudioSource>();
        if (audioSource == null)
        {
            audioSource = gameObject.AddComponent<AudioSource>();
        }
        audioSource.playOnAwake = false;
        audioSource.spatialBlend = 1f;

        if (affordanceVisual != null)
        {
            affordanceVisual.SetActive(false);
        }
    }

    private void Update()
    {
        if (hotspotCollider == null)
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
        if (hotspotCollider.Raycast(ray, out RaycastHit hit, float.PositiveInfinity))
        {
            HandlePressed();
        }
    }

    private void HandlePressed()
    {
        if (pressClip != null)
        {
            audioSource.clip = pressClip;
            audioSource.loop = false;
            audioSource.Play();
        }

        if (affordanceVisual != null)
        {
            if (flashRoutine != null)
            {
                StopCoroutine(flashRoutine);
            }
            flashRoutine = StartCoroutine(FlashAffordance());
        }

        OnPressed?.Invoke();
    }

    private IEnumerator FlashAffordance()
    {
        affordanceVisual.SetActive(true);
        yield return new WaitForSeconds(affordanceFlashSeconds);
        affordanceVisual.SetActive(false);
        flashRoutine = null;
    }
}
