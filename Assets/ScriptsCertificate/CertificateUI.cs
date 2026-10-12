using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Certificate rendering for the employee app. A passed training calls <see cref="ShowForTraining"/>:
/// it issues (or re-uses) the worker's stored certificate for that module, renders it and opens the
/// zoomable <see cref="CertificateViewer"/>. <see cref="RenderCertificate"/> composes the per-module
/// template (rendered from CertificateTemplates/*.svg) with the live name, date, score/ID and the QR
/// code, and is also what "My Certificates" and Save to Gallery use.
/// The results screens still hold a serialized reference of this type from earlier versions; it is unused.
/// </summary>
public class CertificateUI : MonoBehaviour
{
    private const string TraineeNamePrefKey = "ARBT_TraineeName";

    // Reference layout the artwork is authored against (matches the SVG template's 1607x1080).
    private const float TemplateWidth = 1607f;
    private const float TemplateHeight = 1080f;
    private const int ExportLayer = 31;

    private sealed class ArtworkRefs
    {
        public RectTransform frame;
        public RawImage image;
        public RawImage qr;
        public TMP_Text name;
        public TMP_Text date;
        public TMP_Text meta;
        public float nameMax, dateMax, metaMax;
    }

    /// <summary>
    /// Called by a results screen once the whole training (practice + quiz) is passed. The certificate
    /// is stable per worker and module, so replaying never issues a second one.
    /// </summary>
    public static void ShowForTraining(string forModuleKey, string forModuleName, int forScore)
    {
        string traineeName = ResolveTraineeName();
        if (string.IsNullOrWhiteSpace(traineeName))
        {
            Debug.LogWarning("CertificateUI: no logged-in worker, so no certificate was issued.");
            return;
        }

        CertificateRecord record = CertificateStore.GetOrCreate(forModuleKey, forScore, DateTime.UtcNow);
        SyncService.Instance?.RequestSyncSoon();

        Texture2D image = RenderCertificate(forModuleKey, traineeName, record);
        if (image == null) return;
        CertificateViewer.Open(image, forModuleName, () => SaveToGallery(image, forModuleKey, record), true);
    }

    public static string ResolveTraineeName()
    {
        return AuthService.IsLoggedIn
            ? AuthService.CurrentWorker.displayName
            : PlayerPrefs.GetString(TraineeNamePrefKey, string.Empty);
    }

    /// <summary>Encodes the rendered certificate and saves it to the phone's gallery.</summary>
    public static bool SaveToGallery(Texture2D image, string moduleKey, CertificateRecord record)
    {
        string id = record != null ? record.id : null;
        string shortId = string.IsNullOrEmpty(id) ? "cert" : id.Substring(0, Math.Min(8, id.Length));
        return GallerySaver.SavePng(image.EncodeToPNG(), $"Abhyas_{moduleKey}_{shortId}.png", out _);
    }

    private static string TemplateResourcePath(string key)
    {
        return key == "fire_safety"
            ? "Certificates/AbhyasCertificate_FireSafety"
            : "Certificates/AbhyasCertificate_MachineTraining";
    }

    /// <summary>
    /// Renders the certificate at the template's native resolution (2411x1620) on a temporary canvas and
    /// camera, independent of the phone's screen size. The caller owns (and must destroy) the texture.
    /// </summary>
    public static Texture2D RenderCertificate(string moduleKey, string traineeName, CertificateRecord record)
    {
        string path = TemplateResourcePath(moduleKey);
        var template = Resources.Load<Texture2D>(path);
        if (template == null)
        {
            Debug.LogError($"CertificateUI: template '{path}' is missing from Resources/Certificates.");
            return null;
        }

        int width = template.width;
        int height = template.height;
        var rt = new RenderTexture(width, height, 24, RenderTextureFormat.ARGB32);
        var camGo = new GameObject("CertificateExportCamera") { layer = ExportLayer };
        var canvasGo = new GameObject("CertificateExportCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler)) { layer = ExportLayer };
        Texture2D qrTexture = null;
        try
        {
            var cam = camGo.AddComponent<Camera>();
            cam.orthographic = true;
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = Color.white;
            cam.cullingMask = 1 << ExportLayer;
            cam.targetTexture = rt;
            cam.enabled = false;

            var canvas = canvasGo.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceCamera;
            canvas.worldCamera = cam;
            canvas.planeDistance = 1f;
            var scaler = canvasGo.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(TemplateWidth, TemplateHeight);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0f;

            qrTexture = CertificateService.RenderQrTexture(CertificateService.BuildVerificationUrl(record.id));
            ArtworkRefs art = BuildArtwork(canvasGo.transform);
            foreach (var t in canvasGo.GetComponentsInChildren<Transform>(true))
            {
                t.gameObject.layer = ExportLayer;
            }
            FillArtwork(art, template, traineeName, record, qrTexture);
            foreach (var text in canvasGo.GetComponentsInChildren<TMP_Text>(true))
            {
                text.ForceMeshUpdate();
            }

            cam.Render();
            var previous = RenderTexture.active;
            RenderTexture.active = rt;
            // Mip-mapped so the full-size image still looks clean when the stack shows it small.
            var result = new Texture2D(width, height, TextureFormat.RGB24, true) { filterMode = FilterMode.Trilinear };
            result.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            result.Apply(true);
            RenderTexture.active = previous;
            return result;
        }
        catch (Exception ex)
        {
            Debug.LogError($"CertificateUI: rendering the certificate image failed — {ex}");
            return null;
        }
        finally
        {
            DestroyImmediate(canvasGo);
            DestroyImmediate(camGo);
            rt.Release();
            DestroyImmediate(rt);
            if (qrTexture != null) DestroyImmediate(qrTexture);
        }
    }

    private static void FillArtwork(ArtworkRefs art, Texture2D template, string traineeName, CertificateRecord record, Texture2D qr)
    {
        art.image.texture = template;
        // Same Devanagari fix the rest of the app applies (pre-base "ि" reordering) so a Hindi name
        // reads like the localized UI does. Applied exactly once: FixForDisplay is not idempotent.
        art.name.text = DevanagariShaping.FixForDisplay(traineeName);
        DateTime issuedAt = DateTime.TryParse(record.issuedAtUtc, out var parsed) ? parsed : DateTime.UtcNow;
        art.date.text = issuedAt.ToLocalTime().ToString("dd MMMM yyyy");
        string id = record.id;
        string shortId = string.IsNullOrEmpty(id) ? "—" : id.Substring(0, Math.Min(8, id.Length)).ToUpperInvariant();
        art.meta.text = $"Score {record.score}/100   •   Certificate {shortId}";
        art.qr.texture = qr;
        ApplyFontScale(art);
    }

    /// <summary>
    /// Font sizes are authored for the 1607-unit-wide template; scale them to the frame's actual width.
    /// The width is derived from the parent's rect (the frame's own rect isn't laid out yet when created).
    /// </summary>
    private static void ApplyFontScale(ArtworkRefs art)
    {
        var parent = (RectTransform)art.frame.parent;
        Canvas.ForceUpdateCanvases();
        float availW = parent.rect.width * (art.frame.anchorMax.x - art.frame.anchorMin.x);
        float availH = parent.rect.height * (art.frame.anchorMax.y - art.frame.anchorMin.y);
        float frameW = Mathf.Min(availW, availH * (TemplateWidth / TemplateHeight));
        if (frameW <= 1f) return;
        float k = frameW / TemplateWidth;
        ScaleText(art.name, art.nameMax, k);
        ScaleText(art.date, art.dateMax, k);
        ScaleText(art.meta, art.metaMax, k);
    }

    private static void ScaleText(TMP_Text text, float baseMax, float k)
    {
        text.fontSizeMax = baseMax * k;
        text.fontSizeMin = Mathf.Max(6f, baseMax * 0.48f * k);
    }

    /// <summary>Creates a full-bleed certificate frame with its live text fields and QR.</summary>
    private static ArtworkRefs BuildArtwork(Transform parent)
    {
        var art = new ArtworkRefs();
        var frame = new GameObject("Certificate", typeof(RectTransform), typeof(RawImage));
        frame.transform.SetParent(parent, false);
        art.frame = frame.GetComponent<RectTransform>();
        art.image = frame.GetComponent<RawImage>();
        art.frame.anchorMin = Vector2.zero;
        art.frame.anchorMax = Vector2.one;
        art.frame.offsetMin = Vector2.zero;
        art.frame.offsetMax = Vector2.zero;

        art.nameMax = 52; art.dateMax = 25; art.metaMax = 17;
        art.name = AddText(frame.transform, "TraineeName", new Vector2(0.20f, 0.57f), new Vector2(0.80f, 0.67f), art.nameMax, FontStyles.Bold);
        art.date = AddText(frame.transform, "IssueDate", new Vector2(0.45f, 0.245f), new Vector2(0.68f, 0.315f), art.dateMax, FontStyles.Bold);
        art.date.alignment = TextAlignmentOptions.MidlineLeft;
        art.meta = AddText(frame.transform, "CertificateMeta", new Vector2(0.07f, 0.10f), new Vector2(0.40f, 0.17f), art.metaMax, FontStyles.Normal);

        var qr = new GameObject("VerificationQr", typeof(RectTransform), typeof(RawImage));
        qr.transform.SetParent(frame.transform, false);
        var qrRect = qr.GetComponent<RectTransform>();
        qrRect.anchorMin = new Vector2(0.802f, 0.085f);
        qrRect.anchorMax = new Vector2(0.912f, 0.260f);
        qrRect.offsetMin = Vector2.zero;
        qrRect.offsetMax = Vector2.zero;
        art.qr = qr.GetComponent<RawImage>();
        return art;
    }

    private static TMP_Text AddText(Transform parent, string objectName, Vector2 anchorMin, Vector2 anchorMax,
        float fontSize, FontStyles style)
    {
        var go = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = anchorMin;
        rect.anchorMax = anchorMax;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
        var text = go.GetComponent<TextMeshProUGUI>();
        text.alignment = TextAlignmentOptions.Center;
        text.enableAutoSizing = true;
        text.fontSizeMax = fontSize;
        text.fontSizeMin = Mathf.Max(11, fontSize * 0.48f);
        text.fontStyle = style;
        text.color = new Color32(46, 58, 81, 255);
        text.enableWordWrapping = true;
        return text;
    }
}
