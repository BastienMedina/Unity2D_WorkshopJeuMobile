using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// Spawns documents whose specificities are derived exclusively from the rules currently
/// active in <see cref="LibraryRuleAssigner"/>. Only combinations that satisfy at least one
/// active rule and route to exactly one bin are eligible — unplaceable and ambiguous
/// combinations are purged before the spawn loop starts.
///
/// Subscribes to <see cref="LibraryRuleAssigner.OnRulesAssigned"/> to rebuild its
/// valid combination pool whenever the rule set changes.
/// Subscribes to <see cref="DocumentStackManager.onStackBelowTarget"/> to spawn
/// demand-driven (no polling loop).
///
/// Does NOT validate documents, assign rules, or manage the document stack position.
/// </summary>
public class LibraryDocumentSpawner : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [SerializeField] private GameObject documentPrefab;
    [SerializeField] private RectTransform spawnAreaParent;
    [SerializeField] private Canvas mainCanvas;
    [SerializeField] private DropZoneDetector dropZoneDetector;
    [SerializeField] private DocumentStackManager documentStackManager;
    [SerializeField] private SpecificityDatabase specificityDatabase;

    /// <summary>The assigner whose OnRulesAssigned event drives pool rebuilds.</summary>
    [SerializeField] private LibraryRuleAssigner ruleAssigner;

    // ─── Runtime state ────────────────────────────────────────────────────────────

    private List<RuleData> activeRules = new List<RuleData>();
    private List<List<string>> validCombinations = new List<List<string>>();
    private int spawnCounter;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────────

    private void OnEnable()
    {
        if (ruleAssigner != null)
            ruleAssigner.OnRulesAssigned += OnRulesAssigned;
    }

    private void OnDisable()
    {
        if (ruleAssigner != null)
            ruleAssigner.OnRulesAssigned -= OnRulesAssigned;

        StopDay();
    }

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>Starts demand-driven spawning and performs the initial stack fill.</summary>
    public void StartDay()
    {
        documentStackManager.onStackBelowTarget += SpawnDocument;
        FillStackToTarget();
    }

    /// <summary>Stops all future spawning.</summary>
    public void StopDay()
    {
        if (documentStackManager != null)
            documentStackManager.onStackBelowTarget -= SpawnDocument;
    }

    /// <summary>Destroys all documents and stops spawning.</summary>
    public void ClearAllDocuments()
    {
        StopDay();
    }

    /// <summary>Returns the number of valid combinations currently in the pool.</summary>
    public int GetValidCombinationCount() => validCombinations.Count;

    /// <summary>
    /// Rebuilds the valid combination pool from an externally supplied rule list.
    /// Called by <see cref="LibraryGameManager"/> after <see cref="RuleComplexityUpgrader"/>
    /// upgrades the bin rules post-assignment, so the spawner reflects the upgraded conditions.
    /// </summary>
    /// <param name="upgradedRules">The full flat rule list after complexity upgrades.</param>
    public void RebuildPool(List<RuleData> upgradedRules)
    {
        activeRules = new List<RuleData>(upgradedRules);
        RebuildValidCombinations();

        // Propagate the upgraded rule list to any documents already in the scene
        // so their validation uses the new rules immediately.
        DraggableDocument[] existingDocuments =
            spawnAreaParent != null
                ? spawnAreaParent.GetComponentsInChildren<DraggableDocument>()
                : System.Array.Empty<DraggableDocument>();

        foreach (DraggableDocument doc in existingDocuments)
            doc.SetAllActiveRules(activeRules);

        Debug.Log($"[LibrarySpawner] Pool rebuilt after complexity upgrade — {validCombinations.Count} valid combinations.");
    }

    // ─── Event handlers ───────────────────────────────────────────────────────────

    /// <summary>Called by LibraryRuleAssigner after rules are distributed to bins.</summary>
    private void OnRulesAssigned(List<RuleData> rules)
    {
        activeRules = new List<RuleData>(rules);
        RebuildValidCombinations();
    }

    // ─── Combination building ─────────────────────────────────────────────────────

    /// <summary>
    /// Builds the valid combination pool from the active rules.
    /// Each rule contributes one or more exact specificity combinations.
    /// Purges unplaceable (no bin accepts) and ambiguous (multiple distinct bins accept) entries.
    ///
    /// With the independent-rule system every active rule is a positive rule assigned to
    /// one bin, so every generated combination is expected to route to exactly one bin.
    /// The purge step acts as a safety net for edge cases (e.g. conflicting conditions
    /// that slipped past the assigner's conflict check).
    /// </summary>
    private void RebuildValidCombinations()
    {
        validCombinations.Clear();

        foreach (RuleData rule in activeRules)
        {
            List<List<string>> combos = BuildCombosForRule(rule);
            foreach (List<string> combo in combos)
            {
                if (combo == null || combo.Count == 0)
                    continue;

                bool alreadyPresent = validCombinations.Exists(existing =>
                    existing.Count == combo.Count &&
                    existing.TrueForAll(combo.Contains));

                if (!alreadyPresent)
                    validCombinations.Add(combo);
            }
        }

        PurgeInvalidCombinations();

        Debug.Log($"[LibrarySpawner] Valid combinations: {validCombinations.Count}");
        foreach (List<string> combo in validCombinations)
            Debug.Log("  → [" + string.Join(", ", combo) + "]");
    }

    /// <summary>
    /// Returns the minimal exact specificity combination(s) that satisfy the given rule.
    /// Only positive rule types are expected — the new system has no complement rules.
    ///
    /// PositiveForced    → [conditionA]
    /// PositiveDouble    → [conditionA, conditionB]  (plain AND gate — library converter path)
    ///                     Also registers [conditionA] alone as a secondary combo when
    ///                     secondaryBinID is set (legacy self-complementary path).
    /// PositiveOr        → [conditionA] AND [conditionB] as two separate combos
    /// PositiveWithNeg.  → [conditionA]  (conditionB's absence is enforced by the purge step)
    /// </summary>
    private List<List<string>> BuildCombosForRule(RuleData rule)
    {
        List<List<string>> result = new List<List<string>>();

        switch (rule.ruleType)
        {
            case RuleType.Simple:
                if (!string.IsNullOrEmpty(rule.conditionA))
                    result.Add(new List<string> { rule.conditionA });
                break;

            case RuleType.Multiple:
                if (!string.IsNullOrEmpty(rule.conditionA) && !string.IsNullOrEmpty(rule.conditionB))
                    result.Add(new List<string> { rule.conditionA, rule.conditionB });
                break;

            case RuleType.Branch:
                // conditionA must be present; conditionB must be absent.
                if (!string.IsNullOrEmpty(rule.conditionA))
                    result.Add(new List<string> { rule.conditionA });
                break;
        }

        return result;
    }

    /// <summary>
    /// Removes combinations that are unplaceable (no rule accepts them) or ambiguous
    /// (two or more DISTINCT bins accept them). Acts as a safety net for conflicts that
    /// slipped past the assigner's condition-conflict check.
    ///
    /// A Sauf entry produces two rules with different targetBinIDs that share conditionA.
    /// The combo [conditionA, conditionB] must match only the PositiveDouble rule (bin2),
    /// and [conditionA] alone must match only the PositiveWithNegative rule (bin1).
    /// Because each combo maps to exactly one of the two bins, these combos are NOT ambiguous.
    /// </summary>
    private void PurgeInvalidCombinations()
    {
        for (int i = validCombinations.Count - 1; i >= 0; i--)
        {
            List<string> combo = validCombinations[i];

            // Collect every distinct targetBinID that accepts this combo.
            // We track by binID, not by rule count, so multiple rules on the same bin
            // don't inflate the count and trigger a false-positive ambiguity purge.
            System.Collections.Generic.HashSet<string> acceptingBins =
                new System.Collections.Generic.HashSet<string>();

            foreach (RuleData rule in activeRules)
            {
                if (SimulateValidation(rule, combo))
                    acceptingBins.Add(rule.targetBinID);
            }

            string comboLabel = "[" + string.Join(", ", combo) + "]";

            if (acceptingBins.Count == 0)
            {
                Debug.LogWarning("[LibrarySpawner] Purged unplaceable: " + comboLabel);
                validCombinations.RemoveAt(i);
            }
            else if (acceptingBins.Count > 1)
            {
                Debug.LogWarning("[LibrarySpawner] Purged ambiguous: " + comboLabel);
                validCombinations.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Mirrors SortingBin positive validation logic without referencing scene objects.
    /// Must stay in sync with SortingBin.DispatchValidation().
    ///
    /// PositiveDouble follows the same two-path logic as SortingBin.ValidatePositiveDouble:
    ///   - No secondaryBinID (library-converter path): condA AND condB must both be present.
    ///   - secondaryBinID set (legacy path): condA+condB → targetBin, condA alone → secondaryBin.
    ///     The purge step checks both variants against the rule's targetBinID.
    /// </summary>
    private bool SimulateValidation(RuleData rule, List<string> specs)
    {
        switch (rule.ruleType)
        {
            case RuleType.Simple:
                return specs.Contains(rule.conditionA);

            case RuleType.Multiple:
                return specs.Contains(rule.conditionA) && specs.Contains(rule.conditionB);

            case RuleType.Branch:
                return specs.Contains(rule.conditionA) && !specs.Contains(rule.conditionB);

            default:
                return false;
        }
    }

    // ─── Spawning ─────────────────────────────────────────────────────────────────

    private void FillStackToTarget()
    {
        int missing = documentStackManager.targetStackSize
                      - documentStackManager.GetTotalDocumentCount();

        for (int i = 0; i < missing; i++)
            SpawnDocument();
    }

    private void SpawnDocument()
    {
        if (validCombinations.Count == 0)
        {
            Debug.LogWarning("[LibrarySpawner] No valid combinations — cannot spawn document.");
            return;
        }

        if (mainCanvas == null)
        {
            Debug.LogError("[LibrarySpawner] mainCanvas not assigned.");
            return;
        }

        if (dropZoneDetector == null)
        {
            Debug.LogError("[LibrarySpawner] dropZoneDetector not assigned.");
            return;
        }

        List<string> combo = validCombinations[Random.Range(0, validCombinations.Count)];

        spawnCounter++;
        DocumentData data = new DocumentData
        {
            documentID   = $"lib_doc_{spawnCounter:D4}",
            specificities = new List<string>(combo)
        };

        GameObject instance = Instantiate(documentPrefab, spawnAreaParent);
        instance.transform.SetSiblingIndex(0);

        DraggableDocument draggable = instance.GetComponent<DraggableDocument>();
        draggable.SetDocumentData(data);
        draggable.SetCanvasReference(mainCanvas);
        draggable.SetDropZoneDetector(dropZoneDetector);
        draggable.SetStackManager(documentStackManager);
        draggable.SetAllActiveRules(activeRules);

        AssignSpecificitiesToLabel(instance, data.specificities);

        DocumentVisualizer visualizer = instance.GetComponent<DocumentVisualizer>();
        if (visualizer != null)
            visualizer.ApplyVisuals(data);

        documentStackManager.EnqueueDocument(instance);
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private void AssignSpecificitiesToLabel(GameObject instance, List<string> specificities)
    {
        TextMeshProUGUI label = instance.GetComponentInChildren<TextMeshProUGUI>();
        if (label != null)
            label.text = string.Join("\n", specificities);
    }
}
