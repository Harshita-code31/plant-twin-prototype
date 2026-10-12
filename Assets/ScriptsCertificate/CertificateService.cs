using System;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;
using Net.Codecrete.QrCodeGenerator;

/// <summary>
/// Decoded contents of a certificate payload, returned by <see cref="CertificateService.TryVerify"/>.
/// </summary>
public struct CertificateInfo
{
    public string traineeName;
    public string moduleName;
    public int score;
    public DateTime completedAt;
}

/// <summary>
/// Generates and verifies QR-based training certificates entirely offline: no server,
/// no blockchain. The QR payload is a compact pipe-delimited string carrying its own
/// checksum, so any device running this app (or a paste-the-text verify screen) can
/// confirm a certificate wasn't fabricated or altered without looking anything up.
/// <para>
/// The checksum is an HMAC-SHA256 keyed with a fixed app-embedded secret, truncated to
/// 8 hex characters. This deters casual tampering (editing the score in a text editor,
/// say) but is not a cryptographic guarantee against a determined attacker who has the
/// app's source — appropriate for a training simulator's completion record, not a
/// legal credential. Replacing this with a real signature (e.g. a per-installation
/// asymmetric key, or a server-issued signature) would close that gap if ever needed.
/// </para>
/// </summary>
public static class CertificateService
{
    public const string VerificationBaseUrl = "https://namannpatel.github.io/AbhyasAR/#/verify/";
    private const string Prefix = "AR-CERT";
    private const string PayloadVersion = "v1";
    private const int ExpectedFieldCount = 7; // Prefix, version, name, module, score, timestamp, checksum

    // Fixed app-embedded key — see the tampering-resistance note in the class summary.
    private static readonly byte[] AppKey = Encoding.UTF8.GetBytes("ARBT-SmartEducation-2026-CertKey-v1");

    /// <summary>
    /// Builds a self-verifying certificate payload for the given completed attempt.
    /// </summary>
    public static string GenerateCertificate(string traineeName, string moduleName, int score, DateTime completedAt)
    {
        string safeName = Sanitize(traineeName);
        string safeModule = Sanitize(moduleName);
        long unixTime = new DateTimeOffset(completedAt.ToUniversalTime()).ToUnixTimeSeconds();
        string body = $"{Prefix}|{PayloadVersion}|{safeName}|{safeModule}|{score}|{unixTime}";
        string checksum = ComputeChecksum(body);
        return $"{body}|{checksum}";
    }

    /// <summary>
    /// Verifies a certificate payload (as produced by <see cref="GenerateCertificate"/> or
    /// decoded from its QR code) and decodes its fields if valid.
    /// </summary>
    public static bool TryVerify(string payload, out CertificateInfo info)
    {
        info = default;
        if (string.IsNullOrWhiteSpace(payload))
        {
            return false;
        }

        string[] parts = payload.Trim().Split('|');
        if (parts.Length != ExpectedFieldCount)
        {
            return false;
        }
        if (parts[0] != Prefix || parts[1] != PayloadVersion)
        {
            return false;
        }

        string body = string.Join("|", parts, 0, ExpectedFieldCount - 1);
        string providedChecksum = parts[ExpectedFieldCount - 1];
        string expectedChecksum = ComputeChecksum(body);
        if (!string.Equals(providedChecksum, expectedChecksum, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (!int.TryParse(parts[4], out int score))
        {
            return false;
        }
        if (!long.TryParse(parts[5], out long unixTime))
        {
            return false;
        }

        info = new CertificateInfo
        {
            traineeName = parts[2],
            moduleName = parts[3],
            score = score,
            completedAt = DateTimeOffset.FromUnixTimeSeconds(unixTime).UtcDateTime
        };
        return true;
    }

    /// <summary>
    /// Renders a QR code for the given payload as an opaque, square Texture2D
    /// (black modules on white, point-filtered so it stays crisp when scaled up in the UI).
    /// </summary>
    public static Texture2D RenderQrTexture(string payload, int pixelsPerModule = 8, int borderModules = 4)
    {
        QrCode qr = QrCode.EncodeText(payload, QrCode.Ecc.Medium);
        int size = qr.Size + borderModules * 2;
        int texSize = size * pixelsPerModule;

        var texture = new Texture2D(texSize, texSize, TextureFormat.RGB24, false)
        {
            filterMode = FilterMode.Point,
            wrapMode = TextureWrapMode.Clamp
        };

        var dark = new Color32(0, 0, 0, 255);
        var light = new Color32(255, 255, 255, 255);
        var pixels = new Color32[texSize * texSize];

        for (var y = 0; y < texSize; y++)
        {
            // Texture2D row 0 is the bottom of the image; flip so the QR reads right-side-up
            // once applied to a RawImage/Texture.
            var flippedY = texSize - 1 - y;
            var moduleY = y / pixelsPerModule - borderModules;
            for (var x = 0; x < texSize; x++)
            {
                var moduleX = x / pixelsPerModule - borderModules;
                // GetModule returns false (light) for any coordinate outside the code's
                // bounds, so the border falls out of this call for free.
                pixels[flippedY * texSize + x] = qr.GetModule(moduleX, moduleY) ? dark : light;
            }
        }

        texture.SetPixels32(pixels);
        texture.Apply();
        return texture;
    }

    /// <summary>
    /// Public HTTPS URL encoded by new certificates. A normal phone camera can open it; the
    /// dashboard then asks Supabase for the server-backed issuance record identified by the UUID.
    /// </summary>
    public static string BuildVerificationUrl(string certificateId)
    {
        return VerificationBaseUrl + Uri.EscapeDataString(certificateId ?? string.Empty);
    }

    private static string ComputeChecksum(string body)
    {
        using (var hmac = new HMACSHA256(AppKey))
        {
            byte[] hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(body));
            var sb = new StringBuilder(8);
            for (var i = 0; i < 4; i++) // 4 bytes -> 8 hex chars
            {
                sb.Append(hash[i].ToString("x2"));
            }
            return sb.ToString();
        }
    }

    // Strips the field separator out of free-text fields so a name/module containing
    // "|" can't be mistaken for extra payload fields.
    private static string Sanitize(string value)
    {
        return string.IsNullOrEmpty(value) ? "-" : value.Replace("|", "/").Trim();
    }
}
