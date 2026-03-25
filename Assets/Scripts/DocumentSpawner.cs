using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for generating DocumentData, instantiating document prefabs
/// at runtime, and refilling the stack on demand whenever DocumentStackManager signals a deficit.
/// Injects all scene dependencies (canvas, detector, stack manager) into each spawned document
/// immediately after instantiation, then hands the document off to DocumentStackManager.
/// Does NOT use a coroutine or timer — spawning is driven entirely by onStackBelowTarget events.
/// Does NOT validate documents, access SortingBin logic, store rules beyond the
/// time needed to rebuild the valid combination list, or manage document position.
/// </summary>
public class DocumentSpawner : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>Prefab instantiated for each spawned document. Must carry a DraggableDocument component.</summary>
    [SerializeField] private GameObject documentPrefab;

    /// <summary>
    /// Parent transform under which documents are instantiated.
    /// DocumentStackManager owns all positioning after spawn — this parent just scopes
    /// the document inside the correct Canvas hierarchy.
    /// </summary>
    [SerializeField] private RectTransform spawnAreaParent;

    /// <summary>
    /// The root scene Canvas injected into every spawned document after instantiation.
    /// DocumentSpawner is the only class that instantiates documents, so it is the correct
    /// and single owner of this reference — centralising the injection in one place
    /// ensures no spawned document is ever left without a canvas reference.
    /// </summary>
    [SerializeField] private Canvas mainCanvas;

    /// <summary>
    /// The single DropZoneDetector instance in the scene, injected into every spawned document
    /// after instantiation. Held here for the same reason as mainCanvas: the prefab asset
    /// cannot store scene object references, so DocumentSpawner is the correct injection point.
    /// </summary>
    [SerializeField] private DropZoneDetector dropZoneDetector;

    /// <summary>
    /// The DocumentStackManager that receives every newly spawned document and signals
    /// when the stack needs refilling via onStackBelowTarget.
    /// Injected into each DraggableDocument so the document can report drag events
    /// back to the manager, which owns all visual state and document lifetime.
    /// </summary>
    [SerializeField] private DocumentStackManager documentStackManager;

    /// <summary>
    /// Database of all available specificities and sentence templates.
    /// Used here to retrieve the full specificity pool when building neutral-element combinations
    /// for dual-condition rules — specificities that appear in no active rule condition.
    /// </summary>
    [SerializeField] private SpecificityDatabase specificityDatabase;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Rules active for the current day, used exclusively to build validCombinations.</summary>
    private List<RuleData> activeRules = new List<RuleData>();

    /// <summary>
    /// All valid specificity combinations derived from the active rules.
    /// Rebuilt once per day in RebuildValidCombinations and read-only during spawning.
    /// </summary>
    private List<List<string>> validCombinations = new List<List<string>>();

    /// <summary>Monotonically increasing counter used to produce unique document IDs.</summary>
    private int documentSpawnCounter;

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stores the day's active rules and rebuilds the valid combination list.
    /// Must be called by GameManager before StartDay() so the combination pool is ready.
    /// </summary>
    /// <param name="rules">The list of RuleData generated for the current day.</param>
    public void UpdateActiveRules(List<RuleData> rules)
    {
        activeRules = new List<RuleData>(rules);
        RebuildValidCombinations();
    }

    /// <summary>
    /// Subscribes SpawnDocument to onStackBelowTarget and performs the initial fill.
    /// Interval-based spawning is removed — all spawning is now demand-driven by the stack event.
    /// </summary>
    public void StartDay()
    {
        // Subscribe so every future deficit triggers an immediate spawn in the same frame.
        // Interval-based spawning is replaced entirely by demand-based spawning driven by stack size.
        documentStackManager.onStackBelowTarget += SpawnDocument;

        // On day start the stack is empty — fill it immediately to targetStackSize
        // without waiting for onStackBelowTarget, which only fires after EnqueueDocument
        // triggers CheckAndRequestRefill for the first time.
        FillStackToTarget();
    }

    /// <summary>
    /// Unsubscribes SpawnDocument from onStackBelowTarget to halt all future spawning.
    /// Unsubscribing prevents documents from spawning during transitions or after a day ends.
    /// Safe to call even if StartDay() was never called.
    /// </summary>
    public void StopDay()
    {
        documentStackManager.onStackBelowTarget -= SpawnDocument;
    }

    /// <summary>
    /// Destroys every document GameObject still alive in the scene and clears tracking state.
    /// Unsubscribes from onStackBelowTarget as a defensive backstop against ghost spawns
    /// that could occur if StopDay() was skipped in an exceptional code path.
    /// Called by GameManager at the end of a day before transitioning to the next one.
    /// </summary>
    public void ClearAllDocuments()
    {
        // Defensive unsubscribe: DocumentStackManager.ClearStack already nulls the event,
        // but unsubscribing here ensures no spawn fires if ClearAllDocuments is called
        // independently of the normal day-end flow.
        documentStackManager.onStackBelowTarget -= SpawnDocument;
    }

    // -------------------------------------------------------------------------
    // Data building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds validCombinations from the current activeRules.
    /// Each rule contributes exactly one combination — the minimal set of specificities a document
    /// must carry to satisfy that rule and ONLY that rule. Using exact combinations eliminates
    /// the cross-contamination that occurred when combinations from one rule's conditions were
    /// mixed with another rule's conditions, producing documents valid in multiple bins at once.
    ///
    /// Additionally, for every dual-condition rule (one that tests both conditionA and conditionB),
    /// this method also generates bonus combinations of the form [conditionA, neutralSpec] where
    /// neutralSpec is a specificity that appears in no active rule's conditionA or conditionB.
    /// These neutral-element documents test the player's understanding that a document with conditionA
    /// plus a random irrelevant element is treated differently from one with conditionA plus conditionB,
    /// without introducing any ambiguity — the neutral element has no rule of its own.
    ///
    /// After building, calls RemoveUnplaceableCombinations as a safety net to discard any
    /// combination that is either unplaceable (no bin accepts it) or ambiguous (multiple bins accept it).
    /// Called once each time rules change — never during an active spawn loop.
    /// </summary>
    private void RebuildValidCombinations()
    {
        validCombinations.Clear();

        foreach (RuleData rule in activeRules)
        {
            List<string> exactCombination = BuildExactComboForRule(rule);

            if (exactCombination == null || exactCombination.Count == 0)
                continue;

            // Duplicate combinations are skipped — two rules can produce the same document shape
            // (e.g. a ConditionalBranch and a PositiveForced both using conditionA alone),
            // and having two identical entries in the pool would over-represent that document type.
            bool isAlreadyPresent = validCombinations.Exists(existing =>
                existing.Count == exactCombination.Count &&
                existing.TrueForAll(exactCombination.Contains));

            if (!isAlreadyPresent)
                validCombinations.Add(exactCombination);
        }

        // Bonus pass — add neutral-element combinations for dual-condition rules.
        // For example, if a rule says "Si [X] avec [Y], mettre ici", a document with [X, neutral]
        // (where neutral is in no active rule) must also be spawnable so the player sees that
        // conditionA alone with an unrecognised element follows the single-condition rule path,
        // not the dual-condition one. The neutral element provides a realistic distractor.
        List<List<string>> neutralCombinations = BuildNeutralCombinationsForDualRules();
        foreach (List<string> neutralCombo in neutralCombinations)
        {
            bool isAlreadyPresent = validCombinations.Exists(existing =>
                existing.Count == neutralCombo.Count &&
                existing.TrueForAll(neutralCombo.Contains));

            if (!isAlreadyPresent)
                validCombinations.Add(neutralCombo);
        }

        // Safety net: discard combinations that are unplaceable or map to multiple bins.
        // BuildExactComboForRule produces correct combinations in theory — this pass catches
        // edge cases caused by unusual rule configurations or pool exhaustion fallbacks.
        RemoveUnplaceableCombinations(activeRules);

        Debug.Log("[Spawner] Valid combinations: " + validCombinations.Count);

        foreach (List<string> combo in validCombinations)
            Debug.Log("  → [" + string.Join(", ", combo) + "]");
    }

    /// <summary>
    /// Builds bonus specificity combinations for every active dual-condition rule — rules that test
    /// both conditionA and conditionB (PositiveDouble, ConditionalBranch, PositiveWithNegative,
    /// and their complements). For each such rule, produces one combination of the form
    /// [conditionA, neutralSpec] where neutralSpec is a specificity drawn from the database that
    /// does not appear as conditionA or conditionB in any active rule.
    ///
    /// Why this matters: a player who only sees [conditionA, conditionB] documents can learn
    /// the rule by memorising one shape. Adding [conditionA, neutralSpec] forces them to reason
    /// about whether the extra element satisfies the secondary condition or not — the core
    /// cognitive challenge of the dual-condition mechanic.
    ///
    /// Returns an empty list when specificityDatabase is null, when the database pool has no
    /// neutral specificities left, or when no dual-condition rules are active.
    /// The caller (RebuildValidCombinations) runs RemoveUnplaceableCombinations afterwards,
    /// which silently drops any neutral combo that fails the placeability check — no validation
    /// logic is needed here.
    /// </summary>
    /// <returns>A list of [conditionA, neutralSpec] combinations, one per dual-condition rule.</returns>
    private List<List<string>> BuildNeutralCombinationsForDualRules()
    {
        List<List<string>> results = new List<List<string>>();

        if (specificityDatabase == null || specificityDatabase.allSpecificities == null)
        {
            Debug.LogWarning("[Spawner] specificityDatabase not assigned — skipping neutral combinations.");
            return results;
        }

        // Collect every condition string currently used by any active rule.
        // A specificity is "neutral" only when it is absent from this set entirely.
        HashSet<string> ruleSpecificities = CollectAllActiveConditions();

        // Build the pool of neutral candidates — specificities in the database that no rule uses.
        List<string> neutralPool = new List<string>();
        foreach (string spec in specificityDatabase.allSpecificities)
        {
            if (!string.IsNullOrEmpty(spec) && !ruleSpecificities.Contains(spec))
                neutralPool.Add(spec);
        }

        if (neutralPool.Count == 0)
        {
            Debug.Log("[Spawner] No neutral specificities available — all database entries are used by active rules.");
            return results;
        }

        foreach (RuleData rule in activeRules)
        {
            // A dual-condition rule is any rule that has both conditionA and conditionB populated.
            // This covers PositiveDouble, ConditionalBranch, PositiveWithNegative and their complements.
            bool isDualCondition = !string.IsNullOrEmpty(rule.conditionA) &&
                                   !string.IsNullOrEmpty(rule.conditionB);

            if (!isDualCondition)
                continue;

            // Pick a random neutral specificity — using Random.Range here keeps the pool varied
            // across days. The same neutral could appear on multiple rules in the same day, which
            // is acceptable because RemoveUnplaceableCombinations deduplicates the final list.
            string neutralSpec = neutralPool[Random.Range(0, neutralPool.Count)];

            results.Add(new List<string> { rule.conditionA, neutralSpec });
        }

        return results;
    }

    /// <summary>
    /// Collects every distinct conditionA and conditionB string from all active rules into a HashSet.
    /// Used by BuildNeutralCombinationsForDualRules to determine which specificities are already
    /// "owned" by a rule and therefore cannot serve as neutral elements.
    /// </summary>
    /// <returns>A HashSet of all condition strings referenced by at least one active rule.</returns>
    private HashSet<string> CollectAllActiveConditions()
    {
        HashSet<string> conditions = new HashSet<string>();

        foreach (RuleData rule in activeRules)
        {
            if (!string.IsNullOrEmpty(rule.conditionA))
                conditions.Add(rule.conditionA);

            if (!string.IsNullOrEmpty(rule.conditionB))
                conditions.Add(rule.conditionB);
        }

        return conditions;
    }

    /// <summary>
    /// Returns the exact specificity combination a document must have to satisfy this rule —
    /// no more, no less. Keeping combinations exact prevents a spawned document from
    /// accidentally satisfying a rule in a different bin, which would create ambiguity.
    /// For rule types that route documents to a secondary bin (ConditionalBranch, PositiveDouble),
    /// the combination reflects which branch of the rule the spawned document targets.
    /// Returns null for unknown rule types — callers must null-check before using the result.
    /// </summary>
    /// <param name="rule">The rule to derive the exact combination from.</param>
    /// <returns>A List of specificity strings, or null if the rule type is unrecognised.</returns>
    private List<string> BuildExactComboForRule(RuleData rule)
    {
        switch (rule.ruleType)
        {
            case RuleType.PositiveForced:
                // Document needs exactly conditionA — PositiveForced ignores extra specificities,
                // but adding more would risk satisfying positive rules in other bins simultaneously.
                return new List<string> { rule.conditionA };

            case RuleType.PositiveExclusive:
                // PositiveExclusive requires ONLY conditionA — any extra specificity disqualifies
                // the document, so the combination must contain nothing else.
                return new List<string> { rule.conditionA };

            case RuleType.ConditionalBranch:
                // ConditionalBranch has two branches: both conditions → targetBinID,
                // conditionA alone (conditionB absent) → secondaryBinID.
                // Each rule entry represents one branch — check targetBinID to know which one.
                // A rule is the "yes" branch when its targetBinID is the primary destination.
                bool isConditionalYesBranch = rule.targetBinID != rule.secondaryBinID;
                if (isConditionalYesBranch)
                    return new List<string> { rule.conditionA, rule.conditionB };

                // No-branch: conditionA present, conditionB must be absent — exact combo is [conditionA].
                return new List<string> { rule.conditionA };

            case RuleType.PositiveDouble:
                // PositiveDouble mirrors ConditionalBranch: both conditions → targetBinID,
                // conditionA alone → secondaryBinID.
                bool isPositiveDoubleYesBranch = rule.targetBinID != rule.secondaryBinID;
                if (isPositiveDoubleYesBranch)
                    return new List<string> { rule.conditionA, rule.conditionB };

                return new List<string> { rule.conditionA };

            case RuleType.NegativeSimple:
                // NegativeSimple accepts documents WITHOUT conditionA.
                // Spawn a document that has a DIFFERENT known specificity so it satisfies this
                // rule but cannot accidentally satisfy a PositiveForced rule for conditionA.
                // Picking from conditionA values of other active rules guarantees the spawned
                // specificity is a known, displayable game value — not an arbitrary string.
                if (TryGetAnyOtherSpecificity(rule.conditionA, out string negativeSimpleOther))
                    return new List<string> { negativeSimpleOther };

                return null; // No other active specificity exists — cannot spawn for this rule.

            case RuleType.PositiveWithNegative:
                // Document has conditionA but must NOT have conditionB — exact combo is [conditionA].
                // conditionB must stay absent, so we never include it in the combination.
                return new List<string> { rule.conditionA };

            case RuleType.ComplementPositiveForced:
                // Complement of PositiveForced means the document does NOT have conditionA.
                // Spawn a document with any other known specificity instead.
                if (TryGetAnyOtherSpecificity(rule.conditionA, out string complementForcedOther))
                    return new List<string> { complementForcedOther };

                return null;

            case RuleType.ComplementPositiveExclusive:
                // Complement of PositiveExclusive means conditionA IS present but WITH at least
                // one other specificity — the "exclusive" condition is violated by the extra spec.
                if (TryGetAnyOtherSpecificity(rule.conditionA, out string complementExclusiveOther))
                    return new List<string> { rule.conditionA, complementExclusiveOther };

                return null;

            case RuleType.ComplementNegativeSimple:
                // Complement of "no conditionA" means the document HAS conditionA.
                return new List<string> { rule.conditionA };

            case RuleType.ComplementPositiveWithNegative:
                // Complement means both conditionA and conditionB are present — the negative
                // condition (conditionB absent) is violated, routing the document to this complement bin.
                return new List<string> { rule.conditionA, rule.conditionB };

            case RuleType.PositiveOr:
                // PositiveOr is satisfied by either condition alone.
                // Return the conditionA variant — the complement covers neither case.
                return new List<string> { rule.conditionA };

            case RuleType.ComplementPositiveOr:
                // Neither conditionA nor conditionB — pick any other active specificity.
                if (TryGetAnyOtherSpecificity(rule.conditionA, out string complementOrOther)
                    && complementOrOther != rule.conditionB)
                    return new List<string> { complementOrOther };

                return null;

            default:
                Debug.LogWarning("[Spawner] Unknown rule type: " + rule.ruleType + " — skipping");
                return null;
        }
    }

    /// <summary>
    /// Evaluates whether the given specificity combination satisfies the given rule with STRICT
    /// exact matching. Mirrors SortingBin validation exactly — any divergence would cause documents
    /// to spawn that cannot be placed in the expected bin, breaking the game's core guarantee.
    /// Unlike SimulateRuleValidation (used for placeability checks), this method handles
    /// ConditionalBranch and PositiveDouble with branch awareness: the yes-branch requires
    /// both conditions, the no-branch requires conditionA alone with conditionB absent.
    /// </summary>
    /// <param name="rule">The rule to evaluate.</param>
    /// <param name="specificities">The specificity list of the candidate document.</param>
    /// <returns>True if the rule accepts this combination under strict exact matching.</returns>
    private bool ValidateExactCombination(RuleData rule, List<string> specificities)
    {
        switch (rule.ruleType)
        {
            case RuleType.PositiveForced:
                // PositiveForced only requires conditionA to be present — count is irrelevant.
                return specificities.Contains(rule.conditionA);

            case RuleType.PositiveExclusive:
                // Exclusive: conditionA must be the ONLY specificity present.
                return specificities.Contains(rule.conditionA) && specificities.Count == 1;

            case RuleType.ConditionalBranch:
                // Yes-branch (targetBinID): both conditionA and conditionB present.
                // No-branch (secondaryBinID): conditionA present, conditionB absent.
                bool isConditionalYesBranch = rule.targetBinID != rule.secondaryBinID;
                if (isConditionalYesBranch)
                    return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);

                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.PositiveDouble:
                // Identical branch logic to ConditionalBranch.
                bool isPositiveDoubleYesBranch = rule.targetBinID != rule.secondaryBinID;
                if (isPositiveDoubleYesBranch)
                    return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);

                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.NegativeSimple:
                return !specificities.Contains(rule.conditionA);

            case RuleType.PositiveWithNegative:
                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.ComplementPositiveForced:
                return !specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveExclusive:
                // Complement of exclusive: conditionA present AND at least one other specificity exists.
                return specificities.Contains(rule.conditionA) && specificities.Count > 1;

            case RuleType.ComplementNegativeSimple:
                return specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveWithNegative:
                return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);

            case RuleType.PositiveOr:
                return specificities.Contains(rule.conditionA) || specificities.Contains(rule.conditionB);

            case RuleType.ComplementPositiveOr:
                return !specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            default:
                return false;
        }
    }

    /// <summary>
    /// Checks whether the given specificity combination is placeable by at least one active rule.
    /// Iterates all rules and simulates validation logic inline — SortingBin validation cannot be
    /// called here because DocumentSpawner has no scene references to SortingBin instances.
    /// Returns true on the first rule whose validation would accept the combination.
    /// </summary>
    /// <param name="specificities">The specificity list of the candidate document.</param>
    /// <param name="allRules">All active rules across all bins for the current day.</param>
    /// <returns>True if at least one active rule accepts this combination; false otherwise.</returns>
    private bool IsDocumentPlaceable(List<string> specificities, List<RuleData> allRules)
    {
        foreach (RuleData rule in allRules)
        {
            bool isAccepted = SimulateRuleValidation(specificities, rule);
            if (isAccepted)
                return true;
        }

        return false;
    }

    /// <summary>
    /// Simulates the validation logic for a single rule against a specificity list.
    /// Mirrors the logic in SortingBin's per-type helpers without referencing any scene objects.
    /// Returns true if the combination satisfies the rule's accept condition
    /// (routing destination is irrelevant here — we only care whether the document is accepted).
    /// </summary>
    /// <param name="specificities">The specificity list of the candidate document.</param>
    /// <param name="rule">The rule to evaluate.</param>
    /// <returns>True if the rule accepts this specificity combination.</returns>
    private bool SimulateRuleValidation(List<string> specificities, RuleData rule)
    {
        switch (rule.ruleType)
        {
            case RuleType.PositiveForced:
                return specificities.Contains(rule.conditionA);

            case RuleType.PositiveExclusive:
                return specificities.Contains(rule.conditionA) && specificities.Count == 1;

            case RuleType.ConditionalBranch:
                // ConditionalBranch always accepts when conditionA is present (routes to one of its two bins).
                return specificities.Contains(rule.conditionA);

            case RuleType.PositiveDouble:
                // PositiveDouble always accepts when conditionA is present (routes to one of its two bins).
                return specificities.Contains(rule.conditionA);

            case RuleType.NegativeSimple:
                return !specificities.Contains(rule.conditionA);

            case RuleType.PositiveWithNegative:
                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.ComplementPositiveForced:
                return !specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveExclusive:
                return specificities.Contains(rule.conditionA) && specificities.Count > 1;

            case RuleType.ComplementNegativeSimple:
                return specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveWithNegative:
                return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);

            default:
                return false;
        }
    }

    /// <summary>
    /// Iterates validCombinations and removes entries that are either unplaceable or ambiguous.
    /// After BuildExactComboForRule, each combination should already map to exactly one bin —
    /// this pass is the safety net that catches edge cases from unusual rule configurations.
    ///
    /// A combination is UNPLACEABLE when no rule accepts it (validBinCount == 0): the player
    /// would receive a document they can never correctly sort, which is unwinnable.
    /// A combination is AMBIGUOUS when multiple distinct bins accept it (validBinCount > 1):
    /// the player cannot determine the correct bin, also an unwinnable situation.
    ///
    /// Both failure modes are removed here with distinct log messages so designers can
    /// identify which rule configurations trigger each case.
    ///
    /// Uses ValidateExactCombination rather than SimulateRuleValidation — the exact validator
    /// handles ConditionalBranch and PositiveDouble branch awareness correctly, matching the
    /// same logic SortingBin uses at runtime to route actual player drops.
    /// </summary>
    /// <param name="allRules">All active rules for the current day.</param>
    private void RemoveUnplaceableCombinations(List<RuleData> allRules)
    {
        // Iterate backwards so removing an element does not shift indices of unvisited entries.
        for (int index = validCombinations.Count - 1; index >= 0; index--)
        {
            List<string> combination = validCombinations[index];

            // Count how many DISTINCT bins accept this combination.
            // Multiple rules on the same bin both accepting the document is not a problem —
            // we only care about distinct targetBinIDs to detect routing conflicts.
            int validBinCount      = 0;
            string firstValidBinID = string.Empty;

            foreach (RuleData rule in allRules)
            {
                if (!ValidateExactCombination(rule, combination))
                    continue;

                if (firstValidBinID == string.Empty)
                {
                    firstValidBinID = rule.targetBinID;
                    validBinCount   = 1;
                }
                else if (rule.targetBinID != firstValidBinID)
                {
                    // A second distinct bin accepts this combination — ambiguous routing.
                    validBinCount++;
                }
            }

            if (validBinCount == 0)
            {
                // No bin accepts this combination — the document would have no valid destination.
                Debug.LogWarning("[Spawner] Removed unplaceable: [" +
                                 string.Join(", ", combination) + "]");
                validCombinations.RemoveAt(index);
                continue;
            }

            if (validBinCount > 1)
            {
                // Multiple distinct bins accept this combination — the player cannot determine
                // the correct bin, so spawning this document would create an unwinnable situation.
                Debug.LogWarning("[Spawner] Removed ambiguous: [" +
                                 string.Join(", ", combination) +
                                 "] valid in " + validBinCount + " bins");
                validCombinations.RemoveAt(index);
            }
        }
    }

    /// <summary>
    /// Returns the number of valid combinations currently in the spawn pool.
    /// Used by GameManager to diagnose spawn pool depletion after conflict resolution —
    /// a count of 1 after evolution signals that the removal logic was too aggressive.
    /// </summary>
    public int GetValidCombinationCount() => validCombinations.Count;

    /// <summary>
    /// Attempts to find any specificity from the active rules that is different from the excluded one.
    /// Used when a rule type needs a combination that deliberately does NOT include a specific condition.
    /// </summary>
    /// <param name="excludedSpecificity">The specificity to exclude from the result.</param>
    /// <param name="result">A different specificity from the active rules, or empty string on failure.</param>
    /// <returns>True if a different specificity was found; false if none exists.</returns>
    private bool TryGetAnyOtherSpecificity(string excludedSpecificity, out string result)
    {
        foreach (RuleData rule in activeRules)
        {
            if (!string.IsNullOrEmpty(rule.conditionA) && rule.conditionA != excludedSpecificity)
            {
                result = rule.conditionA;
                return true;
            }
        }

        result = string.Empty;
        return false;
    }

    // -------------------------------------------------------------------------
    // Document generation and instantiation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a new DocumentData instance populated with a randomly chosen valid combination.
    /// The document ID is unique within the session thanks to the monotonic counter.
    /// </summary>
    /// <returns>A fully populated DocumentData ready for injection into a prefab instance.</returns>
    private DocumentData GenerateDocumentData()
    {
        List<string> chosenCombination = PickRandomCombination();

        documentSpawnCounter++;

        return new DocumentData
        {
            documentID = $"doc_{documentSpawnCounter:D4}",
            specificities = new List<string>(chosenCombination)
        };
    }

    /// <summary>
    /// Instantiates one document prefab inside the spawn area, injects its DocumentData
    /// and all scene dependencies, then hands it to DocumentStackManager.EnqueueDocument.
    /// DocumentSpawner creates documents; DocumentStackManager owns and tracks them —
    /// there is no spawnedDocuments list here because lifetime is managed by the stack manager.
    /// Called directly by onStackBelowTarget, so it fires once per missing document in the same frame.
    /// </summary>
    private void SpawnDocument()
    {
        if (validCombinations.Count == 0)
            return; // No combinations available — spawning would produce an empty document.

        // A missing canvas reference would cause a silent NullReferenceException inside
        // OnDrag the moment the player first touches the document, with no obvious cause.
        // Failing loudly here pinpoints the misconfiguration at the source instead.
        if (mainCanvas == null)
        {
            Debug.LogError("[DocumentSpawner] mainCanvas is not assigned. " +
                           "Drag the scene Canvas into the mainCanvas field on DocumentSpawner.");
            return;
        }

        // A missing detector reference would cause a NullReferenceException in OnEndDrag
        // the first time the player drops a document, for the exact same reason as mainCanvas.
        if (dropZoneDetector == null)
        {
            Debug.LogError("[DocumentSpawner] dropZoneDetector is not assigned. " +
                           "Drag the scene DropZoneDetector into the dropZoneDetector field on DocumentSpawner.");
            return;
        }

        DocumentData generatedData = GenerateDocumentData();

        GameObject documentInstance = Instantiate(documentPrefab, spawnAreaParent);

        // Set sibling index to 0 immediately after Instantiate, BEFORE handing the document
        // to EnqueueDocument. Unity places newly instantiated children at the last sibling
        // position by default, which would make the document appear on top for one frame.
        // Forcing index 0 here prevents that single-frame visual pop — the document starts
        // behind everything from the very first frame it exists.
        // EnqueueDocument also sets it to 0, but this call is the first line of defence.
        documentInstance.transform.SetSiblingIndex(0);

        DraggableDocument draggableDocument = documentInstance.GetComponent<DraggableDocument>();
        draggableDocument.SetDocumentData(generatedData);
        draggableDocument.SetCanvasReference(mainCanvas);
        draggableDocument.SetDropZoneDetector(dropZoneDetector);
        draggableDocument.SetStackManager(documentStackManager);
        draggableDocument.SetAllActiveRules(activeRules);

        AssignSpecificitiesToLabel(documentInstance, generatedData.specificities);

        // Apply visual layers (sprites, rotation, tint) based on the document's specificities.
        // Called after data injection and label assignment — purely visual, no data side-effects.
        DocumentVisualizer visualizer = documentInstance.GetComponent<DocumentVisualizer>();
        if (visualizer != null)
            visualizer.ApplyVisuals(generatedData);

        // Hand the document to the stack manager — it owns all positioning and lifetime tracking.
        // DocumentSpawner must not store a reference to the document after this point.
        documentStackManager.EnqueueDocument(documentInstance);
    }

    /// <summary>
    /// Spawns documents until the total stack count reaches targetStackSize.
    /// Called once at the start of each day because the stack is empty at that point
    /// and onStackBelowTarget has not yet fired (no documents have been enqueued yet).
    /// </summary>
    private void FillStackToTarget()
    {
        int missing = documentStackManager.targetStackSize
                      - documentStackManager.GetTotalDocumentCount();

        for (int i = 0; i < missing; i++)
            SpawnDocument();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Picks a random entry from validCombinations.
    /// Assumes the list is non-empty — callers must guard against the empty case.
    /// </summary>
    /// <returns>One specificity combination list chosen at random.</returns>
    private List<string> PickRandomCombination()
    {
        int randomIndex = Random.Range(0, validCombinations.Count);
        return validCombinations[randomIndex];
    }

    /// <summary>
    /// Finds the first TextMeshProUGUI component on the document instance and sets its text
    /// to the specificity list, one entry per line.
    /// </summary>
    /// <param name="documentInstance">The instantiated document GameObject.</param>
    /// <param name="specificities">The specificities to display on the document label.</param>
    private void AssignSpecificitiesToLabel(GameObject documentInstance, List<string> specificities)
    {
        TextMeshProUGUI documentLabel = documentInstance.GetComponentInChildren<TextMeshProUGUI>();

        // Guard: prefab may not yet have a label wired during early prototyping.
        if (documentLabel == null)
            return;

        documentLabel.text = string.Join("\n", specificities);
    }
}
