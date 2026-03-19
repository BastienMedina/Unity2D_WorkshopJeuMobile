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
    /// Rebuilds validCombinations from the current activeRules, generating document specificity
    /// sets that are appropriate for each rule type.
    /// Complement types do not add separate combinations — complement documents are already
    /// covered by the primary rule's combinations (same specificities, different routing).
    /// After building, calls RemoveUnplaceableCombinations as a final safety net to guarantee
    /// the spawn pool never contains a combination that no active rule can accept.
    /// Called once at the start of each day, never during an active spawn loop.
    /// </summary>
    private void RebuildValidCombinations()
    {
        validCombinations.Clear();

        foreach (RuleData rule in activeRules)
        {
            switch (rule.ruleType)
            {
                case RuleType.PositiveForced:
                    // PositiveForced is valid regardless of other specificities —
                    // both a document with only conditionA and one with conditionA plus another
                    // specificity are valid spawn targets for this rule.
                    validCombinations.Add(new List<string> { rule.conditionA });
                    if (TryGetAnyOtherSpecificity(rule.conditionA, out string otherForced))
                        validCombinations.Add(new List<string> { rule.conditionA, otherForced });
                    break;

                case RuleType.PositiveExclusive:
                    // Only the single conditionA is valid — any extra specificity disqualifies the document.
                    validCombinations.Add(new List<string> { rule.conditionA });
                    break;

                case RuleType.ConditionalBranch:
                    // Add conditionA alone (routes to secondaryBinID) and both together (routes to targetBinID)
                    // so both branches of the rule are exercised by spawned documents.
                    validCombinations.Add(new List<string> { rule.conditionA });
                    validCombinations.Add(new List<string> { rule.conditionA, rule.conditionB });
                    break;

                case RuleType.PositiveDouble:
                    // Add both conditionA+B (goes to targetBinID) and conditionA alone (goes to secondaryBinID)
                    // to cover both branches of the self-complementary rule.
                    validCombinations.Add(new List<string> { rule.conditionA, rule.conditionB });
                    validCombinations.Add(new List<string> { rule.conditionA });
                    break;

                case RuleType.NegativeSimple:
                    // Must generate a document WITHOUT conditionA — pick any other available specificity.
                    // A document that has conditionA would be invalid for NegativeSimple,
                    // so we deliberately exclude it from this combination.
                    if (TryGetAnyOtherSpecificity(rule.conditionA, out string negSimpleOther))
                        validCombinations.Add(new List<string> { negSimpleOther });
                    break;

                case RuleType.PositiveWithNegative:
                    // Add conditionA alone (has A, no B — satisfies the primary rule).
                    // Add conditionA + conditionB for the complement rule's validation.
                    validCombinations.Add(new List<string> { rule.conditionA });
                    validCombinations.Add(new List<string> { rule.conditionA, rule.conditionB });
                    break;

                // Complement types: do NOT add separate combinations.
                // Complement documents are already covered by the primary rule's combinations —
                // the same specificities route to a different bin depending on the rule, but the
                // spawned document shapes are identical. Adding duplicates here would skew the
                // random pick distribution and over-represent complement-case documents.
                case RuleType.ComplementPositiveForced:
                case RuleType.ComplementPositiveExclusive:
                case RuleType.ComplementNegativeSimple:
                case RuleType.ComplementPositiveWithNegative:
                    break;
            }
        }

        // Guard: if all rules produced no combinations, add one non-empty default
        // so SpawnDocument always has at least one spawnable document type.
        if (validCombinations.Count == 0)
            validCombinations.Add(new List<string>());

        // Final safety net: remove any combination that cannot be placed in any active bin.
        // This pass runs even when generation logic is correct, providing an absolute guarantee
        // that no unplaceable document combination can ever reach the spawn pool.
        RemoveUnplaceableCombinations(activeRules);
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
    /// Iterates validCombinations and removes any entry for which IsDocumentPlaceable returns false.
    /// Acts as the final safety net — even when the combination-generation logic is correct,
    /// this pass provides an absolute guarantee that no unplaceable combination reaches the spawn pool.
    /// Logs a warning for each removed combination so designers can identify rule configurations
    /// that produce dead ends and fix them in the SpecificityDatabase or rule setup.
    /// </summary>
    /// <param name="allRules">All active rules for the current day.</param>
    private void RemoveUnplaceableCombinations(List<RuleData> allRules)
    {
        // Iterate backwards so removing an element does not shift indices of unvisited entries.
        for (int index = validCombinations.Count - 1; index >= 0; index--)
        {
            List<string> combination = validCombinations[index];

            if (IsDocumentPlaceable(combination, allRules))
                continue;

            // Log a warning before removing — this is a signal for designers that the current
            // rule configuration produces at least one document type that has no valid destination.
            Debug.LogWarning("[Spawner] Removed unplaceable combination: " +
                             string.Join(",", combination));

            validCombinations.RemoveAt(index);
        }
    }

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
