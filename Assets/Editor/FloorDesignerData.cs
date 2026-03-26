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
    /// Canonical bin ID list ordered by BinLayoutManager.activationOrder {0,2,4,1,3}.
    /// index 0 → slot 0 → bin_left_top     (activated 1st)
    /// index 1 → slot 2 → bin_right_top    (activated 2nd)
    /// index 2 → slot 4 → bin_bottom       (activated 3rd)
    /// index 3 → slot 1 → bin_left_bottom  (activated 4th)
    /// index 4 → slot 3 → bin_right_bottom (activated 5th)
    /// This order must mirror BinLayoutManager.activationOrder exactly —
    /// any divergence causes the Floor Designer to write rules for a bin that
    /// is not yet active at a given numberOfBins count.
    /// </summary>
    public static readonly string[] BinIDsByIndex = new[]
    {
        "bin_left_top",
        "bin_right_top",
        "bin_bottom",
        "bin_left_bottom",
        "bin_right_bottom"
    };

    /// <summary>
    /// Returns the canonical bin ID for <paramref name="index"/>.
    /// Falls back to <c>bin_left_top</c> when the index exceeds the table.
    /// </summary>
    public static string GetBinID(int index)
    {
        return index < BinIDsByIndex.Length ? BinIDsByIndex[index] : BinIDsByIndex[0];
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
    /// <summary>RuleType enum name string (e.g. "PositiveForced", "ConditionalBranch").</summary>
    public string ruleTypeString = string.Empty;

    /// <summary>Primary condition specificity (e.g. "urgent").</summary>
    public string conditionA = string.Empty;

    /// <summary>Secondary condition — only for two-condition rule types.</summary>
    public string conditionB = string.Empty;

    /// <summary>Target bin ID for this rule.</summary>
    public string targetBinID = string.Empty;

    /// <summary>Human-readable display sentence (auto-computed from template).</summary>
    public string displayText = string.Empty;

    public int  complexity;
    public bool isComplement;
}

