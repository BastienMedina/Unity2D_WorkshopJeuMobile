using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for procedurally generating RuleData objects
/// from the SpecificityDatabase according to day parameters supplied by the caller.
/// Does NOT handle UI display, rule validation, item spawning, or any other system.
/// All generated rules are returned to the caller — this class never distributes or stores them.
/// </summary>
public class RuleGenerator : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>Database of all available specificities and sentence templates.</summary>
    [SerializeField] private SpecificityDatabase specificityDatabase;

    /// <summary>
    /// IDs of the SortingBin instances that are active in the current scene.
    /// Must be provided from outside (Inspector or GameManager) because RuleGenerator
    /// has no knowledge of the scene structure and must never query it directly.
    /// Assigning bin IDs here ensures every generated rule always points to a real,
    /// existing bin rather than an invented identifier that has no counterpart in the scene.
    /// </summary>
    [SerializeField] private List<string> availableBinIDs = new List<string>();

    // -------------------------------------------------------------------------
    // Complexity clamping bounds — never hardcoded inline
    // -------------------------------------------------------------------------

    [SerializeField] private int minimumComplexity = 1;
    [SerializeField] private int maximumComplexity = 5;

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Specificities already used during the current level.
    /// Prevents repetition across rules within the same play session.
    /// </summary>
    private List<string> usedSpecificities = new List<string>();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a list of RuleData objects for a single game day,
    /// guaranteeing every active bin receives exactly rulesPerBin rules.
    /// Selects rule types by complexityTarget, picks unused specificities, resolves
    /// a matching typed template into displayText, marks specificities as used,
    /// and computes complexity for each rule.
    /// </summary>
    /// <param name="rulesPerBin">
    /// How many rules to generate for each bin. The unit is per bin, not total,
    /// so the distribution is always even and no bin can end up with zero rules.
    /// </param>
    /// <param name="complexityTarget">
    /// Controls which rule types are unlocked:
    /// 1 → PositiveExclusive only (simplest, one condition, easy to read).
    /// 2 → PositiveForced, NegativeSimple, Fallback (moderate logical inversion).
    /// 3+ → ConditionalBranch, NegativeMultiple (branched or multi-negative logic).
    /// Higher complexity targets unlock harder rule types so early days stay readable.
    /// </param>
    /// <returns>
    /// A list of fully populated RuleData objects, ready for consumption by the caller.
    /// Returns an empty list if the database has no valid bin IDs or insufficient specificities.
    /// </returns>
    public List<RuleData> GenerateRulesForDay(int rulesPerBin, int complexityTarget)
    {
        List<RuleData> generatedRules = new List<RuleData>();

        if (availableBinIDs.Count == 0)
            return generatedRules; // No valid bin IDs — generating any rule would produce an unresolvable target.

        List<string> availableSpecificities = BuildAvailablePool();
        int totalBinCount = availableBinIDs.Count;

        // Outer loop over each bin guarantees full coverage:
        // iterating bin-first means no bin can be skipped or left with zero rules,
        // unlike a flat loop that assigns bins randomly and may cluster all rules on one bin.
        foreach (string binID in availableBinIDs)
        {
            for (int ruleIndex = 0; ruleIndex < rulesPerBin; ruleIndex++)
            {
                RuleType selectedType = SelectRuleType(complexityTarget);
                RuleData newRule      = BuildRule(selectedType, binID, availableSpecificities, totalBinCount);

                if (newRule == null)
                    break; // Pool exhausted — cannot safely build another rule for this bin.

                generatedRules.Add(newRule);
            }
        }

        return generatedRules;
    }

    /// <summary>
    /// Resets the used-specificities tracking list so the next level starts with a clean pool.
    /// Should be called by GameManager at the beginning of every new level.
    /// </summary>
    public void ResetForNewLevel()
    {
        usedSpecificities.Clear();
    }

    /// <summary>
    /// Replaces the current list of valid bin IDs at runtime.
    /// Call this from GameManager when the active set of SortingBins changes between levels.
    /// Overrides any IDs previously assigned via the Inspector.
    /// </summary>
    /// <param name="binIDs">The IDs of all SortingBin instances currently present in the scene.</param>
    public void SetAvailableBins(List<string> binIDs)
    {
        availableBinIDs = new List<string>(binIDs);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the pool of specificities that are still available for this level
    /// by excluding everything already present in usedSpecificities.
    /// </summary>
    /// <returns>A mutable list of candidate specificities.</returns>
    private List<string> BuildAvailablePool()
    {
        // Exclude used specificities so the same condition never appears twice
        // in the same level — repetition would make rules feel identical and
        // undermine the incremental difficulty design.
        return specificityDatabase.allSpecificities
            .Where(specificity => !usedSpecificities.Contains(specificity))
            .ToList();
    }

    /// <summary>
    /// Randomly selects a fixed number of specificities from the given pool.
    /// </summary>
    /// <param name="pool">The mutable pool to draw from.</param>
    /// <param name="count">Number of specificities to pick.</param>
    /// <returns>A list of randomly selected specificities.</returns>
    private List<string> PickSpecificities(List<string> pool, int count)
    {
        List<string> picked = new List<string>();

        for (int pickIndex = 0; pickIndex < count; pickIndex++)
        {
            int randomIndex = Random.Range(0, pool.Count);
            string candidate = pool[randomIndex];

            picked.Add(candidate);

            // Temporarily remove from the local pool so the same entry
            // cannot be picked twice within a single rule's condition list.
            pool.RemoveAt(randomIndex);
        }

        // Restore pool entries so the outer loop manages depletion at rule level.
        // (Caller is responsible for removing picked entries from the shared pool.)
        return picked;
    }

    /// <summary>
    /// Selects a RuleType based on the complexity target tier.
    /// Higher tiers unlock rule types with harder logical structures —
    /// keeping early days limited to simple positives ensures the player
    /// can learn the system before facing inverted or branching logic.
    /// </summary>
    /// <param name="complexityTarget">Tier: 1 = easy, 2 = medium, 3+ = hard.</param>
    /// <returns>A randomly chosen RuleType appropriate for the tier.</returns>
    private RuleType SelectRuleType(int complexityTarget)
    {
        if (complexityTarget <= 1)
            return RuleType.PositiveExclusive;

        if (complexityTarget == 2)
        {
            RuleType[] mediumTypes = { RuleType.PositiveForced, RuleType.NegativeSimple, RuleType.Fallback };
            return mediumTypes[Random.Range(0, mediumTypes.Length)];
        }

        // complexityTarget >= 3 — hardest types with branching or multi-negative logic.
        RuleType[] hardTypes = { RuleType.ConditionalBranch, RuleType.NegativeMultiple };
        return hardTypes[Random.Range(0, hardTypes.Length)];
    }

    /// <summary>
    /// Constructs a single fully populated RuleData for the given type and bin,
    /// consuming specificities from the shared pool and marking them as used.
    /// Returns null when the pool cannot satisfy the rule type's specificity requirements.
    /// </summary>
    /// <param name="ruleType">The rule type to build.</param>
    /// <param name="binID">The primary target bin ID.</param>
    /// <param name="availableSpecificities">Shared mutable pool; modified in place.</param>
    /// <param name="totalBinCount">Total number of active bins, used for complexity scoring.</param>
    /// <returns>A populated RuleData, or null if the pool was exhausted.</returns>
    private RuleData BuildRule(RuleType ruleType, string binID,
                               List<string> availableSpecificities, int totalBinCount)
    {
        RuleData rule = new RuleData { ruleType = ruleType, targetBinID = binID };

        switch (ruleType)
        {
            case RuleType.PositiveExclusive:
            case RuleType.PositiveForced:
            case RuleType.NegativeSimple:
                if (!TryConsumeSpecificity(availableSpecificities, out string singleCondition))
                    return null;
                rule.conditionA = singleCondition;
                break;

            case RuleType.ConditionalBranch:
                if (!TryConsumeSpecificity(availableSpecificities, out string condA))
                    return null;
                if (!TryConsumeSpecificity(availableSpecificities, out string condB))
                    return null;
                rule.conditionA      = condA;
                rule.conditionB      = condB;
                // ConditionalBranch routes to a second bin when conditionB is absent.
                // The secondary bin is the next one in the list (wrapping around) so the
                // two bins it references are always distinct and both exist in the scene.
                rule.secondaryBinID  = PickSecondaryBinID(binID);
                break;

            case RuleType.NegativeMultiple:
                // Pick three independent specificities for the "none of these" condition list.
                if (availableSpecificities.Count < 3)
                    return null; // Cannot satisfy NegativeMultiple without at least 3 candidates.
                List<string> multiConditions = PickSpecificities(availableSpecificities, 3);
                foreach (string consumed in multiConditions)
                    availableSpecificities.Remove(consumed);
                usedSpecificities.AddRange(multiConditions);
                rule.conditionsList = multiConditions;
                break;

            case RuleType.Fallback:
                // Fallback requires no specificities — it matches documents that satisfy nothing else.
                break;
        }

        rule.displayText = ResolveTemplate(FindMatchingTemplate(ruleType), rule, ruleType);
        rule.complexity  = ComputeComplexity(ruleType, totalBinCount);

        return rule;
    }

    /// <summary>
    /// Attempts to remove one specificity from the pool, mark it as used, and return it.
    /// Returns false if the pool is empty.
    /// </summary>
    /// <param name="pool">The shared mutable specificity pool.</param>
    /// <param name="result">The consumed specificity, or empty string on failure.</param>
    /// <returns>True if a specificity was successfully consumed; false otherwise.</returns>
    private bool TryConsumeSpecificity(List<string> pool, out string result)
    {
        if (pool.Count == 0)
        {
            result = string.Empty;
            return false;
        }

        int randomIndex = Random.Range(0, pool.Count);
        result = pool[randomIndex];
        pool.RemoveAt(randomIndex);

        // Mark as globally used for this level so the same specificity is never repeated
        // across rules — repetition would make rules feel identical to the player.
        usedSpecificities.Add(result);
        return true;
    }

    /// <summary>
    /// Returns a bin ID that is different from the given primary bin ID.
    /// Used by ConditionalBranch to guarantee the two target bins are distinct.
    /// Falls back to the first bin if no alternate exists (degenerate single-bin case).
    /// </summary>
    /// <param name="primaryBinID">The primary bin ID to exclude.</param>
    /// <returns>A different bin ID from availableBinIDs, or the primary one as a fallback.</returns>
    private string PickSecondaryBinID(string primaryBinID)
    {
        foreach (string candidate in availableBinIDs)
        {
            if (candidate != primaryBinID)
                return candidate;
        }

        // Only one bin exists — ConditionalBranch degrades gracefully rather than crashing.
        return primaryBinID;
    }

    /// <summary>
    /// Computes the complexity score for a rule based on its type and the number of active bins.
    /// Formula: type base score + (binCount - 1), clamped to [minimumComplexity, maximumComplexity].
    /// Complexity reflects both the logical difficulty of the rule type (negative/branched logic
    /// is harder to parse at a glance) and the number of bins the player must consider simultaneously.
    /// </summary>
    /// <param name="ruleType">The rule type whose base score is looked up.</param>
    /// <param name="binCount">Total number of active bins in the scene.</param>
    /// <returns>A clamped integer complexity score.</returns>
    private int ComputeComplexity(RuleType ruleType, int binCount)
    {
        int baseScore = ruleType switch
        {
            RuleType.PositiveExclusive => 1,
            RuleType.PositiveForced    => 2,
            RuleType.Fallback          => 2,
            RuleType.NegativeSimple    => 2,
            RuleType.ConditionalBranch => 3,
            RuleType.NegativeMultiple  => 3,
            _                          => 1
        };

        // Each additional bin beyond the first adds one point — the player must track more
        // destinations simultaneously, increasing working memory load.
        int binPenalty = Mathf.Max(0, binCount - 1);

        return Mathf.Clamp(baseScore + binPenalty, minimumComplexity, maximumComplexity);
    }

    /// <summary>
    /// Searches the database for the first RuleTemplate whose ruleType matches the requested type.
    /// Falls back to a hard-coded default when no matching entry exists, so a missing or
    /// misconfigured template list never blocks rule generation entirely.
    /// Designers will see the fallback text in play mode as a signal to add proper templates.
    /// </summary>
    /// <param name="ruleType">The rule type to match against the database templates.</param>
    /// <returns>A RuleTemplate — either a database entry or a safe built-in default.</returns>
    private RuleTemplate FindMatchingTemplate(RuleType ruleType)
    {
        foreach (RuleTemplate template in specificityDatabase.templates)
        {
            // Match by ruleType, not by requiredSpecificityCount, so each type always gets
            // the sentence structure designed for it rather than an accidentally matching slot count.
            if (template.ruleType == ruleType)
                return template;
        }

        // Fallback: no designer-authored template matched this rule type.
        // These defaults are intentionally minimal — they are not a substitute for proper setup.
        string fallbackText = ruleType switch
        {
            RuleType.PositiveExclusive => "If the document has only {0}, place it here",
            RuleType.PositiveForced    => "If the document has {0}, it goes here regardless of anything else",
            RuleType.NegativeSimple    => "If the document does not have {0}, it goes here",
            RuleType.NegativeMultiple  => "If the document has neither {0}, nor {1}, nor {2}, place it here",
            RuleType.ConditionalBranch => "If {0} is present, check for {1} — if yes place here, if not use the other bin",
            RuleType.Fallback          => "If no other rule applies, place the document here",
            _                          => "Send documents here"
        };

        return new RuleTemplate { templateText = fallbackText, ruleType = ruleType };
    }

    /// <summary>
    /// Substitutes {0}, {1}, and {2} placeholders in the template text using the rule's typed fields.
    /// Each rule type has its own sentence structure — isolating resolution here keeps BuildRule flat
    /// and prevents inline string manipulation from obscuring the generation logic.
    /// </summary>
    /// <param name="template">The template whose text contains placeholders.</param>
    /// <param name="rule">The rule whose typed condition fields supply the substitution values.</param>
    /// <param name="ruleType">The rule type, used to select which fields to read.</param>
    /// <returns>The fully resolved, human-readable rule sentence.</returns>
    private string ResolveTemplate(RuleTemplate template, RuleData rule, RuleType ruleType)
    {
        string resolvedText = template.templateText;

        switch (ruleType)
        {
            case RuleType.PositiveExclusive:
            case RuleType.PositiveForced:
            case RuleType.NegativeSimple:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                break;

            case RuleType.ConditionalBranch:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                resolvedText = resolvedText.Replace("{1}", rule.conditionB);
                break;

            case RuleType.NegativeMultiple:
                // Only replace {1} and {2} when the corresponding conditions exist —
                // leaving a literal placeholder visible to the player would be confusing.
                if (rule.conditionsList.Count >= 1) resolvedText = resolvedText.Replace("{0}", rule.conditionsList[0]);
                if (rule.conditionsList.Count >= 2) resolvedText = resolvedText.Replace("{1}", rule.conditionsList[1]);
                if (rule.conditionsList.Count >= 3) resolvedText = resolvedText.Replace("{2}", rule.conditionsList[2]);
                break;

            case RuleType.Fallback:
                // Fallback templates have no placeholders — no substitution needed.
                break;
        }

        return resolvedText;
    }
}
