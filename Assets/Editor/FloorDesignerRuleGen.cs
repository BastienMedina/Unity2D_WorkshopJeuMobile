using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Editor-only static rule generator for the Floor Designer tool.
/// Mirrors the generation logic of RuleGenerator.GenerateRulesForDay but produces
/// DesignerRuleEntry / BinDesignerData objects directly — no MonoBehaviour, no scene access.
/// Called by FloorDesignerSaveUtils at save time to populate each night's per-bin rule lists.
/// Does NOT exist at runtime.
/// </summary>
public static class FloorDesignerRuleGen
{
    // ─── Complexity bounds (mirrors RuleGenerator inspector fields) ───────────
    private const int MinComplexity = 1;
    private const int MaxComplexity = 5;

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Generates rules for all bins in one night and returns them distributed per bin.
    /// The returned list has exactly numberOfBins entries, each containing exactly rulesPerBin
    /// primary rules. No complement rules are generated automatically.
    /// </summary>
    /// <param name="numberOfBins">How many bins to generate rules for.</param>
    /// <param name="rulesPerBin">Primary rules per bin.</param>
    /// <param name="maxRuleComplexity">Complexity tier: 1 = easy, 2 = medium, 3+ = hard.</param>
    /// <param name="pinnedSpecificities">
    /// When non-empty, conditionA values are drawn exclusively from this list.
    /// When empty or null, draws from database.allSpecificities.
    /// </param>
    /// <param name="database">The SpecificityDatabase asset to use.</param>
    /// <returns>
    /// One BinDesignerData per bin, each populated with exactly rulesPerBin primary rules.
    /// Returns an empty list when the database is null or has no specificities.
    /// </returns>
    public static List<BinDesignerData> GenerateBinsForNight(
        int numberOfBins,
        int rulesPerBin,
        int maxRuleComplexity,
        List<string> pinnedSpecificities,
        SpecificityDatabase database)
    {
        List<BinDesignerData> bins = new List<BinDesignerData>();

        if (database == null || database.allSpecificities == null || database.allSpecificities.Count == 0)
        {
            Debug.LogWarning("[FloorDesignerRuleGen] SpecificityDatabase is null or empty — skipping rule generation.");
            return bins;
        }

        if (numberOfBins <= 0 || rulesPerBin <= 0)
            return bins;

        // Build the bin ID list: bin_A, bin_B, ...
        List<string> binIDs = new List<string>();
        for (int i = 0; i < numberOfBins; i++)
        {
            bins.Add(new BinDesignerData
            {
                binIndex = i,
                binID    = $"bin_{(char)('A' + i)}"
            });
            binIDs.Add(bins[i].binID);
        }

        // Build the specificity pool: pinned list or full database, minus already-used entries.
        bool hasPinned = pinnedSpecificities != null && pinnedSpecificities.Count > 0;
        List<string> pool = hasPinned
            ? new List<string>(pinnedSpecificities)
            : new List<string>(database.allSpecificities);

        // Track used specificities within this night so the same condition is never repeated
        // across rules — mirrors RuleGenerator.usedSpecificities.
        List<string> usedSpecificities = new List<string>();

        // ── Generate rules bin-first — guarantees every bin receives exactly rulesPerBin primaries.
        for (int binIdx = 0; binIdx < bins.Count; binIdx++)
        {
            BinDesignerData bin = bins[binIdx];

            for (int ruleIdx = 0; ruleIdx < rulesPerBin; ruleIdx++)
            {
                // Rebuild available pool each rule so consumed specificities are excluded.
                List<string> availablePool = BuildAvailablePool(pool, usedSpecificities);

                RuleType selectedType = SelectRuleType(maxRuleComplexity);
                DesignerRuleEntry primary = BuildRule(selectedType, bin.binID, binIDs,
                                                      availablePool, usedSpecificities,
                                                      numberOfBins, database);
                if (primary == null)
                    break; // Pool exhausted — stop generating for this bin.

                bin.rules.Add(primary);
            }
        }

        return bins;
    }

    /// <summary>
    /// Generates rules for all bins in one night by drawing randomly from the
    /// provided <paramref name="libraryEntries"/>, filtered to those whose complexity
    /// is less than or equal to <paramref name="maxRuleComplexity"/>.
    /// Each bin receives exactly <paramref name="rulesPerBin"/> rules.
    /// A library entry is never reused within the same night.
    /// Falls back to an empty bin list when no eligible entries exist.
    /// </summary>
    /// <param name="numberOfBins">How many bins to populate.</param>
    /// <param name="rulesPerBin">Rules to assign per bin.</param>
    /// <param name="maxRuleComplexity">Upper complexity bound (inclusive) used to filter entries.</param>
    /// <param name="libraryEntries">All entries loaded from the Rule Library.</param>
    /// <returns>One BinDesignerData per bin, each with exactly rulesPerBin rules drawn from the library.</returns>
    public static List<BinDesignerData> GenerateBinsFromLibrary(
        int numberOfBins,
        int rulesPerBin,
        int maxRuleComplexity,
        List<RuleLibraryEntry> libraryEntries)
    {
        List<BinDesignerData> bins = new List<BinDesignerData>();

        if (libraryEntries == null || libraryEntries.Count == 0)
        {
            Debug.LogWarning("[FloorDesignerRuleGen] Rule Library is empty — skipping library-based rule generation.");
            return bins;
        }

        if (numberOfBins <= 0 || rulesPerBin <= 0)
            return bins;

        // Build bin ID list.
        List<string> binIDs = new List<string>();
        for (int i = 0; i < numberOfBins; i++)
        {
            bins.Add(new BinDesignerData
            {
                binIndex = i,
                binID    = $"bin_{(char)('A' + i)}"
            });
            binIDs.Add(bins[i].binID);
        }

        // Filter library entries by complexity.
        List<RuleLibraryEntry> eligible = new List<RuleLibraryEntry>();
        foreach (RuleLibraryEntry e in libraryEntries)
        {
            if (e.complexity <= maxRuleComplexity)
                eligible.Add(e);
        }

        if (eligible.Count == 0)
        {
            Debug.LogWarning($"[FloorDesignerRuleGen] No library entries with complexity ≤ {maxRuleComplexity}. " +
                             "Returning empty bins.");
            return bins;
        }

        // Pool of entries not yet used this night — rebuilt by reference so entries are never repeated.
        List<RuleLibraryEntry> pool = new List<RuleLibraryEntry>(eligible);

        for (int binIdx = 0; binIdx < bins.Count; binIdx++)
        {
            BinDesignerData bin = bins[binIdx];

            for (int ruleIdx = 0; ruleIdx < rulesPerBin; ruleIdx++)
            {
                if (pool.Count == 0)
                {
                    // All eligible entries consumed — refill from the full eligible list
                    // so generation can continue even when rulesPerBin * bins > library size.
                    pool = new List<RuleLibraryEntry>(eligible);
                    Debug.LogWarning("[FloorDesignerRuleGen] Library pool exhausted and refilled — some entries will repeat.");
                }

                int pickedIdx = Random.Range(0, pool.Count);
                RuleLibraryEntry entry = pool[pickedIdx];
                pool.RemoveAt(pickedIdx);

                DesignerRuleEntry rule = BuildRuleFromLibraryEntry(entry, bin.binID);
                bin.rules.Add(rule);
            }
        }

        return bins;
    }

    /// <summary>
    /// Converts a <see cref="RuleLibraryEntry"/> to a <see cref="DesignerRuleEntry"/>
    /// targeting the given bin, using the same logic as
    /// <see cref="FloorDesignerWindow.ApplyLibraryEntryToBin"/> (isPrimary = true).
    /// </summary>
    private static DesignerRuleEntry BuildRuleFromLibraryEntry(RuleLibraryEntry entry, string binID)
    {
        DesignerRuleEntry rule = new DesignerRuleEntry
        {
            targetBinID  = binID,
            complexity   = entry.complexity,
            isComplement = false
        };

        if (entry.conditions == null || entry.conditions.Count == 0)
        {
            rule.ruleTypeString = RuleType.Simple.ToString();
            rule.conditionA     = string.Empty;
            rule.conditionB     = string.Empty;
            rule.displayText    = string.IsNullOrEmpty(entry.manuscriptText) ? entry.label : entry.manuscriptText;
            return rule;
        }

        string condA = entry.conditions[0].specificity;
        string condB = entry.conditions.Count > 1 ? entry.conditions[1].specificity : string.Empty;
        string conn  = entry.conditions.Count > 1 ? entry.conditions[0].connector   : string.Empty;

        RuleType resolvedType;
        string   usedCondA;
        string   usedCondB;

        switch (conn)
        {
            case "Et":
                resolvedType = RuleType.Multiple;
                usedCondA    = condA;
                usedCondB    = condB;
                break;

            case "Ou":
                resolvedType = RuleType.Simple;
                usedCondA    = condA;
                usedCondB    = string.Empty;
                break;

            case "Sauf":
                resolvedType = RuleType.Branch;
                usedCondA    = condA;
                usedCondB    = condB;
                break;

            default:
                resolvedType = RuleType.Simple;
                usedCondA    = condA;
                usedCondB    = string.Empty;
                break;
        }

        rule.ruleTypeString = resolvedType.ToString();
        rule.conditionA     = usedCondA;
        rule.conditionB     = usedCondB;
        rule.displayText    = BuildStructuredDisplayText(resolvedType, usedCondA, usedCondB, binID);
        return rule;
    }

    /// <summary>
    /// Builds the display sentence from resolved structural fields.
    /// Mirrors <see cref="FloorDesignerWindow.BuildStructuredDisplayText"/>.
    /// </summary>
    private static string BuildStructuredDisplayText(RuleType ruleType, string condA, string condB, string binID)
    {
        string a = string.IsNullOrEmpty(condA) ? "?" : condA;
        string b = string.IsNullOrEmpty(condB) ? "?" : condB;

        return ruleType switch
        {
            RuleType.Simple   => $"Si le document contient {a}, posez-le ici",
            RuleType.Multiple => $"Si le document contient {a} et {b}, posez-le ici",
            RuleType.Branch   => $"Si le document contient {a} mais pas {b}, posez-le ici",
            _                 => "Posez le document ici"
        };
    }

    // ─── Private helpers ──────────────────────────────────────────────────────

    /// <summary>Returns all specificities from the master pool that have not yet been used.</summary>
    private static List<string> BuildAvailablePool(List<string> masterPool, List<string> usedSpecificities)
    {
        List<string> available = new List<string>();
        foreach (string spec in masterPool)
        {
            if (!usedSpecificities.Contains(spec))
                available.Add(spec);
        }
        return available;
    }

    /// <summary>
    /// Selects a RuleType based on the complexity tier.
    /// Mirrors RuleGenerator.SelectRuleType exactly.
    /// </summary>
    private static RuleType SelectRuleType(int complexityTarget)
    {
        if (complexityTarget <= 1)
            return RuleType.Simple;

        if (complexityTarget == 2)
            return RuleType.Multiple;

        // complexityTarget >= 3
        return RuleType.Branch;
    }

    /// <summary>
    /// Builds a single DesignerRuleEntry for the given type and bin.
    /// Consumes specificities from availablePool and marks them in usedSpecificities.
    /// Returns null when the pool cannot satisfy the rule type's requirements.
    /// Mirrors RuleGenerator.BuildRule.
    /// </summary>
    private static DesignerRuleEntry BuildRule(
        RuleType ruleType,
        string binID,
        List<string> binIDs,
        List<string> availablePool,
        List<string> usedSpecificities,
        int totalBinCount,
        SpecificityDatabase database)
    {
        DesignerRuleEntry rule = new DesignerRuleEntry
        {
            ruleTypeString = ruleType.ToString(),
            targetBinID    = binID,
            isComplement   = false
        };

        switch (ruleType)
        {
            case RuleType.Simple:
                if (!TryConsume(availablePool, usedSpecificities, out string condA))
                    return null;
                rule.conditionA = condA;
                break;

            case RuleType.Multiple:
            case RuleType.Branch:
                if (!TryConsume(availablePool, usedSpecificities, out string condAB))
                    return null;
                if (!TryConsume(availablePool, usedSpecificities, out string condBB))
                    return null;
                rule.conditionA = condAB;
                rule.conditionB = condBB;
                break;

            default:
                return null;
        }

        rule.displayText = ResolveDisplayText(ruleType, rule.conditionA, rule.conditionB, binID, database);
        rule.complexity  = ComputeComplexity(ruleType, totalBinCount);
        return rule;
    }

    /// <summary>
    /// Removes and returns a random specificity from availablePool,
    /// then records it in usedSpecificities so it is never repeated.
    /// </summary>
    private static bool TryConsume(List<string> availablePool, List<string> usedSpecificities, out string result)
    {
        if (availablePool.Count == 0)
        {
            result = string.Empty;
            return false;
        }

        int idx = Random.Range(0, availablePool.Count);
        result = availablePool[idx];
        availablePool.RemoveAt(idx);
        usedSpecificities.Add(result);
        return true;
    }

    /// <summary>
    /// Picks a random bin ID from binIDs that is different from primaryBinID.
    /// Falls back to primaryBinID when no alternative exists.
    /// </summary>
    private static string PickDifferentBinID(string primaryBinID, List<string> binIDs)
    {
        List<string> others = binIDs.FindAll(id => id != primaryBinID);
        if (others.Count == 0)
            return primaryBinID;
        return others[Random.Range(0, others.Count)];
    }

    /// <summary>
    /// Computes the complexity score for a rule.
    /// Mirrors RuleGenerator.ComputeComplexity.
    /// </summary>
    private static int ComputeComplexity(RuleType ruleType, int binCount)
    {
        int baseScore = ruleType switch
        {
            RuleType.Simple   => 1,
            RuleType.Multiple => 2,
            RuleType.Branch   => 3,
            _                 => 1
        };

        int binPenalty = Mathf.Max(0, binCount - 1);
        return Mathf.Clamp(baseScore + binPenalty, MinComplexity, MaxComplexity);
    }

    /// <summary>
    /// Resolves the display text for a rule using the SpecificityDatabase templates.
    /// Mirrors RuleGenerator.ResolveTemplate / FindMatchingTemplate.
    /// Falls back to a hard-coded sentence when no matching template is found.
    /// </summary>
    private static string ResolveDisplayText(RuleType ruleType, string condA, string condB,
                                              string binID, SpecificityDatabase database)
    {
        string templateText = FindTemplateText(ruleType, database);

        templateText = templateText.Replace("{0}", condA ?? string.Empty);
        templateText = templateText.Replace("{1}", condB ?? string.Empty);
        templateText = templateText.Replace("{bin}", binID);
        return templateText;
    }

    /// <summary>
    /// Returns the template text for the given ruleType from the database,
    /// falling back to a hard-coded default when none is found.
    /// </summary>
    private static string FindTemplateText(RuleType ruleType, SpecificityDatabase database)
    {
        if (database?.templates != null)
        {
            foreach (RuleTemplate t in database.templates)
            {
                if (t.ruleType == ruleType)
                    return t.templateText;
            }
        }

        return ruleType switch
        {
            RuleType.Simple   => "Si le document a {0}, il doit aller ici",
            RuleType.Multiple => "Si {0} et {1} sont présents → ici",
            RuleType.Branch   => "Si {0} est présent mais pas {1}, posez-le ici",
            _                 => "Envoyez les documents ici"
        };
    }
}
