using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that reads saved floor_N.json files from disk and displays them
/// as stacked floor blocks inside the Designer Tower Preview scene.
/// Allows the designer to visually verify the tower state before any player
/// has ever entered the game.
/// Does NOT generate new floors, does NOT modify save files, and does NOT
/// load GameScene under any circumstances — this is a read-only preview tool.
/// </summary>
public class DesignerTowerPreview : MonoBehaviour
{
    // ─── Inspector References ─────────────────────────────────────────────────

    /// <summary>Content RectTransform of the ScrollRect. All block instances are parented here.</summary>
    [SerializeField] private RectTransform towerContainer;

    /// <summary>ScrollRect used to navigate the tower vertically.</summary>
    [SerializeField] private ScrollRect towerScrollRect;

    /// <summary>
    /// Prefab for each floor block. Reuses FloorBlockPrefab from TowerScene
    /// to ensure consistent visual appearance between the designer preview and the live player tower.
    /// </summary>
    [SerializeField] private GameObject floorBlockPrefab;

    /// <summary>InfoPanel TextMeshProUGUI label updated when the designer taps a block.</summary>
    [SerializeField] private TextMeshProUGUI infoText;

    // ─── Layout Settings ──────────────────────────────────────────────────────

    /// <summary>Height in pixels of each floor block. Must match the prefab's sizeDelta.y.</summary>
    [SerializeField] private float blockHeight = 240f;

    /// <summary>Height in pixels reserved for the visual ground panel at the bottom of the tower.</summary>
    [SerializeField] private float groundHeight = 120f;

    // ─── Constants ────────────────────────────────────────────────────────────

    private const int MaxFloorIndex = 99;

    private const string NoFloorsSavedMessage =
        "No floors saved yet.\nUse Tools → Floor Designer to create and save floors.";

    private static readonly Color CompletedBlockColor  = new Color(34f / 255f, 139f / 255f, 34f / 255f, 1f);
    private static readonly Color UncompletedBlockColor = new Color(144f / 255f, 238f / 255f, 144f / 255f, 1f);

    // ─── Runtime State ────────────────────────────────────────────────────────

    /// <summary>
    /// Absolute path of the directory where floor JSON files are read from.
    /// Matches the path written by FloorDesignerSaveUtils so both tools share the same folder.
    /// </summary>
    private string saveFolderPath => Application.persistentDataPath + "/floors/";

    // ─── Unity Lifecycle ──────────────────────────────────────────────────────

    private void Start()
    {
        LoadAndDisplayFloors();
    }

    // ─── Public API ───────────────────────────────────────────────────────────

    /// <summary>
    /// Clears all block instances from the tower container, re-reads all floor_N.json
    /// files from disk, and rebuilds the visual tower bottom-to-top.
    /// If no files are found, displays a clear guidance message in the info panel.
    /// Does not throw on missing directory — handles it gracefully with a message.
    /// </summary>
    public void LoadAndDisplayFloors()
    {
        // Log the exact path being scanned so a path mismatch with FloorDesignerSaveUtils
        // is immediately visible in the Console — one wrong character silences all floors.
        Debug.Log("[DesignerTower] Looking for floors in: " + saveFolderPath);

        // Null guards: any unassigned Inspector field will cause a silent NullReferenceException
        // that swallows the rest of the method and leaves the tower empty with no error shown.
        if (floorBlockPrefab == null)
        {
            Debug.LogError("[DesignerTower] floorBlockPrefab is not assigned in Inspector");
            return;
        }

        if (towerContainer == null)
        {
            Debug.LogError("[DesignerTower] towerContainer is not assigned in Inspector");
            return;
        }

        ClearTowerContainer();

        List<FloorSaveData> loadedFloors = ReadAllFloorFiles();

        if (loadedFloors.Count == 0)
        {
            if (infoText != null)
                infoText.text = NoFloorsSavedMessage;
            return;
        }

        Debug.Log("[DesignerTower] Starting to spawn " + loadedFloors.Count + " blocks");

        for (int i = 0; i < loadedFloors.Count; i++)
        {
            Debug.Log("[DesignerTower] Spawning block for index: " + loadedFloors[i].floorIndex);
            SpawnFloorBlock(loadedFloors[i], i);
        }

        UpdateContainerHeight(loadedFloors.Count);

        // Force TowerContainer anchors at runtime so ScrollRect content is always bottom-anchored
        // with pivot at bottom-center. This ensures blocks stacked upward from Y=0 remain visible
        // inside the viewport regardless of any stale scene serialization state at startup.
        towerContainer.anchorMin        = new Vector2(0f, 0f);
        towerContainer.anchorMax        = new Vector2(1f, 0f);
        towerContainer.pivot            = new Vector2(0.5f, 0f);
        towerContainer.anchoredPosition = Vector2.zero;

        // POINT 2 — CanvasGroup alpha check.
        // A CanvasGroup with alpha = 0 anywhere in the parent hierarchy makes all children
        // invisible without disabling them — they exist in the Hierarchy but render nothing.
        CanvasGroup[] groups = towerContainer.GetComponentsInParent<CanvasGroup>();
        foreach (CanvasGroup g in groups)
        {
            Debug.Log("[DesignerTower] CanvasGroup: " + g.gameObject.name
                + " alpha: "           + g.alpha
                + " blocksRaycasts: "  + g.blocksRaycasts
                + " interactable: "    + g.interactable);
        }

        // POINT 3 — Viewport and container rect check.
        // If TowerViewport's Mask rect has height 0 or is mispositioned, it clips all content
        // inside it to nothing even if the blocks are correctly instantiated and positioned.
        if (towerScrollRect != null && towerScrollRect.viewport != null)
        {
            RectTransform viewportRT = towerScrollRect.viewport.GetComponent<RectTransform>();
            Debug.Log("[DesignerTower] Viewport rect: " + viewportRT.rect
                + " world pos: " + viewportRT.position);
            Debug.Log("[DesignerTower] Container rect: " + towerContainer.rect
                + " world pos: " + towerContainer.position);
        }

        ScrollToTop();
    }

    /// <summary>
    /// Called by the InfoPanel when the designer taps a floor block.
    /// Builds a human-readable summary of the block's saved parameters
    /// and displays it so the designer can verify the values match what
    /// was entered in the Floor Designer tool.
    /// </summary>
    /// <param name="floorData">The saved floor data associated with the tapped block.</param>
    public void OnBlockTapped(FloorSaveData floorData)
    {
        // Tapping a block shows its exact saved parameters so the designer can verify
        // values match what was entered in the Floor Designer tool without opening JSON manually.
        string info =
            "Floor " + (floorData.floorIndex + 1) + "\n" +
            "Fail Threshold: "  + floorData.failThreshold.ToString("F1")      + "%\n" +
            "Day Duration: "    + floorData.dayDuration.ToString("F1")         + "s\n" +
            "Decay Rate: "      + floorData.decayRatePerSecond.ToString("F3")  + "/s\n" +
            "Bins: "            + floorData.numberOfBins                        + "\n" +
            "Rules per bin: "   + floorData.rulesPerBin                         + "\n" +
            "Max Complexity: "  + floorData.maxRuleComplexity                   + "\n" +
            "Rules saved: "     + floorData.rules.Count;

        infoText.text = info;
    }

    /// <summary>
    /// Reloads the entire tower from disk without restarting the scene.
    /// Called by the Refresh button in the Header so the designer can save new floors
    /// in the Floor Designer Editor tool while this scene is open and immediately see them.
    /// </summary>
    public void OnRefreshButtonTapped()
    {
        // Designer may save new floors in the Editor tool while this scene is open;
        // refresh reloads without restarting the scene so the workflow stays uninterrupted.
        LoadAndDisplayFloors();
    }

    // ─── Private Helpers ──────────────────────────────────────────────────────

    /// <summary>
    /// Destroys all child GameObjects of towerContainer so the list can be rebuilt cleanly.
    /// </summary>
    private void ClearTowerContainer()
    {
        for (int i = towerContainer.childCount - 1; i >= 0; i--)
            Destroy(towerContainer.GetChild(i).gameObject);
    }

    /// <summary>
    /// Reads floor_0.json, floor_1.json, etc. from the save folder until the first missing index.
    /// Returns an empty list (never throws) when the folder does not exist or has no floor files.
    /// </summary>
    private List<FloorSaveData> ReadAllFloorFiles()
    {
        List<FloorSaveData> floors = new List<FloorSaveData>();

        if (!Directory.Exists(saveFolderPath))
            return floors;

        for (int i = 0; i <= MaxFloorIndex; i++)
        {
            string filePath = saveFolderPath + $"floor_{i}.json";

            if (!File.Exists(filePath))
                break;

            try
            {
                string json = File.ReadAllText(filePath);
                FloorSaveData data = JsonUtility.FromJson<FloorSaveData>(json);

                if (data != null)
                {
                    floors.Add(data);

                    // Log after deserialization to verify JsonUtility round-tripped the fields
                    // correctly. If floorIndex shows -1 or isCompleted is always false when it
                    // should not be, the JSON field names do not match FloorSaveData.
                    Debug.Log("[DesignerTower] Loaded floor " + data.floorIndex
                        + " completed: " + data.isCompleted);
                }
            }
            catch (Exception exception)
            {
                Debug.LogError($"[DesignerTowerPreview] Failed to read floor_{i}.json: {exception.Message}");
                break;
            }
        }

        // Log count immediately after scanning so a zero here confirms the path is correct
        // but no files were written, distinguishing a path bug from a save bug.
        Debug.Log("[DesignerTower] Floor files found: " + floors.Count);

        return floors;
    }

    /// <summary>
    /// Instantiates one floor block prefab, positions it at the correct Y offset,
    /// applies a color based on isCompleted, sets the label, and wires the tap callback.
    /// </summary>
    /// <param name="floorData">Save data for this block's floor.</param>
    /// <param name="visualIndex">Zero-based position in the visual stack (bottom = 0).</param>
    private void SpawnFloorBlock(FloorSaveData floorData, int visualIndex)
    {
        Debug.Log("[DesignerTower] SpawnFloorBlock called for floor: " + floorData.floorIndex);

        GameObject blockObject = Instantiate(floorBlockPrefab, towerContainer);

        // Setting maskable = false on all Image components in the block guarantees they are never
        // clipped by any parent Mask, regardless of prefab default settings or Unity version behavior.
        Image[] images = blockObject.GetComponentsInChildren<Image>(true);
        foreach (Image img in images)
            img.maskable = false;

        // Text components are also affected by Mask and must be explicitly excluded.
        TextMeshProUGUI[] texts = blockObject.GetComponentsInChildren<TextMeshProUGUI>(true);
        foreach (TextMeshProUGUI t in texts)
            t.maskable = false;

        Debug.Log("[DesignerTower] Block instantiated: " + blockObject.name
            + " parent: " + blockObject.transform.parent.name);

        RectTransform blockRect = blockObject.GetComponent<RectTransform>();

        // Anchor horizontally (0→1) and pin to the bottom of the container (anchorMax.y = 0).
        // This makes the block stretch edge-to-edge horizontally and stack upward from Y = 0,
        // matching TowerContainer which is itself bottom-anchored inside TowerViewport.
        blockRect.anchorMin = new Vector2(0f, 0f);
        blockRect.anchorMax = new Vector2(1f, 0f);

        // Pivot at bottom-center means anchoredPosition.y = 120 places the block's bottom edge
        // 120 px above the container's bottom edge, directly on top of the ground panel.
        blockRect.pivot = new Vector2(0.5f, 0f);

        // -120 on X = 60 px margin on each side so the block does not bleed to the screen edge.
        // 240 on Y = the fixed block height; must match blockHeight field.
        blockRect.sizeDelta = new Vector2(-120f, 240f);

        // Set anchoredPosition AFTER anchors and pivot are locked. Unity recalculates world
        // position when anchors change, so setting position first and then changing anchors
        // shifts the block to an unintended location.
        blockRect.anchoredPosition = new Vector2(0f, groundHeight + visualIndex * blockHeight);

        Debug.Log("[DesignerTower] Block position: " + blockRect.anchoredPosition
            + " sizeDelta: " + blockRect.sizeDelta);

        // Initialize label and internal state first. ApplyBlockColor() inside Initialize()
        // will set a color, but we override it afterward to guarantee visibility.
        FloorBlock floorBlock = blockObject.GetComponent<FloorBlock>();
        if (floorBlock != null)
        {
            // isCurrent = false: all blocks in the preview show their saved state only.
            floorBlock.Initialize(floorData.floorIndex, floorData.isCompleted, current: false);
        }

        // In the preview all blocks must be fully visible and tappable regardless of
        // completion state. Force the Button to Normal (interactable, not locked) so its
        // ColorTint transition does not apply the DisabledColor (alpha 0.5) which would
        // make the block semi-transparent even after the color is forced below.
        Button button = blockObject.GetComponent<Button>();
        if (button != null)
            button.interactable = true;

        // Force opaque green AFTER Initialize() and AFTER forcing interactable=true.
        // Button.interactable = false triggers a ColorTint fade to DisabledColor which
        // multiplies the Image color by (r:0.78, g:0.78, b:0.78, a:0.5) — overwriting any
        // color set before that transition completes. Setting color last guarantees it wins.
        Image blockImage = blockObject.GetComponent<Image>();
        if (blockImage != null)
        {
            blockImage.color = new Color(0.2f, 0.6f, 0.2f, 1f);
            blockImage.enabled = true;
        }

        Debug.Log("[DesignerTower] Image color at runtime: "
            + (blockImage != null ? blockImage.color.ToString() : "NULL")
            + " interactable: " + (button != null ? button.interactable.ToString() : "NULL"));

        // Wire tap listener AFTER Initialize() because Initialize() calls RemoveListener,
        // which would strip any listener added before it.
        Button tapButton = blockObject.GetComponentInChildren<Button>();
        if (tapButton != null)
        {
            FloorSaveData capturedData = floorData;
            tapButton.onClick.AddListener(() => OnBlockTapped(capturedData));
        }
    }

    /// <summary>
    /// Sets towerContainer.sizeDelta.y so ScrollRect can compute its full scrollable range.
    /// Without an explicit height, vertical scrolling will not function after adding blocks.
    /// </summary>
    private void UpdateContainerHeight(int floorCount)
    {
        float requiredHeight = groundHeight + floorCount * blockHeight;
        towerContainer.sizeDelta = new Vector2(towerContainer.sizeDelta.x, requiredHeight);
        Debug.Log("[DesignerTower] Container height set to: " + towerContainer.sizeDelta.y
            + " for " + floorCount + " floors");
    }

    /// <summary>
    /// Scrolls the ScrollRect to the bottom of the tower (verticalNormalizedPosition = 0)
    /// so the designer sees Floor 1 — the lowest block — immediately on load.
    /// In Unity's ScrollRect, 0 = bottom, 1 = top; floors stack upward from the ground,
    /// so the first floor lives at the bottom of the content container.
    /// Canvas.ForceUpdateCanvases() is called first to guarantee the layout system has
    /// calculated all RectTransform sizes before the normalized position is set — setting
    /// it before layout runs produces an incorrect value because content height is still 0.
    /// </summary>
    private void ScrollToTop()
    {
        Canvas.ForceUpdateCanvases();
        towerScrollRect.verticalNormalizedPosition = 0f;
        Debug.Log("[DesignerTower] Scroll position set to: "
            + towerScrollRect.verticalNormalizedPosition
            + " (0 = bottom, block is at bottom of container)");
    }
}
