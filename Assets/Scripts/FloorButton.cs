using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Represents a single floor entry in the tower selection list.
/// Initialised by FloorButtonSpawner immediately after instantiation.
/// When clicked, loads the floor's saved parameters into FloorSessionData and transitions to GameScene.
/// </summary>
[RequireComponent(typeof(Button))]
public class FloorButton : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string GameSceneName = "GameScene";

    // -------------------------------------------------------------------------
    // Inspector-configurable fields
    // -------------------------------------------------------------------------

    /// <summary>Label displaying the floor number, e.g. "ETAGE 1".</summary>
    [SerializeField] private TextMeshProUGUI floorLabel;

    /// <summary>
    /// Optional overlay shown when the floor has no save file and is locked.
    /// Assign a darkening panel or lock icon GameObject here in the prefab.
    /// </summary>
    [SerializeField] private GameObject lockedOverlay;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    /// <summary>1-based floor number displayed to the player.</summary>
    private int _floorNumber;

    /// <summary>Zero-based floor index used for save file lookup (floorNumber - 1).</summary>
    private int _floorIndex;

    private Button _button;
    private FloorSaveSystem _saveSystem;
    private bool _isLocked;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the floor number, resolves its save state, and configures the button accordingly.
    /// Must be called by FloorButtonSpawner right after Instantiate.
    /// </summary>
    /// <param name="floorNumber">1-based floor number shown in the label.</param>
    /// <param name="saveSystem">The FloorSaveSystem used to check whether a save exists on disk.</param>
    public void Initialise(int floorNumber, FloorSaveSystem saveSystem)
    {
        _floorNumber = floorNumber;
        _floorIndex  = floorNumber - 1;
        _saveSystem  = saveSystem;
        _button      = GetComponent<Button>();

        bool hasSave = _saveSystem != null && _saveSystem.FloorExists(_floorIndex);
        _isLocked    = !hasSave;

        // Update label text.
        if (floorLabel != null)
        {
            floorLabel.enableAutoSizing = true;
            floorLabel.fontSizeMin      = 12f;
            floorLabel.fontSizeMax      = 200f;
            floorLabel.text             = $"ETAGE {floorNumber}";
        }

        // Show the locked overlay and disable interaction for floors without a save.
        if (lockedOverlay != null)
            lockedOverlay.SetActive(_isLocked);

        _button.interactable = !_isLocked;
        _button.onClick.AddListener(OnClicked);
    }

    // -------------------------------------------------------------------------
    // Button callback
    // -------------------------------------------------------------------------

    /// <summary>
    /// Loads the floor's FloorSaveData from disk, writes it into FloorSessionData,
    /// and transitions to GameScene. The floor runs fresh (not a replay) so rules
    /// are derived from the per-night BinSaveData authored in the Floor Designer.
    /// </summary>
    private void OnClicked()
    {
        if (_isLocked || _saveSystem == null)
        {
            Debug.LogWarning($"[FloorButton] Floor {_floorNumber} is locked or save system is missing.");
            return;
        }

        FloorSaveData floorData = _saveSystem.LoadFloor(_floorIndex);

        if (floorData == null)
        {
            Debug.LogError($"[FloorButton] Could not load save data for floor {_floorNumber} (index {_floorIndex}).");
            return;
        }

        Debug.Log($"[FloorButton] Floor {_floorNumber} selected — loading GameScene with designer data.");

        // IsReplayingFloor = false → GameManager calls InitializeFromFloorData(), which reads
        // per-night BinSaveData to reconstruct the designer-authored rule set via LibraryRuleAssigner.
        FloorSessionData.SelectedFloor    = floorData;
        FloorSessionData.IsReplayingFloor = false;

        SceneManager.LoadScene(GameSceneName);
    }

    // -------------------------------------------------------------------------
    // Accessors
    // -------------------------------------------------------------------------

    /// <summary>Returns the 1-based floor number assigned to this button.</summary>
    public int FloorNumber => _floorNumber;

    /// <summary>Returns true when this button has no corresponding save file and is non-interactive.</summary>
    public bool IsLocked => _isLocked;
}
