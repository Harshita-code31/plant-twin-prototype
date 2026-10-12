using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Loads and serves offline translation strings. All translation data ships inside the app
/// as JSON under Resources/Localization/{code}.json (Resources loads are bundled into the
/// build and read synchronously, no network) — matching the app's offline-first design, the
/// same reasoning behind NarrationPlayer's pre-generated audio clips.
///
/// One flat key -&gt; string map per language. Dynamic lines use .NET composite-format
/// placeholders ({0}, {1}, ...) inside the translated string itself, so word order can
/// differ correctly per language instead of being stitched together from separately
/// translated fragments (e.g. "Step {0}/{1} — {2}" vs. a language where the step count
/// naturally comes last).
///
/// Static, lazily-initialized rather than a MonoBehaviour singleton: every scene (MainMenu,
/// FireTraining) needs this before any UI script's Awake/OnEnable runs, and a plain static
/// class guarantees that without scene-load-order dependencies.
/// </summary>
public static class LocalizationManager
{
    public const string English = "en";
    public const string Hindi = "hi";
    public const string Santali = "sat";

    private const string PrefKey = "ARBT_Language";

    /// <summary>Raised after the active language changes and the new table has finished loading -- UI should re-fetch every localized string it's showing.</summary>
    public static event Action OnLanguageChanged;

    public static string CurrentLanguage { get; private set; } = English;

    private static Dictionary<string, string> currentTable;
    private static Dictionary<string, string> fallbackTable; // English, used when a key is missing from the active language
    private static bool initialized;

    /// <summary>Language codes this build ships translations for, in the order the dropdown should list them.</summary>
    public static readonly (string code, string displayName)[] AvailableLanguages =
    {
        (English, "English"),
        (Hindi, "हिन्दी"),
        (Santali, "ᱥᱟᱱᱛᱟᱲᱤ"),
    };

    /// <summary>Loads the saved language preference (or English if none saved yet). Safe to call multiple times -- only does real work once.</summary>
    public static void EnsureInitialized()
    {
        if (initialized)
        {
            return;
        }
        initialized = true;

        fallbackTable = LoadTable(English);
        string saved = PlayerPrefs.GetString(PrefKey, English);
        SetLanguage(saved, persist: false);
    }

    /// <summary>
    /// Switches the active language, reloading its table, persisting the choice (so it's
    /// remembered next launch, offline), and notifying every subscribed UI element.
    /// </summary>
    public static void SetLanguage(string languageCode, bool persist = true)
    {
        if (string.IsNullOrEmpty(languageCode))
        {
            languageCode = English;
        }

        currentTable = languageCode == English ? fallbackTable ?? LoadTable(English) : LoadTable(languageCode);
        if (currentTable == null)
        {
            // Unknown/failed-to-load language code -- fall back to English rather than showing blank text everywhere.
            languageCode = English;
            currentTable = fallbackTable ?? LoadTable(English);
        }

        CurrentLanguage = languageCode;

        if (persist)
        {
            PlayerPrefs.SetString(PrefKey, languageCode);
            PlayerPrefs.Save();
        }

        OnLanguageChanged?.Invoke();
    }

    /// <summary>Plain lookup -- returns the translated string for this key in the active language, falling back to English, falling back to the key itself so a missing translation is visibly obvious instead of silently blank.</summary>
    public static string Get(string key)
    {
        return DevanagariShaping.FixForDisplay(LookupRaw(key));
    }

    /// <summary>Formatted lookup -- the translated string is used as a composite-format template (string.Format), so each language controls its own word order around the inserted values.</summary>
    public static string Get(string key, params object[] args)
    {
        // Fix the template's own static text before substitution, not the combined result --
        // an arg can itself be the (already-fixed) return value of a nested Get() call, and
        // DevanagariShaping.FixForDisplay is NOT idempotent (running it twice on already-
        // reordered text corrupts it further). Fixing only the template here, once, and
        // trusting any nested Get() call to have already fixed its own piece, keeps every
        // segment fixed exactly once regardless of how deeply these calls nest.
        string template = DevanagariShaping.FixForDisplay(LookupRaw(key));
        try
        {
            return string.Format(template, args);
        }
        catch (FormatException)
        {
            // A translation with a typo'd/missing {n} placeholder shouldn't crash the UI --
            // surface the raw template so it's obviously wrong rather than throwing.
            return template;
        }
    }

    /// <summary>Raw table lookup -- no display-shaping fixups applied. Only Get() should call this.</summary>
    private static string LookupRaw(string key)
    {
        EnsureInitialized();

        if (currentTable != null && currentTable.TryGetValue(key, out string value) && !string.IsNullOrEmpty(value))
        {
            return value;
        }
        if (fallbackTable != null && fallbackTable.TryGetValue(key, out string fallback) && !string.IsNullOrEmpty(fallback))
        {
            return fallback;
        }
        return key;
    }

    private static Dictionary<string, string> LoadTable(string languageCode)
    {
        TextAsset asset = Resources.Load<TextAsset>($"Localization/{languageCode}");
        if (asset == null)
        {
            Debug.LogWarning($"LocalizationManager: no translation table found for '{languageCode}' at Resources/Localization/{languageCode}.json");
            return null;
        }

        try
        {
            var wrapper = JsonUtility.FromJson<StringMapWrapper>(WrapForJsonUtility(asset.text));
            var dict = new Dictionary<string, string>();
            foreach (var entry in wrapper.entries)
            {
                dict[entry.key] = entry.value;
            }
            return dict;
        }
        catch (Exception ex)
        {
            Debug.LogError($"LocalizationManager: failed to parse {languageCode}.json — {ex.Message}");
            return null;
        }
    }

    /// <summary>
    /// JsonUtility can't deserialize a bare JSON object (arbitrary key set) directly into a
    /// Dictionary, and can't deserialize a top-level array either -- it needs a wrapper
    /// object. The source .json files on disk are authored as plain, ordinary
    /// {"key": "value", ...} objects (easy for a human/translator to read and diff); this
    /// converts that into the {"entries":[{"key":...,"value":...}, ...]} shape JsonUtility
    /// requires, at load time, so the on-disk format stays simple.
    /// </summary>
    private static string WrapForJsonUtility(string plainJsonObject)
    {
        var sb = new System.Text.StringBuilder();
        sb.Append("{\"entries\":[");
        bool first = true;
        int i = 0;
        int len = plainJsonObject.Length;

        // Skip to the first '{'
        while (i < len && plainJsonObject[i] != '{') i++;
        i++;

        while (i < len)
        {
            while (i < len && (char.IsWhiteSpace(plainJsonObject[i]) || plainJsonObject[i] == ',')) i++;
            if (i >= len || plainJsonObject[i] == '}') break;

            if (plainJsonObject[i] != '"') { i++; continue; }
            var key = ReadJsonString(plainJsonObject, ref i);

            while (i < len && plainJsonObject[i] != ':') i++;
            i++; // skip ':'
            while (i < len && char.IsWhiteSpace(plainJsonObject[i])) i++;

            var value = ReadJsonString(plainJsonObject, ref i);

            if (!first) sb.Append(',');
            first = false;
            sb.Append("{\"key\":").Append(EscapeJson(key)).Append(",\"value\":").Append(EscapeJson(value)).Append('}');
        }

        sb.Append("]}");
        return sb.ToString();
    }

    private static string ReadJsonString(string s, ref int i)
    {
        // i points at the opening quote.
        i++; // skip opening quote
        var sb = new System.Text.StringBuilder();
        while (i < s.Length && s[i] != '"')
        {
            if (s[i] == '\\' && i + 1 < s.Length)
            {
                char next = s[i + 1];
                switch (next)
                {
                    case 'n': sb.Append('\n'); break;
                    case 't': sb.Append('\t'); break;
                    case 'r': sb.Append('\r'); break;
                    case '"': sb.Append('"'); break;
                    case '\\': sb.Append('\\'); break;
                    case '/': sb.Append('/'); break;
                    case 'u':
                        if (i + 5 < s.Length)
                        {
                            string hex = s.Substring(i + 2, 4);
                            sb.Append((char)Convert.ToInt32(hex, 16));
                            i += 4;
                        }
                        break;
                    default: sb.Append(next); break;
                }
                i += 2;
            }
            else
            {
                sb.Append(s[i]);
                i++;
            }
        }
        i++; // skip closing quote
        return sb.ToString();
    }

    private static string EscapeJson(string s)
    {
        return "\"" + s.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("\n", "\\n").Replace("\r", "\\r").Replace("\t", "\\t") + "\"";
    }

    [Serializable]
    private class StringMapWrapper
    {
        public StringEntry[] entries;
    }

    [Serializable]
    private class StringEntry
    {
        public string key;
        public string value;
    }
}
