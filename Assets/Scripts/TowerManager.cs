using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that builds and displays the floor tower at runtime, manages vertical scrolling,
/// responds to floor block taps, and triggers the transition to GameScene.
///
/// Supports two modes depending on whether a FloorSaveSystem is assigned:
/// - Procedural mode (no FloorSaveSystem): parameters are generated at runtime by
///   FloorDifficultyProgression and progress is tracked in PlayerPrefs only.
/// - Designer mode (FloorSaveSystem assigned): floor parameters are loaded from floor_N.json
///   files authored in the Floor Designer tool. Only floors with a save file are selectable.
///
/// Both modes write FloorSessionData before loading GameScene so GameManager always receives
/// the same interface regardless of which mode produced the data.
/// </summary>
public class TowerManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes floor parameters from base values and per-floor deltas.
    /// In procedural mode, GetOrGenerateFloorData is called with a null saveSystem.
    /// In designer mode, GetOrGenerateFloorData is called with the live FloorSaveSystem.
    /// </summary>
    [SerializeField] private FloorDifficultyProgression floorDifficultyProgression;

    /// <summary>Prefab instantiated for each floor block. Must carry a FloorBlock component.</summary>
    [SerializeField] private GameObject floorBlockPrefab;

    /// <summary>
    /// Content RectTransform of the ScrollRect. Blocks are instantiated as children of this transform,
    /// and its height is updated every time a block is added so ScrollRect can scroll correctly.
    /// </summary>
    [SerializeField] private RectTransform towerContainer;

    /// <summary>ScrollRect component that provides vertical scrolling for the tower.</summary>
    [SerializeField] private ScrollRect towerScrollRect;

    /// <summary>
    /// Reference to TransitionManager so the RDC button can trigger PlayTransitionToMainMenu().
    /// Assign the MenuController GameObject in the Inspector.
    /// </summary>
    [SerializeField] private TransitionManager transitionManager;

    /// <summary>
    /// Manages the infinite tower background tiling.
    /// Notified whenever the container height changes so it can resize and re-tile.
    /// </summary>
    [SerializeField] private TowerScrollView towerScrollView;

    /// <summary>
    /// Optional reference to the FloorSaveSystem singleton.
    /// When assigned, the tower operates in designer mode and only renders floors that have a
    /// corresponding floor_N.json file. When null, the tower operates in procedural mode.
    /// Assign the FloorSaveSystem GameObject present in the Menu_Principal scene.
    /// </summary>
    [SerializeField] private FloorSaveSystem floorSaveSystem;

    // -------------------------------------------------------------------------
    // Layout constants — serialised so designers can tune without touching code
    // -------------------------------------------------------------------------

    /// <summary>Height in pixels of each floor block. Must match the prefab's sizeDelta.y.</summary>
    [SerializeField] private float blockHeight = 240f;

    /// <summary>
    /// Vertical gap in pixels between consecutive blocks.
    /// Set to 0 so blocks sit flush against each other like physical stacked tiles.
    /// Any gap would break the tower aesthetic — stacked building blocks have no space between them.
    /// </summary>
    [SerializeField] private float blockSpacing = 0f;

    /// <summary>
    /// Height in pixels reserved for the visual ground panel at the bottom of the tower.
    /// The lowest block (index 0) sits on top of this ground area — blocks stack upward from it.
    /// </summary>
    [SerializeField] private float groundHeight = 120f;

    // -------------------------------------------------------------------------
    // Debug / testing
    // -------------------------------------------------------------------------

    /// <summary>
    /// When greater than zero, overrides the floor count derived from PlayerPrefs so
    /// all blocks (including locked ones) are displayed regardless of player progress.
    /// Set to 0 in production builds to restore normal progression behaviour.
    /// </summary>
    [Header("Debug")]
    [SerializeField] private int debugFloorCount = 0;

    [SerializeField] private string gameSceneName = "GameScene";

    // -------------------------------------------------------------------------
    // PlayerPrefs keys — centralised constants prevent typo-driven key mismatches
    // -------------------------------------------------------------------------

    /// <summary>
    /// PlayerPrefs key that stores how many floors the player has completed in procedural mode.
    /// Kept separate from any JSON keys used by story mode so the two systems never collide.
    /// </summary>
    private const string ProceduralHighestFloorKey = "ProceduralHighestFloor";

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>All FloorBlock instances instantiated during the current tower build.</summary>
    private List<FloorBlock> spawnedBlocks = new List<FloorBlock>();

    /// <summary>Zero-based index of the floor the player is currently on.</summary>
    private int currentFloorIndex;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        // Fallback: if no FloorSaveSystem was assigned in Inspector, try the singleton.
        // This handles the case where FloorSaveSystemBootstrap created it via DontDestroyOnLoad
        // from a previous scene load, meaning it won't be in the hierarchy at edit time.
        if (floorSaveSystem == null)
            floorSaveSystem = FloorSaveSystem.Instance;

        LoadAndBuildTower();
        ScrollToCurrentFloor();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the tower based on the active mode:
    /// - Designer mode (floorSaveSystem assigned): reads all floor_N.json files from disk
    ///   and displays one block per saved floor. Blocks are non-interactive if no save exists.
    /// - Procedural mode (floorSaveSystem null): reads progress from PlayerPrefs and generates
    ///   blocks procedurally, one per reached floor plus one for the current floor on top.
    /// </summary>
    public void LoadAndBuildTower()
    {
        if (floorSaveSystem != null)
            BuildDesignerTower();
        else
            BuildProceduralTower();
    }

    /// <summary>
    /// Instantiates a new floor block for the given index and adds it to the tower.
    /// Saves the new highest reached floor to PlayerPrefs so progress survives app restarts.
    /// Called by GameManager after a floor is completed so the tower grows without a full scene reload.
    /// Only valid in procedural mode — in designer mode, the tower is defined by saved files.
    /// </summary>
    /// <param name="newFloorIndex">Zero-based index of the new floor block to add.</param>
    public void AddNewFloorBlock(int newFloorIndex)
    {
        // Persist the new highest reached floor index before spawning the block.
        // PlayerPrefs.Save() flushes immediately so progress is not lost on a crash or force-quit.
        PlayerPrefs.SetInt(ProceduralHighestFloorKey, newFloorIndex);
        PlayerPrefs.Save();

        FloorSaveData data = floorDifficultyProgression.GetOrGenerateFloorData(newFloorIndex, null);

        // The new block starts as current (not yet completed).
        SpawnBlock(newFloorIndex, completed: false, current: true, data);

        currentFloorIndex = newFloorIndex;

        UpdateContainerHeight(spawnedBlocks.Count);
    }

    // -------------------------------------------------------------------------
    // Private helpers — building
    // -------------------------------------------------------------------------

    /// <summary>
    /// Designer mode: scans all floor_N.json files and renders one block per saved floor.
    /// Blocks whose save file exists are interactive; all others are locked.
    /// The highest saved floor index determines how many blocks to show.
    /// </summary>
    private void BuildDesignerTower()
    {
        int highestSaved = floorSaveSystem.GetHighestSavedFloorIndex();

        // Always show at least one block (floor 0) so the UI is never empty.
        int totalBlockCount = debugFloorCount > 0 ? debugFloorCount : Mathf.Max(highestSaved + 1, 1);

        for (int i = 0; i < totalBlockCount; i++)
        {
            bool hasSave    = floorSaveSystem.FloorExists(i);
            FloorSaveData data = hasSave
                ? floorSaveSystem.LoadFloor(i)
                : floorDifficultyProgression.GetOrGenerateFloorData(i, null);

            bool isCompleted = hasSave && data != null && data.isCompleted;
            bool isCurrent   = hasSave && !isCompleted;

            if (isCurrent)
                currentFloorIndex = i;

            SpawnBlock(i, isCompleted, isCurrent, data);
        }

        UpdateContainerHeight(totalBlockCount);
        SpawnRdcButton(totalBlockCount);
    }

    /// <summary>
    /// Procedural mode: reads highest reached floor from PlayerPrefs and builds the tower.
    /// Parameters are generated at runtime — no disk reads.
    /// </summary>
    private void BuildProceduralTower()
    {
        int highestFloorReached = PlayerPrefs.GetInt(ProceduralHighestFloorKey, 0);

        // debugFloorCount > 0 overrides progression for testing: shows all blocks regardless of progress.
        int totalBlockCount = debugFloorCount > 0 ? debugFloorCount : highestFloorReached + 1;

        for (int i = 0; i < totalBlockCount; i++)
        {
            // Generate floor data procedurally — pass null for saveSystem so
            // FloorDifficultyProgression skips all file-loading logic entirely.
            FloorSaveData data = floorDifficultyProgression.GetOrGenerateFloorData(i, null);

            bool isCompleted = i < highestFloorReached;
            bool isCurrent   = i == highestFloorReached;

            if (isCurrent)
                currentFloorIndex = i;

            SpawnBlock(i, isCompleted, isCurrent, data);
        }

        UpdateContainerHeight(totalBlockCount);
        SpawnRdcButton(totalBlockCount);
    }

    /// <summary>
    /// Instantiates one FloorBlock prefab, positions it, initialises it, and wires its event.
    /// </summary>
    /// <param name="blockIndex">Zero-based floor index for this block.</param>
    /// <param name="completed">True when the floor has been fully completed.</param>
    /// <param name="current">True when this is the active floor.</param>
    /// <param name="data">Pre-generated floor data used for display and tap handoff.</param>
    private void SpawnBlock(int blockIndex, bool completed, bool current, FloorSaveData data)
    {
        GameObject blockObject = Instantiate(floorBlockPrefab, towerContainer);
        FloorBlock block       = blockObject.GetComponent<FloorBlock>();

        if (block == null)
        {
            Debug.LogError("[TowerManager] floorBlockPrefab is missing a FloorBlock component.");
            return;
        }

        block.Initialize(blockIndex, completed, current);
        block.OnFloorSelected += OnFloorSelected;

        // Blocks stack upward from the ground: index 0 sits at groundHeight, each subsequent
        // block is placed one step higher. This keeps the visual order consistent with the
        // metaphor of climbing floors in a building.
        RectTransform blockRect = blockObject.GetComponent<RectTransform>();
        float yPosition = groundHeight + blockIndex * (blockHeight + blockSpacing);
        blockRect.anchoredPosition = new Vector2(0f, yPosition);

        spawnedBlocks.Add(block);
    }

    /// <summary>
    /// Spawns an RDC block at position y = 0 (below all floor blocks) using the same prefab.
    /// Tapping it calls TransitionManager.PlayTransitionToMainMenu().
    /// </summary>
    /// <param name="totalFloorCount">Used only for container height — RDC is placed at y = 0.</param>
    private void SpawnRdcButton(int totalFloorCount)
    {
        if (floorBlockPrefab == null) return;

        GameObject rdcObject = Instantiate(floorBlockPrefab, towerContainer);
        FloorBlock rdcBlock  = rdcObject.GetComponent<FloorBlock>();

        if (rdcBlock == null)
        {
            Debug.LogError("[TowerManager] floorBlockPrefab is missing a FloorBlock component (RDC).");
            return;
        }

        rdcBlock.InitializeAsRDC(() =>
        {
            if (transitionManager != null)
                transitionManager.PlayTransitionToMainMenu();
            else
                Debug.LogWarning("[TowerManager] TransitionManager not assigned — RDC button has no effect.");
        });

        // RDC sits at the very bottom, below floor index 0.
        RectTransform rdcRect = rdcObject.GetComponent<RectTransform>();
        rdcRect.anchoredPosition = new Vector2(0f, 0f);
    }

    /// <summary>
    /// Recalculates and sets the towerContainer's sizeDelta.y so ScrollRect can scroll correctly.
    /// ScrollRect requires the content RectTransform to have an explicit height equal to the full
    /// content size — without this update, vertical scrolling will not work after adding blocks.
    /// </summary>
    /// <param name="totalBlocks">Current total number of blocks in the tower.</param>
    private void UpdateContainerHeight(int totalBlocks)
    {
        float requiredHeight = groundHeight + totalBlocks * (blockHeight + blockSpacing);
        towerContainer.sizeDelta = new Vector2(towerContainer.sizeDelta.x, requiredHeight);

        // Notify the background so it stretches and re-tiles to match the new content height.
        if (towerScrollView != null)
            towerScrollView.RefreshBackground(requiredHeight);
    }

    // -------------------------------------------------------------------------
    // Private helpers — scrolling
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scrolls the ScrollRect so the current floor block is visible when TowerScene opens.
    /// Converts the block's position into a normalised vertical scroll value (0 = bottom, 1 = top).
    /// </summary>
    private void ScrollToCurrentFloor()
    {
        float contentHeight = towerContainer.rect.height;

        // Guard: avoid divide-by-zero if the container height has not been set yet.
        if (contentHeight <= 0f)
            return;

        float blockBottomY = currentFloorIndex * (blockHeight + blockSpacing);

        // Normalise the block position relative to the full content height so ScrollRect
        // can position the viewport correctly regardless of screen resolution.
        float normalizedY = blockBottomY / contentHeight;

        towerScrollRect.verticalNormalizedPosition = Mathf.Clamp01(normalizedY);
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called when the player taps a floor block.
    ///
    /// Designer mode: loads the exact FloorSaveData from disk so GameManager reproduces
    /// the designer-authored rule set for every night of this floor.
    ///
    /// Procedural mode: generates data at runtime via FloorDifficultyProgression.
    ///
    /// In both cases, FloorSessionData is populated and GameScene is loaded.
    /// IsReplayingFloor is always false — the player always starts fresh from night 1.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the tapped floor block.</param>
    private void OnFloorSelected(int floorIndex)
    {
        Debug.Log("[TowerManager] Floor selected: " + floorIndex);

        FloorSaveData selectedFloorData;

        if (floorSaveSystem != null && floorSaveSystem.FloorExists(floorIndex))
        {
            // Designer mode — load the exact save authored in the Floor Designer.
            // GameManager will read BinSaveData from each night to reconstruct the
            // designer-authored rule set via RestoreRulesFromNight().
            selectedFloorData = floorSaveSystem.LoadFloor(floorIndex);

            Debug.Log($"[TowerManager] Designer floor {floorIndex} loaded from disk " +
                      $"({selectedFloorData?.nights?.Count ?? 0} nights).");
        }
        else
        {
            // Procedural mode — generate data entirely at runtime.
            // Passing null tells FloorDifficultyProgression to skip file-loading.
            selectedFloorData = floorDifficultyProgression.GetOrGenerateFloorData(
                floorIndex, null);

            Debug.Log($"[TowerManager] Procedural floor {floorIndex} generated.");
        }

        // IsReplayingFloor = false → GameManager calls InitializeFromFloorData() / InitializeDay(),
        // which applies per-night BinSaveData rules when present or falls back to procedural generation.
        FloorSessionData.SelectedFloor    = selectedFloorData;
        FloorSessionData.IsReplayingFloor = false;

        SceneManager.LoadScene(gameSceneName);
    }
}

