using System.Collections;
using System.Collections.Generic;
using TMPro;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for generating DocumentData, instantiating document prefabs
/// at runtime, and controlling the spawn rhythm throughout a game day.
/// Injects all scene dependencies (canvas, detector, stack manager) into each spawned document
/// immediately after instantiation, then hands the document off to DocumentStackManager.
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
    /// The DocumentStackManager that receives every newly spawned document.
    /// Injected into each DraggableDocument so the document can report drag events
    /// back to the manager, which owns all visual state and document lifetime.
    /// </summary>
    [SerializeField] private DocumentStackManager documentStackManager;

    // -------------------------------------------------------------------------
    // Spawn timing — never hardcoded inline
    // -------------------------------------------------------------------------

    /// <summary>Seconds between consecutive document spawns. Adjusted externally by DifficultyManager.</summary>
    [SerializeField] private float spawnInterval = 0.5f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Rules active for the current day, used exclusively to build validCombinations.</summary>
    private List<RuleData> activeRules = new List<RuleData>();

    /// <summary>
    /// All valid specificity combinations derived from the active rules.
    /// Rebuilt once per day in RebuildValidCombinations and read-only during SpawnLoop.
    /// </summary>
    private List<List<string>> validCombinations = new List<List<string>>();

    /// <summary>All document GameObjects currently alive in the scene this day.</summary>
    private List<GameObject> spawnedDocuments = new List<GameObject>();

    /// <summary>Reference to the running SpawnLoop coroutine; null when no day is active.</summary>
    private Coroutine spawnCoroutine;

    /// <summary>Monotonically increasing counter used to produce unique document IDs.</summary>
    private int documentSpawnCounter;

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Overrides the spawn interval at runtime.
    /// Called by GameManager after receiving a new DifficultySettings each day.
    /// </summary>
    /// <param name="intervalInSeconds">New seconds between document spawns.</param>
    public void SetSpawnInterval(float intervalInSeconds)
    {
        spawnInterval = intervalInSeconds;
    }

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
    /// Stops any running spawn coroutine and starts a fresh SpawnLoop for the new day.
    /// Safe to call even if no day was previously running.
    /// </summary>
    public void StartDay()
    {
        StopActiveCoroutineSafely();
        spawnCoroutine = StartCoroutine(SpawnLoop());
    }

    /// <summary>
    /// Halts the active SpawnLoop coroutine without affecting any other coroutines on this object.
    /// Safe to call even if no spawn is currently running.
    /// </summary>
    public void StopDay()
    {
        StopActiveCoroutineSafely();
    }

    /// <summary>
    /// Destroys every document GameObject spawned during the current day and clears the tracking list.
    /// Called by GameManager at the end of a day before transitioning to the next one.
    /// </summary>
    public void ClearAllDocuments()
    {
        foreach (GameObject spawnedDocument in spawnedDocuments)
        {
            // Guard against documents that were already destroyed by a valid drop.
            if (spawnedDocument == null)
                continue;

            Destroy(spawnedDocument);
        }

        spawnedDocuments.Clear();
    }

    // -------------------------------------------------------------------------
    // Data building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds validCombinations from the current activeRules, generating document specificity
    /// sets that are appropriate for each rule type.
    /// Called once at the start of each day, never during an active SpawnLoop —
    /// this avoids modifying the combination list while SpawnDocument reads from it,
    /// and ensures all spawned documents remain consistent with the rules the player sees.
    /// </summary>
    private void RebuildValidCombinations()
    {
        validCombinations.Clear();

        foreach (RuleData rule in activeRules)
        {
            switch (rule.ruleType)
            {
                case RuleType.PositiveForced:
                case RuleType.PositiveExclusive:
                case RuleType.NegativeSimple:
                    // A document with conditionA will trigger these rules (positively or negatively).
                    validCombinations.Add(new List<string> { rule.conditionA });
                    break;

                case RuleType.ConditionalBranch:
                    // Add conditionA alone (routes to secondaryBinID) and both together (routes to targetBinID),
                    // so both branches of the rule are exercised by spawned documents.
                    validCombinations.Add(new List<string> { rule.conditionA });
                    validCombinations.Add(new List<string> { rule.conditionA, rule.conditionB });
                    break;

                case RuleType.NegativeMultiple:
                    // A document with no specificities satisfies "none of these conditions" by definition.
                    // Spawning empty-specificity documents ensures NegativeMultiple rules are reachable.
                    validCombinations.Add(new List<string>());
                    break;

                case RuleType.Fallback:
                    // A document matching no rule is the intended trigger for the Fallback bin.
                    // An empty specificity list guarantees no positive rule can claim it.
                    validCombinations.Add(new List<string>());
                    break;
            }
        }

        // Guard: if all rules produced empty combinations, add one non-empty default
        // so SpawnDocument always has at least one spawnable document type.
        if (validCombinations.Count == 0)
            validCombinations.Add(new List<string>());
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
    /// Instantiates one document prefab inside the spawn area, positions it randomly,
    /// injects its DocumentData, updates its label, and registers it for later cleanup.
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

        // Do NOT set anchoredPosition here — DocumentStackManager owns all document positioning
        // and will centre the document on the stack zone as soon as it becomes the top document.

        DraggableDocument draggableDocument = documentInstance.GetComponent<DraggableDocument>();
        draggableDocument.SetDocumentData(generatedData);
        draggableDocument.SetCanvasReference(mainCanvas);
        draggableDocument.SetDropZoneDetector(dropZoneDetector);
        draggableDocument.SetStackManager(documentStackManager);
        draggableDocument.SetAllActiveRules(activeRules);

        AssignSpecificitiesToLabel(documentInstance, generatedData.specificities);

        // Hand the document off to the stack manager instead of positioning it manually.
        // DocumentStackManager owns all document positioning — spawner must not set anchoredPosition
        // after this point, as the manager will immediately override it when showing the document.
        documentStackManager.EnqueueDocument(documentInstance);

        spawnedDocuments.Add(documentInstance);
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Continuously waits spawnInterval seconds then spawns a document.
    /// Runs until explicitly stopped by StopDay() or ClearAllDocuments().
    /// </summary>
    private IEnumerator SpawnLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(spawnInterval);
            SpawnDocument();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops the stored spawnCoroutine if one is running.
    /// StopAllCoroutines is intentionally avoided here — it would cancel any other
    /// coroutines started on this MonoBehaviour (e.g. flash effects from DraggableDocument
    /// or future animation coroutines), causing unrelated behaviour to silently break.
    /// </summary>
    private void StopActiveCoroutineSafely()
    {
        // Null check is mandatory: StopCoroutine throws if passed a null reference.
        if (spawnCoroutine == null)
            return;

        StopCoroutine(spawnCoroutine);
        spawnCoroutine = null;
    }

    /// <summary>
    /// Returns a random anchored position within the bounds of spawnAreaParent.
    /// </summary>
    /// <returns>A Vector2 anchored position within the spawn area rectangle.</returns>
    private Vector2 ComputeRandomSpawnPosition()
    {
        float halfWidth  = spawnAreaParent.rect.width  * 0.5f;
        float halfHeight = spawnAreaParent.rect.height * 0.5f;

        float randomX = Random.Range(-halfWidth,  halfWidth);
        float randomY = Random.Range(-halfHeight, halfHeight);

        return new Vector2(randomX, randomY);
    }

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
