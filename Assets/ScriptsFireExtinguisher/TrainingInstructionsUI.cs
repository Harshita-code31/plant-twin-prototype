using TMPro;
using UnityEngine;

/// <summary>
/// Single-line "what to do next" prompt shown in a corner of the screen throughout a
/// training attempt — place the fire, place the extinguisher, pick it up, pull the
/// pin, aim, spray, sweep. Lives on the always-present ARRig (see ResultsHUD) and
/// polls ARPlacementController plus the placed extinguisher's own components each
/// frame, since those come into existence only once the player places them.
///
/// Also drives narration: GetCurrentInstruction computes a stable id alongside the display
/// text for every branch -- the SAME id doubles as both the LocalizationManager key (display
/// text) and the NarrationPlayer clip id (spoken line), one lookup driving both in whichever
/// language is active. Update() plays that id only on the frame it actually changes -- not
/// every frame -- so a line is spoken once per state transition instead of restarting itself
/// 60 times a second while the player is still working through that step. Because this text
/// is recomputed fresh every frame from LocalizationManager, a language switch mid-attempt
/// (see LanguageSelectModal) takes effect within one frame with no extra wiring needed here.
/// </summary>
public class TrainingInstructionsUI : MonoBehaviour
{
    [SerializeField] private TMP_Text label;

    [Tooltip("Shows the title/content of whichever AR lesson exhibit was last tapped. Optional — leave empty if not building the Learn stage's detail panel.")]
    [SerializeField] private TMP_Text lessonDetailText;

    [Tooltip("How long a wrong-extinguisher warning overrides the normal instruction line for.")]
    [SerializeField] private float warningDuration = 3f;

    [Tooltip("Prominent red banner shown for warningDuration on a wrong-extinguisher pickup, in addition to the corner instruction text -- a small text swap alone is easy to miss.")]
    [SerializeField] private GameObject warningBanner;

    [Tooltip("The banner's own caption text -- kept in sync with whichever warning is currently active (see ShowTimedWarning) instead of a static LocalizedText, since this one banner is now reused for more than one warning message.")]
    [SerializeField] private TMP_Text warningBannerText;

    [Tooltip("Shared AR toolbar chrome (see ArHudToolbar) — optional, wires this module's timer and reposition action into it if present.")]
    [SerializeField] private ArHudToolbar toolbar;

    private ARPlacementController placementController;
    private FireResponseCoordinator coordinator;
    private FireSafetyLessonController subscribedLesson;
    private NarrationPlayer narrationPlayer;
    private float warningUntilTime;
    private string warningLocalizationKey;
    private string lastNarrationId;

    private void OnEnable()
    {
        placementController = FindFirstObjectByType<ARPlacementController>();
        narrationPlayer = FindFirstObjectByType<NarrationPlayer>();

        // FireResponseCoordinator lives on this same GameObject (ResultsHUD).
        coordinator = GetComponent<FireResponseCoordinator>();
        if (coordinator != null)
        {
            coordinator.OnWrongExtinguisherWarning.AddListener(HandleWrongExtinguisherWarning);
            coordinator.OnPickupBlockedAlarmNotActive.AddListener(HandleAlarmNotActiveWarning);
        }

        if (toolbar != null)
        {
            toolbar.ElapsedSecondsProvider = () => coordinator != null ? coordinator.ElapsedSeconds : 0f;
            toolbar.OnReposition = () => placementController?.ResetTraining();
        }

        if (warningBanner != null)
        {
            warningBanner.SetActive(false);
        }
    }

    private void OnDisable()
    {
        if (coordinator != null)
        {
            coordinator.OnWrongExtinguisherWarning.RemoveListener(HandleWrongExtinguisherWarning);
            coordinator.OnPickupBlockedAlarmNotActive.RemoveListener(HandleAlarmNotActiveWarning);
        }

        if (subscribedLesson != null)
        {
            subscribedLesson.OnExhibitViewed -= HandleExhibitViewed;
            subscribedLesson = null;
        }
    }

    private void HandleWrongExtinguisherWarning()
    {
        ShowTimedWarning("wrong_extinguisher_warning_line", "warning_wrong_extinguisher");
    }

    private void HandleAlarmNotActiveWarning()
    {
        ShowTimedWarning("alarm_not_activated_warning_line", "warning_alarm_not_activated");
    }

    /// <summary>
    /// Overrides the normal instruction line with a prominent, timed warning banner --
    /// shared by every blocked-action case (wrong extinguisher, alarm not yet activated)
    /// so they all reuse the one existing banner/timer mechanism instead of each needing
    /// their own. One-shot, event-driven -- played here rather than through the per-frame
    /// state-change tracking in Update(), since a warning overrides the normal instruction
    /// for a fixed duration rather than being a state of its own.
    /// </summary>
    private void ShowTimedWarning(string localizationKey, string narrationId)
    {
        warningUntilTime = Time.time + warningDuration;
        warningLocalizationKey = localizationKey;
        if (warningBannerText != null)
        {
            warningBannerText.text = LocalizationManager.Get(localizationKey);
        }
        narrationPlayer?.Play(narrationId);
    }

    private void HandleExhibitViewed(string title, string content)
    {
        if (lessonDetailText != null)
        {
            lessonDetailText.gameObject.SetActive(true);
            lessonDetailText.text = $"{title}\n{content}";
        }
    }

    private void Update()
    {
        // The lesson content only exists once ARPlacementController auto-places it, so
        // (re)subscribe here rather than at OnEnable/design time.
        if (placementController != null && placementController.LessonController != subscribedLesson)
        {
            if (subscribedLesson != null)
            {
                subscribedLesson.OnExhibitViewed -= HandleExhibitViewed;
            }
            subscribedLesson = placementController.LessonController;
            if (subscribedLesson != null)
            {
                subscribedLesson.OnExhibitViewed += HandleExhibitViewed;
            }
        }

        if (label == null)
        {
            return;
        }

        bool warningActive = Time.time < warningUntilTime;
        if (warningBanner != null)
        {
            warningBanner.SetActive(warningActive);
        }

        if (warningActive)
        {
            label.text = LocalizationManager.Get(warningLocalizationKey);
            return;
        }

        label.text = GetCurrentInstruction(out string narrationId);

        if (narrationId != lastNarrationId)
        {
            lastNarrationId = narrationId;
            narrationPlayer?.Play(narrationId);
        }
    }

    /// <summary>
    /// Returns the display text for the corner instruction line, and (via narrationId) the
    /// matching LocalizationManager/NarrationPlayer id for that same branch -- kept as one
    /// method, not two, so the text and the spoken line can never drift out of sync with
    /// each other. A branch with no recorded narration line yet (the AR "Learn" stage's
    /// per-exhibit titles, or an empty-checklist fallback) sets narrationId to null;
    /// NarrationPlayer.Play silently no-ops on that.
    /// </summary>
    private string GetCurrentInstruction(out string narrationId)
    {
        narrationId = null;

        if (placementController == null)
        {
            placementController = FindFirstObjectByType<ARPlacementController>();
            if (placementController == null)
            {
                return string.Empty;
            }
        }

        string placementPromptKey = placementController.CurrentPlacementPrompt();
        if (placementPromptKey != null)
        {
            if (placementController.IsScanningForSurface)
            {
                narrationId = "prompt_scan_floor";
                return LocalizationManager.Get("step_format", placementController.CurrentStageNumber, placementController.TotalStages, LocalizationManager.Get(narrationId));
            }
            narrationId = placementPromptKey; // CurrentPlacementPrompt already returns a stable key (see its own doc comment).
            return LocalizationManager.Get("step_format", placementController.CurrentStageNumber, placementController.TotalStages, LocalizationManager.Get(placementPromptKey));
        }

        if (placementController.LessonController != null && !placementController.LessonController.IsComplete)
        {
            var current = placementController.LessonController.CurrentExhibit;
            string title = current != null ? current.Title : "…";
            return LocalizationManager.Get("learning_format", title, placementController.LessonController.ViewedCount, placementController.LessonController.ExhibitCount);
        }

        if (placementController.CallPointController != null && !placementController.CallPointController.IsActivated)
        {
            narrationId = "prompt_activate_alarm";
            return LocalizationManager.Get(narrationId);
        }

        GameObject fire = placementController.PlacedFire;

        if (fire == null)
        {
            narrationId = "prompt_scanning_floor";
            return LocalizationManager.Get(narrationId);
        }

        if (placementController.PlacedExtinguisherOptions.Count == 0)
        {
            narrationId = "prompt_mount_extinguishers";
            return LocalizationManager.Get(narrationId);
        }

        // Gas furnace scenario: putting the fire out with the gas still on makes the furnace
        // explode (FurnaceExplosionController), so shutting the valve comes before -- and, if
        // the trainee skipped it, interrupts -- choosing and using an extinguisher.
        var gasValve = fire.GetComponentInChildren<GasShutoffValve>();
        var burningFire = fire.GetComponent<FireSource>();
        if (gasValve != null && !gasValve.IsShutOff && (burningFire == null || !burningFire.IsExtinguished))
        {
            narrationId = "prompt_shut_gas";
            return LocalizationManager.Get(narrationId);
        }

        GameObject extinguisher = placementController.ChosenExtinguisher;
        if (extinguisher == null)
        {
            var fireSource = fire.GetComponent<FireSource>();
            if (fireSource == null)
            {
                narrationId = "guide_default";
                return LocalizationManager.Get(narrationId);
            }
            narrationId = NarrationIdForFireClass(fireSource.fireClass);
            return LocalizationManager.Get(narrationId);
        }

        var tracker = extinguisher.GetComponent<PassChecklistTracker>();
        if (tracker == null)
        {
            return string.Empty;
        }

        if (!tracker.FireFullyOut)
        {
            if (!tracker.PinPulled)
            {
                narrationId = "pass_pull_pin";
                return LocalizationManager.Get(narrationId);
            }

            if (!tracker.Squeezed)
            {
                var aim = extinguisher.GetComponentInChildren<ExtinguisherAimController>();
                var fireSource = tracker.TargetFire;
                bool aimed = aim != null && fireSource != null && aim.IsAimedAtFireBase(fireSource.transform);
                narrationId = aimed ? "pass_squeeze" : "pass_aim_base";
                return LocalizationManager.Get(narrationId);
            }

            if (!tracker.Swept)
            {
                narrationId = "pass_sweep";
                return LocalizationManager.Get(narrationId);
            }

            narrationId = "pass_keep_spraying";
            return LocalizationManager.Get(narrationId);
        }

        narrationId = "pass_fire_out";
        return LocalizationManager.Get(narrationId);
    }

    private static string NarrationIdForFireClass(FireClass fireClass)
    {
        switch (fireClass)
        {
            case FireClass.A: return "guide_class_a";
            case FireClass.BC: return "guide_class_bc";
            case FireClass.ABC: return "guide_class_abc";
            default: return "guide_default";
        }
    }
}
