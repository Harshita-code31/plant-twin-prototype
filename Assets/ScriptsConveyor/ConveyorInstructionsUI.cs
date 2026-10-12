using TMPro;
using UnityEngine;

/// <summary>
/// Single-line "what to do next" prompt for the Conveyor Belt module -- same per-frame
/// recomputed-from-state role as ChemicalInstructionsUI/TrainingInstructionsUI. Priority
/// order (highest first) so it never says two contradictory things at once: not placed,
/// then the practice tracker's next task, then (free play once the practice is done)
/// E-stopped, then whichever of Auto/Manual is the current mode. A short suffix reports
/// speed (and, only when relevant, Manual mode) without cluttering the main line.
///
/// Also drives narration, like TrainingInstructionsUI: every branch yields a stable id that is
/// both the LocalizationManager key and the NarrationPlayer clip id, and a line is spoken only
/// on the frame that id changes.
/// </summary>
public class ConveyorInstructionsUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Shared AR toolbar chrome (see ArHudToolbar) -- optional, wires this module's timer and reposition action into it if present.")]
    [SerializeField] private ArHudToolbar toolbar;

    [Tooltip("Practice tracker -- while tasks remain, its 'Task n/7' line replaces the free-play prompts below. Auto-found if left empty.")]
    [SerializeField] private ConveyorTrainingTracker tracker;

    private ConveyorPlacementController placementController;
    private ConveyorSpeedDialController speedDial;
    private NarrationPlayer narrationPlayer;
    private string lastNarrationId;

    private void OnEnable()
    {
        placementController = FindFirstObjectByType<ConveyorPlacementController>();
        narrationPlayer = FindAnyObjectByType<NarrationPlayer>();
        if (tracker == null)
        {
            tracker = FindAnyObjectByType<ConveyorTrainingTracker>();
        }

        if (toolbar != null)
        {
            toolbar.ElapsedSecondsProvider = () => placementController != null ? placementController.ElapsedSeconds : 0f;
            toolbar.OnReposition = () => placementController?.ResetTraining();
        }
    }

    private void Update()
    {
        if (label == null)
        {
            return;
        }

        label.text = GetCurrentInstruction(out string narrationId);

        if (narrationId != lastNarrationId)
        {
            lastNarrationId = narrationId;
            narrationPlayer?.Play(narrationId);
        }
    }

    private string GetCurrentInstruction(out string narrationId)
    {
        narrationId = null;

        if (placementController == null)
        {
            placementController = FindFirstObjectByType<ConveyorPlacementController>();
            if (placementController == null)
            {
                return string.Empty;
            }
        }

        if (placementController.PlacedConveyor == null)
        {
            narrationId = "conveyor_prompt_place";
            return LocalizationManager.Get(narrationId);
        }

        var motion = placementController.MotionController;
        if (motion == null)
        {
            return string.Empty;
        }

        string taskLine = tracker != null ? tracker.CurrentInstruction(motion, out narrationId) : null;
        if (taskLine != null)
        {
            return taskLine + GetStatusSuffix(motion);
        }

        if (motion.IsEStopped)
        {
            narrationId = "conveyor_estopped";
        }
        else if (motion.Mode == ConveyorMotionController.OperatingMode.Manual)
        {
            narrationId = motion.IsJogging ? "conveyor_release_jog" : "conveyor_hold_jog";
        }
        else
        {
            narrationId = motion.IsRunning ? "conveyor_tap_stop" : "conveyor_tap_start";
        }

        return LocalizationManager.Get(narrationId) + GetStatusSuffix(motion);
    }

    private string GetStatusSuffix(ConveyorMotionController motion)
    {
        if (speedDial == null)
        {
            speedDial = FindFirstObjectByType<ConveyorSpeedDialController>();
        }

        string suffix = string.Empty;
        if (speedDial != null)
        {
            suffix += "  •  " + LocalizationManager.Get("conveyor_speed_format", LocalizationManager.Get(SpeedKey(speedDial.CurrentPreset)));
        }
        if (motion.Mode == ConveyorMotionController.OperatingMode.Manual)
        {
            suffix += "  •  " + LocalizationManager.Get("conveyor_mode_manual");
        }
        return suffix;
    }

    private static string SpeedKey(ConveyorSpeedDialController.SpeedPreset preset)
    {
        switch (preset)
        {
            case ConveyorSpeedDialController.SpeedPreset.Slow: return "conveyor_speed_slow";
            case ConveyorSpeedDialController.SpeedPreset.Fast: return "conveyor_speed_fast";
            default: return "conveyor_speed_normal";
        }
    }
}
