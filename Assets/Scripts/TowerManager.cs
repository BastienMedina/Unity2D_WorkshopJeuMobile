using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that builds and displays the floor tower at runtime, manages vertical scrolling,
/// responds to floor block taps, and triggers the transition to GameScene.
/// Operates exclusively in procedural mode: floor parameters are generated at runtime by
/// FloorDifficultyProgression and progress is tracked in PlayerPrefs only.
/// Does NOT read or write floor_N.json files — JSON persistence belongs to DesignerTowerScene.
/// </summary>
public class TowerManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes floor parameters from base values and per-floor deltas.
    /// Passed null for saveSystem on every call — procedural mode never reads from disk.
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
    /// Manages the infinite tower background tiling.
    /// Notified whenever the container height changes so it can resize and re-tile.
    /// </summary>
    [SerializeField] private TowerScrollView towerScrollView;

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
        LoadAndBuildTower();
        ScrollToCurrentFloor();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reads procedural progress from PlayerPrefs and instantiates one floor block per reached floor,
    /// plus one extra block for the current (not yet completed) floor on top.
    /// On first launch (no PlayerPrefs key) the player sees exactly one block — Floor 1.
    /// Never reads floor_N.json files — procedural progress lives in PlayerPrefs only.
    /// </summary>
    public void LoadAndBuildTower()
    {
        // PlayerPrefs stores the zero-based index of the highest floor the player has
        // fully completed. Defaults to 0 on first launch so the player sees one block.
        // This key is written exclusively by AddNewFloorBlock() and is completely isolated
        // from the JSON saves used by DesignerTowerScene.
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
    }

    /// <summary>
    /// Instantiates a new floor block for the given index and adds it to the tower.
    /// Saves the new highest reached floor to PlayerPrefs so progress survives app restarts.
    /// Called by GameManager after a floor is completed so the tower grows without a full scene reload.
    /// Never writes floor_N.json — procedural progress uses PlayerPrefs exclusively.
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
    /// Generates the floor's data procedurally (no disk read), stores it in FloorSessionData
    /// for GameScene to read, and transitions to GameScene.
    /// IsReplayingFloor is always false — procedural floors are never exact replays.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the tapped floor block.</param>
    private void OnFloorSelected(int floorIndex)
    {
        Debug.Log("[TowerManager] Floor selected: " + floorIndex);

        // Generate data procedurally — null saveSystem tells FloorDifficultyProgression
        // to skip all file-loading and use the inheritance chain exclusively.
        FloorSaveData selectedFloorData = floorDifficultyProgression.GetOrGenerateFloorData(
            floorIndex, null);

        // Procedural floors are never replays — rules are always generated fresh at runtime.
        // Setting IsReplayingFloor = false ensures GameManager calls InitializeFromFloorData()
        // rather than RestoreFloorFromSave(), which would look for saved rules that do not exist.
        FloorSessionData.SelectedFloor    = selectedFloorData;
        FloorSessionData.IsReplayingFloor = false;

        SceneManager.LoadScene(gameSceneName);
    }
}
