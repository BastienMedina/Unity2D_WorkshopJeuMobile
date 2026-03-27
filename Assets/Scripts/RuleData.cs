using System;
using System.Collections.Generic;

/// <summary>
/// Pure data container for a single sorting rule in the game.
/// Holds the rule type, typed condition fields, target bins, complexity, display text,
/// and complement metadata used to guarantee every document is always placeable.
/// Does NOT contain any game logic, validation, or UI references.
/// </summary>
[Serializable]
public class RuleData
{
    /// <summary>
    /// Determines which validation logic applies to this rule at runtime.
    /// Stored as a typed enum rather than an implicit convention so the correct
    /// validation path is always explicit — no guesswork about which conditions are active.
    /// </summary>
    public RuleType ruleType;

    /// <summary>
    /// Primary condition string. Used by all rule types except complement-less types.
    /// Example: "red_stamp", "urgent".
    /// In prefab mode, stores the asset path of the first (or only) document prefab.
    /// When prefabPaths is non-empty, conditionA mirrors prefabPaths[0] for backward compatibility.
    /// </summary>
    public string conditionA = string.Empty;

    /// <summary>
    /// Secondary condition string. Used by ConditionalBranch, PositiveDouble,
    /// PositiveWithNegative, and their complements.
    /// </summary>
    public string conditionB = string.Empty;

    /// <summary>
    /// All prefab asset paths accepted by this rule in prefab mode.
    /// When this list is non-empty, SortingBin.ValidateSimple matches any path in this list
    /// against DocumentData.specificities, and DocumentSpawner adds each path to the spawn pool.
    /// conditionA stores prefabPaths[0] for backward compatibility with old save files.
    /// Ignored when the rule uses condition-based matching (conditionA is a specificity string).
    /// </summary>
    public List<string> prefabPaths = new List<string>();

    /// <summary>
    /// Retained for backwards compatibility but no longer used by any active rule type.
    /// NegativeMultiple (the only consumer) was replaced by PositiveWithNegative.
    /// </summary>
    public List<string> conditionsList = new List<string>();

    /// <summary>Identifier of the primary bin that matching documents must be sorted into.</summary>
    public string targetBinID = string.Empty;

    /// <summary>
    /// Identifier of the secondary bin used by ConditionalBranch and PositiveDouble.
    /// These are the only rule types that route to two different bins depending on
    /// the presence or absence of a secondary condition — all other types use targetBinID only.
    /// </summary>
    public string secondaryBinID = string.Empty;

    /// <summary>
    /// Pre-computed complexity score, clamped between 1 and 5.
    /// Reflects logical difficulty of the rule type and the number of bins involved.
    /// </summary>
    public int complexity;

    /// <summary>
    /// The fully resolved, human-readable sentence for this rule after placeholder substitution.
    /// Stored here to avoid re-resolving the template string every frame inside DisplayRules().
    /// </summary>
    public string displayText = string.Empty;

    /// <summary>
    /// True when this rule was automatically generated as a complement of a primary rule.
    /// Complement rules must be identifiable so the system never generates a complement of a
    /// complement — this flag is the guard against infinite recursion in GenerateComplementRule.
    /// </summary>
    public bool isComplement;

    /// <summary>
    /// The bin ID where the complement of this primary rule has been placed.
    /// Set during generation by RuleGenerator immediately after complement creation.
    /// Stored on the primary rule so GameManager can look up where the complement was assigned
    /// without having to search the full rule list.
    /// Only meaningful when isComplement is false.
    /// </summary>
    public string complementBinID = string.Empty;
}
