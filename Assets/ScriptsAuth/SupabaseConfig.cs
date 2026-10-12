using UnityEngine;

/// <summary>
/// Supabase project settings, loaded from Resources/SupabaseConfig.asset. The anon key is
/// safe to ship: the app can only call the worker_login / submit_attempts RPCs, and every
/// table is locked behind RLS (see supabase/migrations/0001_init.sql).
/// </summary>
[CreateAssetMenu(fileName = "SupabaseConfig", menuName = "AbhyasAR/Supabase Config")]
public class SupabaseConfig : ScriptableObject
{
    [Tooltip("e.g. https://abcdefgh.supabase.co")]
    public string projectUrl;

    [Tooltip("Project Settings > API > anon public key")]
    public string anonKey;

    [Tooltip("Seconds before a request is treated as offline.")]
    public int requestTimeoutSeconds = 15;

    private static SupabaseConfig instance;

    public static SupabaseConfig Instance
    {
        get
        {
            if (instance == null)
            {
                instance = Resources.Load<SupabaseConfig>("SupabaseConfig");
                if (instance == null)
                {
                    Debug.LogWarning("SupabaseConfig: Resources/SupabaseConfig.asset is missing — login and sync will run offline-only.");
                    instance = CreateInstance<SupabaseConfig>();
                }
            }
            return instance;
        }
    }

    public bool IsConfigured => !string.IsNullOrWhiteSpace(projectUrl) && !string.IsNullOrWhiteSpace(anonKey);
}
