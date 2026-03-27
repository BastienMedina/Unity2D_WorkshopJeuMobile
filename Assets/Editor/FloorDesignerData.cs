using System;
using System.Collections.Generic;

/// <summary>
/// Runtime data container used exclusively by the Floor Designer Editor tool.
/// Holds all editable state for one floor, broken down into 5 per-night configurations.
/// Does NOT inherit from FloorSaveData — it is a parallel structure owned by the tool.
/// Does NOT exist at runtime — Editor only.
/// </summary>
public class FloorDesignerData
{
    private const int NightCount = 5;

    public int    floorIndex;
    public string displayName;

    // ─── Profitability ────────────────────────────────────────────────────────
    public float failThreshold;
    public float dayDuration;
    public float decayRatePerSecond;

    // ─── Rule Parameters (creation-time) ─────────────────────────────────────

    /// <summary>Number of bins — drives auto-generation of rules at creation.</summary>
    public int numberOfBins;

    /// <summary>Rules per bin — drives auto-generation.</summary>
    public int rulesPerBin;

    /// <summary>Maximum complexity — drives auto-generation.</summary>
    public int maxRuleComplexity;

    // ─── State ────────────────────────────────────────────────────────────────

    /// <summary>
    /// True once the floor has been saved at least once.
    /// Controls UI mode: creation-only (false) vs full rule editor per bin (true).
    /// </summary>
    public bool isSaved;

    public bool isDirty;
    public bool isCompleted;

    // ─── Nights ───────────────────────────────────────────────────────────────

    /// <summary>Per-night configurations — always exactly 5 entries after InitializeNights.</summary>
    public List<NightDesignerData> nights = new List<NightDesignerData>();

    // ─── Methods ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Populates nights with 5 NightDesignerData entries using the floor's base parameters.
    /// Bin rule lists start empty — filled after first save from the saved data.
    /// </summary>
    public void InitializeNights()
    {
        if (nights.Count > 0)
            return;

        for (int i = 0; i < NightCount; i++)
        {
            nights.Add(new NightDesignerData
            {
                nightIndex        = i,
                numberOfBins      = numberOfBins,
                rulesPerBin       = rulesPerBin,
                maxRuleComplexity = maxRuleComplexity,
                wasManuallyEdited = false
            });
        }
    }

    /// <summary>
    /// Ensures each night has exactly its numberOfBins BinDesignerData entries.
    /// Called after load or after bin count changes to keep the bin list consistent.
    /// Existing bins are preserved; surplus bins are trimmed; missing bins are appended.
    /// </summary>
    public void SyncBinsToNights()
    {
        foreach (NightDesignerData night in nights)
            night.SyncBins(night.numberOfBins);
    }

    /// <summary>
    /// Propagates rulesPerBin and maxRuleComplexity forward from fromNightIndex
    /// to all subsequent nights that have not been manually edited.
    /// </summary>
    public void PropagateNightRules(int fromNightIndex)
    {
        for (int i = fromNightIndex + 1; i < nights.Count; i++)
        {
            if (nights[i].wasManuallyEdited)
                continue;

            nights[i].rulesPerBin       = nights[i - 1].rulesPerBin;
            nights[i].maxRuleComplexity = nights[i - 1].maxRuleComplexity;
        }
    }

    // ─── Canonical bin ID table ───────────────────────────────────────────────

    /// <summary>
    /// Canonical bin IDs ordered by activation index (first activated = index 0).
    /// The order matches BinLayoutManager.activationOrder — any divergence causes
    /// the Floor Designer to write rules for a bin not yet active at a given count.
    ///
    /// index 0 → Haut Gauche   (activated 1st)
    /// index 1 → Haut Droit    (activated 2nd)
    /// index 2 → Bas Gauche    (activated 3rd)
    /// index 3 → Bas Droit     (activated 4th)
    /// index 4 → Poubelle      (trash — activated only when hasTrashedPrefab is true)
    /// </summary>
    public static readonly string[] BinIDsByIndex = new[]
    {
        "bin_top_left",
        "bin_top_right",
        "bin_bottom_left",
        "bin_bottom_right",
        "bin_trash"
    };

    /// <summary>
    /// Human-readable French positional names for each bin, aligned with BinIDsByIndex.
    /// Used in editor UI labels and dropdowns in place of raw bin IDs.
    /// </summary>
    public static readonly string[] BinDisplayNames = new[]
    {
        "Haut Gauche",
        "Haut Droit",
        "Bas Gauche",
        "Bas Droit",
        "Poubelle"
    };

    /// <summary>
    /// Returns the canonical bin ID for <paramref name="index"/>.
    /// Falls back to index 0 when the index exceeds the table.
    /// </summary>
    public static string GetBinID(int index)
    {
        return index < BinIDsByIndex.Length ? BinIDsByIndex[index] : BinIDsByIndex[0];
    }

    /// <summary>
    /// Returns the French positional display name for a bin ID (e.g. "bin_top_left" → "Haut Gauche").
    /// Falls back to the raw ID when no match is found.
    /// </summary>
    public static string GetBinDisplayName(string binID)
    {
        int idx = Array.IndexOf(BinIDsByIndex, binID);
        return idx >= 0 ? BinDisplayNames[idx] : binID;
    }
}

/// <summary>
/// Holds the complete configuration for one night within a floor.
/// Contains generation parameters AND the explicit per-bin rule list the designer edits after creation.
/// </summary>
public class NightDesignerData
{
    public int nightIndex;
    public int numberOfBins;
    public int rulesPerBin;
    public int maxRuleComplexity;

    /// <summary>Specificities used as the exclusive conditionA pool at runtime.</summary>
    public List<string> pinnedSpecificities = new List<string>();

    /// <summary>
    /// Per-bin rule configuration for this night — populated after first save.
    /// Each entry maps to one sorting bin and holds its assigned rules.
    /// </summary>
    public List<BinDesignerData> bins = new List<BinDesignerData>();

    public bool wasManuallyEdited;

    /// <summary>UI-only: whether this night's panel is expanded (never persisted).</summary>
    public bool isExpanded;

    // ─── Trash bin ────────────────────────────────────────────────────────────

    /// <summary>
    /// When true, the fifth "trash" bin (bottom-centre) is active for this night.
    /// Document prefabs listed in trashedPrefabPaths are injected into the spawn pool
    /// alongside the regular bin prefabs; the player must route them to the trash bin.
    /// </summary>
    public bool hasTrashedPrefab;

    /// <summary>
    /// Asset paths of the prefabs selected as trash documents for this night.
    /// Each path is added to the spawn pool in addition to all regular-bin prefabs.
    /// Only populated when hasTrashedPrefab is true.
    /// Every path must reference a prefab absent from every regular bin's rule list.
    /// </summary>
    public List<string> trashedPrefabPaths = new List<string>();

    // ─── Legacy flat rule list — kept for backward-compatibility on load ───────

    /// <summary>
    /// Flat rule list from the old format (pre-per-bin editing).
    /// Retained so old JSON saves can be migrated on load.
    /// Not used by the editor UI when isSaved is true.
    /// </summary>
    public List<DesignerRuleEntry> designerRules = new List<DesignerRuleEntry>();

    // ─── Methods ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Ensures this night has exactly targetCount BinDesignerData entries.
    /// Existing bins are preserved; surplus bins are removed; missing bins are appended.
    /// </summary>
    public void SyncBins(int targetCount)
    {
        while (bins.Count > targetCount)
            bins.RemoveAt(bins.Count - 1);

        while (bins.Count < targetCount)
        {
            int idx = bins.Count;
            bins.Add(new BinDesignerData
            {
                binIndex = idx,
                binID    = FloorDesignerData.GetBinID(idx)
            });
        }

        // Keep binIndex consistent after trim/add.
        for (int i = 0; i < bins.Count; i++)
            bins[i].binIndex = i;
    }
}

/// <summary>
/// Editable rule configuration for a single sorting bin within one night.
/// The designer assigns rules from the global rule list to each bin explicitly.
/// </summary>
public class BinDesignerData
{
    /// <summary>Zero-based position of this bin in the night's bin list.</summary>
    public int binIndex;

    /// <summary>Runtime bin identifier (e.g. "bin_left_top", "bin_right_top").</summary>
    public string binID = string.Empty;

    /// <summary>
    /// Rules explicitly assigned to this bin by the designer.
    /// The designer can add, remove, or replace any rule from the global rule pool.
    /// </summary>
    public List<DesignerRuleEntry> rules = new List<DesignerRuleEntry>();

    /// <summary>UI-only: whether this bin's rule list is expanded in the editor.</summary>
    public bool isExpanded;
}

/// <summary>
/// One editable rule entry in the Floor Designer.
/// Stores rule type, conditions, and target bin so the designer
/// can author every rule explicitly before saving the floor.
/// </summary>
public class DesignerRuleEntry
{
    /// <summary>RuleType enum name string (e.g. "Simple", "Branch").</summary>
    public string ruleTypeString = string.Empty;

    /// <summary>Primary condition specificity or prefab path (Prefab A).</summary>
    public string conditionA = string.Empty;

    /// <summary>Secondary condition — only for two-condition rule types (Branch).</summary>
    public string conditionB = string.Empty;

    /// <summary>Target bin ID for this rule (primary bin).</summary>
    public string targetBinID = string.Empty;

    /// <summary>Human-readable display sentence (auto-computed from template).</summary>
    public string displayText = string.Empty;

    public int  complexity;
    public bool isComplement;

    // ─── Branch bin resolution ────────────────────────────────────────────────

    /// <summary>
    /// Physical bin ID resolved for "Corbeille 1" when this is a Branch (prefab B) rule.
    /// Set by the designer via the two dropdowns that appear under a Branch rule row.
    /// Empty for Simple and Multiple rules.
    /// </summary>
    public string branchSlot1BinID = string.Empty;

    /// <summary>
    /// Physical bin ID resolved for "Corbeille 2" when this is a Branch (prefab B) rule.
    /// Set by the designer via the two dropdowns that appear under a Branch rule row.
    /// Empty for Simple and Multiple rules.
    /// </summary>
    public string branchSlot2BinID = string.Empty;
}

