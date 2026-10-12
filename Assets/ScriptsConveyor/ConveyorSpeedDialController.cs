using UnityEngine;

/// <summary>
/// Cycles the belt's speed preset -- wired from the speed-dial hotspot's
/// ConveyorControlButton.OnPressed (role = SpeedDial). Applies live via
/// ConveyorBeltAnimator.SetSpeedMetersPerSecond regardless of whether the belt is currently
/// running or E-stopped: adjusting the dial doesn't move anything by itself, so it's a safe
/// no-op to leave responsive at all times, and lets an operator pre-stage the next run's
/// speed while a fault is being cleared.
/// </summary>
public class ConveyorSpeedDialController : MonoBehaviour
{
    public enum SpeedPreset { Slow, Normal, Fast }

    [SerializeField] private ConveyorBeltAnimator beltAnimator;
    [SerializeField] private ConveyorMotionController motionController;

    [Tooltip("Meters/second for each preset, index-aligned with SpeedPreset.")]
    [SerializeField] private float[] speedPresetsMps = { 0.2f, 0.4f, 0.7f };

    public SpeedPreset CurrentPreset { get; private set; } = SpeedPreset.Normal;

    private void Awake()
    {
        ApplyCurrentPreset();
    }

    public void CycleSpeed()
    {
        CurrentPreset = (SpeedPreset)(((int)CurrentPreset + 1) % speedPresetsMps.Length);
        ApplyCurrentPreset();

        if (motionController != null)
        {
            motionController.OnSpeedChanged?.Invoke();
        }
    }

    private void ApplyCurrentPreset()
    {
        if (beltAnimator == null)
        {
            return;
        }

        int index = (int)CurrentPreset;
        if (index >= 0 && index < speedPresetsMps.Length)
        {
            beltAnimator.SetSpeedMetersPerSecond(speedPresetsMps[index]);
        }
    }
}
