using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// The conveyor's physical-effect layer (analogous to FireExtinguisherController for the
/// fire module) -- owns whether the belt is actually running, in which operating mode, and
/// whether it's E-stopped, and drives the belt/drum animators together. Every hotspot
/// (Start, Stop, Jog, E-Stop, Mode toggle) only ever talks to this one place; none of them
/// touch beltAnimator/rollers/motorHumLoop directly.
///
/// Run()/Stop()'s effects are funneled through StartMotion()/StopMotion() so every entry
/// point -- a deliberate Start/Stop cycle, a held jog, or a forced stop from E-Stop/mode
/// switch -- raises the exact same OnStarted/OnStopped pair. Consumers (the HUD, and later
/// the checklist tracker) only ever need to listen to those two events, never to which
/// specific control caused them.
/// </summary>
public class ConveyorMotionController : MonoBehaviour
{
    public enum OperatingMode { Auto, Manual }

    [SerializeField] private ConveyorBeltAnimator beltAnimator;
    [SerializeField] private RotatingConveyorPart[] rollers;
    [SerializeField] private AudioSource motorHumLoop;

    public UnityEvent OnStarted;
    public UnityEvent OnStopped;
    public UnityEvent OnEStopTriggered;
    public UnityEvent OnEStopReset;
    public UnityEvent OnModeChanged;
    public UnityEvent OnSpeedChanged;

    public bool IsRunning { get; private set; }
    public bool IsJogging { get; private set; }
    public bool IsEStopped { get; private set; }
    public OperatingMode Mode { get; private set; } = OperatingMode.Auto;

    /// <summary>Auto mode's Start/Stop entry point -- a deliberate, latching run until Stop is pressed.</summary>
    public void Run()
    {
        if (IsEStopped || Mode == OperatingMode.Manual || IsRunning)
        {
            return;
        }

        StartMotion();
    }

    /// <summary>Universal safety stop -- always live regardless of mode or how the belt got running.</summary>
    public void Stop()
    {
        if (!IsRunning)
        {
            return;
        }

        StopMotion();
        IsJogging = false;
    }

    /// <summary>Manual mode's jog entry point -- runs only while held; see ConveyorJogButton.</summary>
    public void BeginJog()
    {
        if (IsEStopped || Mode == OperatingMode.Auto || IsRunning)
        {
            return;
        }

        StartMotion();
        IsJogging = true;
    }

    /// <summary>Releasing the jog hotspot -- immediate stop, not a toggle.</summary>
    public void EndJog()
    {
        if (!IsJogging)
        {
            return;
        }

        StopMotion();
        IsJogging = false;
    }

    /// <summary>
    /// Tapping the E-Stop mushroom. Not latched -> latches and force-stops. Latched -> resets,
    /// but deliberately does NOT auto-restart -- the operator must press Start/Jog again.
    /// </summary>
    public void ToggleEStop()
    {
        if (!IsEStopped)
        {
            IsEStopped = true;
            if (IsRunning)
            {
                StopMotion();
                IsJogging = false;
            }
            OnEStopTriggered?.Invoke();
        }
        else
        {
            IsEStopped = false;
            OnEStopReset?.Invoke();
        }
    }

    /// <summary>
    /// Tapping the MANUAL/AUTO selector. Inert while E-stopped. Force-stops before flipping
    /// mode so the belt is never left running unattended across a mode switch (a jog-driven
    /// run in Manual has nothing holding it up once Mode becomes Auto, and vice versa).
    /// </summary>
    public void ToggleMode()
    {
        if (IsEStopped)
        {
            return;
        }

        if (IsRunning)
        {
            StopMotion();
            IsJogging = false;
        }

        Mode = Mode == OperatingMode.Auto ? OperatingMode.Manual : OperatingMode.Auto;
        OnModeChanged?.Invoke();
    }

    private void StartMotion()
    {
        IsRunning = true;
        if (beltAnimator != null)
        {
            beltAnimator.SetRunning(true);
        }
        SetRollersRunning(true);

        if (motorHumLoop != null)
        {
            motorHumLoop.loop = true;
            motorHumLoop.Play();
        }

        OnStarted?.Invoke();
    }

    private void StopMotion()
    {
        IsRunning = false;
        if (beltAnimator != null)
        {
            beltAnimator.SetRunning(false);
        }
        SetRollersRunning(false);
        if (motorHumLoop != null)
        {
            motorHumLoop.Stop();
        }

        OnStopped?.Invoke();
    }

    private void SetRollersRunning(bool running)
    {
        if (rollers == null)
        {
            return;
        }

        foreach (var roller in rollers)
        {
            if (roller != null)
            {
                roller.SetRunning(running);
            }
        }
    }
}
