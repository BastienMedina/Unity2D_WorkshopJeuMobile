using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that builds and displays the floor tower at runtime, manages vertical scrolling,
/// responds to floor block taps, and triggers the transition to GameScene.
/// Does NOT save or load floor data directly, does NOT run game logic, and does NOT validate rules.
/// </summary>
public class TowerManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>Save system used to read floor metadata at tower build time.</summary>
    [SerializeField] private FloorSaveSystem floorSaveSystem;

    /// <summary>
    /// Computes floor parameters from the inheritance chain when the floor has no saved data.
    /// Used in OnFloorSelected to always hand GameScene a fully populated FloorSaveData,
    /// whether the floor was previously played or is being started for the first time.
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
    // Scene names
    // -------------------------------------------------------------------------

    [SerializeField] private string gameSceneName = "GameScene";

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
    /// Instantiates all floor blocks based on saved data and places them at the correct positions.
    /// Reads the highest saved floor index to know how many blocks to create, then spawns one
    /// extra block for the current (not yet completed) floor.
    /// </summary>
    public void LoadAndBuildTower()
    {
        int highestSavedIndex = floorSaveSystem.GetHighestSavedFloorIndex();

        // Total block count = all saved floors + the current incomplete floor on top.
        int totalBlockCount = highestSavedIndex + 2;

        for (int blockIndex = 0; blockIndex < totalBlockCount; blockIndex++)
        {
            FloorSaveData saveData  = floorSaveSystem.LoadFloor(blockIndex);

            bool isCompleted = floorSaveSystem.FloorExists(blockIndex)
                               && saveData != null
                               && saveData.isCompleted;

            // The topmost block is the current floor only when the saved block at highestSavedIndex
            // is already completed — that means the player advanced beyond it and is now on the next one.
            bool isCurrent = blockIndex == highestSavedIndex + 1
                             || (blockIndex == highestSavedIndex && !isCompleted);

            if (isCurrent)
                currentFloorIndex = blockIndex;

            SpawnBlock(blockIndex, isCompleted, isCurrent);
        }

        UpdateContainerHeight(totalBlockCount);
    }

    /// <summary>
    /// Instantiates a new floor block for the given index and adds it to the tower.
    /// Called by GameManager after a floor is completed so the tower grows without a full scene reload.
    /// </summary>
    /// <param name="newFloorIndex">Zero-based index of the new floor block to add.</param>
    public void AddNewFloorBlock(int newFloorIndex)
    {
        // The new block starts as current (not yet completed).
        SpawnBlock(newFloorIndex, completed: false, current: true);

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
    private void SpawnBlock(int blockIndex, bool completed, bool current)
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
    /// Loads or generates the floor's data, stores it in FloorSessionData for GameScene to read,
    /// and transitions to GameScene.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the tapped floor block.</param>
    private void OnFloorSelected(int floorIndex)
    {
        // Log here to verify the event subscription is active.
        // If FloorBlock logs its tap but this line does not appear, the += subscription
        // in SpawnBlock() was not reached (e.g. early return before it).
        Debug.Log("[TowerManager] Floor selected: " + floorIndex);

        // Use GetOrGenerateFloorData so tapping any block — saved or not — always produces
        // a valid FloorSaveData with correct inherited parameters for GameScene to consume.
        FloorSaveData selectedFloorData = floorDifficultyProgression.GetOrGenerateFloorData(
            floorIndex, floorSaveSystem);

        // A floor is a replay only when a completed save file actually exists on disk.
        // A newly computed (unsaved) floor must be treated as a fresh start so rules are
        // generated at runtime — not restored from data that does not exist yet.
        bool isReplay = floorSaveSystem.FloorExists(floorIndex)
                        && selectedFloorData != null
                        && selectedFloorData.isCompleted;

        FloorSessionData.SelectedFloor    = selectedFloorData;
        FloorSessionData.IsReplayingFloor = isReplay;

        SceneManager.LoadScene(gameSceneName);
    }
}
