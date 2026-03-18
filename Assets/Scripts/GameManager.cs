using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that acts as the single orchestrator for one game day.
/// Coordinates RuleGenerator, DocumentSpawner, DifficultyManager, and SortingBins
/// in the correct sequence without containing any rule logic, spawn logic, or validation logic.
/// Does NOT generate rules, spawn documents, compute difficulty, or validate documents directly.
/// </summary>
public class GameManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned system references
    // -------------------------------------------------------------------------

    /// <summary>Generates the day's rule set from the SpecificityDatabase.</summary>
    [SerializeField] private RuleGenerator ruleGenerator;

    /// <summary>Controls document instantiation and the spawn loop.</summary>
    [SerializeField] private DocumentSpawner documentSpawner;

    /// <summary>Computes difficulty settings and day evolution type from the progression index.</summary>
    [SerializeField] private DifficultyManager difficultyManager;

    /// <summary>All sorting bins active in the scene. GameManager distributes rules across them.</summary>
    [SerializeField] private List<SortingBin> sortingBins;

    // -------------------------------------------------------------------------
    // Inspector-editable progression state
    // -------------------------------------------------------------------------

    /// <summary>Zero-based index of the current day within the current level.</summary>
    [SerializeField] private int currentDayIndex = 0;

    /// <summary>Zero-based index of the current level.</summary>
    [SerializeField] private int currentLevelIndex = 0;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        InitializeDay();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Bootstraps all systems for the current day in the correct dependency order:
    /// difficulty → bin IDs → rule generation → rule distribution → spawner setup → spawn start.
    /// </summary>
    public void InitializeDay()
    {
        (DifficultySettings daySettings, DayEvolutionType evolutionType) =
            difficultyManager.OnNewDay(currentDayIndex, currentLevelIndex);

        List<string> activeBinIDs = ExtractBinIDs(sortingBins);
        ruleGenerator.SetAvailableBins(activeBinIDs);

        List<RuleData> generatedRules = ruleGenerator.GenerateRulesForDay(
            daySettings.numberOfRules,
            daySettings.maxRuleComplexity
        );

        DistributeRulesToBins(generatedRules);

        documentSpawner.UpdateActiveRules(generatedRules);

        // Apply the difficulty-driven interval before starting the loop so the
        // first spawn waits the correct duration rather than the previous day's value.
        documentSpawner.SetSpawnInterval(daySettings.spawnInterval);

        documentSpawner.StartDay();
    }

    /// <summary>
    /// Shuts down the current day, clears all live documents, advances the day counter,
    /// and immediately initialises the next day.
    /// </summary>
    public void EndDay()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();

        currentDayIndex++;

        InitializeDay();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Collects the string ID from each SortingBin in the scene list.
    /// Extracted into its own method to keep InitializeDay at a single level of abstraction.
    /// </summary>
    /// <param name="bins">The list of active SortingBin instances.</param>
    /// <returns>A list of bin ID strings in the same order as the input list.</returns>
    private List<string> ExtractBinIDs(List<SortingBin> bins)
    {
        List<string> binIDs = new List<string>();

        foreach (SortingBin bin in bins)
            binIDs.Add(bin.GetBinID());

        return binIDs;
    }

    /// <summary>
    /// Sends each generated rule to the SortingBin whose ID matches the rule's targetBinID.
    /// Rules whose targetBinID does not match any active bin are silently skipped —
    /// this guards against stale IDs that may no longer exist in the scene.
    /// </summary>
    /// <param name="rules">The full list of RuleData produced for the current day.</param>
    private void DistributeRulesToBins(List<RuleData> rules)
    {
        // Group rules by target bin first so each bin receives one AssignRules call,
        // avoiding multiple overwrites when several rules share the same targetBinID.
        Dictionary<string, List<RuleData>> rulesByBinID = GroupRulesByBinID(rules);

        foreach (SortingBin bin in sortingBins)
        {
            string binID = bin.GetBinID();

            bool hasBinRules = rulesByBinID.TryGetValue(binID, out List<RuleData> binRules);

            // Bins with no rules assigned still receive an empty list so they display
            // a "No rules assigned" message rather than showing stale rules from a previous day.
            bin.AssignRules(hasBinRules ? binRules : new List<RuleData>());
        }
    }

    /// <summary>
    /// Organises a flat rule list into a dictionary keyed by targetBinID.
    /// </summary>
    /// <param name="rules">The flat list of rules to group.</param>
    /// <returns>A dictionary mapping each bin ID to its associated rules.</returns>
    private Dictionary<string, List<RuleData>> GroupRulesByBinID(List<RuleData> rules)
    {
        Dictionary<string, List<RuleData>> groupedRules = new Dictionary<string, List<RuleData>>();

        foreach (RuleData rule in rules)
        {
            bool hasBinEntry = groupedRules.ContainsKey(rule.targetBinID);

            if (!hasBinEntry)
                groupedRules[rule.targetBinID] = new List<RuleData>();

            groupedRules[rule.targetBinID].Add(rule);
        }

        return groupedRules;
    }
}
