using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for instantiating document prefabs at runtime and refilling
/// the stack on demand whenever DocumentStackManager signals a deficit.
///
/// Spawning is prefab-driven: each active rule's conditionA stores the asset path of the
/// document prefab to use for that rule. UpdateActiveRules() builds a pool of those prefabs
/// (one entry per rule, allowing weighted repetition when multiple rules share the same prefab).
/// SpawnDocument() picks one at random and instantiates it.
///
/// Injects all scene dependencies (canvas, detector, stack manager) into each spawned document
/// immediately after instantiation, then hands the document off to DocumentStackManager.
///
/// Does NOT use a coroutine or timer — spawning is driven entirely by onStackBelowTarget events.
/// Does NOT validate documents, access SortingBin logic, or manage document position.
/// </summary>
public class DocumentSpawner : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

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

    /// <summary>Rules active for the current day — stored to pass to each spawned DraggableDocument.</summary>
    private List<RuleData> activeRules = new List<RuleData>();

    /// <summary>
    /// Pool of prefabs eligible for spawning this night.
    /// Built from the conditionA field of every active rule: when conditionA is a Unity asset
    /// path (starts with "Assets/"), the prefab is loaded and added to the pool.
    /// The pool may contain duplicates when several rules reference the same prefab — this
    /// gives those documents proportionally higher spawn weight, which is intentional.
    /// Trash prefabs are appended separately via SetTrashPrefabs and share the same pool.
    /// </summary>
    private List<GameObject> spawnablePrefabs = new List<GameObject>();

    /// <summary>
    /// Prefabs designated as trash documents for the current night.
    /// Loaded from NightSaveData.trashedPrefabPaths via SetTrashPrefabs and appended
    /// to spawnablePrefabs during RebuildSpawnPool so trash documents mix naturally
    /// with regular documents in the spawn stream.
    /// </summary>
    private List<GameObject> trashPrefabs = new List<GameObject>();

    /// <summary>Monotonically increasing counter used to produce unique document IDs.</summary>
    private int documentSpawnCounter;

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stores the day's active rules and rebuilds the spawnable prefab pool.
    /// Must be called by GameManager before StartDay() so the pool is ready.
    /// Each rule whose conditionA is a prefab asset path contributes one prefab to the pool.
    /// </summary>
    /// <param name="rules">The list of RuleData active for the current night.</param>
    public void UpdateActiveRules(List<RuleData> rules)
    {
        activeRules = new List<RuleData>(rules);
        RebuildSpawnPool();
    }

    /// <summary>
    /// Loads and stores the trash prefabs for the current night, then rebuilds the spawn pool.
    /// Must be called by GameManager after UpdateActiveRules so the pool incorporates both
    /// regular-bin prefabs and trash prefabs before StartDay() triggers the initial fill.
    /// Passing an empty or null list clears any previously stored trash prefabs.
    /// </summary>
    /// <param name="assetPaths">Full Unity asset paths of the trash document prefabs.</param>
    public void SetTrashPrefabs(List<string> assetPaths)
    {
        trashPrefabs.Clear();

        if (assetPaths == null || assetPaths.Count == 0)
        {
            RebuildSpawnPool();
            return;
        }

        foreach (string path in assetPaths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            GameObject prefab = LoadPrefabAtPath(path);

            if (prefab == null)
            {
                Debug.LogWarning($"[DocumentSpawner] Trash prefab not found at path '{path}'. " +
                                 "Ensure the prefab exists under Assets/Resources/.");
                continue;
            }

            trashPrefabs.Add(prefab);
        }

        RebuildSpawnPool();
    }

    /// <summary>
    /// Subscribes SpawnDocument to onStackBelowTarget and performs the initial fill.
    /// All spawning is demand-driven by the stack event — no interval-based coroutine.
    /// </summary>
    public void StartDay()
    {
        documentStackManager.onStackBelowTarget += SpawnDocument;
        FillStackToTarget();
    }

    /// <summary>
    /// Unsubscribes SpawnDocument from onStackBelowTarget to halt all future spawning.
    /// Safe to call even if StartDay() was never called.
    /// </summary>
    public void StopDay()
    {
        documentStackManager.onStackBelowTarget -= SpawnDocument;
    }

    /// <summary>
    /// Destroys every document GameObject still alive in the scene and clears tracking state.
    /// Unsubscribes from onStackBelowTarget as a defensive backstop against ghost spawns.
    /// </summary>
    public void ClearAllDocuments()
    {
        documentStackManager.onStackBelowTarget -= SpawnDocument;
    }

    /// <summary>
    /// Returns the number of distinct prefabs currently in the spawn pool.
    /// Used by GameManager to diagnose pool depletion after rule changes.
    /// </summary>
    public int GetValidCombinationCount() => spawnablePrefabs.Count;

    // -------------------------------------------------------------------------
    // Pool building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Rebuilds spawnablePrefabs from the prefabPaths/conditionA fields of the current active rules,
    /// then appends any trash prefabs stored via SetTrashPrefabs.
    ///
    /// For each rule, when prefabPaths is non-empty every path is added individually so all
    /// accepted prefabs for that rule appear in the pool. Otherwise conditionA is used as the
    /// single path (backward-compatible with saves that predate multi-prefab rules).
    /// Trash prefabs are appended after all rule-based prefabs so both pools feed a single
    /// random selection, mixing trash documents naturally into the spawn stream.
    /// </summary>
    private void RebuildSpawnPool()
    {
        spawnablePrefabs.Clear();

        foreach (RuleData rule in activeRules)
        {
            // Multi-prefab mode: add every path in the list.
            if (rule.prefabPaths != null && rule.prefabPaths.Count > 0)
            {
                foreach (string path in rule.prefabPaths)
                {
                    if (string.IsNullOrEmpty(path) || !path.StartsWith("Assets/"))
                        continue;

                    GameObject prefab = LoadPrefabAtPath(path);

                    if (prefab == null)
                    {
                        Debug.LogWarning($"[DocumentSpawner] Multi-prefab path not found: '{path}'. Skipping.");
                        continue;
                    }

                    spawnablePrefabs.Add(prefab);
                }
                continue;
            }

            // Single-prefab / condition mode: fall back to conditionA.
            string singlePath = rule.conditionA;

            if (string.IsNullOrEmpty(singlePath) || !singlePath.StartsWith("Assets/"))
                continue;

            GameObject singlePrefab = LoadPrefabAtPath(singlePath);

            if (singlePrefab == null)
            {
                Debug.LogWarning($"[DocumentSpawner] Prefab not found at path '{singlePath}' " +
                                 $"(rule conditionA). Skipping this rule in the spawn pool.");
                continue;
            }

            spawnablePrefabs.Add(singlePrefab);
        }

        // Append trash prefabs so they mix with regular documents in the same pool.
        foreach (GameObject trashPrefab in trashPrefabs)
            spawnablePrefabs.Add(trashPrefab);

        if (spawnablePrefabs.Count == 0)
        {
            Debug.LogWarning("[DocumentSpawner] Spawn pool is empty after RebuildSpawnPool. " +
                             "Ensure all active rules have a valid prefab path in conditionA or prefabPaths " +
                             "and that the prefabs are located under Assets/Resources/.");
        }
        else
        {
            Debug.Log($"[DocumentSpawner] Spawn pool rebuilt — {spawnablePrefabs.Count} prefab entries " +
                      $"({trashPrefabs.Count} trash) from {activeRules.Count} rules.");
        }
    }

    /// <summary>
    /// Loads a prefab GameObject from a full Unity asset path (e.g. "Assets/Prefabs/Doc_Urgent.prefab").
    ///
    /// Runtime path: converts to a Resources-relative path by stripping the "Assets/Resources/" prefix
    /// and the ".prefab" extension, then calls Resources.Load.
    /// Editor path: falls back to AssetDatabase.LoadAssetAtPath so the tool works in play-mode-in-editor
    /// without requiring prefabs to live under Resources.
    ///
    /// Returns null when the prefab cannot be found by either method.
    /// </summary>
    /// <param name="assetPath">Full Unity asset path starting with "Assets/".</param>
    /// <returns>The loaded prefab GameObject, or null if not found.</returns>
    private static GameObject LoadPrefabAtPath(string assetPath)
    {
        // ── Resources path (runtime + editor) ─────────────────────────────────
        const string resourcesPrefix = "Assets/Resources/";
        if (assetPath.StartsWith(resourcesPrefix))
        {
            // Strip prefix and extension to get the Resources-relative path.
            string resourcePath = assetPath.Substring(resourcesPrefix.Length);
            if (resourcePath.EndsWith(".prefab"))
                resourcePath = resourcePath.Substring(0, resourcePath.Length - ".prefab".Length);

            GameObject loaded = Resources.Load<GameObject>(resourcePath);
            if (loaded != null)
                return loaded;
        }

#if UNITY_EDITOR
        // ── Editor fallback via AssetDatabase ─────────────────────────────────
        // Allows play-mode testing without requiring prefabs under Resources.
        GameObject editorLoaded = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
        if (editorLoaded != null)
            return editorLoaded;
#endif

        return null;
    }

    // -------------------------------------------------------------------------
    // Document instantiation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Instantiates one document prefab chosen randomly from spawnablePrefabs,
    /// injects all scene dependencies and DocumentData, then hands it to DocumentStackManager.
    /// Called directly by onStackBelowTarget — fires once per missing document in the same frame.
    /// </summary>
    private void SpawnDocument()
    {
        if (spawnablePrefabs.Count == 0)
        {
            Debug.LogWarning("[DocumentSpawner] SpawnDocument called but spawn pool is empty.");
            return;
        }

        if (mainCanvas == null)
        {
            Debug.LogError("[DocumentSpawner] mainCanvas is not assigned. " +
                           "Drag the scene Canvas into the mainCanvas field on DocumentSpawner.");
            return;
        }

        if (dropZoneDetector == null)
        {
            Debug.LogError("[DocumentSpawner] dropZoneDetector is not assigned. " +
                           "Drag the scene DropZoneDetector into the dropZoneDetector field on DocumentSpawner.");
            return;
        }

        // Pick a random prefab from the pool.
        GameObject chosenPrefab = spawnablePrefabs[Random.Range(0, spawnablePrefabs.Count)];

        documentSpawnCounter++;
        DocumentData documentData = new DocumentData
        {
            documentID    = $"doc_{documentSpawnCounter:D4}",
            specificities = new System.Collections.Generic.List<string>()
        };

        GameObject documentInstance = Instantiate(chosenPrefab, spawnAreaParent);

        // Force sibling index 0 immediately after Instantiate so the document starts behind
        // everything for its very first frame (EnqueueDocument also sets it to 0 — this is the
        // first line of defence against a one-frame visual pop).
        documentInstance.transform.SetSiblingIndex(0);

        // Inject scene dependencies — the prefab asset cannot hold scene object references.
        DraggableDocument draggableDocument = documentInstance.GetComponent<DraggableDocument>();
        if (draggableDocument != null)
        {
            draggableDocument.SetDocumentData(documentData);
            draggableDocument.SetCanvasReference(mainCanvas);
            draggableDocument.SetDropZoneDetector(dropZoneDetector);
            draggableDocument.SetStackManager(documentStackManager);
            draggableDocument.SetAllActiveRules(activeRules);
        }
        else
        {
            Debug.LogError($"[DocumentSpawner] Spawned prefab '{chosenPrefab.name}' is missing a " +
                           $"DraggableDocument component. Attach DraggableDocument to the prefab.");
        }

        documentStackManager.EnqueueDocument(documentInstance);
    }

    /// <summary>
    /// Spawns documents until the stack count reaches targetStackSize.
    /// Called once at the start of each day because the stack is empty at that point.
    /// </summary>
    private void FillStackToTarget()
    {
        int missing = documentStackManager.targetStackSize
                      - documentStackManager.GetTotalDocumentCount();

        for (int i = 0; i < missing; i++)
            SpawnDocument();
    }
}
