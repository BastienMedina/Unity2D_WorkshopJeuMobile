using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Manages the infinite tower background: the RawImage lives inside the scrollable
/// TowerContainer and is stretched to match its height. UV tiling is recalculated
/// whenever the content height changes so the tower image tiles seamlessly end-to-end
/// without any scroll-position parallax — the background scrolls with the buttons.
/// </summary>
public class TowerScrollView : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// RawImage placed as the first child of TowerContainer (behind the floor buttons).
    /// Its texture must have Wrap Mode set to "Repeat" in the importer.
    /// </summary>
    [SerializeField] private RawImage backgroundImage;

    /// <summary>
    /// Height in pixels of the source tower texture (unscaled).
    /// Used to compute how many times the image must tile to fill the content.
    /// </summary>
    [SerializeField] private float towerImageHeight = 1920f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private RectTransform _backgroundRect;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (backgroundImage != null)
            _backgroundRect = backgroundImage.GetComponent<RectTransform>();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Call this every time TowerContainer's height changes (after spawning blocks).
    /// Stretches the RawImage to match contentHeight and adjusts UV tiling so the
    /// tower image tiles exactly the number of times needed to fill the background.
    /// </summary>
    /// <param name="contentHeight">The new total height of TowerContainer in pixels.</param>
    public void RefreshBackground(float contentHeight)
    {
        if (backgroundImage == null || _backgroundRect == null)
            return;

        // Stretch background to fill the full content height.
        _backgroundRect.sizeDelta = new Vector2(_backgroundRect.sizeDelta.x, contentHeight);

        // UV height = how many times the texture tiles vertically across contentHeight.
        // e.g. contentHeight 3840 with towerImageHeight 1920 → 2 tiles.
        float tileCount = contentHeight / towerImageHeight;

        Rect uvRect = backgroundImage.uvRect;
        uvRect.x      = 0f;
        uvRect.y      = 0f;
        uvRect.width  = 1f;
        uvRect.height = tileCount;
        backgroundImage.uvRect = uvRect;
    }
}
