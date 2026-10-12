using System.Text;

/// <summary>
/// Works around a real TextMeshPro limitation, not a translation error: TMP's standard
/// (non-shaping) text renderer draws Unicode codepoints in logical/encoded order and does
/// not implement the Indic script reordering the Unicode Standard requires for correct
/// display -- specifically, Devanagari's "ि" (U+093F, VOWEL SIGN I) is a pre-base matra,
/// stored in the text AFTER the consonant it belongs to but required to render BEFORE the
/// whole consonant cluster (conjunct) that consonant is part of. Left alone, TMP renders it
/// in its logical (post-base) position, which reads as visibly wrong/scrambled -- e.g.
/// "प्रशिक्षण" rendering with the ि floating in the wrong place. The JSON translation files
/// are intentionally kept in correct, human-readable, standard logical order (so a
/// translator or reviewer reads normal Hindi); this class reorders only the in-memory
/// string actually handed to a TMP_Text component, immediately before display.
/// </summary>
public static class DevanagariShaping
{
    private const char VowelSignI = 'ि';   // ि -- U+093F, the one Devanagari matra that needs pre-base reordering for simple renderers.
    private const char Virama = '्';        // ् -- U+094D, joins consonants into a conjunct.

    /// <summary>
    /// Returns a version of the string safe to assign directly to a TMP_Text.text -- with
    /// every pre-base vowel sign moved before the full consonant conjunct it attaches to.
    /// NOT idempotent: calling this a second time on already-fixed text will move the vowel
    /// sign again and corrupt it. Every independent string should pass through here exactly
    /// once (see LocalizationManager.Get, which handles this for composed/formatted text).
    /// Harmless no-op on text with no Devanagari in it (English, Ol Chiki, plain numbers).
    /// </summary>
    public static string FixForDisplay(string s)
    {
        if (string.IsNullOrEmpty(s) || s.IndexOf(VowelSignI) < 0)
        {
            return s; // fast path -- most strings (English, Ol Chiki, or Devanagari with no ि at all) are untouched.
        }

        var sb = new StringBuilder(s.Length);
        foreach (char c in s)
        {
            if (c == VowelSignI && sb.Length > 0)
            {
                // Walk back over the output built so far to find the start of the consonant
                // conjunct this vowel sign belongs to -- zero or more (Consonant, Virama)
                // pairs immediately preceding it -- then insert the vowel sign there instead
                // of at its logical (post-cluster) position.
                int clusterStart = sb.Length - 1;
                while (clusterStart >= 2 && sb[clusterStart - 1] == Virama && IsDevanagariConsonant(sb[clusterStart - 2]))
                {
                    clusterStart -= 2;
                }
                sb.Insert(clusterStart, VowelSignI);
            }
            else
            {
                sb.Append(c);
            }
        }
        return sb.ToString();
    }

    private static bool IsDevanagariConsonant(char c) => c >= 'क' && c <= 'ह';
}
