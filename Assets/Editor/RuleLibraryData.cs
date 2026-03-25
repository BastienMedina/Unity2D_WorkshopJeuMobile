using System;
using System.Collections.Generic;

/// <summary>
/// Data container for a single rule entry authored in the Rule Library editor tool.
/// Supports both manuscript (free-text) and structured (condition-chain) authoring modes.
/// Does NOT contain any validation logic, MonoBehaviour lifecycle, or UI references.
/// </summary>
[Serializable]
public class RuleLibraryEntry
{
    // ─── Identity ─────────────────────────────────────────────────────────────

    /// <summary>Unique identifier assigned at creation time. Never changes after creation.</summary>
    public string guid = string.Empty;

    /// <summary>Human-readable name shown in the left-panel list (e.g. "Red Stamp → Bin A").</summary>
    public string label = string.Empty;

    // ─── Authoring mode ───────────────────────────────────────────────────────

    /// <summary>
    /// True when this rule was authored in free-text manuscript mode.
    /// False when authored in structured condition-chain mode.
    /// Stored explicitly so the editor can restore the correct panel on re-selection.
    /// </summary>
    public bool isManuscript;

    // ─── Manuscript mode ──────────────────────────────────────────────────────

    /// <summary>
    /// Free-text sentence written by the designer in manuscript mode.
    /// Example: "Si le document contient un tampon rouge, le mettre dans Corbeille A."
    /// </summary>
    public string manuscriptText = string.Empty;

    // ─── Structured mode ──────────────────────────────────────────────────────

    /// <summary>
    /// Ordered list of condition nodes that make up the rule in structured mode.
    /// Each node carries a specificity name, a connector (Et / Ou / Sauf) relative
    /// to the next node, and the target bin when this node is terminal.
    /// </summary>
    public List<ConditionNode> conditions = new List<ConditionNode>();

    /// <summary>
    /// Slot number (1 or 2) assigned to the secondary bin used when a "Sauf" connector
    /// is present in the chain. 0 means no secondary bin applies.
    /// </summary>
    public int secondaryBinSlot;

    /// <summary>
    /// Actual bin ID randomly resolved for slot 1 (e.g. "bin_B").
    /// Empty until the designer clicks "Assigner les corbeilles".
    /// </summary>
    public string resolvedBin1 = string.Empty;

    /// <summary>
    /// Actual bin ID randomly resolved for slot 2 (e.g. "bin_D").
    /// Empty until the designer clicks "Assigner les corbeilles".
    /// </summary>
    public string resolvedBin2 = string.Empty;

    // ─── Shared metadata ──────────────────────────────────────────────────────

    /// <summary>Rule type as selected by the designer (e.g. "PositiveForced", "NegativeSimple").</summary>
    public string ruleTypeString = string.Empty;

    /// <summary>Difficulty index rated by the designer, clamped between 1 and 5.</summary>
    public int complexity = 1;
}

/// <summary>
/// One condition node inside a structured rule.
/// Carries its specificity value, how it connects to the NEXT node, and the target bin when terminal.
/// Does NOT contain validation logic.
/// </summary>
[Serializable]
public class ConditionNode
{
    /// <summary>The specificity this node tests (e.g. "red_stamp", "torn").</summary>
    public string specificity = string.Empty;

    /// <summary>
    /// Logical connector to the next node in the chain.
    /// "Et" = AND, "Ou" = OR, "Sauf" = UNLESS (negation of the next).
    /// Ignored on the last node (no next node exists).
    /// </summary>
    public string connector = "Et";

    /// <summary>
    /// Bin slot (1 or 2) this terminal node routes to.
    /// Slot 1 and slot 2 are randomly resolved to actual bin IDs at assignment time.
    /// Defaults to 1 (primary bin).
    /// </summary>
    public int targetBinSlot = 1;
}

/// <summary>
/// Root serialisable container written to / read from the JSON library file.
/// Wraps the flat list of all authored rule entries.
/// </summary>
[Serializable]
public class RuleLibraryFile
{
    /// <summary>All rule entries persisted in the library.</summary>
    public List<RuleLibraryEntry> entries = new List<RuleLibraryEntry>();
}
