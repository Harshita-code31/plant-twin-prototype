using System;
using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Entry point for MainMenu's "View Certificate" button. Opens the signed-in worker's certificates
/// (see <see cref="MyCertificatesUI"/>: stacked list, zoomable viewer, Save to Gallery) straight from
/// what is saved on the device, then -- when online -- refreshes them from Supabase so certificates
/// issued on another device appear too, and redraws the list if it is still open.
/// The serialized legacy paste-a-payload verification controls are no longer shown.
/// </summary>
public class CertificateVerifyUI : MonoBehaviour
{
    [Serializable] private class CertificateListRequest { public string p_token; }
    [Serializable] private class ServerCertificate { public string id; public string module; public int score; public string issued_at; }
    [Serializable] private class CertificateListResponse { public ServerCertificate[] certificates; }

    [SerializeField] private GameObject panelRoot;
    [SerializeField] private TMP_InputField payloadInput;
    [SerializeField] private TMP_Text resultText;
    [SerializeField] private Button verifyButton;
    [SerializeField] private Button closeButton;

    private bool showing;

    private void Awake()
    {
        // This script lives on panelRoot, which is saved inactive, so Awake first runs during
        // Show()'s SetActive(true) -- hiding unconditionally here undid the first tap.
        if (panelRoot != null && !showing)
        {
            panelRoot.SetActive(false);
        }
    }

    private void OnEnable()
    {
        verifyButton?.onClick.AddListener(HandleVerify);
        closeButton?.onClick.AddListener(HandleClose);
    }

    private void OnDisable()
    {
        verifyButton?.onClick.RemoveListener(HandleVerify);
        closeButton?.onClick.RemoveListener(HandleClose);
    }

    /// <summary>Called by MainMenu's "View Certificate" button.</summary>
    public void Show()
    {
        MyCertificatesUI.Open(); // instant: whatever is already saved on this device

        if (AuthService.IsLoggedIn && !string.IsNullOrEmpty(AuthService.CurrentWorker.token))
        {
            StartCoroutine(RefreshFromServer(AuthService.CurrentWorker.workerId, AuthService.CurrentWorker.token));
        }
    }

    private IEnumerator RefreshFromServer(string workerId, string token)
    {
        RpcResult result = default;
        yield return SupabaseRpc.Call("worker_list_certificates",
            JsonUtility.ToJson(new CertificateListRequest { p_token = token }), r => result = r);
        if (!result.Ok) yield break; // Cached certificates remain available offline.

        CertificateListResponse response;
        try { response = JsonUtility.FromJson<CertificateListResponse>(result.body); }
        catch (ArgumentException) { yield break; }
        if (response?.certificates == null) yield break;

        var records = new List<CertificateRecord>();
        foreach (var item in response.certificates)
        {
            records.Add(new CertificateRecord
            {
                id = item.id, module = item.module, score = item.score, issuedAtUtc = item.issued_at, synced = true,
            });
        }
        CertificateStore.MergeFromServer(workerId, records);

        // Redraw only if this worker's list is still the thing on screen: rebuilding underneath an open
        // certificate viewer would free the texture it is showing.
        if (MyCertificatesUI.IsOpen && !CertificateViewer.IsOpen
            && AuthService.IsLoggedIn && AuthService.CurrentWorker.workerId == workerId)
        {
            MyCertificatesUI.Open();
        }
    }

    // Legacy offline payload check: kept wired to the old serialized controls, which are never shown.
    private void HandleVerify()
    {
        string payload = payloadInput != null ? payloadInput.text : string.Empty;
        if (resultText == null)
        {
            return;
        }

        if (CertificateService.TryVerify(payload, out CertificateInfo info))
        {
            resultText.text = LocalizationManager.Get("cert_verify_valid_format",
                info.traineeName, info.moduleName, info.score, info.completedAt.ToString("yyyy-MM-dd HH:mm"));
        }
        else
        {
            resultText.text = LocalizationManager.Get("cert_verify_invalid");
        }
    }

    private void HandleClose()
    {
        if (panelRoot != null)
        {
            panelRoot.SetActive(false);
        }
    }
}
