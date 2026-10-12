using TMPro;
using UnityEngine;

/// <summary>
/// Real-world safety reminder matching the reference AR training app: warns the player
/// if they stand too close to the fire while it's still burning. Purely a UI nudge — it
/// doesn't block or penalize anything, it just surfaces the same "stand back" guidance a
/// real fire-safety instructor would give. Disappears once the fire is out.
/// </summary>
public class FireProximityWarning : MonoBehaviour
{
    [SerializeField] private GameObject warningBar;
    [SerializeField] private TMP_Text warningText;

    [Tooltip("Real-world minimum safe distance from a fire (~4 ft).")]
    [SerializeField] private float minSafeDistance = 1.2f;

    private ARPlacementController placementController;
    private Camera arCamera;

    private void OnEnable()
    {
        placementController = FindFirstObjectByType<ARPlacementController>();
        arCamera = Camera.main;
        if (warningBar != null) warningBar.SetActive(false);
        RefreshText();
        LocalizationManager.OnLanguageChanged += RefreshText;
    }

    private void OnDisable()
    {
        LocalizationManager.OnLanguageChanged -= RefreshText;
    }

    private void RefreshText()
    {
        if (warningText != null) warningText.text = LocalizationManager.Get("proximity_warning");
    }

    private void Update()
    {
        if (warningBar == null)
        {
            return;
        }

        if (placementController == null)
        {
            placementController = FindFirstObjectByType<ARPlacementController>();
        }
        if (arCamera == null)
        {
            arCamera = Camera.main;
        }

        GameObject fire = placementController != null ? placementController.PlacedFire : null;
        if (fire == null || !fire.activeInHierarchy || arCamera == null)
        {
            warningBar.SetActive(false);
            return;
        }

        var fireSource = fire.GetComponent<FireSource>();
        if (fireSource != null && fireSource.IsExtinguished)
        {
            warningBar.SetActive(false);
            return;
        }

        float distance = Vector3.Distance(arCamera.transform.position, fire.transform.position);
        warningBar.SetActive(distance < minSafeDistance);
    }
}
