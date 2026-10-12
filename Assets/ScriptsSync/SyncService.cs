using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Uploads ProgressStore's unsynced attempts to Supabase whenever the device is online.
/// Created automatically at startup and survives scene loads. Syncs every worker who has
/// used this device (shared factory tablets), not just the one logged in now.
/// </summary>
public class SyncService : MonoBehaviour
{
    private const int BatchSize = 50;
    private const float BaseRetrySeconds = 30f;
    private const float MaxRetrySeconds = 300f;

    public static SyncService Instance { get; private set; }

    /// <summary>Raised whenever pending count / syncing state / last-sync time may have changed.</summary>
    public static event Action OnStatusChanged;

    /// <summary>Unsynced attempts belonging to the logged-in worker.</summary>
    public static int PendingCount { get; private set; }
    public static bool IsSyncing { get; private set; }
    public static DateTime? LastSyncUtc { get; private set; }
    /// <summary>The logged-in worker has pending attempts but their token was rejected; an online login fixes it.</summary>
    public static bool NeedsRelogin => AuthService.IsLoggedIn && string.IsNullOrEmpty(AuthService.CurrentWorker.token) && PendingCount > 0;

    private float nextAttemptAt;
    private float retryDelay = BaseRetrySeconds;

    [Serializable]
    private class AttemptDto
    {
        public string id;
        public string module;
        public string scenario;
        public bool passed;
        public int score;
        public float elapsed_seconds;
        public string details_json;
        public string completed_at;
    }

    [Serializable]
    private class SubmitRequest
    {
        public string p_token;
        public AttemptDto[] p_attempts;
    }

    [Serializable]
    private class SubmitResponse
    {
        public string[] accepted;
    }

    [Serializable]
    private class CertificateDto
    {
        public string id;
        public string module;
        public int score;
        public string issued_at;
    }

    [Serializable]
    private class CertificateSubmitRequest
    {
        public string p_token;
        public CertificateDto[] p_certificates;
    }

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        if (Instance != null)
        {
            return;
        }
        var go = new GameObject(nameof(SyncService));
        DontDestroyOnLoad(go);
        Instance = go.AddComponent<SyncService>();
    }

    public void RequestSyncSoon()
    {
        nextAttemptAt = 0f;
        retryDelay = BaseRetrySeconds;
    }

    private void OnEnable()
    {
        ProgressStore.OnRecordAdded += HandleLocalChange;
        CertificateStore.OnRecordAdded += HandleLocalChange;
        AuthService.OnSessionChanged += HandleLocalChange;
    }

    private void OnDisable()
    {
        ProgressStore.OnRecordAdded -= HandleLocalChange;
        CertificateStore.OnRecordAdded -= HandleLocalChange;
        AuthService.OnSessionChanged -= HandleLocalChange;
    }

    private void Start()
    {
        AuthService.TryRestoreSession();
        RefreshStatus();
        StartCoroutine(SyncLoop());
    }

    private void OnApplicationFocus(bool hasFocus)
    {
        if (hasFocus)
        {
            RequestSyncSoon();
        }
    }

    private void HandleLocalChange()
    {
        RefreshStatus();
        RequestSyncSoon();
    }

    private IEnumerator SyncLoop()
    {
        var tick = new WaitForSecondsRealtime(1f);
        NetworkReachability lastReachability = Application.internetReachability;
        while (true)
        {
            // Offline retries back off up to 5 minutes; sync the moment connectivity returns instead.
            NetworkReachability reachability = Application.internetReachability;
            if (lastReachability == NetworkReachability.NotReachable && reachability != NetworkReachability.NotReachable)
            {
                RequestSyncSoon();
            }
            lastReachability = reachability;

            if (Time.realtimeSinceStartup >= nextAttemptAt)
            {
                yield return SyncAll();
            }
            yield return tick;
        }
    }

    private IEnumerator SyncAll()
    {
        var tokens = new Dictionary<string, string>();
        foreach (var cached in AuthService.LoadAllCaches())
        {
            if (!string.IsNullOrEmpty(cached.token))
            {
                tokens[cached.workerId] = cached.token;
            }
        }

        var workers = new HashSet<string>();
        foreach (string workerId in ProgressStore.WorkerIdsWithData())
        {
            if (tokens.ContainsKey(workerId) && ProgressStore.CountUnsynced(workerId) > 0)
            {
                workers.Add(workerId);
            }
        }
        foreach (string workerId in CertificateStore.WorkerIdsWithData())
        {
            if (tokens.ContainsKey(workerId) && CertificateStore.CountUnsynced(workerId) > 0)
            {
                workers.Add(workerId);
            }
        }

        if (workers.Count == 0)
        {
            nextAttemptAt = float.MaxValue; // idle until a new record, login, or app focus
            yield break;
        }

        if (Application.internetReachability == NetworkReachability.NotReachable)
        {
            ScheduleRetry();
            yield break;
        }

        IsSyncing = true;
        OnStatusChanged?.Invoke();

        bool networkFailed = false;
        foreach (string workerId in workers)
        {
            List<ProgressRecord> batch;
            while (!networkFailed && (batch = ProgressStore.GetUnsynced(workerId, BatchSize)).Count > 0)
            {
                RpcResult rpc = default;
                yield return SupabaseRpc.Call("submit_attempts", BuildRequest(tokens[workerId], batch), r => rpc = r);

                if (rpc.Ok)
                {
                    var accepted = JsonUtility.FromJson<SubmitResponse>(rpc.body)?.accepted ?? Array.Empty<string>();
                    ProgressStore.MarkSynced(workerId, new HashSet<string>(accepted));
                    LastSyncUtc = DateTime.UtcNow;
                    if (accepted.Length < batch.Count)
                    {
                        Debug.LogWarning($"SyncService: server accepted {accepted.Length}/{batch.Count} attempts for {workerId}; the rest will be retried.");
                        break;
                    }
                    continue;
                }

                if (rpc.status == RpcStatus.NetworkError)
                {
                    networkFailed = true;
                }
                else if (rpc.errorMessage == "invalid_token")
                {
                    AuthService.InvalidateToken(workerId);
                }
                else
                {
                    Debug.LogError($"SyncService: server rejected upload for {workerId}: {rpc.errorMessage}");
                }
                break;
            }

            // Certificates are deliberately uploaded after attempts: the database accepts an
            // issuance only when it can see the passed practical and quiz records behind it.
            List<CertificateRecord> certificateBatch;
            while (!networkFailed && (certificateBatch = CertificateStore.GetUnsynced(workerId, 20)).Count > 0)
            {
                RpcResult rpc = default;
                yield return SupabaseRpc.Call("submit_certificates",
                    BuildCertificateRequest(tokens[workerId], certificateBatch), r => rpc = r);

                if (rpc.Ok)
                {
                    var accepted = JsonUtility.FromJson<SubmitResponse>(rpc.body)?.accepted ?? Array.Empty<string>();
                    CertificateStore.MarkSynced(workerId, new HashSet<string>(accepted));
                    LastSyncUtc = DateTime.UtcNow;
                    if (accepted.Length < certificateBatch.Count)
                    {
                        Debug.LogWarning($"SyncService: server accepted {accepted.Length}/{certificateBatch.Count} certificates for {workerId}; the rest will be retried.");
                        break;
                    }
                    continue;
                }

                if (rpc.status == RpcStatus.NetworkError)
                {
                    networkFailed = true;
                }
                else if (rpc.errorMessage == "invalid_token")
                {
                    AuthService.InvalidateToken(workerId);
                }
                else
                {
                    Debug.LogError($"SyncService: server rejected certificate upload for {workerId}: {rpc.errorMessage}");
                }
                break;
            }
            if (networkFailed)
            {
                break;
            }
        }

        IsSyncing = false;
        RefreshStatus();

        if (networkFailed)
        {
            ScheduleRetry();
        }
        else
        {
            retryDelay = BaseRetrySeconds;
            nextAttemptAt = Time.realtimeSinceStartup + BaseRetrySeconds;
        }
    }

    private void ScheduleRetry()
    {
        nextAttemptAt = Time.realtimeSinceStartup + retryDelay;
        retryDelay = Mathf.Min(retryDelay * 2f, MaxRetrySeconds);
    }

    private static string BuildRequest(string token, List<ProgressRecord> batch)
    {
        var attempts = new AttemptDto[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            var r = batch[i];
            attempts[i] = new AttemptDto
            {
                id = r.id,
                module = r.module,
                scenario = r.scenario,
                passed = r.passed,
                score = r.score,
                elapsed_seconds = r.elapsedSeconds,
                details_json = r.detailsJson,
                completed_at = r.completedAtUtc,
            };
        }
        return JsonUtility.ToJson(new SubmitRequest { p_token = token, p_attempts = attempts });
    }

    private static string BuildCertificateRequest(string token, List<CertificateRecord> batch)
    {
        var certificates = new CertificateDto[batch.Count];
        for (int i = 0; i < batch.Count; i++)
        {
            var record = batch[i];
            certificates[i] = new CertificateDto
            {
                id = record.id,
                module = record.module,
                score = record.score,
                issued_at = record.issuedAtUtc,
            };
        }
        return JsonUtility.ToJson(new CertificateSubmitRequest
        {
            p_token = token,
            p_certificates = certificates,
        });
    }

    private static void RefreshStatus()
    {
        PendingCount = AuthService.IsLoggedIn
            ? ProgressStore.CountUnsynced(AuthService.CurrentWorker.workerId)
              + CertificateStore.CountUnsynced(AuthService.CurrentWorker.workerId)
            : 0;
        OnStatusChanged?.Invoke();
    }
}
