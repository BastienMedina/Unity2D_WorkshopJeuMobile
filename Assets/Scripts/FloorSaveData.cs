using System;
using System.Collections.Generic;

/// <summary>
/// Pure data container for one saved floor state.
/// Stores all fields required to reconstruct the exact difficulty and rule set of a completed floor.
/// Does NOT load or save itself — all persistence is delegated to FloorSaveSystem.
/// </summary>
[Serializable]
public class FloorSaveData
{
    /// <summary>Zero-based index of the floor this data represents.</summary>
    public int floorIndex;

    /// <summary>True when all required days on this floor have been successfully completed.</summary>
    public bool isCompleted;

    /// <summary>Number of sorting bins that were active when this floor was played.</summary>
    public int numberOfBins;

    /// <summary>
    /// Simplified serialisable snapshots of all rules active when the floor was completed.
    /// </summary>
    public List<SavedRuleData> rules = new List<SavedRuleData>();

    /// <summary>All specificity strings used in this floor's rule generation.</summary>
    public List<string> specificities = new List<string>();

    /// <summary>Profitability threshold below which the player fails a day on this floor.</summary>
    public float failThreshold;

    /// <summary>Duration of a single day in seconds for this floor.</summary>
    public float dayDuration;

    /// <summary>Profitability decay rate per second for this floor.</summary>
    public float decayRatePerSecond;

    /// <summary>
    /// Random seed used when generating this floor's rules.
    /// Stored here so the exact rule set can be reproduced from the seed alone, without
    /// having to persist every rule detail in full — the seed is the compact canonical form.
    /// </summary>
    public string floorSeed;

    /// <summary>Number of rules assigned per bin when this floor was generated.</summary>
    public int rulesPerBin;

    /// <summary>Maximum rule complexity allowed during rule generation for this floor.</summary>
    public int maxRuleComplexity;

    /// <summary>
    /// True when this floor's parameters were inherited from the previous floor via delta computation.
    /// False when floor 0 base values were used directly.
    /// Distinguishes base floors from inherited floors, which makes save files easier to debug
    /// and lets tooling identify whether a floor started from defaults or from a progression chain.
    /// </summary>
    public bool wasGenerated;

    /// <summary>
    /// Per-night configuration snapshots for this floor (5 entries, one per night).
    /// Null or empty in save files created before per-night editing was introduced;
    /// callers must handle the missing-nights case for backward compatibility.
    /// </summary>
    public List<NightSaveData> nights = new List<NightSaveData>();
}

/// <summary>
/// Serialisable snapshot of one night's configuration within a saved floor.
/// Stores the generator parameters and the explicit per-bin rule assignments authored by the designer.
/// Does NOT contain UI state such as isExpanded — only data that belongs in the save file.
/// </summary>
[Serializable]
public class NightSaveData
{
    /// <summary>Zero-based index of this night within the floor (0 to 4).</summary>
    public int nightIndex;

    /// <summary>Number of sorting bins active during this night.</summary>
    public int numberOfBins;

    /// <summary>How many rules each bin has during this night (used for difficulty display).</summary>
    public int rulesPerBin;

    /// <summary>
    /// Maximum rule complexity score for rules generated during this night (range 1–5).
    /// </summary>
    public int maxRuleComplexity;

    /// <summary>
    /// True when the designer explicitly edited this night's difficulty sliders.
    /// False when values were auto-propagated from the previous night.
    /// </summary>
    public bool wasManuallyEdited;

    /// <summary>
    /// Specificities pinned by the designer for this night.
    /// When non-empty, RuleGenerator uses this list as the exclusive conditionA pool.
    /// </summary>
    public List<string> pinnedSpecificities = new List<string>();

    /// <summary>
    /// Per-bin rule assignments for this night — authored explicitly by the designer
    /// in the rule editor panel after the floor's first save.
    /// Null or empty entries are tolerated for backward compatibility with older saves.
    /// </summary>
    public List<BinSaveData> bins = new List<BinSaveData>();

    /// <summary>
    /// When true, the fifth "trash" bin (bottom-centre) is active for this night.
    /// The GameManager activates this bin via BinLayoutManager.SetTrashBinActive and
    /// injects trashedPrefabPaths into the DocumentSpawner pool alongside regular prefabs.
    /// </summary>
    public bool hasTrashedPrefab;

    /// <summary>
    /// Asset paths of the prefabs designated as trash documents for this night.
    /// Populated by the Floor Designer; injected into the spawn pool by GameManager.
    /// Only relevant when hasTrashedPrefab is true.
    /// </summary>
    public List<string> trashedPrefabPaths = new List<string>();
}

/// <summary>
/// Serialisable snapshot of the rules assigned to one sorting bin in a saved night.
/// </summary>
[Serializable]
public class BinSaveData
{
    /// <summary>Zero-based position of this bin in the night's bin list.</summary>
    public int binIndex;

    /// <summary>Runtime bin identifier (e.g. "bin_A").</summary>
    public string binID = string.Empty;

    /// <summary>Rules explicitly assigned to this bin by the designer.</summary>
    public List<SavedRuleData> rules = new List<SavedRuleData>();
}

/// <summary>
/// Serialisable snapshot of a single RuleData for JSON persistence.
/// Does NOT contain validation logic, events, or Unity references.
/// </summary>
[Serializable]
public class SavedRuleData
{
    /// <summary>
    /// The rule type stored as its enum name string.
    /// RuleData stores a typed RuleType enum which JsonUtility cannot round-trip reliably —
    /// converting to string is safer and remains readable inside the saved JSON file.
    /// </summary>
    public string ruleType;

    /// <summary>Primary condition string (e.g. "red_stamp", "urgent").</summary>
    public string conditionA;

    /// <summary>Secondary condition string used by two-condition rule types.</summary>
    public string conditionB;

    /// <summary>ID of the primary bin that matching documents must be sorted into.</summary>
    public string targetBinID;

    /// <summary>ID of the secondary bin used by branch and double rule types.</summary>
    public string secondaryBinID;

    /// <summary>Pre-computed human-readable sentence for this rule.</summary>
    public string displayText;

    /// <summary>Complexity score, clamped between 1 and 5 during generation.</summary>
    public int complexity;

    /// <summary>True when this rule was automatically generated as a complement of another rule.</summary>
    public bool isComplement;
}
