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
    /// The returned list has exactly numberOfBins entries, each containing rulesPerBin
    /// primary rules plus their auto-generated complement rules.
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
    /// One BinDesignerData per bin, each already populated with primary + complement rules.
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

                // Generate the complement immediately and place it on a different bin.
                string complementBinID  = PickDifferentBinID(bin.binID, binIDs);
                DesignerRuleEntry complement = GenerateComplement(primary, complementBinID, database);

                if (complement != null)
                {
                    // Assign the complement to the correct bin's rule list.
                    BinDesignerData complementBin = bins.Find(b => b.binID == complementBinID);
                    complementBin?.rules.Add(complement);
                }
            }
        }

        return bins;
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
            return RuleType.PositiveExclusive;

        if (complexityTarget == 2)
        {
            RuleType[] medium = { RuleType.PositiveForced, RuleType.NegativeSimple };
            return medium[Random.Range(0, medium.Length)];
        }

        // complexityTarget >= 3
        RuleType[] hard = { RuleType.ConditionalBranch, RuleType.PositiveDouble, RuleType.PositiveWithNegative };
        return hard[Random.Range(0, hard.Length)];
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
            case RuleType.PositiveExclusive:
            case RuleType.PositiveForced:
            case RuleType.NegativeSimple:
                if (!TryConsume(availablePool, usedSpecificities, out string condA))
                    return null;
                rule.conditionA = condA;
                break;

            case RuleType.ConditionalBranch:
            case RuleType.PositiveDouble:
            case RuleType.PositiveWithNegative:
                if (!TryConsume(availablePool, usedSpecificities, out string condAB))
                    return null;
                if (!TryConsume(availablePool, usedSpecificities, out string condBB))
                    return null;
                rule.conditionA = condAB;
                rule.conditionB = condBB;
                break;

            default:
                // Complement types are never selected by SelectRuleType — safe to ignore.
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
    /// Generates a complement DesignerRuleEntry for the given primary rule.
    /// Returns null for self-complementary types (ConditionalBranch, PositiveDouble)
    /// and when no complement type mapping exists.
    /// Mirrors RuleGenerator.GenerateComplementRule.
    /// </summary>
    private static DesignerRuleEntry GenerateComplement(DesignerRuleEntry primary,
                                                         string complementBinID,
                                                         SpecificityDatabase database)
    {
        if (primary.isComplement)
            return null;

        if (!System.Enum.TryParse(primary.ruleTypeString, out RuleType primaryType))
            return null;

        // Self-complementary types already handle both outcomes.
        if (primaryType == RuleType.ConditionalBranch || primaryType == RuleType.PositiveDouble)
            return null;

        RuleType complementType = ResolveComplementType(primaryType);
        if (complementType == primaryType)
            return null;

        DesignerRuleEntry complement = new DesignerRuleEntry
        {
            ruleTypeString = complementType.ToString(),
            conditionA     = primary.conditionA,
            conditionB     = primary.conditionB,
            targetBinID    = complementBinID,
            isComplement   = true,
            complexity     = primary.complexity
        };

        complement.displayText = ResolveDisplayText(
            complementType, complement.conditionA, complement.conditionB, complementBinID, database);

        return complement;
    }

    /// <summary>Maps a primary RuleType to its complement type. Mirrors RuleGenerator.ResolveComplementType.</summary>
    private static RuleType ResolveComplementType(RuleType primaryType)
    {
        return primaryType switch
        {
            RuleType.PositiveForced       => RuleType.ComplementPositiveForced,
            RuleType.PositiveExclusive    => RuleType.ComplementPositiveExclusive,
            RuleType.NegativeSimple       => RuleType.ComplementNegativeSimple,
            RuleType.PositiveWithNegative => RuleType.ComplementPositiveWithNegative,
            _                             => primaryType
        };
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
            RuleType.PositiveExclusive              => 1,
            RuleType.PositiveForced                 => 2,
            RuleType.NegativeSimple                 => 2,
            RuleType.ConditionalBranch              => 3,
            RuleType.PositiveDouble                 => 3,
            RuleType.PositiveWithNegative           => 3,
            RuleType.ComplementPositiveForced       => 2,
            RuleType.ComplementPositiveExclusive    => 1,
            RuleType.ComplementNegativeSimple       => 2,
            RuleType.ComplementPositiveWithNegative => 3,
            _                                       => 1
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

        // Hard-coded fallbacks — mirrors RuleGenerator's fallback block.
        return ruleType switch
        {
            RuleType.PositiveExclusive              => "Si le document a seulement {0}, posez-le ici",
            RuleType.PositiveForced                 => "Si le document a {0}, il doit aller ici",
            RuleType.NegativeSimple                 => "Si le document n'a pas {0}, posez-le ici",
            RuleType.ConditionalBranch              => "Si {0} : {1} présent → ici, sinon autre corbeille",
            RuleType.PositiveDouble                 => "Si {0} et {1} sont présents → ici, sinon autre corbeille",
            RuleType.PositiveWithNegative           => "Si {0} est présent mais pas {1}, posez-le ici",
            RuleType.ComplementPositiveForced       => "Si le document n'a PAS {0}, posez-le ici",
            RuleType.ComplementPositiveExclusive    => "Si {0} est présent avec d'autres spécificités, posez-le ici",
            RuleType.ComplementNegativeSimple       => "Si le document a {0}, posez-le ici",
            RuleType.ComplementPositiveWithNegative => "Si {0} et {1} sont tous deux présents, posez-le ici",
            _                                       => "Envoyez les documents ici"
        };
    }
}
