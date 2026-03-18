using System;
using System.Collections.Generic;

/// <summary>
/// Pure data container for a single sorting rule in the game.
/// Holds the rule type, typed condition fields, target bins, complexity, and display text.
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
    /// Primary condition string. Used by all rule types except Fallback.
    /// Example: "red_stamp", "urgent".
    /// </summary>
    public string conditionA = string.Empty;

    /// <summary>
    /// Secondary condition string. Used exclusively by ConditionalBranch to determine
    /// which of the two target bins the document routes to.
    /// </summary>
    public string conditionB = string.Empty;

    /// <summary>
    /// List of conditions of which the document must contain none. Used by NegativeMultiple only.
    /// Typed as a separate list rather than reusing conditionA/B to make the multi-condition
    /// intent unambiguous — a flat list of two named fields would not scale to N conditions.
    /// </summary>
    public List<string> conditionsList = new List<string>();

    /// <summary>Identifier of the primary bin that matching documents must be sorted into.</summary>
    public string targetBinID = string.Empty;

    /// <summary>
    /// Identifier of the secondary bin used only by ConditionalBranch.
    /// ConditionalBranch is the only rule type that routes to two different bins
    /// depending on the presence of a secondary condition — all other types use targetBinID only.
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
}
