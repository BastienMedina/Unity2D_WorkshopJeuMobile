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
    // Editor-only static preview helper
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a human-readable rule preview for each specificity in the given list,
    /// using the supplied SpecificityDatabase templates. Intended for the Floor Designer
    /// tool only — never called at runtime. Returns one string per specificity in the form
    /// the simplest template produces, so the designer can verify which rules a night will
    /// generate without entering Play mode.
    /// </summary>
    /// <param name="specificities">Specificities to preview rules for.</param>
    /// <param name="database">Database containing templates.</param>
    /// <param name="binLabels">Ordered bin labels to use instead of raw bin IDs (e.g. "Corbeille A").</param>
    /// <returns>List of preview strings, one per specificity.</returns>
    public static List<string> GenerateRulesPreview(
        List<string> specificities,
        SpecificityDatabase database,
        List<string> binLabels)
    {
        List<string> previews = new List<string>();

        if (specificities == null || database == null || database.templates == null)
            return previews;

        // Find the simplest single-condition template (one {0} placeholder, no {1}).
        RuleTemplate singleTemplate = null;
        foreach (RuleTemplate t in database.templates)
        {
            if (t.templateText.Contains("{0}") && !t.templateText.Contains("{1}"))
            {
                singleTemplate = t;
                break;
            }
        }

        string binA = binLabels != null && binLabels.Count > 0 ? binLabels[0] : "Corbeille A";
        string binB = binLabels != null && binLabels.Count > 1 ? binLabels[1] : "Corbeille B";

        for (int i = 0; i < specificities.Count; i++)
        {
            string spec = specificities[i];

            if (string.IsNullOrEmpty(spec))
                continue;

            string primaryBin = (i % 2 == 0) ? binA : binB;

            string displayText = singleTemplate != null
                ? singleTemplate.templateText.Replace("{0}", spec)
                : $"Si [{spec}] → {primaryBin}";

            previews.Add(displayText);
        }

        return previews;
    }

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
    /// <param name="pinnedSpecificities">
    /// Optional list of specificities pinned by the designer in the Floor Designer tool.
    /// When non-empty, conditionA values are drawn exclusively from this list instead of
    /// the full SpecificityDatabase pool. Null or empty means normal random selection.
    /// </param>
    /// <returns>
    /// A list of fully populated RuleData objects (primary + complements), ready for the caller.
    /// Returns an empty list if the database has no valid bin IDs or insufficient specificities.
    /// </returns>
    public List<RuleData> GenerateRulesForDay(int rulesPerBin, int complexityTarget, List<string> pinnedSpecificities = null)
    {
        // rulesPerBin primary rules go into each bin, plus one auto-generated complement per primary.
        // We collect all rules (primary + complement) in a flat list for the caller.
        List<RuleData> generatedRules = new List<RuleData>();

        if (availableBinIDs.Count == 0)
            return generatedRules; // No valid bin IDs — generating any rule would produce an unresolvable target.

        // When pinnedSpecificities is provided and non-empty, it becomes the exclusive conditionA pool.
        // This lets the Floor Designer guarantee that specific concepts always appear on a given night
        // without manually authoring individual rules. The pinned pool is a copy so the generator
        // can freely remove consumed entries without mutating the caller's list.
        bool hasPinnedPool = pinnedSpecificities != null && pinnedSpecificities.Count > 0;

        List<string> availableSpecificities = hasPinnedPool
            ? new List<string>(pinnedSpecificities)
            : BuildAvailablePool();

        if (hasPinnedPool)
            Debug.Log($"[RuleGenerator] Using {availableSpecificities.Count} pinned specificities for this day.");

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
    /// Generates exactly one RuleData for the given bin, then checks the new rule against all
    /// existing rules for conflicts. If a conflict is detected, generates a PositiveForced
    /// resolution rule for the conflicting bin so no document is ever valid in two bins at once.
    /// Returns both the primary rule and the resolution rule as a tuple — the caller must assign
    /// each to the correct bin. resolutionRule is null when no conflict was found.
    ///
    /// Broad-acceptance fallback: NegativeSimple and ComplementPositiveForced accept virtually
    /// every document that lacks their negated condition, making them structurally irresolvable
    /// via a single PositiveForced patch when the active specificity pool is small. When such a
    /// rule is generated and any conflict is detected, the method discards it and rebuilds with
    /// SelectNarrowRuleType so a targeted rule replaces the broad one. This prevents a situation
    /// where the spawner's RemoveUnplaceableCombinations would discard nearly all valid combinations,
    /// leaving only a single document type in the pool.
    /// </summary>
    /// <param name="targetBinID">The bin ID the new rule should target.</param>
    /// <param name="complexityTarget">Determines which RuleTypes are eligible for the new rule.</param>
    /// <param name="existingRules">All rules currently active, used for conflict detection.</param>
    /// <returns>
    /// A tuple of (primaryRule, resolutionRule).
    /// primaryRule is null when the specificity pool is exhausted.
    /// resolutionRule is null when no conflict was detected or when there is only one active bin.
    /// </returns>
    public (RuleData primaryRule, RuleData resolutionRule) GenerateSingleRule(
        string targetBinID,
        int complexityTarget,
        List<RuleData> existingRules = null)
    {
        // Guard: an empty targetBinID would produce a rule that no bin can ever claim,
        // silently breaking document routing without a clear error at the call site.
        if (string.IsNullOrEmpty(targetBinID))
        {
            Debug.LogError("[RuleGenerator] targetBinID is null or empty in GenerateSingleRule");
            return (null, null);
        }

        Debug.Log("[RuleGenerator] Generating rule for bin: " + targetBinID +
                  " complexity: " + complexityTarget);

        List<string> availablePool = BuildAvailablePool();
        RuleType selectedType      = SelectRuleType(complexityTarget);

        // targetBinID is passed directly to BuildRule so the generated rule always targets
        // the caller's intended bin and is never overridden internally.
        RuleData newRule = BuildRule(selectedType, targetBinID, availablePool, availableBinIDs.Count);

        if (newRule == null)
            return (null, null);

        // Conflict detection requires at least two active bins — with only one bin there is
        // nowhere else a document could route to, so resolution is impossible and unnecessary.
        bool canDetectConflict = existingRules != null &&
                                 existingRules.Count > 0 &&
                                 availableBinIDs.Count > 1;

        if (!canDetectConflict)
            return (newRule, null);

        // STEP 1 — Build test combinations from the existing rules before the new rule is added.
        // Passing newRule activates the broad-acceptance injection path: if newRule is a
        // NegativeSimple or ComplementPositiveForced, every single-specificity combination from
        // the existing pool is added to the test set so the conflict check can detect that the
        // new rule would claim documents already owned by another bin via simple positive rules.
        List<List<string>> testCombinations = BuildTestCombinations(existingRules, newRule);

        // STEP 2 — Check each combination for a conflict with the new rule.
        foreach (List<string> combination in testCombinations)
        {
            if (!DoesNewRuleConflictWithExisting(newRule, existingRules, combination))
                continue;

            // Found a combination that is valid in both the new rule's bin AND an existing bin.

            // Broad-acceptance fallback: NegativeSimple and ComplementPositiveForced conflict with
            // every document that lacks their negated condition. A single PositiveForced patch on
            // the conflicting bin would still leave the new rule claiming that document — so the
            // patch creates a different ambiguity rather than resolving the original one.
            // The correct fix is to not use the broad type at all: discard it and rebuild
            // with a narrow rule type that targets exactly one specificity.
            if (IsBroadAcceptanceType(newRule.ruleType))
            {
                Debug.LogWarning("[RuleGenerator] Broad-acceptance rule type " + newRule.ruleType +
                                 " conflicts on [" + string.Join(", ", combination) + "] — " +
                                 "discarding and retrying with a narrow rule type.");

                // Rebuild the pool because TryConsumeSpecificity already marked the
                // broad rule's specificity as used — we need to undo that side-effect.
                UndoLastConsumedSpecificity(newRule.conditionA);

                List<string> retryPool   = BuildAvailablePool();
                RuleType     narrowType  = SelectNarrowRuleType(complexityTarget);
                RuleData     narrowRule  = BuildRule(narrowType, targetBinID, retryPool, availableBinIDs.Count);

                if (narrowRule == null)
                {
                    Debug.LogWarning("[RuleGenerator] Narrow rule fallback also failed — pool exhausted.");
                    return (null, null);
                }

                // Re-run conflict detection on the narrow rule before returning it.
                // Narrow rules can still conflict via cross-rule pairings, so the resolution
                // path below must still execute for the narrow replacement.
                newRule          = narrowRule;
                testCombinations = BuildTestCombinations(existingRules, newRule);

                // Restart the conflict scan on the new rule — break out of the current loop.
                break;
            }

            // Non-broad conflict: resolve by adding a PositiveForced rule on the conflicting bin.
            // PositiveForced takes unconditional priority so the ambiguous document always routes
            // to the bin that already owned that specificity, without touching the new rule.
            RuleData conflictingRule = existingRules.FirstOrDefault(existingRule =>
                existingRule.targetBinID != newRule.targetBinID &&
                ValidateRuleAgainstCombination(existingRule, combination));

            if (conflictingRule == null)
                continue;

            RuleData resolutionRule = GenerateConflictResolutionRule(conflictingRule, newRule);

            Debug.Log("[RuleGenerator] Conflict detected between new rule on " + newRule.targetBinID +
                      " and existing rule on " + conflictingRule.targetBinID +
                      " — resolution rule added for: " + conflictingRule.conditionA);

            return (newRule, resolutionRule);
        }

        // STEP 3 — Run conflict detection on the (possibly replaced) narrow rule.
        // This second pass only executes when the broad-acceptance fallback triggered above
        // and broke out of the first loop with a new testCombinations list.
        foreach (List<string> combination in testCombinations)
        {
            if (!DoesNewRuleConflictWithExisting(newRule, existingRules, combination))
                continue;

            RuleData conflictingRule = existingRules.FirstOrDefault(existingRule =>
                existingRule.targetBinID != newRule.targetBinID &&
                ValidateRuleAgainstCombination(existingRule, combination));

            if (conflictingRule == null)
                continue;

            RuleData resolutionRule = GenerateConflictResolutionRule(conflictingRule, newRule);

            Debug.Log("[RuleGenerator] Conflict resolved for narrow fallback rule on " +
                      newRule.targetBinID + " — resolution rule for: " + conflictingRule.conditionA);

            return (newRule, resolutionRule);
        }

        return (newRule, null);
    }

    // -------------------------------------------------------------------------
    // Conflict detection helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Evaluates a single specificity combination against the new rule and every existing rule
    /// to detect whether the same document would be valid in two different bins simultaneously.
    /// A conflict exists when the new rule accepts the combination AND at least one existing rule
    /// on a different bin also accepts it — this would make the document ambiguous for the player.
    /// </summary>
    /// <param name="newRule">The rule just generated, not yet added to the active list.</param>
    /// <param name="existingRules">All rules already active before this generation call.</param>
    /// <param name="testSpecificities">The specificity combination to test.</param>
    /// <returns>True if a conflict exists for this combination; false otherwise.</returns>
    public bool DoesNewRuleConflictWithExisting(
        RuleData newRule,
        List<RuleData> existingRules,
        List<string> testSpecificities)
    {
        // The new rule must first accept this combination — if it doesn't, there is nothing to conflict.
        if (!ValidateRuleAgainstCombination(newRule, testSpecificities))
            return false;

        // Check whether any existing rule on a DIFFERENT bin also accepts the same combination.
        // A conflict exists only when two different bins claim the same document — same-bin overlap is harmless.
        foreach (RuleData existingRule in existingRules)
        {
            bool isDifferentBin     = existingRule.targetBinID != newRule.targetBinID;
            bool isExistingAccepted = ValidateRuleAgainstCombination(existingRule, testSpecificities);

            if (isDifferentBin && isExistingAccepted)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Generates all specificity combinations that are currently handled by the existing rule set.
    /// Tests only combinations relevant to the active rules — exhaustively testing all possible
    /// specificity combinations would be exponentially expensive and mostly irrelevant.
    /// Includes single-condition, dual-condition, and cross-rule pairings so every realistic
    /// document type that could be spawned is covered by the conflict check.
    ///
    /// The optional <paramref name="newRule"/> parameter enables broad-acceptance detection:
    /// rule types whose accept condition is NOT conditionA-positive (NegativeSimple,
    /// ComplementPositiveForced) accept virtually every combination that doesn't contain
    /// their negated condition. When such a rule is provided, all single-specificity combinations
    /// from the existing rule set are injected as additional test cases so the conflict check
    /// can detect that the new rule would claim documents already owned by another bin.
    /// Without this, a NegativeSimple rule on [torn] passes all existing cross-rule tests
    /// because those tests always include [torn] as part of the pairing, making the conflict invisible.
    /// </summary>
    /// <param name="existingRules">The rules already active before the new rule is generated.</param>
    /// <param name="newRule">
    /// Optional. The rule just generated, not yet added to the active list.
    /// Used to activate the broad-acceptance injection path for wide-accept rule types.
    /// Pass null to use the standard combination-only path.
    /// </param>
    /// <returns>A list of specificity combinations to test the new rule against.</returns>
    public List<List<string>> BuildTestCombinations(List<RuleData> existingRules, RuleData newRule = null)
    {
        List<List<string>> combinations = new List<List<string>>();

        foreach (RuleData rule in existingRules)
        {
            // Every rule uses conditionA — add it alone as the minimal test case.
            if (!string.IsNullOrEmpty(rule.conditionA))
                combinations.Add(new List<string> { rule.conditionA });

            // Dual-condition rules define their own pairings — include them so ConditionalBranch,
            // PositiveDouble, and PositiveWithNegative are also tested as they would be for real documents.
            bool hasBothConditions = !string.IsNullOrEmpty(rule.conditionA) &&
                                     !string.IsNullOrEmpty(rule.conditionB);

            if (hasBothConditions)
                combinations.Add(new List<string> { rule.conditionA, rule.conditionB });
        }

        // Cross-rule pairings: combine conditionA from one rule with conditionA from another.
        // This is how a NegativeSimple rule can conflict — the document has some OTHER specificity
        // that satisfies a positive rule elsewhere while not having the negated condition.
        for (int outerIndex = 0; outerIndex < existingRules.Count; outerIndex++)
        {
            for (int innerIndex = outerIndex + 1; innerIndex < existingRules.Count; innerIndex++)
            {
                string conditionFromOuter = existingRules[outerIndex].conditionA;
                string conditionFromInner = existingRules[innerIndex].conditionA;

                bool areBothValid = !string.IsNullOrEmpty(conditionFromOuter) &&
                                    !string.IsNullOrEmpty(conditionFromInner);
                bool areDistinct  = conditionFromOuter != conditionFromInner;

                if (areBothValid && areDistinct)
                    combinations.Add(new List<string> { conditionFromOuter, conditionFromInner });
            }
        }

        // Broad-acceptance injection: NegativeSimple and ComplementPositiveForced accept any document
        // that does NOT contain their negated condition — their accept surface covers almost every
        // combination reachable by the other rules. The cross-rule pairings above always include the
        // negated condition as part of the pair (e.g. [signed, torn]), so NegativeSimple(torn) would
        // never trigger a conflict against [signed, torn]. We must therefore also test every
        // single-specificity combination from the pool independently, without pairing it with
        // the negated condition. This guarantees that NegativeSimple(torn) is tested against
        // [signed] alone, [urgent] alone, etc. — the exact shapes that will collide with positive
        // rules on the other bin and that RemoveUnplaceableCombinations would otherwise discard at runtime.
        if (newRule != null && IsBroadAcceptanceType(newRule.ruleType))
        {
            HashSet<string> knownSpecificities = CollectKnownSpecificities(existingRules);

            foreach (string specificity in knownSpecificities)
            {
                // Skip the negated condition itself — NegativeSimple(torn) rejects [torn],
                // so injecting it here would produce a false positive in conflict detection.
                if (specificity == newRule.conditionA)
                    continue;

                List<string> singleSpecCombo = new List<string> { specificity };

                // Avoid duplicates that were already added by the primary per-rule pass above.
                bool alreadyPresent = combinations.Exists(existing =>
                    existing.Count == 1 && existing[0] == specificity);

                if (!alreadyPresent)
                    combinations.Add(singleSpecCombo);
            }
        }

        return combinations;
    }

    /// <summary>
    /// Returns true for rule types whose accept condition spans a broad set of documents —
    /// specifically those that accept based on the ABSENCE of a condition rather than its presence.
    /// These types require extended conflict testing because their accept surface cannot be
    /// derived from their own conditionA alone.
    /// </summary>
    /// <param name="ruleType">The rule type to check.</param>
    /// <returns>True if the rule type has broad-acceptance semantics.</returns>
    private static bool IsBroadAcceptanceType(RuleType ruleType)
    {
        return false; // No broad-acceptance types remain in the simplified rule set.
    }

    /// <summary>
    /// Collects every distinct conditionA (and conditionB where populated) from the existing
    /// rule list into a flat set of strings. Used by the broad-acceptance injection path to
    /// build single-specificity test combinations covering all known game values.
    /// </summary>
    /// <param name="existingRules">The rules already active before the new rule is generated.</param>
    /// <returns>A HashSet of all distinct specificity strings referenced by the existing rules.</returns>
    private static HashSet<string> CollectKnownSpecificities(List<RuleData> existingRules)
    {
        HashSet<string> specificities = new HashSet<string>();

        foreach (RuleData rule in existingRules)
        {
            if (!string.IsNullOrEmpty(rule.conditionA))
                specificities.Add(rule.conditionA);

            if (!string.IsNullOrEmpty(rule.conditionB))
                specificities.Add(rule.conditionB);
        }

        return specificities;
    }

    /// <summary>
    /// Creates a PositiveForced rule for the bin that owned the conflicting existing rule.
    /// PositiveForced takes priority over NegativeSimple and other lower-priority types —
    /// any document with conditionA is forced into this bin regardless of other conditions,
    /// which eliminates the ambiguity by giving that bin unconditional ownership of conditionA.
    /// </summary>
    /// <param name="conflictingExistingRule">The existing rule whose bin needs disambiguation.</param>
    /// <param name="newRule">The newly generated rule that caused the conflict (unused in logic, kept for logging).</param>
    /// <returns>A fully populated PositiveForced RuleData targeting the conflicting rule's bin.</returns>
    public RuleData GenerateConflictResolutionRule(RuleData conflictingExistingRule, RuleData newRule)
    {
        RuleData resolutionRule = new RuleData
        {
            ruleType     = RuleType.Simple,
            targetBinID  = conflictingExistingRule.targetBinID,
            conditionA   = conflictingExistingRule.conditionA,
            isComplement = false
        };

        resolutionRule.displayText = ResolveTemplate(
            FindMatchingTemplate(RuleType.Simple), resolutionRule, RuleType.Simple);

        resolutionRule.complexity = ComputeComplexity(RuleType.Simple, availableBinIDs.Count);

        return resolutionRule;
    }

    /// <summary>
    /// Evaluates whether the given specificity combination satisfies the given rule's accept condition.
    /// Mirrors the per-type validation helpers in SortingBin exactly — any divergence would produce
    /// false negatives in conflict detection and allow ambiguous documents to slip through.
    /// Does not check routing (targetBinID vs binID) because here we only care whether a rule
    /// *accepts* a document, not which specific bin it routes to.
    /// </summary>
    /// <param name="rule">The rule to evaluate.</param>
    /// <param name="specificities">The specificity combination of the candidate document.</param>
    /// <returns>True if the rule accepts this combination; false otherwise.</returns>
    public bool ValidateRuleAgainstCombination(RuleData rule, List<string> specificities)
    {
        switch (rule.ruleType)
        {
            case RuleType.Simple:
                return specificities.Contains(rule.conditionA);

            case RuleType.Multiple:
                return specificities.Contains(rule.conditionA) &&
                       specificities.Contains(rule.conditionB);

            case RuleType.Branch:
                return specificities.Contains(rule.conditionA) &&
                       !specificities.Contains(rule.conditionB);

            default:
                return false;
        }
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

        // Self-complementary types — no longer exist in the simplified set.
        if (false) return null;

        RuleType complementType = ResolveComplementType(primaryRule.ruleType);

        if (complementType == primaryRule.ruleType)
            return null;

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
    /// Returns true when the RuleGenerator has a valid SpecificityDatabase with at least
    /// one specificity and one template. Used by GameManager to guard against silent
    /// empty-list generation that produces "No rules assigned." in all bins.
    /// </summary>
    public bool CanGenerateRules()
    {
        if (specificityDatabase == null)
        {
            Debug.LogError("[RuleGenerator] CanGenerateRules: specificityDatabase is null. " +
                           "Assign a SpecificityDatabase ScriptableObject in the Inspector.");
            return false;
        }

        if (specificityDatabase.allSpecificities == null || specificityDatabase.allSpecificities.Count == 0)
        {
            Debug.LogError("[RuleGenerator] CanGenerateRules: specificityDatabase has no specificities. " +
                           "Add at least one entry to SpecificityDatabase.allSpecificities.");
            return false;
        }

        if (specificityDatabase.templates == null || specificityDatabase.templates.Count == 0)
        {
            Debug.LogError("[RuleGenerator] CanGenerateRules: specificityDatabase has no templates. " +
                           "Add at least one RuleTemplate to SpecificityDatabase.templates.");
            return false;
        }

        return true;
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

    /// <summary>
    /// Selects a rule type that targets exactly one specificity via a positive condition —
    /// never a broad-acceptance type. Used as fallback when a generated broad-acceptance rule
    /// (NegativeSimple, ComplementPositiveForced) would conflict irresolvably with existing rules.
    /// At complexity 1 this mirrors SelectRuleType exactly (PositiveExclusive only).
    /// At complexity 2+ it allows PositiveForced, which still targets one specificity but
    /// without the exclusivity constraint — safe alongside complement rules on the other bin.
    /// At complexity 3+ it also allows PositiveWithNegative, which uses two specificities
    /// but still has a well-defined, narrow accept surface.
    /// </summary>
    /// <param name="complexityTarget">The current complexity tier.</param>
    /// <returns>A narrow rule type appropriate for the tier.</returns>
    private RuleType SelectNarrowRuleType(int complexityTarget)
    {
        if (complexityTarget <= 1)
            return RuleType.Simple;

        if (complexityTarget == 2)
            return RuleType.Multiple;

        return RuleType.Branch;
    }

    /// <summary>
    /// Removes the given specificity from usedSpecificities so it can be re-consumed
    /// when a rule is discarded and rebuilt with a different type.
    /// Only call this immediately after discarding a rule whose conditionA was consumed
    /// via TryConsumeSpecificity — calling it in any other context will corrupt the pool.
    /// </summary>
    /// <param name="specificity">The specificity to release back into the available pool.</param>
    private void UndoLastConsumedSpecificity(string specificity)
    {
        if (!string.IsNullOrEmpty(specificity))
            usedSpecificities.Remove(specificity);
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
        return primaryType; // No complement types — kept for API compatibility.
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
            RuleType.Simple   => RuleType.Multiple,
            RuleType.Multiple => RuleType.Branch,
            _                 => null
        };
    }

    /// <summary>
    /// Builds the pool of specificities that are still available for this floor
    /// by excluding everything already present in usedSpecificities.
    /// </summary>
    /// <returns>A mutable list of candidate specificities.</returns>
    private List<string> BuildAvailablePool()
    {
        // Guard: a null or unconfigured database produces an empty pool, which causes
        // GenerateRulesForDay to silently return zero rules and all bins to display
        // "No rules assigned." — fail loudly here so the misconfiguration is obvious.
        if (specificityDatabase == null || specificityDatabase.allSpecificities == null)
        {
            Debug.LogError("[RuleGenerator] BuildAvailablePool: specificityDatabase is null or " +
                           "has no allSpecificities list. Assign a SpecificityDatabase in the Inspector.");
            return new List<string>();
        }

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
            return RuleType.Simple;

        if (complexityTarget == 2)
            return RuleType.Multiple;

        // complexityTarget >= 3
        return RuleType.Branch;
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
            case RuleType.Simple:
                if (!TryConsumeSpecificity(availableSpecificities, out string singleCondition))
                    return null;
                rule.conditionA = singleCondition;
                break;

            case RuleType.Multiple:
            case RuleType.Branch:
                if (!TryConsumeSpecificity(availableSpecificities, out string condAB))
                    return null;
                if (!TryConsumeSpecificity(availableSpecificities, out string condBB))
                    return null;
                rule.conditionA = condAB;
                rule.conditionB = condBB;
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
            RuleType.Simple   => 1,
            RuleType.Multiple => 2,
            RuleType.Branch   => 3,
            _                 => 1
        };

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

        string fallbackText = ruleType switch
        {
            RuleType.Simple   => "Si le document a {0}, posez-le ici",
            RuleType.Multiple => "Si le document a {0} et {1}, posez-le ici",
            RuleType.Branch   => "Si le document a {0} mais pas {1}, posez-le ici",
            _                 => "Posez le document ici"
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
            case RuleType.Simple:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                break;

            case RuleType.Multiple:
            case RuleType.Branch:
                resolvedText = resolvedText.Replace("{0}", rule.conditionA);
                resolvedText = resolvedText.Replace("{1}", rule.conditionB);
                break;
        }

        return resolvedText;
    }
}
