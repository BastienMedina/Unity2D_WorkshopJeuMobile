using System.Collections.Generic;
using System.IO;
using UnityEngine;

/// <summary>
/// Upgrades the complexity of rules currently assigned to sorting bins by finding
/// higher-complexity candidates in the Rule Library that share at least one condition
/// with the existing rule on each bin.
///
/// Example:
///   Existing rule:  PositiveForced(pastille_bleu) → bin_A   (complexity 1)
///   Upgrade found:  PositiveDouble(pastille_bleu, tampon_carre) → bin_A  (complexity 2)
///   Result: the bin's rule is replaced by the higher-complexity candidate.
///
/// The upgrade does NOT change which bin a condition points to — only the internal
/// structure of the rule (more conditions, harder connector) is upgraded.
///
/// Integration: call <see cref="TryUpgradeBins"/> after <see cref="LibraryRuleAssigner"/>
/// has finished its initial assignment. The assigner's OnRulesAssigned event is the
/// natural hook point; wire it from <see cref="LibraryGameManager"/> when an upgrade
/// is desired for the current floor/night.
/// </summary>
public class RuleComplexityUpgrader : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [SerializeField] private SpecificityDatabase specificityDatabase;

    // ─── Constants ────────────────────────────────────────────────────────────────

    private static string LibraryFilePath =>
        Path.Combine(Application.dataPath, "Editor", "RuleLibraryData.json");

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to upgrade rules in all active bins to higher-complexity candidates
    /// found in the Rule Library. Each bin is upgraded independently — a bin whose
    /// rule has no valid higher-complexity candidate is left unchanged.
    ///
    /// After upgrades are applied, each bin's display and its cross-bin AllActiveRules
    /// list are refreshed so validation and the spawner both see the updated rules.
    /// </summary>
    /// <param name="activeBins">All bins currently active in the scene.</param>
    /// <param name="targetComplexity">
    /// Minimum complexity the upgraded rule must have.
    /// Typically the current floor's <c>maxRuleComplexity</c> value.
    /// </param>
    public void TryUpgradeBins(List<SortingBin> activeBins, int targetComplexity)
    {
        if (activeBins == null || activeBins.Count == 0)
        {
            Debug.LogWarning("[RuleComplexityUpgrader] No active bins — upgrade skipped.");
            return;
        }

        RuleLibraryFile library = LoadLibrary();

        if (library == null || library.entries == null || library.entries.Count == 0)
        {
            Debug.LogWarning("[RuleComplexityUpgrader] Rule Library is empty or missing — upgrade skipped.");
            return;
        }

        // Collect ALL active rules across every bin so we can push an updated
        // cross-bin list back into each SortingBin after the upgrades.
        List<RuleData> allRulesAfterUpgrade = new List<RuleData>();

        // First pass: attempt upgrade per bin, collect the resulting rules.
        foreach (SortingBin bin in activeBins)
        {
            List<RuleData> upgraded = UpgradeBinRules(bin, library, targetComplexity);
            allRulesAfterUpgrade.AddRange(upgraded);
        }

        // Second pass: push the complete updated rule list back into every bin
        // so ValidateDocument has an accurate cross-bin view for Fallback rules.
        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(allRulesAfterUpgrade);

        Debug.Log($"[RuleComplexityUpgrader] Upgrade pass complete — {allRulesAfterUpgrade.Count} total rules active.");
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Attempts to upgrade each rule assigned to <paramref name="bin"/>.
    /// Returns the bin's final rule list (upgraded or unchanged) and calls
    /// <see cref="SortingBin.AssignRules"/> to refresh the bin's display.
    /// </summary>
    private List<RuleData> UpgradeBinRules(SortingBin bin, RuleLibraryFile library, int targetComplexity)
    {
        // Read the bin's current rules. SortingBin exposes no direct list getter —
        // we use the cross-bin AllActiveRules snapshot filtered by targetBinID as the source.
        // Since we just finished assignment, allActiveRules on the bin is up to date.
        List<RuleData> currentRules = GetBinRules(bin);
        List<RuleData> upgradedRules = new List<RuleData>();

        foreach (RuleData existing in currentRules)
        {
            RuleData candidate = FindUpgradeCandidate(existing, library, targetComplexity);

            if (candidate != null)
            {
                // Preserve the original targetBinID — the library entry may have had a
                // different resolved bin; what matters is keeping the condition on the
                // same bin the assigner originally chose.
                candidate.targetBinID = existing.targetBinID;
                upgradedRules.Add(candidate);

                Debug.Log($"[RuleComplexityUpgrader] Bin '{bin.GetBinID()}': upgraded " +
                          $"'{existing.conditionA}' (complexity {existing.complexity}) → " +
                          $"'{candidate.conditionA}+{candidate.conditionB}' (complexity {candidate.complexity})");
            }
            else
            {
                // No valid upgrade found — keep the existing rule as-is.
                upgradedRules.Add(existing);
            }
        }

        bin.AssignRules(upgradedRules);
        return upgradedRules;
    }

    /// <summary>
    /// Searches the library for an entry that:
    ///   1. Has complexity strictly greater than <paramref name="existing"/>.complexity.
    ///   2. Has complexity at most <paramref name="targetComplexity"/>.
    ///   3. Shares at least one condition with the existing rule.
    ///   4. Uses a connector that increases logical complexity
    ///      (Et or Sauf — both require an additional condition to evaluate).
    ///
    /// Among all qualifying entries, picks the one with the lowest complexity
    /// above the existing rule — this produces the gentlest upgrade step.
    /// Ties are broken randomly so the same floor does not always upgrade
    /// to the identical rule.
    /// </summary>
    private RuleData FindUpgradeCandidate(RuleData existing, RuleLibraryFile library, int targetComplexity)
    {
        // Gather every condition the existing rule uses so we can test overlap below.
        List<string> existingConditions = CollectConditions(existing);

        List<RuleLibraryEntry> candidates = new List<RuleLibraryEntry>();
        int lowestViableComplexity = int.MaxValue;

        foreach (RuleLibraryEntry entry in library.entries)
        {
            // Entries must have structured conditions — manuscript-only entries have no logic.
            if (entry.conditions == null || entry.conditions.Count < 2)
                continue;

            // Only entries whose complexity is strictly higher but within the target ceiling.
            if (entry.complexity <= existing.complexity || entry.complexity > targetComplexity)
                continue;

            // Only Et or Sauf connectors add a meaningful condition constraint.
            string connector = entry.conditions[0].connector;
            if (connector != "Et" && connector != "Sauf")
                continue;

            // At least one condition must overlap with the existing rule.
            bool hasOverlap = false;
            foreach (ConditionNode node in entry.conditions)
            {
                if (existingConditions.Contains(node.specificity))
                {
                    hasOverlap = true;
                    break;
                }
            }

            if (!hasOverlap)
                continue;

            // Track the gentlest step: prefer the lowest complexity above the current.
            if (entry.complexity < lowestViableComplexity)
            {
                lowestViableComplexity = entry.complexity;
                candidates.Clear();
                candidates.Add(entry);
            }
            else if (entry.complexity == lowestViableComplexity)
            {
                candidates.Add(entry);
            }
        }

        if (candidates.Count == 0)
            return null;

        // Pick randomly among tied candidates for variety.
        RuleLibraryEntry chosen = candidates[Random.Range(0, candidates.Count)];
        return ConvertEntryToRuleData(chosen, existing.targetBinID);
    }

    /// <summary>
    /// Converts a <see cref="RuleLibraryEntry"/> into a single <see cref="RuleData"/>
    /// targeting <paramref name="binID"/>.
    ///
    /// Only the primary rule is produced — the upgrade replaces the existing rule on
    /// that bin; the secondary rule (bin2) of a Sauf entry is ignored here because the
    /// upgrade does not restructure the global bin assignment, only the rule on one bin.
    /// </summary>
    private RuleData ConvertEntryToRuleData(RuleLibraryEntry entry, string binID)
    {
        List<RuleData> batch = LibraryRuleConverter.Convert(entry, binID, string.Empty, specificityDatabase);

        // Take only the first rule — it targets binID as requested.
        if (batch == null || batch.Count == 0)
            return null;

        RuleData rule = batch[0];
        rule.complexity = entry.complexity;
        return rule;
    }

    /// <summary>
    /// Returns all non-empty condition values referenced by <paramref name="rule"/>.
    /// </summary>
    private static List<string> CollectConditions(RuleData rule)
    {
        List<string> conditions = new List<string>();

        if (!string.IsNullOrEmpty(rule.conditionA))
            conditions.Add(rule.conditionA);

        if (!string.IsNullOrEmpty(rule.conditionB))
            conditions.Add(rule.conditionB);

        return conditions;
    }

    /// <summary>
    /// Retrieves the rules currently assigned to <paramref name="bin"/> by asking the bin
    /// to validate a sentinel document — no such document exists, so this is a no-op.
    ///
    /// Because <see cref="SortingBin"/> does not expose its rule list directly (by design —
    /// the bin is the authority on its own rules), we use <see cref="SortingBin.GetAssignedRules"/>
    /// if available, or fall back to the cross-bin snapshot filtered by binID.
    ///
    /// NOTE: <see cref="SortingBin.GetAssignedRules"/> must be added as a public getter
    /// returning a copy of the assignedRules field. This is the only addition needed
    /// to SortingBin to support this upgrader.
    /// </summary>
    private static List<RuleData> GetBinRules(SortingBin bin)
    {
        return bin.GetAssignedRules();
    }

    /// <summary>
    /// Loads the Rule Library JSON from disk.
    /// Returns null when the file does not exist or is malformed.
    /// </summary>
    private static RuleLibraryFile LoadLibrary()
    {
        if (!File.Exists(LibraryFilePath))
        {
            Debug.LogWarning("[RuleComplexityUpgrader] RuleLibraryData.json not found.");
            return null;
        }

        try
        {
            string json = File.ReadAllText(LibraryFilePath);
            return JsonUtility.FromJson<RuleLibraryFile>(json);
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[RuleComplexityUpgrader] Failed to load library: {ex.Message}");
            return null;
        }
    }
}
