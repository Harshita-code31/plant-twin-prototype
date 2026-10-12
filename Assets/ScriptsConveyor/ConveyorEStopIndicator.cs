using UnityEngine;

/// <summary>
/// Purely visual: keeps a persistent "latched" glow lit near the E-Stop mushroom while the
/// conveyor is E-stopped, so the trainee can see the fault is still active at a glance --
/// distinct from ConveyorControlButton's own affordanceVisual, which only flashes briefly on
/// press and isn't meant to stay lit. Deliberately decoupled from the E-Stop hotspot itself
/// so ConveyorControlButton doesn't need a second, persistent-glow visual mode bolted on.
/// </summary>
public class ConveyorEStopIndicator : MonoBehaviour
{
    [SerializeField] private ConveyorMotionController motionController;
    [SerializeField] private GameObject latchedGlowVisual;

    private void OnEnable()
    {
        if (motionController != null)
        {
            motionController.OnEStopTriggered.AddListener(HandleTriggered);
            motionController.OnEStopReset.AddListener(HandleReset);
        }

        SetGlow(motionController != null && motionController.IsEStopped);
    }

    private void OnDisable()
    {
        if (motionController != null)
        {
            motionController.OnEStopTriggered.RemoveListener(HandleTriggered);
            motionController.OnEStopReset.RemoveListener(HandleReset);
        }
    }

    private void HandleTriggered()
    {
        SetGlow(true);
    }

    private void HandleReset()
    {
        SetGlow(false);
    }

    private void SetGlow(bool on)
    {
        if (latchedGlowVisual != null)
        {
            latchedGlowVisual.SetActive(on);
        }
    }
}
