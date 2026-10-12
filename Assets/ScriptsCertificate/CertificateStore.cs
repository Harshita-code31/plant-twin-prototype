using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class CertificateRecord
{
    public string id;
    public string module;
    public int score;
    public string issuedAtUtc;
    public bool synced;
}

/// <summary>
/// Offline-first certificate outbox. A worker gets one stable certificate per module on a
/// device; SyncService submits it after the passing attempts have reached Supabase.
/// </summary>
public static class CertificateStore
{
    public static event Action OnRecordAdded;

    [Serializable]
    private class CertificateFile
    {
        public List<CertificateRecord> records = new List<CertificateRecord>();
    }

    private static string CertificateDir => Path.Combine(Application.persistentDataPath, "certificates");

    public static CertificateRecord GetOrCreate(string module, int score, DateTime issuedAtUtc)
    {
        if (!AuthService.IsLoggedIn)
        {
            return new CertificateRecord
            {
                id = Guid.NewGuid().ToString(),
                module = module,
                score = score,
                issuedAtUtc = issuedAtUtc.ToUniversalTime().ToString("o"),
            };
        }

        string workerId = AuthService.CurrentWorker.workerId;
        var file = Load(workerId);
        var existing = file.records.Find(r => r.module == module);
        if (existing != null)
        {
            return existing;
        }

        var record = new CertificateRecord
        {
            id = Guid.NewGuid().ToString(),
            module = module,
            score = Mathf.Clamp(score, TrainingScoring.PassMark, 100),
            issuedAtUtc = issuedAtUtc.ToUniversalTime().ToString("o"),
        };
        file.records.Add(record);
        Save(workerId, file);
        OnRecordAdded?.Invoke();
        return record;
    }

    public static IEnumerable<string> WorkerIdsWithData()
    {
        if (!Directory.Exists(CertificateDir)) yield break;
        foreach (string path in Directory.GetFiles(CertificateDir, "*.json"))
        {
            yield return Path.GetFileNameWithoutExtension(path);
        }
    }

    /// <summary>Certificates earned by this worker on this device, including ones awaiting sync.</summary>
    public static List<CertificateRecord> GetForWorker(string workerId)
    {
        return string.IsNullOrEmpty(workerId)
            ? new List<CertificateRecord>()
            : new List<CertificateRecord>(Load(workerId).records);
    }

    /// <summary>Cache certificates issued on another device for offline viewing.</summary>
    public static void MergeFromServer(string workerId, IEnumerable<CertificateRecord> incoming)
    {
        if (string.IsNullOrEmpty(workerId) || incoming == null) return;
        var file = Load(workerId);
        bool changed = false;
        foreach (var record in incoming)
        {
            if (record == null || string.IsNullOrEmpty(record.id)) continue;
            var existing = file.records.Find(r => r.id == record.id);
            if (existing != null)
            {
                if (!existing.synced) { existing.synced = true; changed = true; }
            }
            else
            {
                record.synced = true;
                file.records.Add(record);
                changed = true;
            }
        }
        if (changed) Save(workerId, file);
    }

    public static List<CertificateRecord> GetUnsynced(string workerId, int max)
    {
        var result = new List<CertificateRecord>();
        foreach (var record in Load(workerId).records)
        {
            if (record.synced) continue;
            result.Add(record);
            if (result.Count >= max) break;
        }
        return result;
    }

    public static int CountUnsynced(string workerId)
    {
        return Load(workerId).records.FindAll(r => !r.synced).Count;
    }

    public static void MarkSynced(string workerId, ICollection<string> ids)
    {
        if (ids.Count == 0) return;
        var file = Load(workerId);
        foreach (var record in file.records)
        {
            if (ids.Contains(record.id)) record.synced = true;
        }
        Save(workerId, file);
    }

    private static string PathFor(string workerId) => Path.Combine(CertificateDir, workerId + ".json");

    private static CertificateFile Load(string workerId)
    {
        string path = PathFor(workerId);
        if (!File.Exists(path)) return new CertificateFile();
        try
        {
            return JsonUtility.FromJson<CertificateFile>(File.ReadAllText(path)) ?? new CertificateFile();
        }
        catch (Exception ex)
        {
            Debug.LogError($"CertificateStore: corrupt certificate file {path} — {ex.Message}");
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks);
            return new CertificateFile();
        }
    }

    private static void Save(string workerId, CertificateFile file)
    {
        Directory.CreateDirectory(CertificateDir);
        SafeFile.WriteAllText(PathFor(workerId), JsonUtility.ToJson(file));
    }
}
