using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads <see cref="RuleLibraryEntry"/> objects from a <see cref="RuleLibraryFile"/>,
/// converts each one into one or two <see cref="RuleData"/> via <see cref="LibraryRuleConverter"/>,
/// applies conflict detection, and distributes the final rule set to the active bins.
///
/// Conversion per connector type:
///   single / Et  → 1 rule  targeting resolvedBin1
///   Ou           → 2 rules: conditionA → resolvedBin1, conditionB → resolvedBin2
///   Sauf         → 2 rules: conditionA without conditionB → resolvedBin1,
///                           conditionA with  conditionB → resolvedBin2
///
/// Conflict detection: a condition may only be the PRIMARY trigger of one bin across
/// DIFFERENT entries. Rules from the same entry batch are exempt from inter-rule conflict
/// checks (e.g. Sauf shares conditionA across two rules targeting different bins by design).
///
/// Fires <see cref="OnRulesAssigned"/> after assignment so <see cref="LibraryDocumentSpawner"/>
/// can rebuild its valid combination pool automatically.
/// </summary>
public class LibraryRuleAssigner : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [SerializeField] private SpecificityDatabase specificityDatabase;

    // ─── Events ───────────────────────────────────────────────────────────────────

    /// <summary>
    /// Fired after all rules have been assigned to bins.
    /// Carries the flat list of every accepted <see cref="RuleData"/>.
    /// </summary>
    public event Action<List<RuleData>> OnRulesAssigned;

    // ─── Runtime state ────────────────────────────────────────────────────────────

    private List<RuleData> activeRules = new List<RuleData>();

    /// <summary>
    /// Maps conditionA of a single-rule entry to the bin ID it is committed to.
    /// Only populated for single-rule entries (single condition, Et) — multi-rule
    /// entries (Ou, Sauf) skip this check since they intentionally split one condition
    /// across two bins.
    /// </summary>
    private Dictionary<string, string> conditionToBinMap = new Dictionary<string, string>();

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Returns a copy of the flat active rule list.</summary>
    public List<RuleData> GetActiveRules() => new List<RuleData>(activeRules);

    /// <summary>Clears all rule assignments and conflict tracking.</summary>
    public void Reset()
    {
        activeRules.Clear();
        conditionToBinMap.Clear();
        Debug.Log("[LibraryRuleAssigner] State reset.");
    }

    /// <summary>
    /// Reads all entries from <paramref name="libraryFile"/>, converts them to runtime rules,
    /// validates conflicts, then distributes accepted rules to bins.
    /// Fires <see cref="OnRulesAssigned"/> with the final flat rule list.
    /// </summary>
    public void AssignFromLibrary(
        RuleLibraryFile libraryFile,
        List<SortingBin> activeBins,
        List<string> availableBinIDs)
    {
        if (libraryFile == null)
        {
            Debug.LogError("[LibraryRuleAssigner] libraryFile is null — no rules assigned.");
            return;
        }

        if (activeBins == null || activeBins.Count == 0)
        {
            Debug.LogError("[LibraryRuleAssigner] No active bins — no rules assigned.");
            return;
        }

        Reset();

        foreach (RuleLibraryEntry entry in libraryFile.entries)
            TryAssignEntry(entry, availableBinIDs);

        DistributeRulesToBins(activeBins);

        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(activeRules);

        Debug.Log($"[LibraryRuleAssigner] Assignment complete — {activeRules.Count} rules active.");
        OnRulesAssigned?.Invoke(new List<RuleData>(activeRules));
    }

    // ─── Private helpers ──────────────────────────────────────────────────────────

    /// <summary>
    /// Converts one library entry to its rule batch and registers all rules.
    ///
    /// bin1 = resolvedBin1 from the entry (the bin for conditionA / the combined condition).
    /// bin2 = resolvedBin2 from the entry (the bin for conditionB in Ou/Sauf entries).
    ///
    /// Conflict detection only runs for single-rule batches, because multi-rule batches
    /// (Ou, Sauf) deliberately route the same or related conditions to different bins.
    /// </summary>
    private void TryAssignEntry(RuleLibraryEntry entry, List<string> availableBinIDs)
    {
        if (entry == null)
            return;

        string bin1 = ResolveBin(entry.resolvedBin1, availableBinIDs, excludedBin: string.Empty);
        string bin2 = ResolveBin(entry.resolvedBin2, availableBinIDs, excludedBin: bin1);

        if (string.IsNullOrEmpty(bin1))
        {
            Debug.LogWarning($"[LibraryRuleAssigner] Entry '{entry.label}' — no bin1 available. Skipped.");
            return;
        }

        List<RuleData> batch = LibraryRuleConverter.Convert(entry, bin1, bin2, specificityDatabase);

        if (batch == null || batch.Count == 0)
        {
            Debug.LogWarning($"[LibraryRuleAssigner] Conversion produced no rules for entry '{entry.label}'.");
            return;
        }

        // For single-rule batches, check conditionA conflict against other entries.
        // Multi-rule batches (Ou, Sauf) are exempt — they split conditions by design.
        if (batch.Count == 1)
        {
            RuleData rule = batch[0];
            if (!TryRegisterCondition(rule.conditionA, rule.targetBinID, entry.label))
                return;
        }

        foreach (RuleData rule in batch)
        {
            activeRules.Add(rule);
            Debug.Log($"[LibraryRuleAssigner] Rule added: '{rule.conditionA}' [{rule.ruleType}] → {rule.targetBinID}");
        }
    }

    /// <summary>
    /// Returns <paramref name="resolved"/> when non-empty and in the available pool.
    /// Falls back to the first available bin that is not <paramref name="excludedBin"/>
    /// and has no rule yet. Returns empty string when no candidate exists.
    /// </summary>
    private string ResolveBin(string resolved, List<string> availableBinIDs, string excludedBin)
    {
        if (!string.IsNullOrEmpty(resolved) && availableBinIDs.Contains(resolved))
            return resolved;

        foreach (string binID in availableBinIDs)
        {
            if (binID == excludedBin)
                continue;

            bool occupied = activeRules.Exists(r => r.targetBinID == binID);
            if (!occupied)
                return binID;
        }

        return string.Empty;
    }

    /// <summary>
    /// Registers <paramref name="condition"/> → <paramref name="binID"/> in the conflict map.
    /// Returns false if the condition is already committed to a different bin.
    /// </summary>
    private bool TryRegisterCondition(string condition, string binID, string entryLabel)
    {
        if (string.IsNullOrEmpty(condition))
            return true;

        if (conditionToBinMap.TryGetValue(condition, out string existingBin))
        {
            if (existingBin == binID)
                return true;

            Debug.LogWarning(
                $"[LibraryRuleAssigner] CONFLICT — entry '{entryLabel}': condition '{condition}' " +
                $"already committed to '{existingBin}', cannot also assign to '{binID}'. Rule rejected.");
            return false;
        }

        conditionToBinMap[condition] = binID;
        return true;
    }

    /// <summary>
    /// Sends each accepted rule to the <see cref="SortingBin"/> whose ID matches.
    /// Bins with no rules receive an empty list.
    /// </summary>
    private void DistributeRulesToBins(List<SortingBin> activeBins)
    {
        Dictionary<string, List<RuleData>> rulesByBin = new Dictionary<string, List<RuleData>>();

        foreach (RuleData rule in activeRules)
        {
            if (!rulesByBin.ContainsKey(rule.targetBinID))
                rulesByBin[rule.targetBinID] = new List<RuleData>();

            rulesByBin[rule.targetBinID].Add(rule);
        }

        foreach (SortingBin bin in activeBins)
        {
            string binID = bin.GetBinID();
            bin.AssignRules(rulesByBin.TryGetValue(binID, out List<RuleData> binRules)
                ? binRules
                : new List<RuleData>());
        }
    }
}
