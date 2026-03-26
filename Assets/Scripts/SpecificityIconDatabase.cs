using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject mapping each specificity string key to its corresponding icon sprite.
/// Also holds the negation overlay sprite shown on top of negated specificities in Branch rules.
/// Assign all entries once in the Inspector — BinIconDisplay reads from this at runtime.
/// Does NOT contain any logic, game state, or UI references.
/// </summary>
[CreateAssetMenu(fileName = "SpecificityIconDatabase", menuName = "Game/Specificity Icon Database")]
public class SpecificityIconDatabase : ScriptableObject
{
    /// <summary>
    /// Sprite overlaid on top of any specificity icon that is negated in a Branch rule.
    /// Displayed at full size over the specificity icon to signal "must NOT have this".
    /// </summary>
    [SerializeField] public Sprite negationIcon;

    /// <summary>
    /// Ordered list of specificity-to-sprite mappings.
    /// Keys must match the string values in <see cref="SpecificityDatabase.allSpecificities"/> exactly.
    /// </summary>
    [SerializeField] public List<SpecificityIconEntry> entries = new List<SpecificityIconEntry>();

    /// <summary>
    /// Returns the sprite associated with the given specificity key, or null if not found.
    /// Logs a detailed warning distinguishing between a missing key and a null sprite reference,
    /// so configuration errors in the Inspector can be pinpointed immediately.
    /// </summary>
    /// <param name="specificityKey">The specificity string to resolve, e.g. "tampon_carre".</param>
    public Sprite GetIcon(string specificityKey)
    {
        foreach (SpecificityIconEntry entry in entries)
        {
            if (entry.specificityKey != specificityKey)
                continue;

            if (entry.icon == null)
                Debug.LogWarning($"[SpecificityIconDatabase] Key '{specificityKey}' found but its Sprite is null — check the Inspector assignment in {name}.");

            return entry.icon;
        }

        Debug.LogWarning($"[SpecificityIconDatabase] No entry found for key '{specificityKey}' in {name}. Entries count: {entries.Count}.");
        return null;
    }

    /// <summary>
    /// Logs the full state of this database to the Unity Console.
    /// Call from BinIconDisplay.Start() or a test context to verify all sprite references are valid.
    /// </summary>
    public void LogDatabaseState()
    {
        Debug.Log($"[SpecificityIconDatabase] '{name}' — {entries.Count} entries, negationIcon={(negationIcon != null ? negationIcon.name : "NULL")}");

        foreach (SpecificityIconEntry entry in entries)
        {
            string iconState = entry.icon != null ? entry.icon.name : "NULL";
            Debug.Log($"  '{entry.specificityKey}' → {iconState}");
        }
    }
}

/// <summary>
/// Associates a specificity string key with its display icon sprite.
/// Serialized as a plain struct so it appears as an expandable row in the Inspector list.
/// </summary>
[System.Serializable]
public class SpecificityIconEntry
{
    /// <summary>Must exactly match a value in SpecificityDatabase.allSpecificities.</summary>
    public string specificityKey;

    /// <summary>The sprite displayed on the bin when this specificity is part of an active rule.</summary>
    public Sprite icon;
}
