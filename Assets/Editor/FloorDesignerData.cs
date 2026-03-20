using System.Collections.Generic;

/// <summary>
/// Runtime data container used exclusively by the Floor Designer Editor tool.
/// Holds all editable state for one floor, broken down into 5 per-night configurations.
/// Does NOT inherit from FloorSaveData — it is a parallel structure owned by the tool.
/// Does NOT exist at runtime — Editor only. Placing this file outside an Editor folder
/// would cause it to be compiled into Android builds and break the build.
/// </summary>
public class FloorDesignerData
{
    /// <summary>Fixed number of nights per floor — always 5, never configurable.</summary>
    private const int NightCount = 5;

    /// <summary>Zero-based index of the floor this entry represents.</summary>
    public int floorIndex;

    /// <summary>
    /// Human-readable name shown in the tower panel.
    /// Defaults to "Floor " + (floorIndex + 1) so floor 0 reads "Floor 1".
    /// Editable by the designer in the window.
    /// </summary>
    public string displayName;

    // ─── Profitability Parameters ─────────────────────────────────────────────

    /// <summary>Profitability percentage below which the player fails a day on this floor.</summary>
    public float failThreshold;

    /// <summary>Duration of one game day in seconds for this floor.</summary>
    public float dayDuration;

    /// <summary>Profitability percentage lost per second while idle on this floor.</summary>
    public float decayRatePerSecond;

    // ─── Rule Parameters ──────────────────────────────────────────────────────

    /// <summary>Default number of sorting bins used as the starting value for night 0.</summary>
    public int numberOfBins;

    /// <summary>Number of rules assigned per bin on this floor.</summary>
    public int rulesPerBin;

    /// <summary>Maximum rule complexity score allowed during generation for this floor.</summary>
    public int maxRuleComplexity;

    // ─── Nights ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Per-night parameter configurations. Always contains exactly 5 entries — one per night.
    /// Each night stores rulesPerBin, maxRuleComplexity, and numberOfBins as parameters;
    /// RuleGenerator creates the actual rules at runtime from these values.
    /// Replacing manual rule entry with parameters means the designer controls QUANTITY
    /// and DIFFICULTY, not individual rule content.
    /// </summary>
    public List<NightDesignerData> nights = new List<NightDesignerData>();

    /// <summary>
    /// True when any field was modified since the last successful save.
    /// Tracks unsaved changes so the tool can warn the designer before closing
    /// or switching floors, preventing accidental loss of work.
    /// </summary>
    public bool isDirty;

    /// <summary>
    /// Mirrors FloorSaveData.isCompleted so the left-panel button can color completed floors
    /// differently from active ones without reading from disk every frame.
    /// </summary>
    public bool isCompleted;

    // ─── Night Initialization ─────────────────────────────────────────────────

    /// <summary>
    /// Populates the nights list with 5 NightDesignerData entries if the list is empty.
    /// All nights inherit the floor's base rulesPerBin, maxRuleComplexity, and numberOfBins.
    /// wasManuallyEdited is false for every night so PropagateNightRules can freely propagate
    /// until the designer explicitly changes a slider.
    /// Called when a floor is first created so every night starts with a valid base state
    /// before the designer manually customizes any of them.
    /// </summary>
    public void InitializeNights()
    {
        if (nights.Count > 0)
            return;

        for (int i = 0; i < NightCount; i++)
        {
            NightDesignerData night = new NightDesignerData
            {
                nightIndex        = i,
                numberOfBins      = numberOfBins,
                rulesPerBin       = rulesPerBin,
                maxRuleComplexity = maxRuleComplexity,
                // wasManuallyEdited starts false — all nights are auto-inherited until touched.
                wasManuallyEdited = false
            };

            nights.Add(night);
        }
    }

    /// <summary>
    /// Propagates rulesPerBin and maxRuleComplexity from the night at fromNightIndex forward.
    /// Only nights where wasManuallyEdited is false are overwritten.
    /// Called after any slider change so subsequent unedited nights stay consistent automatically.
    /// Propagation does not touch numberOfBins — bin layout is independent of rule generation.
    /// </summary>
    /// <param name="fromNightIndex">
    /// Zero-based index of the night that was just edited.
    /// Propagation starts at fromNightIndex + 1.
    /// </param>
    public void PropagateNightRules(int fromNightIndex)
    {
        for (int i = fromNightIndex + 1; i < nights.Count; i++)
        {
            NightDesignerData currentNight  = nights[i];
            NightDesignerData previousNight = nights[i - 1];

            // Skip nights the designer has explicitly customized.
            // wasManuallyEdited == true means the designer's choice must be preserved —
            // propagation is for convenience, not a forced override.
            if (currentNight.wasManuallyEdited)
                continue;

            currentNight.rulesPerBin      = previousNight.rulesPerBin;
            currentNight.maxRuleComplexity = previousNight.maxRuleComplexity;
        }
    }
}

/// <summary>
/// Holds the parameter set for one night within a floor.
/// Stores rulesPerBin, maxRuleComplexity, and numberOfBins so RuleGenerator can create
/// the actual rules procedurally at runtime without any manual rule authoring in the tool.
/// Does NOT inherit from any runtime class — Editor-only data carrier.
/// Does NOT store individual rule entries — those are generated at runtime.
/// </summary>
public class NightDesignerData
{
    /// <summary>Zero-based index of this night within the floor (0 to 4).</summary>
    public int nightIndex;

    /// <summary>
    /// Number of sorting bins active for this specific night.
    /// Defaults to the parent floor's numberOfBins but can be overridden per night
    /// to introduce bin changes at a specific point in the floor's progression.
    /// </summary>
    public int numberOfBins;

    /// <summary>
    /// How many rules each bin has during this night.
    /// RuleGenerator reads this parameter at runtime when populating each bin.
    /// Replacing manual rule entry with this parameter means the designer sets QUANTITY;
    /// RuleGenerator decides which specific rules to assign.
    /// </summary>
    public int rulesPerBin;

    /// <summary>
    /// Maximum complexity score of rules generated for this night (range 1–5).
    /// RuleGenerator will not produce rules whose complexity exceeds this ceiling.
    /// Replacing manual rule entry with this parameter means the designer sets DIFFICULTY;
    /// RuleGenerator selects rule types and conditions accordingly.
    /// </summary>
    public int maxRuleComplexity;

    /// <summary>
    /// True when the designer has explicitly changed at least one slider on this night.
    /// False for nights whose values were propagated automatically from the previous night.
    /// Distinguishes auto-propagated nights from intentionally customized ones so
    /// PropagateNightRules does not overwrite manual edits.
    /// </summary>
    public bool wasManuallyEdited;

    /// <summary>
    /// Controls whether the night's sub-panel is expanded in the UI.
    /// Not serialised to JSON — UI expansion state should not pollute save data,
    /// and only one night needs to be open at a time to keep the designer panel readable.
    /// </summary>
    public bool isExpanded;
}

// ─── DesignerRuleEntry is kept for save-file backward-compatible deserialization ─

/// <summary>
/// Legacy rule entry class retained so old JSON save files can still be deserialized.
/// New floors no longer use individual rule entries in the designer — RuleGenerator creates
/// rules at runtime using NightDesignerData parameters (rulesPerBin, maxRuleComplexity).
/// Does NOT contain validation logic — it is a plain data carrier.
/// </summary>
public class DesignerRuleEntry
{
    /// <summary>
    /// Rule type stored as the matching RuleType enum name string.
    /// Using a string lets the dropdown bind directly to enum name arrays
    /// without coupling this Editor-only class to the runtime RuleType enum at compile time.
    /// </summary>
    public string ruleTypeString = string.Empty;

    /// <summary>Primary condition string (e.g. "red_stamp", "urgent").</summary>
    public string conditionA = string.Empty;

    /// <summary>Secondary condition string — used by two-condition rule types only.</summary>
    public string conditionB = string.Empty;

    /// <summary>ID of the primary bin that matching documents are routed into.</summary>
    public string targetBinID = string.Empty;

    /// <summary>ID of the secondary bin used by branch and double rule types.</summary>
    public string secondaryBinID = string.Empty;

    /// <summary>
    /// Pre-computed human-readable sentence for this rule.
    /// Auto-generated from the rule template when conditionA, conditionB, or the rule type changes.
    /// </summary>
    public string displayText = string.Empty;

    /// <summary>Complexity score for this rule, between 1 and 5.</summary>
    public int complexity;

    /// <summary>
    /// Read-only in the designer UI — set automatically during rule generation,
    /// never manually by the designer. Displayed for informational purposes only.
    /// </summary>
    public bool isComplement;
}

// ─── end of FloorDesignerData.cs ─────────────────────────────────────────────
