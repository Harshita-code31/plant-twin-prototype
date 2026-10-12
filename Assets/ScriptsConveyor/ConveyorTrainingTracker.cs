using System;
using UnityEngine;

/// <summary>
/// Defines when the conveyor (Machine Training) practice is "complete": every control has been
/// operated at least once -- run the belt in AUTO, stop it with STOP, use the E-STOP, reset it,
/// switch to MANUAL, jog the belt, change the speed. Listens only to ConveyorMotionController's
/// events (the same single source of truth every hotspot talks to), re-subscribing whenever
/// ConveyorPlacementController places a new conveyor -- so progress survives the toolbar's
/// Reposition, and only ResetProgress() (the results screen's Retry) clears it.
///
/// Also supplies the next task as the HUD's instruction line (see ConveyorInstructionsUI), so
/// the trainee is walked through every control rather than having to guess what's left.
/// MachineTrainingResultsUI listens for OnCompleted to run the quiz and show the result.
/// </summary>
public class ConveyorTrainingTracker : MonoBehaviour
{
    public enum Task { RunAuto, Stop, EStop, ResetEStop, Manual, Jog, Speed }

    private static readonly string[] InstructionKeys =
    {
        "machine_task_run_auto", "machine_task_stop", "machine_task_estop", "machine_task_reset",
        "machine_task_manual", "machine_task_jog", "machine_task_speed",
    };

    private static readonly string[] DoneKeys =
    {
        "machine_done_run_auto", "machine_done_stop", "machine_done_estop", "machine_done_reset",
        "machine_done_manual", "machine_done_jog", "machine_done_speed",
    };

    [Tooltip("Auto-found on this GameObject (or in the scene) if left empty.")]
    [SerializeField] private ConveyorPlacementController placementController;

    /// <summary>Raised once, the moment the last outstanding task is done.</summary>
    public event Action OnCompleted;

    public int TaskCount => InstructionKeys.Length;
    public bool IsComplete { get; private set; }

    /// <summary>Seconds on the placement timer when the practice was completed.</summary>
    public float CompletedElapsedSeconds { get; private set; }

    private readonly bool[] done = new bool[InstructionKeys.Length];
    private ConveyorMotionController subscribed;

    // A normal STOP is told apart from the stop that a mode switch forces (ToggleMode stops the
    // belt, then flips Mode in the same call) by re-checking the mode on the next frame.
    private bool stopCheckPending;
    private ConveyorMotionController.OperatingMode modeAtStop;

    public bool IsDone(Task task) => done[(int)task];

    public static string DoneLabel(Task task) => LocalizationManager.Get(DoneKeys[(int)task]);

    private void Awake()
    {
        if (placementController == null)
        {
            placementController = GetComponent<ConveyorPlacementController>();
        }
        if (placementController == null)
        {
            placementController = FindAnyObjectByType<ConveyorPlacementController>();
        }
    }

    private void Update()
    {
        var motion = placementController != null ? placementController.MotionController : null;
        if (motion != subscribed)
        {
            Subscribe(motion);
        }

        if (stopCheckPending)
        {
            stopCheckPending = false;
            if (subscribed != null && subscribed.Mode == modeAtStop)
            {
                Mark(Task.Stop);
            }
        }
    }

    private void OnDisable()
    {
        Subscribe(null);
    }

    /// <summary>Clears all progress -- the results screen's Retry, which restarts the practice.</summary>
    public void ResetProgress()
    {
        Array.Clear(done, 0, done.Length);
        IsComplete = false;
        CompletedElapsedSeconds = 0f;
        stopCheckPending = false;
    }

    /// <summary>
    /// Localized "Task n/7 — what to do" line for the next outstanding task, adjusted to the
    /// belt's current state (e.g. asks to switch back to AUTO before START can work). Null once
    /// the practice is complete. narrationId is the key of the spoken part (the task text,
    /// without the "Task n/7" prefix), which doubles as its NarrationPlayer clip id.
    /// </summary>
    public string CurrentInstruction(ConveyorMotionController motion, out string narrationId)
    {
        narrationId = null;
        if (IsComplete || motion == null)
        {
            return null;
        }

        int next = Array.IndexOf(done, false);
        var task = (Task)next;
        bool manual = motion.Mode == ConveyorMotionController.OperatingMode.Manual;
        string key = InstructionKeys[next];

        if (motion.IsEStopped)
        {
            // Nothing but the reset works while latched, whatever task is next.
            key = "machine_task_reset";
        }
        else if (task == Task.RunAuto || (task == Task.Stop && (!motion.IsRunning || motion.IsJogging)))
        {
            key = manual ? "machine_task_back_to_auto" : (task == Task.Stop ? "machine_task_run_auto" : key);
        }
        else if (task == Task.Jog && !manual)
        {
            key = "machine_task_manual";
        }

        narrationId = key;
        return LocalizationManager.Get("machine_task_format", next + 1, TaskCount, LocalizationManager.Get(key));
    }

    private void Subscribe(ConveyorMotionController motion)
    {
        if (subscribed != null)
        {
            subscribed.OnStarted.RemoveListener(HandleStarted);
            subscribed.OnStopped.RemoveListener(HandleStopped);
            subscribed.OnEStopTriggered.RemoveListener(HandleEStopTriggered);
            subscribed.OnEStopReset.RemoveListener(HandleEStopReset);
            subscribed.OnModeChanged.RemoveListener(HandleModeChanged);
            subscribed.OnSpeedChanged.RemoveListener(HandleSpeedChanged);
        }

        subscribed = motion;
        stopCheckPending = false;

        if (subscribed != null)
        {
            subscribed.OnStarted.AddListener(HandleStarted);
            subscribed.OnStopped.AddListener(HandleStopped);
            subscribed.OnEStopTriggered.AddListener(HandleEStopTriggered);
            subscribed.OnEStopReset.AddListener(HandleEStopReset);
            subscribed.OnModeChanged.AddListener(HandleModeChanged);
            subscribed.OnSpeedChanged.AddListener(HandleSpeedChanged);
        }
    }

    private void HandleStarted()
    {
        // BeginJog raises OnStarted before setting IsJogging, so tell a jog apart by mode.
        Mark(subscribed.Mode == ConveyorMotionController.OperatingMode.Auto ? Task.RunAuto : Task.Jog);
    }

    private void HandleStopped()
    {
        // E-stop and jog-release stops don't count as using STOP.
        if (!subscribed.IsEStopped && !subscribed.IsJogging)
        {
            stopCheckPending = true;
            modeAtStop = subscribed.Mode;
        }
    }

    private void HandleEStopTriggered() => Mark(Task.EStop);

    private void HandleEStopReset() => Mark(Task.ResetEStop);

    private void HandleModeChanged()
    {
        if (subscribed.Mode == ConveyorMotionController.OperatingMode.Manual)
        {
            Mark(Task.Manual);
        }
    }

    private void HandleSpeedChanged() => Mark(Task.Speed);

    private void Mark(Task task)
    {
        if (IsComplete || done[(int)task])
        {
            return;
        }
        done[(int)task] = true;

        if (Array.IndexOf(done, false) < 0)
        {
            IsComplete = true;
            CompletedElapsedSeconds = placementController != null ? placementController.ElapsedSeconds : 0f;
            OnCompleted?.Invoke();
        }
    }
}
