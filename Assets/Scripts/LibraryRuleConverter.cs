using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Converts a <see cref="RuleLibraryEntry"/> into one or two runtime <see cref="RuleData"/> objects
/// depending on the logical connector. Each rule targets exactly one bin.
///
/// The "corbeille 1 / corbeille 2" labels in the rule tool are NOT bin indices — they simply
/// designate which bin receives which condition of the rule:
///
///   Single condition → 1 rule
///     conditionA → bin1
///
///   "Et" (AND)       → 1 rule  (both conditions together → bin1, other bin needs its own entry)
///     conditionA AND conditionB → bin1
///
///   "Ou" (OR)        → 2 rules  (each condition independently routes to its own bin)
///     conditionA → bin1
///     conditionB → bin2
///
///   "Sauf" (EXCEPT)  → 2 rules  (conditionB presence determines which bin)
///     conditionA WITHOUT conditionB → bin1
///     conditionA WITH    conditionB → bin2
///
/// Does NOT assign rules to bins — that is the responsibility of <see cref="LibraryRuleAssigner"/>.
/// Does NOT contain any MonoBehaviour lifecycle, scene references, or UI code.
/// </summary>
public static class LibraryRuleConverter
{
    // ─── Public API ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Converts a single <see cref="RuleLibraryEntry"/> into a list of <see cref="RuleData"/>.
    ///
    /// Single-condition and "Et" entries produce one rule (bin1 only).
    /// "Ou" and "Sauf" entries produce two rules (bin1 + bin2) when bin2 is provided.
    ///
    /// Returns an empty list when conversion fails (missing data, empty conditions).
    /// </summary>
    /// <param name="entry">The library entry to convert. Must not be null.</param>
    /// <param name="bin1">Bin ID for the first condition's target.</param>
    /// <param name="bin2">Bin ID for the second condition's target. Empty for single-rule entries.</param>
    /// <param name="database">Optional SpecificityDatabase for display text templates.</param>
    public static List<RuleData> Convert(
        RuleLibraryEntry entry,
        string bin1,
        string bin2,
        SpecificityDatabase database)
    {
        if (entry == null)
        {
            Debug.LogWarning("[LibraryRuleConverter] Entry is null — skipping.");
            return new List<RuleData>();
        }

        if (string.IsNullOrEmpty(bin1))
        {
            Debug.LogWarning($"[LibraryRuleConverter] Entry '{entry.label}' has no bin1 — skipping.");
            return new List<RuleData>();
        }

        // An entry may carry both a manuscript sentence (display text) AND structured conditions.
        // Structured conditions always govern assignment logic when present.
        // The manuscript text is only used as the display label — it never drives bin routing.
        // Fall back to the pure-manuscript path only when no conditions exist at all.
        bool hasStructuredConditions = entry.conditions != null && entry.conditions.Count > 0;

        if (hasStructuredConditions)
            return ConvertStructured(entry, bin1, bin2, database, manuscriptDisplayText: entry.manuscriptText);

        return entry.isManuscript
            ? ConvertManuscript(entry, bin1)
            : ConvertStructured(entry, bin1, bin2, database, manuscriptDisplayText: null);
    }

    // ─── Manuscript conversion ───────────────────────────────────────────────────

    /// <summary>
    /// Produces a single PositiveForced rule whose displayText is the manuscript sentence.
    /// conditionA is intentionally empty — manuscript rules are display-only.
    /// </summary>
    private static List<RuleData> ConvertManuscript(RuleLibraryEntry entry, string bin1)
    {
        return new List<RuleData>
        {
            new RuleData
            {
                ruleType     = RuleType.PositiveForced,
                conditionA   = string.Empty,
                conditionB   = string.Empty,
                targetBinID  = bin1,
                displayText  = string.IsNullOrEmpty(entry.manuscriptText) ? entry.label : entry.manuscriptText,
                complexity   = entry.complexity,
                isComplement = false
            }
        };
    }

    // ─── Structured conversion ───────────────────────────────────────────────────

    private static List<RuleData> ConvertStructured(
        RuleLibraryEntry entry,
        string bin1,
        string bin2,
        SpecificityDatabase database,
        string manuscriptDisplayText = null)
    {
        List<ConditionNode> nodes = entry.conditions;

        if (nodes == null || nodes.Count == 0)
        {
            Debug.LogWarning($"[LibraryRuleConverter] Entry '{entry.label}' has no conditions — skipping.");
            return new List<RuleData>();
        }

        // Single condition → one PositiveForced rule targeting bin1.
        if (nodes.Count == 1)
        {
            string condA = nodes[0].specificity;
            if (string.IsNullOrEmpty(condA))
            {
                Debug.LogWarning($"[LibraryRuleConverter] Entry '{entry.label}' has an empty condition — skipping.");
                return new List<RuleData>();
            }

            RuleData rule = BuildRule(RuleType.PositiveForced, condA, string.Empty, bin1, entry.complexity, database);
            if (!string.IsNullOrEmpty(manuscriptDisplayText))
                rule.displayText = manuscriptDisplayText;
            return new List<RuleData> { rule };
        }

        // Two conditions — connector on node[0] governs the relationship.
        string connector = nodes[0].connector;
        string specA     = nodes[0].specificity;
        string specB     = nodes[1].specificity;

        if (string.IsNullOrEmpty(specA) || string.IsNullOrEmpty(specB))
        {
            Debug.LogWarning($"[LibraryRuleConverter] Entry '{entry.label}' has empty specificities — skipping.");
            return new List<RuleData>();
        }

        List<RuleData> result = connector switch
        {
            "Et"   => ConvertEt(specA, specB, bin1, entry.complexity, database),
            "Ou"   => ConvertOu(specA, specB, bin1, bin2, entry.complexity, database),
            "Sauf" => ConvertSauf(specA, specB, bin1, bin2, entry.complexity, database),
            _      => new List<RuleData>
                      {
                          BuildRule(RuleType.PositiveForced, specA, string.Empty, bin1, entry.complexity, database)
                      }
        };

        // When a manuscript sentence exists, use it as the display text for ALL generated rules
        // so every bin shows the full human-readable rule sentence, not a generated template.
        if (!string.IsNullOrEmpty(manuscriptDisplayText))
        {
            foreach (RuleData r in result)
                r.displayText = manuscriptDisplayText;
        }

        return result;
    }

    // ─── Connector-specific builders ─────────────────────────────────────────────

    /// <summary>
    /// ET: both conditions together → bin1 only (1 rule).
    /// Documents must have BOTH conditionA AND conditionB to enter bin1.
    /// The other bin must be handled by a separate library entry.
    /// </summary>
    private static List<RuleData> ConvertEt(
        string condA, string condB, string bin1, int complexity, SpecificityDatabase database)
    {
        return new List<RuleData>
        {
            BuildRule(RuleType.PositiveDouble, condA, condB, bin1, complexity, database)
        };
    }

    /// <summary>
    /// OU: each condition routes independently to its own bin (2 rules).
    ///   conditionA → bin1  (PositiveForced)
    ///   conditionB → bin2  (PositiveForced)
    /// If bin2 is empty, only the bin1 rule is produced.
    /// </summary>
    private static List<RuleData> ConvertOu(
        string condA, string condB, string bin1, string bin2, int complexity, SpecificityDatabase database)
    {
        List<RuleData> rules = new List<RuleData>
        {
            BuildRule(RuleType.PositiveForced, condA, string.Empty, bin1, complexity, database)
        };

        if (!string.IsNullOrEmpty(bin2))
            rules.Add(BuildRule(RuleType.PositiveForced, condB, string.Empty, bin2, complexity, database));
        else
            Debug.LogWarning("[LibraryRuleConverter] Ou entry has no bin2 — conditionB has no target bin.");

        return rules;
    }

    /// <summary>
    /// SAUF: conditionA is shared; conditionB presence determines the bin (2 rules).
    ///   conditionA WITHOUT conditionB → bin1  (PositiveWithNegative)
    ///   conditionA WITH    conditionB → bin2  (PositiveDouble)
    /// If bin2 is empty, only the bin1 rule is produced.
    /// </summary>
    private static List<RuleData> ConvertSauf(
        string condA, string condB, string bin1, string bin2, int complexity, SpecificityDatabase database)
    {
        List<RuleData> rules = new List<RuleData>
        {
            BuildRule(RuleType.PositiveWithNegative, condA, condB, bin1, complexity, database)
        };

        if (!string.IsNullOrEmpty(bin2))
            rules.Add(BuildRule(RuleType.PositiveDouble, condA, condB, bin2, complexity, database));

        return rules;
    }

    // ─── Generic builder ─────────────────────────────────────────────────────────

    private static RuleData BuildRule(
        RuleType ruleType,
        string condA,
        string condB,
        string targetBinID,
        int complexity,
        SpecificityDatabase database)
    {
        return new RuleData
        {
            ruleType     = ruleType,
            conditionA   = condA,
            conditionB   = condB,
            targetBinID  = targetBinID,
            displayText  = BuildDisplayText(ruleType, condA, condB, database),
            complexity   = complexity,
            isComplement = false
        };
    }

    // ─── Display text ─────────────────────────────────────────────────────────────

    private static string BuildDisplayText(
        RuleType ruleType, string condA, string condB, SpecificityDatabase database)
    {
        string template = FindTemplate(ruleType, database);
        return template
            .Replace("{0}", condA ?? string.Empty)
            .Replace("{1}", condB ?? string.Empty);
    }

    private static string FindTemplate(RuleType ruleType, SpecificityDatabase database)
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
            RuleType.PositiveForced       => "Si le document contient {0}, posez-le ici",
            RuleType.PositiveDouble       => "Si le document contient {0} et {1}, posez-le ici",
            RuleType.PositiveOr           => "Si le document contient {0} ou {1}, posez-le ici",
            RuleType.PositiveWithNegative => "Si le document contient {0} mais pas {1}, posez-le ici",
            _                             => "Posez le document ici"
        };
    }
}

/// <summary>
/// Kept for backwards compatibility with any code that references <see cref="ConvertedRulePair"/>.
/// Not used internally by <see cref="LibraryRuleConverter"/> or <see cref="LibraryRuleAssigner"/>.
/// </summary>
public class ConvertedRulePair
{
    public RuleData Primary    { get; }
    public RuleData Complement { get; }

    public ConvertedRulePair(RuleData primary, RuleData complement)
    {
        Primary    = primary;
        Complement = complement;
    }
}
