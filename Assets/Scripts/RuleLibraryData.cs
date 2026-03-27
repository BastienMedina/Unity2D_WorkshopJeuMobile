using System;
using System.Collections.Generic;

/// <summary>
/// Data container for a single rule entry authored in the Rule Library editor tool.
/// Supports both manuscript (free-text) and structured (condition-chain) authoring modes.
/// Defined in the runtime assembly so it can be shared by both the Editor tool and
/// the runtime systems (LibraryRuleConverter, LibraryRuleAssigner).
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
    /// </summary>
    public bool isManuscript;

    // ─── Manuscript mode ──────────────────────────────────────────────────────

    /// <summary>
    /// Free-text sentence written by the designer in manuscript mode.
    /// Example: "Si le document contient un tampon rouge, le mettre dans Corbeille A."
    /// </summary>
    public string manuscriptText = string.Empty;

    // ─── Prefab mode ──────────────────────────────────────────────────────────

    /// <summary>
    /// Asset paths of all prefabs assigned to this rule entry in prefab mode.
    /// A single entry can accept multiple document types: each prefab path is added to the spawn
    /// pool and accepted by the target bin's validation.
    /// conditionA on the generated DesignerRuleEntry mirrors prefabPaths[0] for backward compat.
    /// </summary>
    public List<string> prefabPaths = new List<string>();

    /// <summary>
    /// Legacy single prefab path — kept for backward-compatible JSON reads of old library files.
    /// On load, migrated into prefabPaths[0] if prefabPaths is empty.
    /// Do NOT write to this field in new code; use prefabPaths instead.
    /// </summary>
    public string prefabPath = string.Empty;

    /// <summary>
    /// Bin slot (1 or 2) assigned to Prefab A in the Floor Designer.
    /// "Corbeille 1" = slot 1, "Corbeille 2" = slot 2.
    /// Defaults to 1.
    /// </summary>
    public int prefabASlot = 1;

    /// <summary>
    /// Asset paths of all secondary prefabs (Prefab B), used only when ruleType == Branch.
    /// Documents matching any Prefab B path route to the opposite slot from Prefab A.
    /// Empty for Simple and Multiple rules.
    /// prefabBPath mirrors prefabBPaths[0] for backward-compatible JSON reads.
    /// </summary>
    public List<string> prefabBPaths = new List<string>();

    /// <summary>
    /// Legacy single secondary prefab path — kept for backward-compatible JSON reads.
    /// On load, migrated into prefabBPaths[0] if prefabBPaths is empty.
    /// Do NOT write to this field in new code; use prefabBPaths instead.
    /// </summary>
    public string prefabBPath = string.Empty;

    /// <summary>
    /// Bin slot (1 or 2) assigned to all Prefab B entries in the Floor Designer.
    /// Only meaningful when ruleType == Branch.
    /// Defaults to 2.
    /// </summary>
    public int prefabBSlot = 2;

    // ─── Legacy structured mode fields — kept for backward-compatible JSON reads ──

    /// <summary>
    /// Ordered list of condition nodes from the old structured mode.
    /// No longer used by the editor UI — retained so existing JSON saves can be read
    /// without a migration pass. New entries leave this list empty.
    /// </summary>
    public List<ConditionNode> conditions = new List<ConditionNode>();

    /// <summary>Legacy secondary bin slot from the old structured mode. No longer used.</summary>
    public int secondaryBinSlot;

    /// <summary>Legacy resolved bin 1 from the old structured mode. No longer used.</summary>
    public string resolvedBin1 = string.Empty;

    /// <summary>Legacy resolved bin 2 from the old structured mode. No longer used.</summary>
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
