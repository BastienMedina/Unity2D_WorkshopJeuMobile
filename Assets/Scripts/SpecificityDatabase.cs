using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// ScriptableObject that acts as an inspector-configurable data source for the rule generation system.
/// Holds all available specificities and sentence templates used to build rule descriptions.
/// Does NOT contain any generation logic, game state, or UI references.
/// </summary>
[CreateAssetMenu(fileName = "SpecificityDatabase", menuName = "Game/Specificity Database")]
public class SpecificityDatabase : ScriptableObject
{
    /// <summary>
    /// Full list of all specificities available in the game (e.g. "red", "heavy", "fragile").
    /// RuleGenerator draws from this pool when building rules.
    /// </summary>
    [SerializeField] public List<string> allSpecificities = new List<string>();

    /// <summary>
    /// Sentence templates used to format rules into human-readable strings.
    /// Use {0} for a single-specificity slot and {0} + {1} for two-specificity slots.
    /// Example: "Sort items that are {0} into the correct bin."
    /// </summary>
    [SerializeField] public List<string> templates = new List<string>();
}
