using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.InputSystem;
using UnityEngine.UI;

/// <summary>
/// Shared touch/mouse-fallback helpers for every script in this module that raycasts
/// screen taps against world content (AR plane placement, hazard/call-point/extinguisher
/// interaction). Centralized here so there's exactly one tap-detection implementation
/// instead of several drifting copies, and so the UI-pointer guard below only needs to be
/// added once: without it, tapping the on-screen instructions/HUD/Retry button also
/// registers as a world tap and can misfire whichever stage is currently listening.
/// Uses the Input System package: AR Foundation's TrackedPoseDriver needs it, and Android
/// builds refuse "Both" as the active input handling.
/// </summary>
public static class ArTouchInput
{
    private static readonly List<RaycastResult> UiHits = new List<RaycastResult>();

    /// <summary>True on the frame a touch/click begins, with its screen position — false if the tap started over UI.</summary>
    public static bool TryGetTapPosition(out Vector2 screenPos)
    {
        var touch = Touchscreen.current?.primaryTouch;
        if (touch != null && touch.press.wasPressedThisFrame)
        {
            screenPos = touch.position.ReadValue();
            return !IsOverUI(screenPos);
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.wasPressedThisFrame)
        {
            screenPos = mouse.position.ReadValue();
            return !IsOverUI(screenPos);
        }

        screenPos = default;
        return false;
    }

    /// <summary>True on every frame a touch/click is currently down, with its screen position — false while held over UI.</summary>
    public static bool TryGetHeldPosition(out Vector2 screenPos)
    {
        var touch = Touchscreen.current?.primaryTouch;
        if (touch != null && touch.press.isPressed)
        {
            screenPos = touch.position.ReadValue();
            return !IsOverUI(screenPos);
        }

        var mouse = Mouse.current;
        if (mouse != null && mouse.leftButton.isPressed)
        {
            screenPos = mouse.position.ReadValue();
            return !IsOverUI(screenPos);
        }

        screenPos = default;
        return false;
    }

    // Raycasts the UI directly instead of IsPointerOverGameObject, which with the Input System
    // UI module reports the previous frame's pointer state on the frame a touch begins.
    private static bool IsOverUI(Vector2 screenPos)
    {
        var eventSystem = EventSystem.current;
        if (eventSystem == null)
        {
            return false;
        }

        UiHits.Clear();
        eventSystem.RaycastAll(new PointerEventData(eventSystem) { position = screenPos }, UiHits);
        foreach (var hit in UiHits)
        {
            if (hit.module is GraphicRaycaster)
            {
                return true;
            }
        }
        return false;
    }
}
