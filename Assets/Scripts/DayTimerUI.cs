using TMPro;
using UnityEngine;

/// <summary>
/// MonoBehaviour that displays the remaining day time as a MM:SS countdown
/// and switches text colour to urgentColor when time is running low.
/// Does NOT manage game time, does NOT trigger day end, and holds NO reference
/// to ProfitabilityManager — it only receives data via UpdateTimer() called by GameManager.
/// </summary>
public class DayTimerUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// TextMeshPro component that renders the MM:SS countdown string.
    /// Assigned via Inspector — DayTimerUI never searches for it at runtime.
    /// </summary>
    [SerializeField] private TextMeshProUGUI timerText;

    /// <summary>Default text colour used while time remaining is above urgentThreshold.</summary>
    [SerializeField] private Color normalColor = Color.white;

    /// <summary>
    /// Text colour applied when remaining time is at or below urgentThreshold.
    /// Switches to red to warn the player visually that time is nearly up.
    /// </summary>
    [SerializeField] private Color urgentColor = Color.red;

    /// <summary>
    /// Seconds remaining at which the text colour switches to urgentColor.
    /// [SerializeField] so designers can tune the urgency threshold without touching code —
    /// the correct threshold depends on the day duration, which can vary per level.
    /// </summary>
    [SerializeField] private float urgentThreshold = 30f;

    /// <summary>
    /// Full day duration in seconds, used exclusively by ResetDisplay() to
    /// seed the initial countdown value at the start of each day.
    /// Must match ProfitabilityManager.dayDuration — kept in sync via Inspector.
    /// </summary>
    [SerializeField] private float fullDayDuration = 120f;

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Formats <paramref name="remainingSeconds"/> as MM:SS and updates the timer text.
    /// Switches text colour to urgentColor when remainingSeconds reaches urgentThreshold.
    /// Called by GameManager every frame via ProfitabilityManager.onTimerUpdated.
    /// </summary>
    /// <param name="remainingSeconds">
    /// Raw seconds left in the day. Values below 0 are clamped to 0 so the
    /// display always shows 00:00 at the end of a day rather than a negative value.
    /// </param>
    public void UpdateTimer(float remainingSeconds)
    {
        // Clamp prevents a negative countdown from appearing if the day-end event
        // fires one frame after dayTimer overshoots dayDuration due to Time.deltaTime.
        float clampedSeconds = Mathf.Max(0f, remainingSeconds);

        // FloorToInt avoids rounding up and displaying "01:00" when 59.9 seconds remain,
        // which would mislead the player about how much time is left.
        // MM:SS format is universally readable and matches standard game timer conventions.
        // urgentThreshold colour warns the player visually without requiring a separate HUD element.
        int minutes = Mathf.FloorToInt(clampedSeconds / 60f);
        int seconds = Mathf.FloorToInt(clampedSeconds % 60f);

        timerText.text  = string.Format("{0:00}:{1:00}", minutes, seconds);
        timerText.color = clampedSeconds <= urgentThreshold ? urgentColor : normalColor;
    }

    /// <summary>
    /// Resets the display to the full day duration countdown (MM:SS of fullDayDuration).
    /// Called by GameManager at the start of each day, after StartDay() on ProfitabilityManager,
    /// so the timer shows the correct initial value before the first onTimerUpdated fires.
    /// </summary>
    public void ResetDisplay()
    {
        UpdateTimer(fullDayDuration);
    }
}
