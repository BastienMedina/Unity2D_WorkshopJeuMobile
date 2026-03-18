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
    /// Each entry specifies the sentence text with {0}/{1} placeholders and the
    /// number of specificities it expects, so RuleGenerator can select a matching
    /// template without parsing the string itself.
    /// </summary>
    [SerializeField] public List<RuleTemplate> templates = new List<RuleTemplate>();
}
