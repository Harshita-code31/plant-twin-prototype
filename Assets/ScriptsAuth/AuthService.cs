using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;

public enum LoginOutcome
{
    SuccessOnline,
    SuccessOffline,
    MissingFields,
    InvalidCredentials,
    FirstLoginNeedsInternet,
}

[Serializable]
public class CachedWorker
{
    public string workerId;
    public string workerCode;
    public string displayName;
    public string token;          // empty once the server has rejected it — progress waits for an online re-login
    public string passwordSalt;   // base64
    public string passwordHash;   // base64 PBKDF2-SHA256
    public int iterations;
}

/// <summary>
/// Worker login. The first login on a device goes to Supabase (worker_login RPC); on
/// success a PBKDF2 hash of the password plus the device token is cached under
/// persistentDataPath/auth, so later logins work offline. The plaintext password is never stored.
/// </summary>
public static class AuthService
{
    private const string CurrentWorkerPrefKey = "ARBT_CurrentWorkerCode";
    private const int Pbkdf2Iterations = 100_000;
    private const int SaltBytes = 16;
    private const int HashBytes = 32;

    public static CachedWorker CurrentWorker { get; private set; }
    public static bool IsLoggedIn => CurrentWorker != null;

    public static event Action OnSessionChanged;

    private static string AuthDir => Path.Combine(Application.persistentDataPath, "auth");

    [Serializable]
    private class LoginRequest
    {
        public string p_code;
        public string p_password;
    }

    [Serializable]
    private class LoginResponse
    {
        public string worker_id;
        public string worker_code;
        public string display_name;
        public string token;
    }

    public static string NormalizeCode(string code) => (code ?? string.Empty).Trim().ToUpperInvariant();

    /// <summary>Restores the last logged-in worker without asking for the password again.</summary>
    public static bool TryRestoreSession()
    {
        if (IsLoggedIn)
        {
            return true;
        }
        string code = PlayerPrefs.GetString(CurrentWorkerPrefKey, string.Empty);
        if (string.IsNullOrEmpty(code))
        {
            return false;
        }
        var cached = LoadCache(code);
        if (cached == null)
        {
            return false;
        }
        SetCurrent(cached);
        return true;
    }

    public static IEnumerator Login(string rawCode, string password, Action<LoginOutcome> onDone)
    {
        string code = NormalizeCode(rawCode);
        if (string.IsNullOrEmpty(code) || string.IsNullOrEmpty(password))
        {
            onDone(LoginOutcome.MissingFields);
            yield break;
        }

        CachedWorker cached = LoadCache(code);

        RpcResult rpc = default;
        string body = JsonUtility.ToJson(new LoginRequest { p_code = code, p_password = password });
        yield return SupabaseRpc.Call("worker_login", body, r => rpc = r);

        if (rpc.Ok)
        {
            var response = JsonUtility.FromJson<LoginResponse>(rpc.body);
            byte[] salt = new byte[SaltBytes];
            using (var rng = RandomNumberGenerator.Create())
            {
                rng.GetBytes(salt);
            }

            Task<byte[]> hashTask = Task.Run(() => Derive(password, salt, Pbkdf2Iterations));
            yield return new WaitUntil(() => hashTask.IsCompleted);

            var fresh = new CachedWorker
            {
                workerId = response.worker_id,
                workerCode = response.worker_code,
                displayName = response.display_name,
                token = response.token,
                passwordSalt = Convert.ToBase64String(salt),
                passwordHash = Convert.ToBase64String(hashTask.Result),
                iterations = Pbkdf2Iterations,
            };
            SaveCache(fresh);
            SetCurrent(fresh);
            onDone(LoginOutcome.SuccessOnline);
            yield break;
        }

        if (cached == null)
        {
            onDone(rpc.status == RpcStatus.ServerRejected ? LoginOutcome.InvalidCredentials : LoginOutcome.FirstLoginNeedsInternet);
            yield break;
        }

        Task<bool> verifyTask = Task.Run(() => Verify(cached, password));
        yield return new WaitUntil(() => verifyTask.IsCompleted);
        bool matchesCache = verifyTask.Result;

        if (rpc.status == RpcStatus.ServerRejected)
        {
            // The server says no but the cached password matches: the account was reset or
            // deactivated since this device last saw it, so the cache must not keep working offline.
            if (matchesCache)
            {
                DeleteCache(code);
            }
            onDone(LoginOutcome.InvalidCredentials);
            yield break;
        }

        if (!matchesCache)
        {
            onDone(LoginOutcome.InvalidCredentials);
            yield break;
        }

        SetCurrent(cached);
        onDone(LoginOutcome.SuccessOffline);
    }

    public static void Logout()
    {
        CurrentWorker = null;
        PlayerPrefs.DeleteKey(CurrentWorkerPrefKey);
        PlayerPrefs.Save();
        OnSessionChanged?.Invoke();
    }

    /// <summary>Called by SyncService when the server rejects a token (password reset / deactivated / expired).</summary>
    public static void InvalidateToken(string workerId)
    {
        foreach (var cached in LoadAllCaches())
        {
            if (cached.workerId != workerId)
            {
                continue;
            }
            cached.token = string.Empty;
            SaveCache(cached);
            if (CurrentWorker != null && CurrentWorker.workerId == workerId)
            {
                CurrentWorker.token = string.Empty;
                OnSessionChanged?.Invoke();
            }
        }
    }

    public static List<CachedWorker> LoadAllCaches()
    {
        var list = new List<CachedWorker>();
        if (!Directory.Exists(AuthDir))
        {
            return list;
        }
        foreach (string file in Directory.GetFiles(AuthDir, "*.json"))
        {
            var cached = ReadCacheFile(file);
            if (cached != null)
            {
                list.Add(cached);
            }
        }
        return list;
    }

    private static void SetCurrent(CachedWorker worker)
    {
        CurrentWorker = worker;
        PlayerPrefs.SetString(CurrentWorkerPrefKey, worker.workerCode);
        PlayerPrefs.Save();
        OnSessionChanged?.Invoke();
    }

    private static byte[] Derive(string password, byte[] salt, int iterations)
    {
        using var pbkdf2 = new Rfc2898DeriveBytes(Encoding.UTF8.GetBytes(password), salt, iterations, HashAlgorithmName.SHA256);
        return pbkdf2.GetBytes(HashBytes);
    }

    private static bool Verify(CachedWorker cached, string password)
    {
        try
        {
            byte[] salt = Convert.FromBase64String(cached.passwordSalt);
            byte[] expected = Convert.FromBase64String(cached.passwordHash);
            byte[] actual = Derive(password, salt, cached.iterations > 0 ? cached.iterations : Pbkdf2Iterations);
            return FixedTimeEquals(expected, actual);
        }
        catch (FormatException)
        {
            return false;
        }
    }

    private static bool FixedTimeEquals(byte[] a, byte[] b)
    {
        if (a.Length != b.Length)
        {
            return false;
        }
        int diff = 0;
        for (int i = 0; i < a.Length; i++)
        {
            diff |= a[i] ^ b[i];
        }
        return diff == 0;
    }

    private static string CachePath(string code)
    {
        var safe = new StringBuilder();
        foreach (char c in NormalizeCode(code))
        {
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                safe.Append(c);
            }
        }
        return Path.Combine(AuthDir, safe + ".json");
    }

    private static CachedWorker LoadCache(string code) => ReadCacheFile(CachePath(code));

    private static CachedWorker ReadCacheFile(string path)
    {
        if (!File.Exists(path))
        {
            return null;
        }
        try
        {
            var cached = JsonUtility.FromJson<CachedWorker>(File.ReadAllText(path));
            return string.IsNullOrEmpty(cached?.workerId) ? null : cached;
        }
        catch (Exception ex)
        {
            Debug.LogWarning($"AuthService: unreadable auth cache {path} — {ex.Message}");
            return null;
        }
    }

    private static void SaveCache(CachedWorker worker)
    {
        Directory.CreateDirectory(AuthDir);
        SafeFile.WriteAllText(CachePath(worker.workerCode), JsonUtility.ToJson(worker));
    }

    private static void DeleteCache(string code)
    {
        string path = CachePath(code);
        if (File.Exists(path))
        {
            File.Delete(path);
        }
    }
}
