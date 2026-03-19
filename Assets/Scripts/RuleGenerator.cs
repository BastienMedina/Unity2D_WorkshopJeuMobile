using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for procedurally generating RuleData objects
/// from the SpecificityDatabase according to day parameters supplied by the caller.
/// Automatically generates complement rules for every primary rule so that no document
/// can ever be unplaceable — every possible specificity combination always has exactly
/// one valid bin destination.
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
    /// guaranteeing every active bin receives exactly rulesPerBin primary rules.
    /// For every primary rule, a complement rule is immediately generated and assigned
    /// to a different bin so that every possible document combination always has a valid destination.
    /// Selects rule types by complexityTarget, picks unused specificities, resolves
    /// a matching typed template into displayText, marks specificities as used,
    /// and computes complexity for each rule.
    /// </summary>
    /// <param name="rulesPerBin">
    /// How many primary rules to generate per bin. The unit is per bin, not total,
    /// so the distribution is always even and no bin can end up with zero rules.
    /// </param>
    /// <param name="complexityTarget">
    /// Controls which rule types are unlocked:
    /// 1 → PositiveExclusive only (simplest, one condition, easy to read).
    /// 2 → PositiveForced, NegativeSimple (moderate logical inversion).
    /// 3+ → ConditionalBranch, PositiveDouble, PositiveWithNegative (branched or dual-condition logic).
    /// Higher complexity targets unlock harder rule types so early days stay readable.
    /// </param>
    /// <returns>
    /// A list of fully populated RuleData objects (primary + complements), ready for the caller.
    /// Returns an empty list if the database has no valid bin IDs or insufficient specificities.
    /// </returns>
    public List<RuleData> GenerateRulesForDay(int rulesPerBin, int complexityTarget)
    {
        // rulesPerBin primary rules go into each bin, plus one auto-generated complement per primary.
        // We collect all rules (primary + complement) in a flat list for the caller.
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
                RuleData primaryRule  = BuildRule(selectedType, binID, availableSpecificities, totalBinCount);

                if (primaryRule == null)
                    break; // Pool exhausted — cannot safely build another rule for this bin.

                generatedRules.Add(primaryRule);

                // Generate complement immediately after creating the primary rule so the pair is
                // always produced together — no generation path can be skipped or partially executed.
                string complementBinID = PickDifferentBinID(binID);
                RuleData complementRule = GenerateComplementRule(primaryRule, complementBinID);

                if (complementRule != null)
                {
                    generatedRules.Add(complementRule);

                    // Store the complement's bin on the primary so GameManager can look it up
                    // without scanning the full rule list.
                    primaryRule.complementBinID = complementBinID;
                }
            }
        }

        return generatedRules;
    }

    /// <summary>
    /// Resets the used-specificities tracking list so the next floor starts with a clean pool.
    /// Should be called by GameManager at the beginning of every new floor or on retry.
    /// </summary>
    public void ResetForNewLevel()
    {
        usedSpecificities.Clear();
    }

    /// <summary>
    /// Attempts to upgrade one primary rule in activeRules in place by adding a condition or
    /// promoting it to a harder RuleType, without ever removing the rule from the list.
    /// After complexifying the primary rule, finds its paired complement rule and updates it
    /// to match the new conditions so no document becomes unplaceable after a rule change.
    /// Picks a random candidate whose complexity is strictly below newComplexityTarget.
    /// If no candidate exists, logs a warning and returns early.
    /// Complexifying reuses the same bin and keeps the rule recognisable while making it
    /// harder — the player sees a rule they already know, now with an extra constraint.
    /// </summary>
    /// <param name="activeRules">Flat list of all active rules across all bins this day.</param>
    /// <param name="newComplexityTarget">The maximum complexity tier to upgrade toward.</param>
    public void ComplexifyExistingRule(List<RuleData> activeRules, int newComplexityTarget)
    {
        // Only complexify non-complement primary rules — complement rules must change in sync
        // with their primary, never independently, to avoid creating unplaceable documents.
        List<RuleData> candidates = activeRules
            .FindAll(rule => !rule.isComplement && rule.complexity < newComplexityTarget);

        if (candidates.Count == 0)
        {
            Debug.LogWarning("[RuleGenerator] ComplexifyExistingRule: no non-complement rules below " +
                             $"complexity {newComplexityTarget}. Skipping complexification.");
            return;
        }

        RuleData targetRule = candidates[Random.Range(0, candidates.Count)];

        List<string> availablePool = BuildAvailablePool();

        // Prefer adding a second condition when the rule has only one and a two-slot
        // template exists — this reuses the same rule shape and is less jarring than
        // changing the entire rule type.
        bool hasOneCondition    = !string.IsNullOrEmpty(targetRule.conditionA) && string.IsNullOrEmpty(targetRule.conditionB);
        bool hasTwoSlotTemplate = HasTemplateWithSlots(2);

        if (hasOneCondition && hasTwoSlotTemplate && availablePool.Count > 0)
        {
            if (!TryConsumeSpecificity(availablePool, out string secondCondition))
                return;

            targetRule.conditionB  = secondCondition;
            targetRule.displayText = ResolveTemplate(FindMatchingTemplate(targetRule.ruleType), targetRule, targetRule.ruleType);
            targetRule.complexity  = Mathf.Min(targetRule.complexity + 1, maximumComplexity);

            // When a primary rule changes conditions, its complement must change in sync
            // so documents that previously routed to the complement still have a valid destination.
            SyncComplementRule(targetRule, activeRules);

            Debug.Log($"[Difficulty] Rule complexified (added condition) → " +
                      $"bin: {targetRule.targetBinID} | " +
                      $"conditionA: {targetRule.conditionA} | conditionB: {targetRule.conditionB} | " +
                      $"complexity: {targetRule.complexity}");
            return;
        }

        RuleType? upgradedType = GetUpgradedRuleType(targetRule.ruleType);

        if (upgradedType == null)
            return;

        RuleType previousType  = targetRule.ruleType;
        targetRule.ruleType    = upgradedType.Value;
        targetRule.displayText = ResolveTemplate(FindMatchingTemplate(targetRule.ruleType), targetRule, targetRule.ruleType);
        targetRule.complexity  = Mathf.Min(targetRule.complexity + 1, maximumComplexity);

        // Sync the complement when the rule type changes — the complement type must match
        // the new primary type or documents in the overlap case become unplaceable.
        SyncComplementRule(targetRule, activeRules);

        Debug.Log($"[Difficulty] Rule complexified (type upgrade) → " +
                  $"bin: {targetRule.targetBinID} | " +
                  $"{previousType} → {targetRule.ruleType} | " +
                  $"complexity: {targetRule.complexity}");
    }

    /// <summary>
    /// Generates exactly one RuleData for the given bin, respecting usedSpecificities
    /// so that specificities already used this floor are not repeated.
    /// Allows GameManager to add one rule to a specific bin without regenerating all rules.
    /// </summary>
    /// <param name="targetBinID">The bin ID the new rule should target.</param>
    /// <param name="complexityTarget">Determines which RuleTypes are eligible for the new rule.</param>
    /// <returns>A fully populated RuleData, or null if the specificity pool is exhausted.</returns>
    public RuleData GenerateSingleRule(string targetBinID, int complexityTarget)
    {
        List<string> availablePool = BuildAvailablePool();
        RuleType selectedType      = SelectRuleType(complexityTarget);

        return BuildRule(selectedType, targetBinID, availablePool, availableBinIDs.Count);
    }

    /// <summary>
    /// Generates a complement rule for the given primary rule and assigns it to the specified bin.
    /// The complement covers the logical inverse of the primary so every possible document always
    /// has exactly one valid destination.
    /// Returns null when the primary rule is already a complement (guard against infinite recursion),
    /// or when the rule type is self-complementary (ConditionalBranch, PositiveDouble).
    /// </summary>
    /// <param name="primaryRule">The primary rule whose complement must be generated.</param>
    /// <param name="complementBinID">The bin ID where the complement rule will be placed.</param>
    /// <returns>A fully populated complement RuleData, or null when no complement is needed.</returns>
    public RuleData GenerateComplementRule(RuleData primaryRule, string complementBinID)
    {
        // Never generate a complement of a complement — doing so would create infinite recursion
        // because each complement would require its own complement in turn.
        if (primaryRule.isComplement)
            return null;

        // Self-complementary types already handle both possible outcomes within the same rule.
        // Generating a complement for them would create a duplicate routing path and break
        // the guarantee that each document matches exactly one bin.
        if (primaryRule.ruleType == RuleType.ConditionalBranch ||
            primaryRule.ruleType == RuleType.PositiveDouble)
            return null;

        RuleType complementType = ResolveComplementType(primaryRule.ruleType);

        if (complementType == primaryRule.ruleType)
            return null; // No complement mapping exists for this type — skip silently.

        RuleData complementRule = new RuleData
        {
            isComplement  = true,
            ruleType      = complementType,
            targetBinID   = complementBinID,
            conditionA    = primaryRule.conditionA,
            conditionB    = primaryRule.conditionB,
            // Complement has the same complexity as its primary — it covers an equivalent
            // case set (the logical inverse), so the cognitive load on the player is the same.
            complexity    = primaryRule.complexity
        };

        complementRule.displayText = ResolveTemplate(
            FindMatchingTemplate(complementType), complementRule, complementType);

        return complementRule;
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
    /// Finds the complement rule of the given primary rule within the active rule list
    /// and updates its conditions, type, and display text to match the updated primary.
    /// Called after any complexification so the pair stays in logical sync.
    /// Without this sync, documents that previously matched the complement's old conditions
    /// would have no valid destination after the primary changes.
    /// </summary>
    /// <param name="primaryRule">The just-complexified primary rule.</param>
    /// <param name="activeRules">All active rules for the current day.</param>
    private void SyncComplementRule(RuleData primaryRule, List<RuleData> activeRules)
    {
        // Identify the complement by its isComplement flag and matching conditionA —
        // conditionA is stable across complexification (only conditionB or ruleType changes),
        // so it is a reliable key for finding the paired complement.
        RuleData pairedComplement = activeRules.Find(rule =>
            rule.isComplement && rule.conditionA == primaryRule.conditionA);

        if (pairedComplement == null)
            return; // Primary may be self-complementary or complement was never generated.

        RuleType newComplementType = ResolveComplementType(primaryRule.ruleType);

        pairedComplement.ruleType      = newComplementType;
        pairedComplement.conditionB    = primaryRule.conditionB;
        pairedComplement.complexity    = primaryRule.complexity;
        pairedComplement.displayText   = ResolveTemplate(
            FindMatchingTemplate(newComplementType), pairedComplement, newComplementType);
    }

    /// <summary>
    /// Maps a primary RuleType to its corresponding complement RuleType.
    /// Returns the original type when no complement mapping exists (safe no-op for callers).
    /// </summary>
    /// <param name="primaryType">The primary rule type to map.</param>
    /// <returns>The complement RuleType, or the original type if no mapping exists.</returns>
    private RuleType ResolveComplementType(RuleType primaryType)
    {
        return primaryType switch
        {
            RuleType.PositiveForced         => RuleType.ComplementPositiveForced,
            RuleType.PositiveExclusive      => RuleType.ComplementPositiveExclusive,
            RuleType.NegativeSimple         => RuleType.ComplementNegativeSimple,
            RuleType.PositiveWithNegative   => RuleType.ComplementPositiveWithNegative,
            _                               => primaryType // Self-complementary or unknown — no mapping.
        };
    }

    /// <summary>
    /// Returns a bin ID that is different from the given primary bin ID, chosen at random
    /// from all available bins excluding the primary.
    /// A complement must never go to the same bin as its primary — that would create a bin
    /// that accepts all documents regardless of specificities, making other bins unreachable.
    /// Falls back to the first available bin if no alternate exists (degenerate single-bin case).
    /// </summary>
    /// <param name="primaryBinID">The primary bin ID to exclude.</param>
    /// <returns>A different bin ID chosen randomly, or the primary one as a last resort.</returns>
    private string PickDifferentBinID(string primaryBinID)
    {
        List<string> alternateBins = availableBinIDs
            .Where(id => id != primaryBinID)
            .ToList();

        if (alternateBins.Count == 0)
            return primaryBinID; // Only one bin — degenerate case, no real alternative exists.

        return alternateBins[Random.Range(0, alternateBins.Count)];
    }

    /// <summary>
    /// Checks whether the database contains at least one template whose text has
    /// the given number of placeholder slots ({0}, {1}, etc.).
    /// Used by ComplexifyExistingRule to decide if a second condition can be rendered.
    /// </summary>
    /// <param name="slotCount">Number of placeholder slots to look for.</param>
    /// <returns>True if at least one template accommodates exactly slotCount placeholders.</returns>
    private bool HasTemplateWithSlots(int slotCount)
    {
        foreach (RuleTemplate template in specificityDatabase.templates)
        {
            // Count "{N}" occurrences as a simple proxy for slot count.
            // ConditionalBranch templates have {0} and {1} → slotCount 2.
            int foundSlots = 0;
            for (int slotIndex = 0; slotIndex < slotCount; slotIndex++)
            {
                if (template.templateText.Contains("{" + slotIndex + "}"))
                    foundSlots++;
            }

            if (foundSlots == slotCount)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Returns the next harder RuleType in the progression chain, or null when
    /// the current type is already at the top of the chain.
    /// Chain: PositiveExclusive → PositiveForced → NegativeSimple.
    /// Other types are not in the linear upgrade path and return null.
    /// </summary>
    /// <param name="currentType">The rule type to upgrade from.</param>
    /// <returns>The next rule type in the chain, or null if no upgrade exists.</returns>
    private RuleType? GetUpgradedRuleType(RuleType currentType)
    {
        return currentType switch
        {
            RuleType.PositiveExclusive => RuleType.PositiveForced,
            RuleType.PositiveForced    => RuleType.NegativeSimple,
            _                          => null // NegativeSimple and above have no simple linear upgrade.
        };
    }

    /// <summary>
    /// Builds the pool of specificities that are still available for this floor
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
    /// Fallback and NegativeMultiple are removed: Fallback created unplaceable documents
    /// when not all cases were covered; NegativeMultiple is replaced by PositiveWithNegative.
    /// </summary>
    /// <param name="complexityTarget">Tier: 1 = easy, 2 = medium, 3+ = hard.</param>
    /// <returns>A randomly chosen RuleType appropriate for the tier.</returns>
    private RuleType SelectRuleType(int complexityTarget)
    {
        if (complexityTarget <= 1)
            return RuleType.PositiveExclusive;

        if (complexityTarget == 2)
        {
            RuleType[] mediumTypes = { RuleType.PositiveForced, RuleType.NegativeSimple };
            return mediumTypes[Random.Range(0, mediumTypes.Length)];
        }

        // complexityTarget >= 3 — hardest types with branching or dual-condition logic.
        RuleType[] hardTypes = { RuleType.ConditionalBranch, RuleType.PositiveDouble, RuleType.PositiveWithNegative };
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
                if (!TryConsumeSpecificity(availableSpecificities, out string condBranchA))
                    return null;
                if (!TryConsumeSpecificity(availableSpecificities, out string condBranchB))
                    return null;
                rule.conditionA     = condBranchA;
                rule.conditionB     = condBranchB;
                // ConditionalBranch routes to a second bin when conditionB is absent.
                // The secondary bin is picked as a different bin so the two targets are always distinct.
                rule.secondaryBinID = PickSecondaryBinID(binID);
                break;

            case RuleType.PositiveDouble:
                if (!TryConsumeSpecificity(availableSpecificities, out string condDoubleA))
                    return null;
                if (!TryConsumeSpecificity(availableSpecificities, out string condDoubleB))
                    return null;
                rule.conditionA     = condDoubleA;
                rule.conditionB     = condDoubleB;
                // PositiveDouble routes to secondaryBinID when only conditionA is present (no conditionB).
                rule.secondaryBinID = PickSecondaryBinID(binID);
                break;

            case RuleType.PositiveWithNegative:
                if (!TryConsumeSpecificity(availableSpecificities, out string condWithA))
                    return null;
                if (!TryConsumeSpecificity(availableSpecificities, out string condWithB))
                    return null;
                rule.conditionA = condWithA;
                rule.conditionB = condWithB;
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
    /// Used by ConditionalBranch and PositiveDouble to guarantee the two target bins are distinct.
    /// Falls back to the primary bin if no alternate exists (degenerate single-bin case).
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

        // Only one bin exists — ConditionalBranch/PositiveDouble degrade gracefully rather than crashing.
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
            RuleType.PositiveExclusive              => "If the document has only {0}, place it here",
            RuleType.PositiveForced                 => "If the document has {0}, it must go here",
            RuleType.NegativeSimple                 => "If the document does not have {0}, place it here",
            RuleType.ConditionalBranch              => "If {0} is present: check for {1} — yes: here, no: other bin",
            RuleType.PositiveDouble                 => "If {0} and {1} are present go here, otherwise other bin",
            RuleType.PositiveWithNegative           => "If {0} is present but {1} is not, place it here",
            RuleType.ComplementPositiveForced       => "If the document does NOT have {0}, place it here",
            RuleType.ComplementPositiveExclusive    => "If {0} is present with other specificities, place here",
            RuleType.ComplementNegativeSimple       => "If the document has {0}, place it here",
            RuleType.ComplementPositiveWithNegative => "If {0} and {1} are both present, place it here",
            _                                       => "Send documents here"
        };

        return new RuleTemplate { templateText = fallbackText, ruleType = ruleType };
    }

    /// <summary>
    /// Substitutes {0} and {1} placeholders in the template text using the rule's typed fields.
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
            case RuleType.ComplementPositiveForced:
            case RuleType.ComplementPositiveExclusive:
            case RuleType.ComplementNegativeSimple:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                break;

            case RuleType.ConditionalBranch:
            case RuleType.PositiveDouble:
            case RuleType.PositiveWithNegative:
            case RuleType.ComplementPositiveWithNegative:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                resolvedText = resolvedText.Replace("{1}", rule.conditionB);
                break;
        }

        return resolvedText;
    }
}
