using System.Collections.Generic;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

/// <summary>
/// Placement for the training scenario: a single-owner, tap-driven stage sequence. Only
/// one stage is ever "listening" for a tap at a time. Every stage places on the floor —
/// the fire safety wall (a self-contained prefab: concrete-wall backdrop + manual call
/// point + the full set of extinguisher choices, all pre-mounted) is placed with a floor
/// tap too, the same as the fire itself, rather than requiring the player to find and tap
/// a real vertical wall.
///
/// Every placed object is attached to a real ARAnchor (see CreateAnchor) rather than a
/// bare Transform, so it stays put relative to the room as the device's own tracking
/// keeps refining itself — without an anchor, world-placed AR content visibly drifts/
/// slides as you move, which reads exactly like it's "stuck to the screen" following you.
/// </summary>
[RequireComponent(typeof(ARRaycastManager))]
public class ARPlacementController : MonoBehaviour
{
    private enum Stage { Lesson, WallContent, Fire, Extinguisher, Done }

    [Tooltip("AR-native 'Learn' stage content (auto-playing exhibits — must include a FireSafetyLessonController). Placed on the floor at the first tap. Leave empty to skip.")]
    [SerializeField] private GameObject fireSafetyLessonPrefab;

    [Tooltip("Activate + Assist content, all in one self-contained prefab: a concrete wall backdrop with a manual call point and the full set of extinguisher choices already mounted on it (must include a ManualCallPointController and one or more ExtinguisherPickup descendants). Placed on the floor with a single tap. Leave empty to skip.")]
    [SerializeField] private GameObject fireSafetyWallPrefab;

    [Tooltip("The scenario options placed on the floor at the next floor tap (each must include FireSource, and may include its own ignition-cause dressing) — one is picked at random per attempt so the module isn't locked to a single scenario.")]
    [SerializeField] private GameObject[] firePrefabOptions;

    [Tooltip("Plane manager scanned for taps and whose visualizers get hidden once every stage is placed.")]
    [SerializeField] private ARPlaneManager planeManager;

    [Tooltip("Creates the ARAnchor each placed object is attached to, so it doesn't drift as AR tracking refines. Auto-found on this GameObject if left empty.")]
    [SerializeField] private ARAnchorManager anchorManager;

    private ARRaycastManager raycastManager;
    private FireResponseCoordinator coordinator;
    private readonly List<ARRaycastHit> hits = new List<ARRaycastHit>();
    private readonly List<GameObject> placedExtinguisherOptions = new List<GameObject>();

    private List<Stage> activeStages;
    private int stageIndex;
    private bool planesVisible = true;

    // Sequential campaign: every scenario in firePrefabOptions gets trained exactly once,
    // in a shuffled order fixed for the whole session -- not a fresh random pick each
    // attempt. The floor tap from the first placement is remembered so each later
    // scenario auto-places without asking the player to re-tap the room. The fire safety
    // wall (call point + extinguishers) is placed once for the whole session and never
    // re-placed between scenarios -- see AdvanceToNextScenario.
    private GameObject[] scenarioOrder;
    private int scenarioIndex;
    private Pose lastFloorPose;
    private ARPlane lastFloorPlane;

    public IReadOnlyList<GameObject> PlacedExtinguisherOptions => placedExtinguisherOptions;
    public GameObject ChosenExtinguisher { get; private set; }
    public GameObject PlacedFire { get; private set; }
    public GameObject PlacedLesson { get; private set; }
    public FireSafetyLessonController LessonController { get; private set; }
    public GameObject PlacedFireSafetyWall { get; private set; }
    public ManualCallPointController CallPointController { get; private set; }

    /// <summary>1-based index of the stage currently awaiting a tap or in progress, for a "Step X/Y" HUD.</summary>
    public int CurrentStageNumber => stageIndex + 1;

    /// <summary>Total number of stages actually wired on this scene (unassigned prefab fields are skipped).</summary>
    public int TotalStages => activeStages?.Count ?? 0;

    /// <summary>1-based index of the scenario currently being trained, out of TotalScenarios — for "Scenario X/Y" on the results screen.</summary>
    public int ScenarioNumber => scenarioIndex + 1;

    /// <summary>How many scenarios this campaign runs through in total.</summary>
    public int TotalScenarios => scenarioOrder?.Length ?? (firePrefabOptions?.Length ?? 0);

    /// <summary>True once there's a next scenario left to train — drives the results screen's NEXT button.</summary>
    public bool HasNextScenario => scenarioOrder != null && scenarioIndex < scenarioOrder.Length - 1;

    private Stage CurrentStage => activeStages != null && stageIndex < activeStages.Count ? activeStages[stageIndex] : Stage.Done;

    /// <summary>
    /// True when the current stage still needs a tap but ARCore/ARKit hasn't found a
    /// horizontal-up (floor) plane yet — drives a "keep scanning" message instead of a bare
    /// tap prompt that looks stuck with no feedback while the player is still moving the
    /// phone around the room. Every stage places on the floor, so this is a plain floor
    /// check -- no per-stage surface-type branching needed.
    /// </summary>
    public bool IsScanningForSurface
    {
        get
        {
            if (planeManager == null || CurrentStage == Stage.Done || IsCurrentStagePlaced())
            {
                return false;
            }

            foreach (var plane in planeManager.trackables)
            {
                if (plane.alignment == PlaneAlignment.HorizontalUp)
                {
                    return false; // at least one floor plane already exists
                }
            }
            return true;
        }
    }

    private void Awake()
    {
        raycastManager = GetComponent<ARRaycastManager>();
        if (anchorManager == null)
        {
            anchorManager = GetComponent<ARAnchorManager>();
        }
        coordinator = FindFirstObjectByType<FireResponseCoordinator>();
        BuildActiveStages();

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

    /// <summary>
    /// Builds the ordered list of stages this scene actually uses, skipping any whose
    /// prefab field is left unassigned (preserves "leave empty to skip" for scenes that
    /// don't wire every stage). Fire and Extinguisher are always present — there's no
    /// training attempt without them. Extinguisher has no placement tap of its own (it's
    /// placed together with WallContent); it stays as a distinct stage purely so the
    /// "Step X/Y" HUD numbering still counts it as its own step of the attempt.
    /// </summary>
    private void BuildActiveStages()
    {
        activeStages = new List<Stage>();
        if (fireSafetyLessonPrefab != null) activeStages.Add(Stage.Lesson);
        // The fire safety station (wall + alarm + extinguishers) goes down first, exactly
        // as it would already exist in a real room before anything catches fire -- then the
        // fire itself starts on a separate, later floor tap. Placing the fire first (an
        // earlier version of this flow) meant the player had no visual reference for where
        // the fire already was while tapping to place the (much larger) wall panel, so the
        // wall could easily land on top of it and visually swallow it. Placing the wall
        // first also means WallContent's own advance condition only needs "is it placed" --
        // requiring the alarm to be activated before the fire even exists would be
        // backwards; the real "sound the alarm before touching an extinguisher" gate is
        // already enforced independently in ExtinguisherPickup.
        if (fireSafetyWallPrefab != null) activeStages.Add(Stage.WallContent);
        activeStages.Add(Stage.Fire);
        activeStages.Add(Stage.Extinguisher);
        stageIndex = 0;
    }

    /// <summary>
    /// Returns a LocalizationManager key (not display text) for whatever the current stage
    /// still needs placed, or null if the current stage is already placed (and
    /// TrainingInstructionsUI should fall back to its own fine-grained in-stage messages —
    /// pulling the pin, aiming, etc.). Returning a key rather than English text lets the
    /// caller localize it, and doubles as the matching NarrationPlayer clip id for two of
    /// the three prompts (see TrainingInstructionsUI).
    /// </summary>
    public string CurrentPlacementPrompt()
    {
        switch (CurrentStage)
        {
            case Stage.Lesson: return PlacedLesson == null ? "prompt_lesson" : null;
            case Stage.WallContent: return PlacedFireSafetyWall == null ? "prompt_place_wall_content" : null;
            case Stage.Fire: return PlacedFire == null ? "prompt_place_fire" : null;
            default: return null;
        }
    }

    private void Update()
    {
        AdvanceStageIfCurrentComplete();

        if (CurrentStage == Stage.Done)
        {
            return;
        }

        if (!IsCurrentStagePlaced() && ArTouchInput.TryGetTapPosition(out Vector2 screenPos))
        {
            TryPlaceCurrentStage(screenPos);
        }
    }

    private bool IsCurrentStagePlaced()
    {
        switch (CurrentStage)
        {
            case Stage.Lesson: return PlacedLesson != null;
            case Stage.WallContent: return PlacedFireSafetyWall != null;
            case Stage.Fire: return PlacedFire != null;
            case Stage.Extinguisher: return placedExtinguisherOptions.Count > 0;
            default: return true;
        }
    }

    /// <summary>
    /// Moves to the next stage once the current one's own completion condition is met —
    /// the same per-stage "done" flags already used for scoring, just also driving when
    /// the next tap prompt appears.
    /// </summary>
    private void AdvanceStageIfCurrentComplete()
    {
        if (activeStages == null || stageIndex >= activeStages.Count)
        {
            return;
        }

        bool done;
        switch (CurrentStage)
        {
            case Stage.Lesson:
                done = LessonController != null && LessonController.IsComplete;
                break;
            case Stage.WallContent:
                done = PlacedFireSafetyWall != null;
                break;
            case Stage.Fire:
                done = PlacedFire != null;
                break;
            case Stage.Extinguisher:
                done = coordinator != null && coordinator.ExtinguisherStageDone;
                break;
            default:
                done = false;
                break;
        }

        if (!done)
        {
            return;
        }

        stageIndex++;
        if (CurrentStage == Stage.Done)
        {
            SetPlanesVisible(false);
        }
    }

    private void TryPlaceCurrentStage(Vector2 screenPos)
    {
        if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinPolygon))
        {
            // A tap can land just outside the currently-tracked polygon edge — very
            // common while a plane is still growing, especially vertical ones — so
            // fall back to the plane's tracked bounds before giving up on this tap.
            if (!raycastManager.Raycast(screenPos, hits, TrackableType.PlaneWithinBounds))
            {
                return;
            }
        }

        ARPlane plane = hits[0].trackable as ARPlane;
        if (plane == null)
        {
            return;
        }

        if (plane.alignment != PlaneAlignment.HorizontalUp)
        {
            return;
        }

        Pose hitPose = hits[0].pose;

        switch (CurrentStage)
        {
            case Stage.Lesson:
                PlaceLessonAt(hitPose, plane);
                break;
            case Stage.WallContent:
                PlaceFireSafetyWallAt(hitPose, plane);
                break;
            case Stage.Fire:
                PlaceFireAt(hitPose, plane);
                break;
        }

        // Drop the scanning overlay the moment every tap-placed artifact actually exists,
        // not when the whole attempt finishes (Extinguisher's own "done" flag only flips
        // once the fire is fully out -- that's minutes away, and leaves the blue plane
        // overlay cluttering the view through the entire fight). WallContent's own stage
        // "done" condition is deliberately "alarm pressed" (see AdvanceStageIfCurrentComplete)
        // for HUD/scoring purposes, which is a different question from "has everything been
        // placed" -- checked separately here.
        if (AllPlacementTapsComplete())
        {
            SetPlanesVisible(false);
        }
    }

    /// <summary>
    /// True once every stage that places something via a tap has actually placed it --
    /// independent of whether that content has since been used/activated (WallContent's
    /// own stage-advancement condition waits for the alarm to be pressed, not just mounted;
    /// see AdvanceStageIfCurrentComplete). Drives when the blue scanning overlay disappears.
    /// </summary>
    private bool AllPlacementTapsComplete()
    {
        if (fireSafetyLessonPrefab != null && PlacedLesson == null) return false;
        if (PlacedFire == null) return false;
        if (fireSafetyWallPrefab != null && PlacedFireSafetyWall == null) return false;
        return true;
    }

    /// <summary>
    /// Creates a real ARAnchor at the given pose (attached to the plane it was tapped on,
    /// so it tracks the same refinements the plane itself receives) and returns its
    /// transform — parent placed content under this instead of leaving it as a bare
    /// scene-root object at a static Transform, or it will visibly drift as the device's
    /// tracking keeps refining its understanding of the room. Falls back to a plain
    /// Transform (old behavior) if no ARAnchorManager is wired, or if the platform has no
    /// anchor subsystem available (e.g. testing in the Editor without a live AR session,
    /// where AttachAnchor throws rather than returning null) — placement still works
    /// rather than the whole attempt breaking.
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

    private void PlaceLessonAt(Pose pose, ARPlane floor)
    {
        Transform anchor = CreateAnchor(pose, floor, "LessonAnchor");
        PlacedLesson = Instantiate(fireSafetyLessonPrefab, Vector3.zero, Quaternion.identity, anchor);
        PlacedLesson.transform.localPosition = Vector3.zero;
        PlacedLesson.transform.localRotation = Quaternion.identity;
        LessonController = PlacedLesson.GetComponentInChildren<FireSafetyLessonController>(true);
    }

    /// <summary>
    /// Places the whole fire safety wall — concrete-wall backdrop, manual call point, and
    /// the full fixed set of extinguisher choices, all pre-mounted as children of one
    /// prefab — with a single floor tap (same pattern as PlaceLessonAt/PlaceFireAt).
    /// Replaces the old two-step "place the call point, then scatter extinguisher options
    /// along the tapped wall" flow: since correctness is judged per-attempt by comparing
    /// the fire's class against each extinguisher's own rating (not by which physical props
    /// exist), one fixed wall design serves every scenario in the campaign.
    /// </summary>
    private void PlaceFireSafetyWallAt(Pose pose, ARPlane floor)
    {
        Transform anchor = CreateAnchor(pose, floor, "FireSafetyWallAnchor");
        PlacedFireSafetyWall = Instantiate(fireSafetyWallPrefab, Vector3.zero, Quaternion.identity, anchor);
        PlacedFireSafetyWall.transform.localPosition = Vector3.zero;

        // Unlike a real wall (whose facing direction is defined by the physical wall's own
        // plane), a floor tap's hit pose carries no meaningful "which way should this face"
        // information -- its rotation just aligns up with the plane's normal, leaving the
        // yaw around that axis arbitrary. Face the wall's front (+Z, where the call point and
        // extinguishers are mounted) toward whoever just tapped the floor to place it, so it
        // isn't a coin flip whether the player sees the front or the bare back of the wall.
        Camera arCamera = Camera.main;
        if (arCamera != null)
        {
            Vector3 towardCamera = arCamera.transform.position - PlacedFireSafetyWall.transform.position;
            towardCamera.y = 0f;
            if (towardCamera.sqrMagnitude > 0.0001f)
            {
                PlacedFireSafetyWall.transform.rotation = Quaternion.LookRotation(towardCamera.normalized, Vector3.up);
            }
            else
            {
                PlacedFireSafetyWall.transform.localRotation = Quaternion.identity;
            }
        }
        else
        {
            PlacedFireSafetyWall.transform.localRotation = Quaternion.identity;
        }

        CallPointController = PlacedFireSafetyWall.GetComponentInChildren<ManualCallPointController>(true);
        coordinator?.ConfigureActivateStage(CallPointController);

        FireSource fireSource = PlacedFire != null ? PlacedFire.GetComponent<FireSource>() : null;

        foreach (var pickup in PlacedFireSafetyWall.GetComponentsInChildren<ExtinguisherPickup>(true))
        {
            // Must run after the localPosition/localRotation fix-up above -- see
            // CaptureRestPose's own comment for why Awake()'s own snapshot can't be
            // trusted as the "put it back here" pose for a rejected pickup.
            pickup.CaptureRestPose();
            pickup.SetRequiredCallPoint(CallPointController);

            var tracker = pickup.GetComponent<PassChecklistTracker>();
            if (fireSource != null)
            {
                pickup.SetTargetFire(fireSource);
                if (tracker != null)
                {
                    tracker.SetTargetFire(fireSource);
                }
            }

            GameObject instance = pickup.gameObject;
            pickup.OnPickedUp.AddListener(() => HandleExtinguisherChosen(instance));
            pickup.OnPickupBlockedAlarmNotActive.AddListener(() => coordinator?.OnPickupBlockedAlarmNotActive?.Invoke());
            pickup.OnPickupBlockedWrongExtinguisher.AddListener(() => coordinator?.OnWrongExtinguisherWarning?.Invoke());

            placedExtinguisherOptions.Add(instance);
        }
    }

    /// <summary>
    /// Builds the fixed, shuffled-once order every scenario in firePrefabOptions is
    /// trained in across the campaign, the first time it's needed. Shuffled (not just
    /// firePrefabOptions' own order) so which scenario comes first still varies between
    /// sessions, while every scenario still gets trained exactly once per campaign.
    /// </summary>
    private void EnsureScenarioOrder()
    {
        if (scenarioOrder != null)
        {
            return;
        }

        if (firePrefabOptions == null || firePrefabOptions.Length == 0)
        {
            scenarioOrder = new GameObject[0];
            return;
        }

        scenarioOrder = (GameObject[])firePrefabOptions.Clone();
        for (int i = scenarioOrder.Length - 1; i > 0; i--)
        {
            int j = Random.Range(0, i + 1);
            (scenarioOrder[i], scenarioOrder[j]) = (scenarioOrder[j], scenarioOrder[i]);
        }
    }

    private void PlaceFireAt(Pose pose, ARPlane floor)
    {
        EnsureScenarioOrder();
        if (scenarioOrder.Length == 0)
        {
            return;
        }

        // Remembered so AdvanceToNextScenario can re-place each later scenario at the same
        // floor spot without asking the player to re-tap the room.
        lastFloorPose = pose;
        lastFloorPlane = floor;

        GameObject chosen = scenarioOrder[scenarioIndex];
        if (chosen == null)
        {
            return;
        }

        Transform anchor = CreateAnchor(pose, floor, "FireAnchor");
        PlacedFire = Instantiate(chosen, Vector3.zero, Quaternion.identity, anchor);
        PlacedFire.transform.localPosition = Vector3.zero;
        PlacedFire.transform.localRotation = Quaternion.identity;

        // The fire safety wall (including the extinguishers) is placed before the fire ever
        // exists (see BuildActiveStages) — wire every already-placed extinguisher's target
        // now that it does.
        var placedFireSource = PlacedFire.GetComponent<FireSource>();
        if (placedFireSource != null)
        {
            foreach (var option in placedExtinguisherOptions)
            {
                if (option == null)
                {
                    continue;
                }

                var pickup = option.GetComponent<ExtinguisherPickup>();
                if (pickup != null)
                {
                    pickup.SetTargetFire(placedFireSource);
                }

                var tracker = option.GetComponent<PassChecklistTracker>();
                if (tracker != null)
                {
                    tracker.SetTargetFire(placedFireSource);
                }
            }
        }

        // Frame whichever scenario actually got picked -- the alert modal only knows the
        // right wording (not always "trash can") once the fire exists to ask it.
        var alertIntro = FindFirstObjectByType<FireAlertIntro>();
        if (alertIntro != null && placedFireSource != null)
        {
            alertIntro.Show(placedFireSource.scenarioNarrationId);
        }
    }

    /// <summary>
    /// Called once, the moment the player picks up an extinguisher option — reached only
    /// for a correctly-rated pickup now, since ExtinguisherPickup itself (see
    /// SetRequiredCallPoint/SetTargetFire) rejects a wrong-class tap before OnPickedUp ever
    /// fires. The CanExtinguish/UndoPickup check below is kept as a harmless defensive
    /// fallback in case a pickup was ever left unwired, not something normal play should
    /// reach. Wires the chosen option's tracker into the module coordinator and disables
    /// pickup on the other options (no back-out/reselect once genuinely committed).
    /// </summary>
    private void HandleExtinguisherChosen(GameObject chosen)
    {
        if (ChosenExtinguisher != null || chosen == null)
        {
            return;
        }

        var pickup = chosen.GetComponent<ExtinguisherPickup>();
        var identity = chosen.GetComponentInChildren<ExtinguisherIdentity>();
        var fireSource = PlacedFire != null ? PlacedFire.GetComponent<FireSource>() : null;

        var trackerForFire = chosen.GetComponent<PassChecklistTracker>();
        if (trackerForFire != null && fireSource != null)
        {
            trackerForFire.SetTargetFire(fireSource);
        }

        if (identity != null && fireSource != null && !identity.CanExtinguish(fireSource.fireClass))
        {
            pickup?.UndoPickup();
            coordinator?.OnWrongExtinguisherWarning?.Invoke();
            return;
        }

        ChosenExtinguisher = chosen;

        foreach (var option in placedExtinguisherOptions)
        {
            if (option == null || option == chosen)
            {
                continue;
            }

            var otherPickup = option.GetComponent<ExtinguisherPickup>();
            if (otherPickup != null)
            {
                otherPickup.enabled = false;
            }
        }

        var tracker = chosen.GetComponent<PassChecklistTracker>();

        if (coordinator != null && tracker != null)
        {
            coordinator.ConfigureExtinguisher(tracker);
        }

        var resultsUI = FindFirstObjectByType<TrainingResultsUI>();
        if (resultsUI != null && coordinator != null)
        {
            resultsUI.SetCoordinator(coordinator);
        }

        var resultsModal = FindFirstObjectByType<FireResultsModal>();
        if (resultsModal != null && coordinator != null)
        {
            resultsModal.SetCoordinator(coordinator);
        }
    }

    /// <summary>
    /// Clears the current placement, resets the stage sequence to the start, and
    /// re-enables plane detection so the player can place again — used by the Retry
    /// button. Deliberately does NOT reload the scene: destroying/recreating the AR
    /// Session/XR Origin on every retry is what caused "wall no longer detected after
    /// retry" — planes already being tracked stay tracked, so placement works
    /// immediately instead of needing the camera to re-scan the room from scratch.
    /// </summary>
    public void ResetTraining()
    {
        // Every extinguisher is a fixed child of the one PlacedFireSafetyWall instance now
        // (not individually anchored), so destroying that instance below cleans up every
        // still-mounted extinguisher automatically. The one exception is a CURRENTLY HELD
        // extinguisher: ExtinguisherPickup.PickUp reparents it to the AR camera, so at that
        // point it's no longer a child of the wall at all and needs destroying directly --
        // never via transform.parent, which would take the actual AR camera down with it.
        foreach (var option in placedExtinguisherOptions)
        {
            if (option == null)
            {
                continue;
            }
            var pickup = option.GetComponent<ExtinguisherPickup>();
            if (pickup != null && pickup.IsHeld)
            {
                Destroy(option);
            }
        }
        placedExtinguisherOptions.Clear();
        ChosenExtinguisher = null;

        if (PlacedFire != null)
        {
            Destroy(PlacedFire.transform.parent != null ? PlacedFire.transform.parent.gameObject : PlacedFire);
            PlacedFire = null;
        }

        if (PlacedLesson != null)
        {
            Destroy(PlacedLesson.transform.parent != null ? PlacedLesson.transform.parent.gameObject : PlacedLesson);
            PlacedLesson = null;
            LessonController = null;
        }

        if (PlacedFireSafetyWall != null)
        {
            Destroy(PlacedFireSafetyWall.transform.parent != null ? PlacedFireSafetyWall.transform.parent.gameObject : PlacedFireSafetyWall);
            PlacedFireSafetyWall = null;
            CallPointController = null;
        }

        stageIndex = 0;
        SetPlanesVisible(true);
    }

    /// <summary>
    /// Moves the campaign on to the next scenario in sequence, called by the results
    /// screen's NEXT button. Replaces the fire, but the fire safety wall (call point +
    /// extinguishers) stays mounted for the whole session rather than being destroyed and
    /// re-placed — instead each extinguisher is reset in place (put back if held, and
    /// re-enabled if it had been disabled by a prior commit), the new fire's class is
    /// rewired onto all of them, and the alarm is re-armed so raising it is required again
    /// each scenario, reinforcing the habit every round.
    /// </summary>
    public void AdvanceToNextScenario()
    {
        if (!HasNextScenario)
        {
            return;
        }

        scenarioIndex++;

        foreach (var option in placedExtinguisherOptions)
        {
            if (option == null)
            {
                continue;
            }

            var pickup = option.GetComponent<ExtinguisherPickup>();
            if (pickup != null)
            {
                if (pickup.IsHeld)
                {
                    pickup.UndoPickup();
                }
                pickup.enabled = true;
            }

            var tracker = option.GetComponent<PassChecklistTracker>();
            if (tracker != null)
            {
                tracker.ResetForNewAttempt();
            }
        }
        ChosenExtinguisher = null;

        if (CallPointController != null)
        {
            CallPointController.ResetActivation();
            // Also clears the coordinator's own ActivateStageDone flag, which
            // ResetActivation (a call-point-only concern) doesn't touch -- without this
            // it stays stuck true from the previous scenario forever.
            coordinator?.ConfigureActivateStage(CallPointController);
        }

        if (PlacedFire != null)
        {
            Destroy(PlacedFire.transform.parent != null ? PlacedFire.transform.parent.gameObject : PlacedFire);
            PlacedFire = null;
        }

        if (lastFloorPlane != null)
        {
            PlaceFireAt(lastFloorPose, lastFloorPlane);
        }
    }

    /// <summary>
    /// Toggles the blue scanning-overlay visuals only -- deliberately never touches
    /// planeManager.enabled. Disabling an ARPlaneManager calls SubsystemLifecycleManager.
    /// OnDisable(), which stops the underlying native plane-tracking subsystem outright;
    /// re-enabling it restarts that subsystem asynchronously and can require the device to
    /// redetect planes from scratch. That's exactly what broke Retry after the overlay
    /// started hiding itself right after placement instead of only at the very end of the
    /// whole attempt: hitting Retry mid-session re-enabled the manager, but tracking hadn't
    /// necessarily come back yet, so the very next tap had no plane to raycast against and
    /// looked like Retry "did nothing." Hiding the plane GameObjects' renderers is enough
    /// to make the overlay disappear without ever touching tracking itself.
    /// </summary>
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

    /// <summary>
    /// Keeps newly-detected planes in sync with the last SetPlanesVisible call -- without
    /// this, a plane discovered while the overlay is hidden (e.g. late in a scan, or after
    /// AllPlacementTapsComplete already fired) would default to visible and the blue
    /// overlay would unexpectedly pop back in for just that one plane.
    /// </summary>
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
