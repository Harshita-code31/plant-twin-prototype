using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

[Serializable]
public class ProgressRecord
{
    public string id;             // Guid generated on device — the server dedupes on it
    public string module;
    public string scenario;
    public bool passed;
    public int score;
    public float elapsedSeconds;
    public string detailsJson;
    public string completedAtUtc; // ISO-8601
    public bool synced;
}

/// <summary>
/// Per-worker outbox of completed training attempts at persistentDataPath/progress/&lt;workerId&gt;.json.
/// Every attempt lands here first, online or not; SyncService uploads unsynced records later.
/// </summary>
public static class ProgressStore
{
    private const int MaxSyncedRecordsKept = 200;

    public static event Action OnRecordAdded;

    [Serializable]
    private class ProgressFile
    {
        public List<ProgressRecord> records = new List<ProgressRecord>();
    }

    private static string ProgressDir => Path.Combine(Application.persistentDataPath, "progress");

    /// <summary>Saves an attempt for the logged-in worker. Returns false (and saves nothing) when nobody is logged in.</summary>
    public static bool Record(string module, string scenario, bool passed, int score, float elapsedSeconds, string detailsJson)
    {
        if (!AuthService.IsLoggedIn)
        {
            Debug.Log($"ProgressStore: no worker logged in, not recording {module}/{scenario}.");
            return false;
        }

        string workerId = AuthService.CurrentWorker.workerId;
        var file = Load(workerId);
        file.records.Add(new ProgressRecord
        {
            id = Guid.NewGuid().ToString(),
            module = module,
            scenario = scenario,
            passed = passed,
            score = score,
            elapsedSeconds = elapsedSeconds,
            detailsJson = detailsJson,
            completedAtUtc = DateTime.UtcNow.ToString("o"),
        });
        Save(workerId, file);

        OnRecordAdded?.Invoke();
        return true;
    }

    public static IEnumerable<string> WorkerIdsWithData()
    {
        if (!Directory.Exists(ProgressDir))
        {
            yield break;
        }
        foreach (string path in Directory.GetFiles(ProgressDir, "*.json"))
        {
            yield return Path.GetFileNameWithoutExtension(path);
        }
    }

    public static List<ProgressRecord> GetUnsynced(string workerId, int max)
    {
        var result = new List<ProgressRecord>();
        foreach (var record in Load(workerId).records)
        {
            if (!record.synced)
            {
                result.Add(record);
                if (result.Count >= max)
                {
                    break;
                }
            }
        }
        return result;
    }

    public static int CountUnsynced(string workerId)
    {
        int count = 0;
        foreach (var record in Load(workerId).records)
        {
            if (!record.synced)
            {
                count++;
            }
        }
        return count;
    }

    public static void MarkSynced(string workerId, ICollection<string> ids)
    {
        if (ids.Count == 0)
        {
            return;
        }
        var file = Load(workerId);
        foreach (var record in file.records)
        {
            if (ids.Contains(record.id))
            {
                record.synced = true;
            }
        }

        // Keep the file small: drop the oldest already-uploaded records beyond the cap.
        int syncedCount = file.records.FindAll(r => r.synced).Count;
        for (int i = 0; i < file.records.Count && syncedCount > MaxSyncedRecordsKept;)
        {
            if (file.records[i].synced)
            {
                file.records.RemoveAt(i);
                syncedCount--;
            }
            else
            {
                i++;
            }
        }
        Save(workerId, file);
    }

    private static string PathFor(string workerId) => Path.Combine(ProgressDir, workerId + ".json");

    private static ProgressFile Load(string workerId)
    {
        string path = PathFor(workerId);
        if (!File.Exists(path))
        {
            return new ProgressFile();
        }
        try
        {
            return JsonUtility.FromJson<ProgressFile>(File.ReadAllText(path)) ?? new ProgressFile();
        }
        catch (Exception ex)
        {
            // Keep the unreadable file aside instead of overwriting it, so no attempts are silently lost.
            Debug.LogError($"ProgressStore: corrupt progress file {path} — {ex.Message}");
            File.Move(path, path + ".corrupt-" + DateTime.UtcNow.Ticks);
            return new ProgressFile();
        }
    }

    private static void Save(string workerId, ProgressFile file)
    {
        Directory.CreateDirectory(ProgressDir);
        SafeFile.WriteAllText(PathFor(workerId), JsonUtility.ToJson(file));
    }
}
