# Editor-only steps — AR Fire Safety Module

**Status: steps 1–7 below are done** (completed via a live `unity-mcp` session — XR Plug-in
Management, VR-remnant cleanup, `ARRig.prefab`, `Fire_A/ABC/BC.prefab`, and scene wiring for
`Tipo_A`/`Tipo_ABC`/`Tipo_BC`/`Main`/`Tutorial` all exist and compile clean). **Also done**: the
exit-identification + evacuation-sequencing extension — `Assets/Prefabs/EvacuationRoute.prefab`
(3 `ExitSign`s + 4 `EvacuationWaypoint`s, root has `ExitIdentificationController` +
`EvacuationSequenceController`), `FireResponseCoordinator` added to `ARRig.prefab`'s `ResultsHUD`,
`ARPlacementController.evacuationRoutePrefab` wired to the new prefab (inherited automatically by
every scene's `ARRig` instance — verified on `Tipo_A.unity`), and the results panel extended with
`ExitLine`/`EvacuationLine` TMP rows. See "Exit identification + evacuation sequencing" below for
what's still manual polish.

The scripts and text-file changes for this module are done (see `Assets/ScriptsFireExtinguisher/`,
the updated `Assets/Scripts/FireExtinguisherController.cs` + new `MenuManager.cs`, and this repo's
`README.md`). The following steps needed a **live Unity Editor** (or a live `unity-mcp` session) —
they involve scene/prefab binary-ish data that can't be safely hand-edited as raw YAML for changes
this extensive.

## 1. XR Plug-in Management (5 min)
`Edit > Project Settings > XR Plug-in Management > Android tab` → check **ARCore**.

## 2. URP AR Camera Background renderer feature (5 min)
Open the renderer asset actually assigned to your active URP asset under `Assets/Settings/`
(check `Edit > Project Settings > Graphics` for which URP asset is active, then its
`m_RendererData` for which `-Renderer.asset` that points to). `Add Renderer Feature` →
**AR Background Rendering** (provided by AR Foundation).

## 3. Clean up broken VR remnants
- Open `Assets/Prefabs/FE_Yellow.prefab`, `FE_Red.prefab`, `FE_Grey.prefab`,
  `FE_Yellow_Trial.prefab`, `Extinguisher_Yellow.prefab`, `Extinguisher_Red.prefab`,
  `Extinguisher_Grey.prefab` — each will show **Missing Script** warnings on several child
  objects (search "Missing" in the hierarchy, or select the prefab root and look for the warning
  icon). Remove those missing-script `Grabbable`/`PhysicsGrabbable`/`HandGrabInteractable`(+
  `_mirror`/`_Nozzle`)/`HandGrabPose`/`UseInteractable`(+`_mirror`) components. Keep the mesh/
  particle hierarchy.
- In `MainMenu.unity`, `Tutorial.unity`, `Tipo_ABC.unity`, `Tipo_BC.unity`: find UnityEvent
  listeners pointing at `Oculus.Interaction.AudioTrigger`/`Oculus.Interaction.Samples.SceneLoader`
  (Console will show "referenced script... is missing" or the OnClick/UnityEvent list will show a
  broken entry) and remove those listener entries.
- In `MainMenu.unity`: delete the leftover **ExamplesMenu** 3D poke-button GameObject entirely
  (its "Iniciar"/"Practica"/etc. buttons point at nonexistent SDK-sample scenes; the new
  `MenuManager` button wiring in step 6 replaces it).
- Delete `Assets/Resources/OculusPlatformSettings.asset`, `OculusRuntimeSettings.asset`,
  `OVRPlatformToolSettings.asset`, `MetaXRAudioSettings.asset` (no package left to use them).
- In `Tipo_ABC.unity` and `Tipo_BC.unity`: delete the Meta Quest `[BuildingBlock] Camera Rig`
  (`CenterEyeAnchor`, controller/hand anchors, `TrackingSpace`, `TrackerAnchor`).

## 4. Build `Assets/Prefabs/ARRig.prefab`
New empty scene (or a scratch GameObject), build:
- `XR Origin` (`GameObject > XR > XR Origin (AR)` creates this pre-wired: XR Origin + AR Camera
  Manager + AR Camera Background + Tracked Pose Driver on the child Camera).
- `AR Session` (`GameObject > XR > AR Session`).
- On the XR Origin: add **AR Plane Manager** (assign a plane-visualizer prefab — Unity's AR
  Foundation samples ship a basic one, or make a simple transparent quad) and **AR Raycast
  Manager**.
- Add an `ARPlacementController` component (new script) somewhere in the rig (e.g. on the XR
  Origin) — it requires an `ARRaycastManager` on the same object (it already is, per above).
  Assign its `trainingRigPrefab` field once step 5 exists, and its `planeManager` field to the AR
  Plane Manager above.
- Drag the whole hierarchy into `Assets/Prefabs/` as `ARRig.prefab`.

## 5. Build the placeable "training rig" prefab (per fire class)
For each fire class (A / ABC / BC), make one prefab that's the actual placed object:
container GameObject with:
- The matching `FE_Grey`/`FE_Red`/`FE_Yellow` extinguisher instance (per the plan's assumption:
  Grey=A, Red=ABC, Yellow=BC — change if you have a different real mapping).
  - Add `ExtinguisherIdentity` next to `FireExtinguisherController` on its spray ParticleSystem
    object, set `rating` accordingly.
  - Add a small pin child object (simple ring/cylinder mesh) with a `Collider` + `ExtinguisherPin`.
  - Add `ExtinguisherAimController` on the nozzle/hose child transform.
  - Add an on-screen UI Button (Canvas, screen-space overlay) with `ExtinguisherTrigger`, wiring
    its `pin`/`spray`/`aimController` fields.
- A fire VFX instance (from `Assets/Vefects/Free Fire VFX URP/Particles/`) tagged `Fire A`/
  `Fire ABC`/`Fire BC` to match, with `FireSource` added and `fireClass` set to match.
- A `PassChecklistTracker` with all references wired (pin/aim/trigger/extinguisherController/
  targetFire).
- A `TrainingResultsUI` panel (Canvas, reuse `MainMenu`'s button/panel visual style) with its
  `tracker` field wired, TMP text fields assigned, Retry/Back buttons wired.

Save each as `Assets/Prefabs/TrainingRig_A.prefab` / `_ABC.prefab` / `_BC.prefab` (or one prefab
with a fire-class field if you'd rather parameterize it — the scripts don't require 3 separate
prefabs, that's just the simplest to wire by hand).

## 6. Wire scenes
- `Main.unity`, `Tutorial.unity`, `Tipo_A.unity`, `Tipo_ABC.unity`, `Tipo_BC.unity`: replace
  whatever camera exists (none / plain Camera / old Meta rig) with an instance of `ARRig.prefab`.
  Assign each scene's `ARPlacementController.trainingRigPrefab` to the matching
  `TrainingRig_*.prefab` from step 5 (`Tipo_A` → `_A`, `Tipo_ABC` → `_ABC`, `Tipo_BC` → `_BC`;
  `Main`/`Tutorial` → whichever fits that scene's purpose).
- `MainMenu.unity`: wire the real Canvas buttons' `OnClick()` (currently empty):
  - `ButtonTutorial` → `MenuManager.LoadTutorial()`
  - `ButtonPractica` → `MenuManager.LoadPractice()` (or directly show a 3-button fire-class
    picker panel with `MenuManager.LoadFireType("Tipo_A"|"Tipo_ABC"|"Tipo_BC")` per button)
  - `ButtonCreditos` → `MenuManager.LoadCredits()`
  - `ButtonSalir` → `MenuManager.Quit()`
  - `ButtonOpciones` → wire once an options panel exists; leave unwired for now otherwise.

## 7. Verify (see plan's Verification section for full detail)
- Batchmode/Editor compile: zero `error CS` in the Console.
- Play Mode with XR Simulation (`Assets/XR/UserSimulationSettings/`) in `Main.unity`: AR Session
  reaches Tracking, simulated planes appear.
- Tap a plane → rig places → drag nozzle to aim → tap-hold spray button → pin/aim/squeeze/sweep
  all register in `PassChecklistTracker` → `TrainingResultsUI` shows correct pass/fail.
- Test all 9 extinguisher×fire class combinations for correct pass/fail + safety-violation
  behavior per `ExtinguisherIdentity.CanExtinguish`.
- MainMenu navigation end-to-end.
- Final: Build & Run on an Android/ARCore device.

## 8. Exit identification + evacuation sequencing — done, remaining polish

Done via `unity-mcp`: `Assets/Prefabs/EvacuationRoute.prefab` (`Exit A`/`B`/`C` — A is the correct,
unobstructed exit — plus `Waypoint 0`/`1`/`2`/`Assembly Point`, ordered 0–3); `ARRig.prefab`'s
`ResultsHUD` now carries `FireResponseCoordinator`, wired to `TrainingResultsUI`'s new `coordinator`
field; `ARPlacementController.evacuationRoutePrefab` set on the base `ARRig.prefab` (every scene's
`ARRig` instance inherits it — confirmed on `Tipo_A.unity`). `ARPlacementController` places the
route ~2.5m from the fire, offset along the extinguisher→fire direction, once the extinguisher
stage completes.

Left for manual polish (none block a working play-through):
- **Visual distinction**: exit signs and waypoints currently use default primitives/materials with
  no color coding. Give `Exit A` a green-ish material and `Exit B`/`Exit C` a red-ish one, and give
  waypoints a distinct emissive/glow material so they read clearly through the AR camera feed.
- **Real sign/label text**: `ExitSign.label` values ("Exit A - clear" etc.) aren't rendered as
  visible 3D text yet — either add a `TextMeshPro` (3D) child to each sign, or drop the idea and
  rely on color coding alone.
- **Per-scene tuning**: `evacuationRouteOffsetDistance` (2.5m) and the route's local layout (spread
  across ~1.5m width, ~7.5m depth) are reasonable defaults but assume a fairly open room — adjust
  per scene if a fire/extinguisher's usual placement area is tighter.
- **Playtest**: run the verification steps above through the new stage too — extinguish the fire,
  confirm the route appears, tap `Exit B` or `Exit C` (see the "blocked" feedback, stage doesn't
  advance), tap `Exit A`, walk to each waypoint in order (only the current one should be visible),
  reach `Assembly Point`, confirm `TrainingResultsUI` shows all 7 checklist lines + correct overall
  PASS/FAIL (a wrong-exit tap should force FAIL even if everything else was done correctly).

## 9. Worker login + offline progress sync

Already done: `Login.unity` (build index 0), Logout button and sync status on `MainMenu`, and
`FireResponseCoordinator` now saves every completed attempt through `ProgressStore`.

- **Supabase**: follow `dashboard/README.md` to create the project and run
  `supabase/migrations/0001_init.sql`, then select `Assets/Resources/SupabaseConfig.asset` and fill
  in `Project Url` and `Anon Key`. Until this is filled in, login only works for workers already
  cached on the device, and progress stays queued locally.
- **Santali**: the `login_*`, `menu_logout` and `sync_*` keys still need translating in
  `Resources/Localization/sat.json`. They fall back to English for now.
- **Other modules** (conveyor, hazard inspection): when they get a scored result, call
  `ProgressStore.Record(module, scenario, passed, score, elapsedSeconds, detailsJson)` and the
  result syncs and appears on the dashboard automatically.
