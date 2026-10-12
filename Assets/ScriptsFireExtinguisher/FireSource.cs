using System;
using UnityEngine;
using UnityEngine.Events;

/// <summary>
/// Concrete UnityEvent&lt;FireSource&gt; subclass — Unity does not serialize/display bare
/// generic UnityEvent&lt;T&gt; fields in the Inspector, so extinguisher scripts that want
/// Inspector-wireable fire-related events use this type instead.
/// </summary>
[Serializable]
public class FireSourceEvent : UnityEvent<FireSource>
{
}

/// <summary>
/// Marks a fire GameObject with its real-world fire class and tracks whether it has
/// been fully extinguished. Attach this to the same object carrying the "Fire A" /
/// "Fire ABC" / "Fire BC" tag (or any of its extinguishable children) so
/// FireExtinguisherController and PassChecklistTracker can reason about fire class
/// without re-parsing tag strings.
/// </summary>
public class FireSource : MonoBehaviour
{
    [Tooltip("Real-world class of this fire. Must match the training scenario this fire represents.")]
    public FireClass fireClass = FireClass.A;

    [Tooltip("LocalizationManager key for this scenario's opening-alert text (see Resources/Localization/*.json, e.g. \"scenario_trashcan\") -- same id doubles as the NarrationPlayer clip id (Assets/Audio/Narration/manifest.json), one key drives both the on-screen text and the spoken line in whichever language is active. Set per scenario prefab.")]
    public string scenarioNarrationId = string.Empty;

    [Tooltip("Raised once when this fire transitions from burning to fully extinguished.")]
    public UnityEvent OnExtinguished;

    public bool IsExtinguished { get; private set; }

    /// <summary>
    /// Called by FireExtinguisherController when this fire's particle systems have all
    /// been shrunk/disabled. Safe to call multiple times; only fires the event once.
    /// </summary>
    public void MarkExtinguished()
    {
        if (IsExtinguished)
        {
            return;
        }

        IsExtinguished = true;
        OnExtinguished?.Invoke();
    }
}
