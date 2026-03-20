using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that represents one floor block in the tower UI.
/// Handles visual state (completed / current / locked) and fires an event when the player taps the block.
/// Does NOT load save data, does NOT manage scrolling, and does NOT trigger scene transitions directly.
/// </summary>
public class FloorBlock : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned UI references
    // -------------------------------------------------------------------------

    /// <summary>Text label that displays the floor number to the player (e.g. "Floor 3").</summary>
    [SerializeField] private TextMeshProUGUI floorLabel;

    /// <summary>Image component whose color reflects the block's current state.</summary>
    [SerializeField] private Image blockImage;

    /// <summary>Button the player taps to select and enter this floor.</summary>
    [SerializeField] private Button enterButton;

    // -------------------------------------------------------------------------
    // Block color configuration
    // -------------------------------------------------------------------------

    [Header("Block Colors")]

    /// <summary>
    /// Color applied when the floor has been fully completed.
    /// Strong dark green gives an unambiguous "done" signal against any background.
    /// </summary>
    [SerializeField] private Color completedColor = new Color(0.13f, 0.55f, 0.13f, 1f);

    /// <summary>
    /// Color applied to the floor the player is currently on but has not yet completed.
    /// Light green contrasts clearly with the darker completed blocks above and below it.
    /// </summary>
    [SerializeField] private Color currentColor = new Color(0.56f, 0.93f, 0.56f, 1f);

    /// <summary>
    /// Color applied to floors beyond the player's current progress.
    /// Mid-gray is the conventional disabled-state color in mobile UI — immediately recognisable.
    /// </summary>
    [SerializeField] private Color lockedColor = new Color(0.4f, 0.4f, 0.4f, 1f);

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Zero-based index of the floor this block represents.</summary>
    private int floorIndex;

    /// <summary>True when this floor has been fully completed by the player.</summary>
    private bool isCompleted;

    /// <summary>True when this floor is the one currently available for play.</summary>
    private bool isCurrent;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired when the player taps the enter button.
    /// Passes the zero-based floor index to TowerManager, which decides how to transition to GameScene.
    /// Using an event keeps FloorBlock completely decoupled from scene management.
    /// </summary>
    public event Action<int> OnFloorSelected;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Configures this block with its identity and visual state.
    /// Sets the label text, applies the correct color, and enables or disables the enter button.
    /// Must be called immediately after instantiation, before the block becomes visible.
    /// </summary>
    /// <param name="index">Zero-based floor index this block represents.</param>
    /// <param name="completed">True when the floor has been fully completed.</param>
    /// <param name="current">True when this is the floor currently available for play.</param>
    public void Initialize(int index, bool completed, bool current)
    {
        floorIndex  = index;
        isCompleted = completed;
        isCurrent   = current;

        // Null-check every serialized reference up front. After a prefab restructure the
        // Inspector fields that pointed to deleted or moved child GameObjects become null.
        // Logging which field is null here identifies exactly which reference needs rewiring
        // in the Inspector rather than showing a generic NullReferenceException at line 93.
        if (blockImage == null)
            Debug.LogError("[FloorBlock] blockImage is null — check Inspector reference on FloorBlockPrefab");

        if (enterButton == null)
            Debug.LogError("[FloorBlock] enterButton is null — check Inspector reference on FloorBlockPrefab");

        if (floorLabel == null)
            Debug.LogError("[FloorBlock] floorLabel is null — check Inspector reference on FloorBlockPrefab");

        // Display floor number as 1-based to the player — zero-based indices are an implementation detail.
        if (floorLabel != null)
            floorLabel.text = "Floor " + (index + 1);

        ApplyBlockColor();

        // Locked floors (future floors beyond current progress) must not be interactive —
        // entering a floor the player has not reached would bypass the progression system entirely.
        if (enterButton != null)
            enterButton.interactable = isCompleted || isCurrent;

        // RemoveAllListeners first to clear any stale wiring from a previous Initialize call
        // on a recycled object, then add the single authoritative listener.
        // RemoveAllListeners (not RemoveListener) is used to guarantee no duplicate callbacks
        // survive even if the object was initialized by multiple callers.
        if (enterButton != null)
        {
            enterButton.onClick.RemoveAllListeners();
            enterButton.onClick.AddListener(OnBlockTapped);
            Debug.Log("[FloorBlock] Adding onClick listener for floor: " + floorIndex);
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets blockImage.color based on the block's state: completed, current, or locked.
    /// Centralised here so color logic never leaks into Initialize.
    /// </summary>
    private void ApplyBlockColor()
    {
        // Guard against a missing Inspector reference — a null blockImage throws silently inside
        // Initialize() and leaves the block invisible with no error shown in the Console.
        if (blockImage == null)
        {
            Debug.LogError("[FloorBlock] blockImage is null on floor " + floorIndex);
            return;
        }

        if (isCompleted)
            blockImage.color = completedColor;
        else if (isCurrent)
            blockImage.color = currentColor;
        else
            // Neither completed nor current — this floor is locked and not yet reachable.
            blockImage.color = lockedColor;

        Debug.Log("[FloorBlock] Color set to: " + blockImage.color + " for floor: " + floorIndex);
    }

    /// <summary>
    /// Responds to a tap on the enter button by firing OnFloorSelected with this block's floor index.
    /// TowerManager subscribes to this event to handle scene transition logic.
    /// Debug log kept intentionally to confirm tap detection during button-fix verification.
    /// </summary>
    private void OnBlockTapped()
    {
        // If this log never appears, the Button onClick is not connected to OnBlockTapped.
        Debug.Log("[FloorBlock] OnBlockTapped called for floor: " + floorIndex);

        OnFloorSelected?.Invoke(floorIndex);
    }
}
