using System;
using System.Collections.Generic;

/// <summary>
/// Pure data container for a single sorting rule in the game.
/// Holds the required conditions, the target bin, and a pre-computed complexity score.
/// Does NOT contain any game logic, validation, or UI references.
/// </summary>
[Serializable]
public class RuleData
{
    /// <summary>List of specificities that must be matched for this rule to apply.</summary>
    public List<string> conditions = new List<string>();

    /// <summary>Identifier of the bin that items matching this rule must be sorted into.</summary>
    public string targetBinID = string.Empty;

    /// <summary>
    /// Pre-computed complexity score, clamped between 1 and 5.
    /// Higher values indicate rules that are harder to evaluate at a glance.
    /// </summary>
    public int complexity;
}
