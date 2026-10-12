using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// The worker's certificates (opened from MainMenu's "View Certificate" button via
/// CertificateVerifyUI.Show). Each earned certificate is a card stacked one above the other in a
/// scrolling list, showing its score and whether it has reached the server; tapping a card opens it in
/// the zoomable <see cref="CertificateViewer"/>. A training with no certificate yet gets a card that
/// says what the pass mark is. Built at runtime on the scene's canvas; no scene wiring needed.
/// </summary>
public static class MyCertificatesUI
{
    // Localization key of each module's display name (same keys the results screens use).
    private static readonly (string key, string titleKey)[] Modules =
    {
        ("fire_safety", "module_fire_safety_training"),
        ("machine_training", "module_machine_safety_training"),
    };

    private static GameObject panel;

    public static bool IsOpen => panel != null;

    public static void Open()
    {
        Close();
        if (!AuthService.IsLoggedIn)
        {
            ShowMessage(LocalizationManager.Get("cert_login_required"));
            return;
        }

        var records = CertificateStore.GetForWorker(AuthService.CurrentWorker.workerId);
        var cards = new List<(string key, string title, CertificateRecord record)>();
        foreach (var (key, titleKey) in Modules)
        {
            // Prefer the copy the server already knows about (it may have been issued on another device).
            var record = records.Find(r => r.module == key && r.synced) ?? records.Find(r => r.module == key);
            cards.Add((key, LocalizationManager.Get(titleKey), record));
        }

        ShowStack(cards);
    }

    private static GameObject CreatePanel()
    {
        Canvas canvas = Object.FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError("MyCertificatesUI: no Canvas found in this scene.");
            return null;
        }
        var go = new GameObject("MyCertificatesPanel", typeof(RectTransform), typeof(Image), typeof(Canvas), typeof(GraphicRaycaster));
        go.transform.SetParent(canvas.transform, false);
        CertificateViewer.Stretch(go.GetComponent<RectTransform>());
        go.GetComponent<Image>().color = new Color(0.03f, 0.04f, 0.07f, 1f);
        var sorting = go.GetComponent<Canvas>();
        sorting.overrideSorting = true;
        sorting.sortingOrder = 500; // own layer so nothing else on the menu draws over it
        panel = go;
        return go;
    }

    private static void ShowMessage(string text)
    {
        var go = CreatePanel();
        if (go == null) return;
        CertificateViewer.AddLabel(go.transform, "Message", text, new Vector2(0.08f, 0.4f), new Vector2(0.92f, 0.65f), 46, FontStyles.Normal, Color.white);
        CertificateViewer.AddButton(go.transform, "Close", LocalizationManager.Get("close_button"), new Vector2(0.5f, 0.3f), new Vector2(420, 110), new Color(0.11f, 0.13f, 0.2f, 1f), 40, Close);
    }

    private static void ShowStack(List<(string key, string title, CertificateRecord record)> cards)
    {
        var go = CreatePanel();
        if (go == null) return;

        CertificateViewer.AddLabel(go.transform, "Title", LocalizationManager.Get("cert_my_title"), new Vector2(0.05f, 0.925f), new Vector2(0.95f, 0.985f), 52, FontStyles.Bold, Color.white);

        // Scrolling column: one card per training, stacked top to bottom.
        var scroll = new GameObject("Scroll", typeof(RectTransform), typeof(Image), typeof(RectMask2D), typeof(ScrollRect));
        scroll.transform.SetParent(go.transform, false);
        var scrollRect = scroll.GetComponent<RectTransform>();
        scrollRect.anchorMin = new Vector2(0f, 0.13f);
        scrollRect.anchorMax = new Vector2(1f, 0.92f);
        scrollRect.offsetMin = Vector2.zero;
        scrollRect.offsetMax = Vector2.zero;
        scroll.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.004f);

        var content = new GameObject("Content", typeof(RectTransform), typeof(VerticalLayoutGroup), typeof(ContentSizeFitter));
        content.transform.SetParent(scroll.transform, false);
        var contentRect = content.GetComponent<RectTransform>();
        contentRect.anchorMin = new Vector2(0f, 1f);
        contentRect.anchorMax = new Vector2(1f, 1f);
        contentRect.pivot = new Vector2(0.5f, 1f);
        contentRect.offsetMin = Vector2.zero;
        contentRect.offsetMax = Vector2.zero;
        var layout = content.GetComponent<VerticalLayoutGroup>();
        layout.spacing = 36;
        layout.padding = new RectOffset(30, 30, 20, 30);
        layout.childControlWidth = true;
        layout.childControlHeight = true; // honour each card's LayoutElement.preferredHeight
        layout.childForceExpandWidth = true;
        layout.childForceExpandHeight = false;
        content.GetComponent<ContentSizeFitter>().verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        var scrollComponent = scroll.GetComponent<ScrollRect>();
        scrollComponent.viewport = scrollRect;
        scrollComponent.content = contentRect;
        scrollComponent.horizontal = false;
        scrollComponent.vertical = true;
        scrollComponent.movementType = ScrollRect.MovementType.Elastic;
        scrollComponent.scrollSensitivity = 40f;

        Canvas.ForceUpdateCanvases();
        float cardWidth = scrollRect.rect.width - layout.padding.horizontal;
        string traineeName = CertificateUI.ResolveTraineeName();
        var textures = new List<Texture2D>();

        foreach (var (key, title, record) in cards)
        {
            if (record == null)
            {
                AddPlaceholderCard(content.transform, key, title);
                continue;
            }

            Texture2D image = CertificateUI.RenderCertificate(key, traineeName, record);
            if (image == null) continue;
            textures.Add(image);

            const float headerHeight = 130f;
            float imageHeight = cardWidth * image.height / image.width;

            var card = new GameObject("Card_" + key, typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
            card.transform.SetParent(content.transform, false);
            card.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.18f, 1f);
            card.GetComponent<LayoutElement>().preferredHeight = headerHeight + imageHeight + 16f;

            // Line 1: the training. Line 2: score and whether the server has this certificate yet.
            var headerTitle = CertificateViewer.AddLabel(card.transform, "Header", title, new Vector2(0f, 1f), new Vector2(1f, 1f), 38, FontStyles.Bold, Color.white);
            PlaceAtTop(headerTitle, 0f, 68f);
            var status = LocalizationManager.Get(record.synced ? "cert_status_synced" : "cert_status_pending");
            var headerStatus = CertificateViewer.AddLabel(card.transform, "Status", $"{LocalizationManager.Get("cert_score_format", record.score)}   •   {status}", new Vector2(0f, 1f), new Vector2(1f, 1f), 28, FontStyles.Normal, new Color(0.75f, 0.8f, 0.88f, 1f));
            PlaceAtTop(headerStatus, 66f, headerHeight);

            var picture = new GameObject("Picture", typeof(RectTransform), typeof(RawImage));
            picture.transform.SetParent(card.transform, false);
            var pictureRect = picture.GetComponent<RectTransform>();
            pictureRect.anchorMin = Vector2.zero;
            pictureRect.anchorMax = Vector2.one;
            pictureRect.offsetMin = new Vector2(8f, 8f);
            pictureRect.offsetMax = new Vector2(-8f, -headerHeight);
            var raw = picture.GetComponent<RawImage>();
            raw.texture = image;
            raw.raycastTarget = false;

            var savedRecord = record;
            var savedKey = key;
            card.GetComponent<Button>().onClick.AddListener(() =>
                CertificateViewer.Open(image, title, () => CertificateUI.SaveToGallery(image, savedKey, savedRecord), false));
        }

        go.AddComponent<CertificateStackOwner>().textures = textures;
        CertificateViewer.AddButton(go.transform, "Close", LocalizationManager.Get("close_button"), new Vector2(0.5f, 0.065f), new Vector2(420, 100), new Color(0.11f, 0.13f, 0.2f, 1f), 40, Close);
    }

    /// <summary>A training with no certificate yet: says what it takes to earn one.</summary>
    private static void AddPlaceholderCard(Transform parent, string key, string title)
    {
        var card = new GameObject("Card_" + key + "_notearned", typeof(RectTransform), typeof(Image), typeof(LayoutElement));
        card.transform.SetParent(parent, false);
        card.GetComponent<Image>().color = new Color(0.10f, 0.12f, 0.18f, 1f);
        card.GetComponent<LayoutElement>().preferredHeight = 230f;

        var headerTitle = CertificateViewer.AddLabel(card.transform, "Header", title, new Vector2(0f, 1f), new Vector2(1f, 1f), 38, FontStyles.Bold, Color.white);
        PlaceAtTop(headerTitle, 0f, 80f);
        var hint = CertificateViewer.AddLabel(card.transform, "Hint", LocalizationManager.Get("cert_not_earned_format", TrainingScoring.PassMark), new Vector2(0f, 0f), new Vector2(1f, 1f), 30, FontStyles.Normal, new Color(0.75f, 0.8f, 0.88f, 1f));
        var rect = hint.rectTransform;
        rect.offsetMin = new Vector2(20f, 12f);
        rect.offsetMax = new Vector2(-20f, -84f);
    }

    /// <summary>Pins a label to the top of its card, between two distances from the top edge (in canvas units).</summary>
    private static void PlaceAtTop(TMP_Text label, float fromTop, float toTop)
    {
        var rect = label.rectTransform;
        rect.pivot = new Vector2(0.5f, 1f);
        rect.offsetMin = new Vector2(20f, -toTop);
        rect.offsetMax = new Vector2(-20f, -fromTop);
        label.alignment = TextAlignmentOptions.MidlineLeft;
    }

    public static void Close()
    {
        if (panel != null)
        {
            Object.Destroy(panel);
            panel = null;
        }
    }
}

/// <summary>Frees the certificate textures rendered for the stack when its panel goes away.</summary>
public class CertificateStackOwner : MonoBehaviour
{
    public List<Texture2D> textures;

    private void OnDestroy()
    {
        if (textures == null) return;
        foreach (var texture in textures)
        {
            if (texture != null) Destroy(texture);
        }
    }
}
