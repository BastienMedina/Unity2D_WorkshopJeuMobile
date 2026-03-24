using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the infinite vertical tiling of the tower background image
/// synchronized with a ScrollRect's normalized vertical position.
/// Attach this to the tower selection screen root GameObject.
/// </summary>
[RequireComponent(typeof(ScrollRect))]
public class TowerScrollView : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-configurable fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// The RawImage used as the scrollable tower background.
    /// Its texture must have Wrap Mode set to "Repeat" in the importer.
    /// </summary>
    [SerializeField] private RawImage backgroundImage;

    /// <summary>
    /// How much the UV offset changes per unit of normalized scroll position.
    /// Higher values = faster parallax scroll of the background.
    /// </summary>
    [SerializeField] private float backgroundScrollFactor = 1f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private ScrollRect _scrollRect;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _scrollRect = GetComponent<ScrollRect>();
    }

    private void OnEnable()
    {
        _scrollRect.onValueChanged.AddListener(OnScrollValueChanged);
    }

    private void OnDisable()
    {
        _scrollRect.onValueChanged.RemoveListener(OnScrollValueChanged);
    }

    // -------------------------------------------------------------------------
    // Scroll callback
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by ScrollRect.onValueChanged whenever the player drags the list.
    /// Adjusts the RawImage UV offset to simulate infinite vertical tiling.
    /// </summary>
    private void OnScrollValueChanged(Vector2 normalizedPosition)
    {
        if (backgroundImage == null) return;

        // normalizedPosition.y goes from 1 (top) to 0 (bottom).
        // We invert it so scrolling down (lower floors) shifts texture downward.
        float uvOffset = (1f - normalizedPosition.y) * backgroundScrollFactor;

        // Fractional part keeps the offset within [0, 1) for seamless tiling.
        Rect uvRect = backgroundImage.uvRect;
        uvRect.y = uvOffset % 1f;
        backgroundImage.uvRect = uvRect;
    }
}
