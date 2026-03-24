using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Represents a single floor entry in the tower selection list.
/// Initialised by FloorButtonSpawner immediately after instantiation.
/// </summary>
[RequireComponent(typeof(Button))]
public class FloorButton : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-configurable fields
    // -------------------------------------------------------------------------

    /// <summary>Label displaying the floor number, e.g. "ETAGE 42".</summary>
    [SerializeField] private TextMeshProUGUI floorLabel;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private int _floorNumber;
    private Button _button;

    // -------------------------------------------------------------------------
    // Initialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the floor number and updates the label text.
    /// Must be called by FloorButtonSpawner right after Instantiate.
    /// </summary>
    /// <param name="floorNumber">1-based floor index.</param>
    public void Initialise(int floorNumber)
    {
        _floorNumber = floorNumber;
        _button = GetComponent<Button>();

        if (floorLabel != null)
        {
            // Enable auto-sizing so the label always fits its container.
            floorLabel.enableAutoSizing = true;
            floorLabel.fontSizeMin = 12f;
            floorLabel.fontSizeMax = 200f;
            floorLabel.text = $"ETAGE {floorNumber}";
        }

        _button.onClick.AddListener(OnClicked);
    }

    // -------------------------------------------------------------------------
    // Button callback
    // -------------------------------------------------------------------------

    private void OnClicked()
    {
        Debug.Log($"[FloorButton] Player selected floor {_floorNumber}");
        // TODO: wire up to scene loading / game manager when ready.
    }

    // -------------------------------------------------------------------------
    // Accessors
    // -------------------------------------------------------------------------

    /// <summary>Returns the 1-based floor number assigned to this button.</summary>
    public int FloorNumber => _floorNumber;
}
