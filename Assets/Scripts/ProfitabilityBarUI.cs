using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour responsible for rendering the profitability bar, threshold marker, and percentage text.
/// Purely visual: reacts to values pushed by GameManager and never reads from ProfitabilityManager directly.
/// Does NOT compute profitability, does NOT know about game state or day transitions.
/// </summary>
public class ProfitabilityBarUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned UI references
    // -------------------------------------------------------------------------

    /// <summary>
    /// RectTransform of the green fill GameObject.
    /// Fill width is driven by setting anchorMax.x to the normalised profitability value.
    /// Driving fill via anchorMax is more reliable than fillAmount because it works regardless
    /// of the Image Type set in the Inspector — the rectangle physically grows and shrinks.
    /// </summary>
    [SerializeField] private RectTransform fillRect;

    /// <summary>
    /// Image component on the fill GameObject. Used for colour changes only.
    /// Fill width is controlled by fillRect, not by fillAmount.
    /// </summary>
    [SerializeField] private Image fillImage;

    /// <summary>
    /// Thin vertical RectTransform representing the fail threshold on the bar.
    /// Positioned once by Initialize() — never moved per frame.
    /// </summary>
    [SerializeField] private RectTransform thresholdMarker;

    /// <summary>Percentage text label centred over the bar. Displays the current value as an integer.</summary>
    [SerializeField] private TextMeshProUGUI profitabilityText;

    /// <summary>
    /// RectTransform of the bar background. Used by Initialize() to compute
    /// the pixel position of the threshold marker relative to the bar's width.
    /// </summary>
    [SerializeField] private RectTransform barRectTransform;

    // -------------------------------------------------------------------------
    // Private cached state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fail threshold cached from the last Initialize() call.
    /// Stored so UpdateDisplay() can compare the current value without requiring a parameter.
    /// </summary>
    private float cachedFailThreshold;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Positions the threshold marker, caches the fail threshold for colour comparison,
    /// and renders an initial fill so the bar is correct before the first Update fires.
    /// Called once per day by GameManager immediately after ProfitabilityManager.StartDay().
    /// </summary>
    /// <param name="failThreshold">
    /// The profitability value (0–100) below which the player fails.
    /// Used to place the marker at the corresponding horizontal position on the bar.
    /// </param>
    /// <param name="initialValue">
    /// The starting profitability value for the day. Passed straight to UpdateDisplay()
    /// so the bar renders correctly before ProfitabilityManager fires its first event.
    /// </param>
    public void Initialize(float failThreshold, float initialValue)
    {
        cachedFailThreshold = failThreshold;

        // Marker is positioned once: failThreshold is a constant for the duration of the day,
        // so recomputing its pixel position every frame would waste CPU for zero visual gain.
        float markerXPosition = barRectTransform.rect.width * (failThreshold / 100f);
        thresholdMarker.anchoredPosition = new Vector2(markerXPosition, 0f);

        // Seed the bar immediately so it shows the correct state from the first frame.
        UpdateDisplay(initialValue);
    }

    /// <summary>
    /// Refreshes the fill width, bar colour, and text label to reflect the current profitability.
    /// Called by GameManager every frame via the onProfitabilityChanged event during an active day.
    /// Fill is driven via anchorMax.x rather than fillAmount so it works regardless of Image Type.
    /// </summary>
    /// <param name="profitabilityValue">The current profitability value on the 0–100 scale.</param>
    public void UpdateDisplay(float profitabilityValue)
    {
        // Drive fill width via anchorMax.x instead of fillAmount.
        // This approach works regardless of the Image Type configured in the Inspector:
        // the green rectangle physically shrinks and grows with the value.
        // anchorMin stays pinned to (0, 0) and anchorMax.y stays at 1 to fill the full height.
        // Clamp01 keeps anchorMax.x in [0, 1] even if profitabilityValue somehow exceeds 100.
        fillRect.anchorMin = Vector2.zero;
        fillRect.anchorMax = new Vector2(Mathf.Clamp01(profitabilityValue / 100f), 1f);

        // Reset offsets to zero after changing anchors so no RectTransform padding
        // offsets the fill incorrectly — anchors alone define the fill extent.
        fillRect.offsetMin = Vector2.zero;
        fillRect.offsetMax = Vector2.zero;

        // Color turns red when the value is within 20% above the fail threshold,
        // giving the player an early visual warning before they actually lose.
        // Using 1.2× the threshold avoids the abrupt colour switch right at the danger line.
        bool isInDangerZone = profitabilityValue <= cachedFailThreshold * 1.2f;
        fillImage.color     = isInDangerZone ? Color.red : Color.green;

        profitabilityText.text = $"{Mathf.RoundToInt(profitabilityValue)}%";
    }
}
