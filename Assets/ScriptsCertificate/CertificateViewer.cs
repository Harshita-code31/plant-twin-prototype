using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen certificate viewer: shows one rendered certificate that can be zoomed (pinch, mouse
/// wheel, double-tap or the +/- buttons) and panned, with a Save to Gallery button. Built at runtime
/// on the scene's canvas, so it needs no scene wiring. Used both right after a training is passed and
/// when a certificate is tapped in the "My Certificates" stack.
/// </summary>
public static class CertificateViewer
{
    private static GameObject root;

    public static bool IsOpen => root != null;

    /// <param name="image">The rendered certificate (see CertificateUI.RenderCertificate).</param>
    /// <param name="onSave">Saves the certificate to the gallery; returns true on success.</param>
    /// <param name="ownsTexture">Destroy <paramref name="image"/> when the viewer closes.</param>
    public static void Open(Texture2D image, string title, Func<bool> onSave, bool ownsTexture)
    {
        Canvas canvas = UnityEngine.Object.FindFirstObjectByType<Canvas>();
        if (canvas == null || image == null)
        {
            Debug.LogError("CertificateViewer: no Canvas in this scene, or no image to show.");
            return;
        }
        Close();

        root = new GameObject("CertificateViewer", typeof(RectTransform), typeof(Image), typeof(Canvas), typeof(GraphicRaycaster), typeof(CertificateViewerHost));
        root.transform.SetParent(canvas.transform, false);
        Stretch(root.GetComponent<RectTransform>());
        root.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);
        var sorting = root.GetComponent<Canvas>();
        sorting.overrideSorting = true;
        sorting.sortingOrder = 700; // above the menu and the "My Certificates" stack (500)

        // --- zoomable area ---
        var viewport = new GameObject("Viewport", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(CertificatePinchZoom));
        viewport.transform.SetParent(root.transform, false);
        var viewRect = viewport.GetComponent<RectTransform>();
        viewRect.anchorMin = new Vector2(0f, 0.20f);
        viewRect.anchorMax = new Vector2(1f, 0.90f);
        viewRect.offsetMin = Vector2.zero;
        viewRect.offsetMax = Vector2.zero;
        viewport.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.004f); // invisible, but receives pointer events

        var content = new GameObject("Certificate", typeof(RectTransform), typeof(RawImage));
        content.transform.SetParent(viewport.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = contentRect.anchorMax = contentRect.pivot = new Vector2(0.5f, 0.5f);
        var raw = content.GetComponent<RawImage>();
        raw.texture = image;
        raw.raycastTarget = false;

        var zoom = viewport.GetComponent<CertificatePinchZoom>();
        zoom.Init(viewRect, contentRect, (float)image.width / image.height);

        // --- chrome ---
        AddLabel(root.transform, "Title", title, new Vector2(0.05f, 0.925f), new Vector2(0.80f, 0.985f), 44, FontStyles.Bold, Color.white)
            .alignment = TextAlignmentOptions.MidlineLeft;
        AddLabel(root.transform, "Hint", LocalizationManager.Get("cert_viewer_hint"), new Vector2(0.05f, 0.895f), new Vector2(0.95f, 0.925f), 24, FontStyles.Normal, new Color(0.7f, 0.75f, 0.85f, 1f));

        var host = root.GetComponent<CertificateViewerHost>();
        AddButton(root.transform, "Close", "×", new Vector2(0.92f, 0.955f), new Vector2(84, 84), new Color(0.11f, 0.13f, 0.2f, 1f), 48, Close);
        AddButton(root.transform, "ZoomOut", "−", new Vector2(0.30f, 0.155f), new Vector2(120, 84), new Color(0.16f, 0.2f, 0.32f, 1f), 52, () => zoom.ZoomBy(1f / 1.5f));
        AddButton(root.transform, "ZoomIn", "+", new Vector2(0.70f, 0.155f), new Vector2(120, 84), new Color(0.16f, 0.2f, 0.32f, 1f), 52, () => zoom.ZoomBy(1.5f));
        zoom.zoomLabel = AddLabel(root.transform, "ZoomLabel", "100%", new Vector2(0.40f, 0.12f), new Vector2(0.60f, 0.19f), 36, FontStyles.Bold, Color.white);

        var saveButton = AddButton(root.transform, "Save", LocalizationManager.Get("cert_save_gallery"), new Vector2(0.5f, 0.075f), new Vector2(520, 96), new Color32(0x2E, 0x7D, 0x32, 0xFF), 38, host.HandleSave);
        var status = AddLabel(root.transform, "Status", string.Empty, new Vector2(0.1f, 0.005f), new Vector2(0.9f, 0.045f), 30, FontStyles.Normal, Color.white);
        host.Init(image, ownsTexture, onSave, saveButton, status);
    }

    public static void Close()
    {
        if (root != null)
        {
            UnityEngine.Object.Destroy(root);
            root = null;
        }
    }

    internal static Button AddButton(Transform parent, string name, string label, Vector2 anchor, Vector2 size, Color color, float fontSize, UnityEngine.Events.UnityAction onClick)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = rect.anchorMax = anchor;
        rect.sizeDelta = size;
        go.GetComponent<Image>().color = color;
        var button = go.GetComponent<Button>();
        button.onClick.AddListener(onClick);
        AddLabel(go.transform, "Label", label, Vector2.zero, Vector2.one, fontSize, FontStyles.Bold, Color.white);
        return button;
    }

    internal static TMP_Text AddLabel(Transform parent, string name, string text, Vector2 min, Vector2 max, float size, FontStyles style, Color color)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = min;
        rect.anchorMax = max;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var label = go.GetComponent<TextMeshProUGUI>();
        label.text = text;
        label.fontSize = size;
        label.enableAutoSizing = true;
        label.fontSizeMax = size;
        label.fontSizeMin = size * 0.5f;
        label.fontStyle = style;
        label.color = color;
        label.alignment = TextAlignmentOptions.Center;
        label.enableWordWrapping = true;
        label.raycastTarget = false;
        return label;
    }

    internal static void Stretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }
}

/// <summary>Lives on the viewer root: owns the texture's lifetime and runs the Save to Gallery action.</summary>
public class CertificateViewerHost : MonoBehaviour
{
    private Texture2D image;
    private bool ownsTexture;
    private Func<bool> onSave;
    private Button saveButton;
    private TMP_Text status;
    private bool saving;

    public void Init(Texture2D texture, bool owns, Func<bool> save, Button button, TMP_Text statusLabel)
    {
        image = texture;
        ownsTexture = owns;
        onSave = save;
        saveButton = button;
        status = statusLabel;
    }

    public void HandleSave()
    {
        if (!saving && onSave != null)
        {
            StartCoroutine(Save());
        }
    }

    private IEnumerator Save()
    {
        saving = true;
        saveButton.interactable = false;
        status.text = LocalizationManager.Get("cert_saving");
        yield return null; // let "Saving..." paint before the (synchronous) PNG encode + write
        bool ok = onSave();
        status.text = LocalizationManager.Get(ok ? "cert_saved" : "cert_save_failed");
        saveButton.interactable = true;
        saving = false;
    }

    private void OnDestroy()
    {
        if (ownsTexture && image != null)
        {
            Destroy(image);
        }
    }
}

/// <summary>
/// Pinch / wheel / double-tap zoom and drag-to-pan for one content rect inside a masked viewport.
/// Built on pointer events (each finger arrives as its own pointer), so it works with the Input System
/// UI module without touching the legacy Input class.
/// </summary>
public class CertificatePinchZoom : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IDragHandler, IScrollHandler, IPointerClickHandler
{
    private const float MinScale = 1f;
    private const float MaxScale = 6f;

    public TMP_Text zoomLabel;

    private RectTransform viewport;
    private RectTransform content;
    private Vector2 baseSize;
    private float scale = 1f;
    private readonly Dictionary<int, Vector2> pointers = new Dictionary<int, Vector2>();
    private float lastPinchDistance;

    public void Init(RectTransform viewportRect, RectTransform contentRect, float aspect)
    {
        viewport = viewportRect;
        content = contentRect;
        // Fit the whole certificate inside the viewport at 100%.
        Canvas.ForceUpdateCanvases();
        Vector2 v = viewport.rect.size;
        float width = Mathf.Min(v.x, v.y * aspect);
        baseSize = new Vector2(width, width / aspect);
        content.sizeDelta = baseSize;
        content.anchoredPosition = Vector2.zero;
        SetScale(1f, Vector2.zero);
    }

    public void ZoomBy(float factor)
    {
        SetScale(scale * factor, Vector2.zero);
    }

    public void OnPointerDown(PointerEventData e)
    {
        pointers[e.pointerId] = e.position;
        lastPinchDistance = 0f;
    }

    public void OnPointerUp(PointerEventData e)
    {
        pointers.Remove(e.pointerId);
        lastPinchDistance = 0f;
    }

    public void OnDrag(PointerEventData e)
    {
        Vector2 previous = pointers.TryGetValue(e.pointerId, out var p) ? p : e.position;
        pointers[e.pointerId] = e.position;

        if (pointers.Count >= 2)
        {
            Vector2 a = Vector2.zero, b = Vector2.zero;
            int i = 0;
            foreach (var pos in pointers.Values)
            {
                if (i == 0) a = pos; else if (i == 1) b = pos;
                i++;
            }
            float distance = Vector2.Distance(a, b);
            if (lastPinchDistance > 0.01f && distance > 0.01f)
            {
                SetScale(scale * distance / lastPinchDistance, ScreenToViewport((a + b) * 0.5f));
            }
            lastPinchDistance = distance;
        }
        else
        {
            Vector2 now = ScreenToViewport(e.position);
            Vector2 was = ScreenToViewport(previous);
            content.anchoredPosition = Clamp(content.anchoredPosition + (now - was), scale);
        }
    }

    public void OnScroll(PointerEventData e)
    {
        SetScale(scale * (1f + Mathf.Clamp(e.scrollDelta.y, -3f, 3f) * 0.1f), ScreenToViewport(e.position));
    }

    public void OnPointerClick(PointerEventData e)
    {
        if (e.clickCount == 2)
        {
            SetScale(scale > 1.5f ? 1f : 2.5f, ScreenToViewport(e.position));
        }
    }

    private Vector2 ScreenToViewport(Vector2 screen)
    {
        Canvas canvas = viewport.GetComponentInParent<Canvas>();
        Camera cam = canvas != null && canvas.renderMode != RenderMode.ScreenSpaceOverlay ? canvas.worldCamera : null;
        RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, screen, cam, out Vector2 local);
        return local;
    }

    /// <summary>Sets the zoom, keeping the point under <paramref name="focus"/> (viewport-local) fixed.</summary>
    private void SetScale(float newScale, Vector2 focus)
    {
        newScale = Mathf.Clamp(newScale, MinScale, MaxScale);
        float ratio = newScale / scale;
        Vector2 position = focus + (content.anchoredPosition - focus) * ratio;
        scale = newScale;
        content.sizeDelta = baseSize * scale;
        content.anchoredPosition = Clamp(position, scale);
        if (zoomLabel != null)
        {
            zoomLabel.text = Mathf.RoundToInt(scale * 100f) + "%";
        }
    }

    /// <summary>Keeps the certificate covering the viewport once it is larger than it; centres it otherwise.</summary>
    private Vector2 Clamp(Vector2 position, float s)
    {
        Vector2 shown = baseSize * s;
        Vector2 view = viewport.rect.size;
        float maxX = Mathf.Max(0f, (shown.x - view.x) * 0.5f);
        float maxY = Mathf.Max(0f, (shown.y - view.y) * 0.5f);
        return new Vector2(Mathf.Clamp(position.x, -maxX, maxX), Mathf.Clamp(position.y, -maxY, maxY));
    }
}
