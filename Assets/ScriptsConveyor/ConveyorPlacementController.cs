using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Placement for the conveyor training module: a single tap-to-place object on a floor
/// plane, unlike ARPlacementController's multi-stage fire/wall/extinguisher sequence --
/// there's only ever one prefab to place here. Same anchor-with-fallback and
/// raycast-with-fallback mechanics as ARPlacementController, simplified to one stage.
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public class ConveyorPlacementController : MonoBehaviour
{
    [Tooltip("The conveyor scenario prefab (must include ConveyorMotionController).")]
    [SerializeField] private GameObject conveyorPrefab;

    [Tooltip("Plane manager scanned for the placement tap and whose visualizers get hidden once placed.")]
    [SerializeField] private ARPlaneManager planeManager;

    [Tooltip("Creates the ARAnchor the conveyor is attached to, so it doesn't drift as AR tracking refines. Auto-found on this GameObject if left empty.")]
    [SerializeField] private ARAnchorManager anchorManager;

    [Tooltip("Uniform scale applied to the placed conveyor (the prefab at 1.0 is ~2.5m long and ~2m tall).")]
    [SerializeField] private float placedScale = 0.8f;

    private ARRaycastManager raycastManager;
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private float placedAtTime;
    private bool planesVisible = true;

    public GameObject PlacedConveyor { get; private set; }
    public ConveyorMotionController MotionController { get; private set; }

    /// <summary>Seconds since the conveyor was placed, for the HUD toolbar's timer. 0 before placement.</summary>
    public float ElapsedSeconds => PlacedConveyor != null ? Time.time - placedAtTime : 0f;

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        if (anchorManager == null)
        {
            anchorManager = GetComponent<ARAnchorManager>();
        }

        if (planeManager != null)
        {
            planeManager.trackablesChanged.AddListener(HandlePlanesChanged);
        }
    }

    private void OnDestroy()
    {
        if (planeManager != null)
        {
            planeManager.trackablesChanged.RemoveListener(HandlePlanesChanged);
        }
    }

    private void Update()
    {
        if (PlacedConveyor != null)
        {
            return;
        }

        if (ArTouchInput.TryGetTapPosition(out Vector2 screenPos))
        {
            TryPlace(screenPos);
        }
    }

    private void TryPlace(Vector2 screenPos)
    {
        if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
        {
            // A tap can land just outside the currently-tracked polygon edge while the
            // plane is still growing -- fall back to the plane's tracked bounds.
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinBounds))
            {
                return;
            }
        }

        ARPlane plane = hits[0].trackable as ARPlane;
        if (plane == null || plane.alignment != PlaneAlignment.HorizontalUp)
        {
            return;
        }

        PlaceConveyorAt(hits[0].pose, plane);
    }

    /// <summary>
    /// Creates a real ARAnchor at the given pose so the placed conveyor doesn't drift as AR
    /// tracking refines, falling back to a plain Transform if no ARAnchorManager is wired or
    /// the platform has no anchor subsystem (e.g. Editor testing without a live AR session,
    /// where AttachAnchor throws instead of returning null).
    /// </summary>
    private Transform CreateAnchor(Pose pose, ARPlane plane, string name)
    {
        if (anchorManager != null)
        {
            try
            {
                ARAnchor anchor = anchorManager.AttachAnchor(plane, pose);
                if (anchor != null)
                {
                    anchor.gameObject.name = name;
                    return anchor.transform;
                }
            }
            catch (System.InvalidOperationException)
            {
                // Fall through to the plain-Transform fallback below.
            }
        }

        var fallback = new GameObject(name);
        fallback.transform.SetPositionAndRotation(pose.position, pose.rotation);
        return fallback.transform;
    }

    private void PlaceConveyorAt(Pose pose, ARPlane floor)
    {
        if (conveyorPrefab == null)
        {
            return;
        }

        Transform anchor = CreateAnchor(pose, floor, "ConveyorAnchor");
        PlacedConveyor = Instantiate(conveyorPrefab, Vector3.zero, Quaternion.identity, anchor);
        PlacedConveyor.transform.localPosition = Vector3.zero;
        PlacedConveyor.transform.localRotation = Quaternion.identity;
        PlacedConveyor.transform.localScale = Vector3.one * placedScale;

        MotionController = PlacedConveyor.GetComponentInChildren<ConveyorMotionController>(true);
        placedAtTime = Time.time;

        SetPlanesVisible(false);
    }

    /// <summary>
    /// Clears the current placement and re-enables plane visuals so the player can place
    /// again -- used by the Retry/Reposition button. Deliberately does NOT reload the scene:
    /// destroying/recreating the AR Session would drop already-tracked planes.
    /// </summary>
    public void ResetTraining()
    {
        if (PlacedConveyor != null)
        {
            Transform parent = PlacedConveyor.transform.parent;
            Destroy(parent != null ? parent.gameObject : PlacedConveyor);
            PlacedConveyor = null;
            MotionController = null;
        }

        SetPlanesVisible(true);
    }

    // Hides the overlay only; planeManager stays enabled (see ARPlacementController.SetPlanesVisible).
    private void SetPlanesVisible(bool visible)
    {
        planesVisible = visible;

        if (planeManager == null)
        {
            return;
        }

        foreach (var plane in planeManager.trackables)
        {
            plane.gameObject.SetActive(visible);
        }
    }

    /// <summary>Keeps planes detected after placement hidden too, so the overlay doesn't reappear around the conveyor.</summary>
    private void HandlePlanesChanged(ARTrackablesChangedEventArgs<ARPlane> args)
    {
        if (planesVisible)
        {
            return;
        }

        foreach (var plane in args.added)
        {
            plane.gameObject.SetActive(false);
        }
    }
}
