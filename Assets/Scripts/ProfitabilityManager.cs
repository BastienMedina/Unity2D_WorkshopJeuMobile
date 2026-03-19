using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// MonoBehaviour that maintains the profitability value (0–100 scale) over the course of a day.
/// Applies constant decay every frame to create sustained pressure, grants gain per correct sort,
/// and detects win/fail conditions, notifying GameManager via events.
/// Does NOT handle UI, does NOT know about bins or rules, and does NOT communicate
/// with any system other than GameManager through events.
/// </summary>
public class ProfitabilityManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned settings
    // -------------------------------------------------------------------------

    [Header("Profitability Settings")]

    /// <summary>Starting profitability value when a day begins. Range: 0 to 100.</summary>
    [SerializeField] private float initialProfitability = 60f;

    /// <summary>
    /// Amount by which profitability falls per second while the day is active.
    /// Not [SerializeField] — value is injected at runtime by GameManager via SetDecayRate
    /// so FloorProgressionManager can scale it per floor without exposing floor logic to this class.
    /// Constant decay creates pressure even when the player is sorting correctly but too slowly —
    /// it ensures speed is a meaningful variable, not just accuracy.
    /// </summary>
    private float decayRatePerSecond = 2f;

    /// <summary>Base profitability gain awarded for each correctly sorted document.</summary>
    [SerializeField] private float gainPerDocument = 5f;

    /// <summary>
    /// Multiplier applied to gainPerDocument when documents are sorted in rapid succession.
    /// Rewarding speed encourages the player to sort faster, not just correctly —
    /// without this, optimal play would be to sort one document and wait.
    /// </summary>
    [SerializeField] private float speedBonusMultiplier = 0.5f;

    /// <summary>
    /// Profitability level below which the player fails the day.
    /// Not [SerializeField] — value is injected at runtime by GameManager via SetFailThreshold
    /// so FloorProgressionManager can scale it per floor without exposing floor logic to this class.
    /// </summary>
    private float failThreshold = 20f;

    /// <summary>
    /// Amount subtracted from currentProfitability when the player drops a document
    /// into the wrong bin. [SerializeField] so designers can tune penalty severity without
    /// touching code — default 10f means one wrong drop costs roughly 5 seconds of decay,
    /// making it feel significant but not instantly fatal.
    /// </summary>
    [SerializeField] private float wrongBinPenalty = 10f;

    /// <summary>Total day length in seconds. Default is 120 seconds (2 minutes).
    /// Not [SerializeField] — value is injected at runtime by GameManager via SetDayDuration
    /// so FloorProgressionManager can scale it per floor without exposing floor logic to this class.
    /// </summary>
    private float dayDuration = 120f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Current profitability on the 0–100 scale.</summary>
    private float currentProfitability;

    /// <summary>
    /// Timestamp (Time.time) of the most recent valid document sort.
    /// Used to compute whether the next sort qualifies for the speed bonus.
    /// </summary>
    private float lastDocumentSortedTime;

    /// <summary>Accumulated time since the day started, in seconds.</summary>
    private float dayTimer;

    /// <summary>True while a day is in progress; false after StopDay() is called.</summary>
    private bool isDayActive;

    /// <summary>Reference to the running day coroutine — stored for safe cancellation.</summary>
    private Coroutine dayCoroutine;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired every frame the day is active, carrying the current profitability value (0–100).
    /// ProfitabilityBarUI subscribes via GameManager to update the visual bar each frame.
    /// </summary>
    public event Action<float> onProfitabilityChanged;

    /// <summary>
    /// Fired when the day timer reaches dayDuration while profitability is at or above failThreshold.
    /// GameManager subscribes to trigger the day-success transition.
    /// </summary>
    public event Action onDaySuccess;

    /// <summary>
    /// Fired when profitability drops below failThreshold at any point during the day.
    /// GameManager subscribes to trigger the day-failure transition.
    /// </summary>
    public event Action onDayFailed;

    /// <summary>
    /// Fired every frame the day is active, carrying the remaining seconds in the day.
    /// Timer display is decoupled from logic via this event so ProfitabilityManager
    /// never touches UI — GameManager relays the value to DayTimerUI.UpdateTimer().
    /// </summary>
    public event Action<float> onTimerUpdated;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Update()
    {
        // Skip all logic when no day is running — avoids redundant computation
        // and prevents stale decay from firing after StopDay() is called.
        if (!isDayActive)
            return;

        dayTimer += Time.deltaTime;

        // Decay uses Time.deltaTime to remain framerate-independent.
        // A fixed per-frame decrement would decay faster on high-refresh devices.
        currentProfitability -= decayRatePerSecond * Time.deltaTime;
        currentProfitability  = Mathf.Clamp(currentProfitability, 0f, 100f);

        onProfitabilityChanged?.Invoke(currentProfitability);

        // Fired every frame so the timer text updates smoothly without perceptible lag.
        // A once-per-second update would produce a stutter visible on every refresh.
        float remainingSeconds = Mathf.Max(0f, dayDuration - dayTimer);
        onTimerUpdated?.Invoke(remainingSeconds);

        // Fail check runs every frame so the player loses the instant they cross the threshold,
        // not on the next document sort — crossing the line must feel immediate.
        bool hasFailedProfitability = currentProfitability < failThreshold;

        if (hasFailedProfitability)
        {
            StopDay();
            onDayFailed?.Invoke();
            return; // Skip win check — fail takes priority.
        }

        // Win check: only triggers at the end of the day and only if the player
        // never crossed the fail threshold (which would have returned above).
        bool hasDayEnded = dayTimer >= dayDuration;

        if (!hasDayEnded)
            return;

        StopDay();
        onDaySuccess?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Resets all day state and begins the profitability tracking loop.
    /// Called by GameManager at the start of every game day.
    /// </summary>
    public void StartDay()
    {
        currentProfitability     = initialProfitability;
        dayTimer                 = 0f;
        isDayActive              = true;
        lastDocumentSortedTime   = Time.time;

        onProfitabilityChanged?.Invoke(currentProfitability);
    }

    /// <summary>
    /// Halts the profitability tracking loop without firing any win or fail event.
    /// Called by GameManager before starting a transition so events do not fire
    /// after the day is officially over.
    /// </summary>
    public void StopDay()
    {
        isDayActive = false;

        StopDayCoroutineSafely();
    }

    /// <summary>
    /// Awards profitability for a correctly sorted document, with a speed bonus
    /// if the sort occurred within 2 seconds of the previous one.
    /// Called by GameManager in response to SortingBin.onValidDrop.
    /// </summary>
    public void OnDocumentSorted()
    {
        float timeSinceLastSort = Time.time - lastDocumentSortedTime;

        // Speed bonus only applies when sorted within 2 seconds of the previous document.
        // The 2-second window rewards sustained fast sorting (combo behaviour) while
        // still giving the player a fair reaction window before the bonus expires.
        bool hasSpeedBonus = timeSinceLastSort < 2f;
        float speedBonus   = hasSpeedBonus ? gainPerDocument * speedBonusMultiplier : 0f;

        currentProfitability  += gainPerDocument + speedBonus;
        currentProfitability   = Mathf.Clamp(currentProfitability, 0f, 100f);
        lastDocumentSortedTime = Time.time;

        onProfitabilityChanged?.Invoke(currentProfitability);
    }

    /// <summary>
    /// Subtracts wrongBinPenalty from currentProfitability and fires onProfitabilityChanged
    /// so the bar reacts visually in the same frame as the drop.
    /// Called by GameManager in response to SortingBin.onInvalidDrop.
    /// Does NOT check the fail condition here — Update() runs every frame and will detect
    /// the threshold crossing on the very next tick, avoiding duplicated fail logic.
    /// </summary>
    public void ApplyWrongBinPenalty()
    {
        currentProfitability -= wrongBinPenalty;

        // Clamp to 0 so the bar never displays a negative value.
        // The fail condition (< failThreshold) is checked in Update() every frame —
        // duplicating it here would create two competing code paths for the same event.
        currentProfitability = Mathf.Clamp(currentProfitability, 0f, 100f);

        // Fire immediately so ProfitabilityBarUI reflects the penalty in the same frame,
        // giving the player instant negative feedback without waiting for the next Update tick.
        onProfitabilityChanged?.Invoke(currentProfitability);
    }

    /// <summary>
    /// Sets the fail threshold and fires onProfitabilityChanged to refresh any UI
    /// that displays the threshold marker.
    /// Clamped to [5, 80] to prevent values that make the game unplayable:
    /// below 5 the player can never lose; above 80 the player fails before they can act.
    /// </summary>
    /// <param name="value">Desired fail threshold on the 0–100 profitability scale.</param>
    public void SetFailThreshold(float value)
    {
        failThreshold = Mathf.Clamp(value, 5f, 80f);
        onProfitabilityChanged?.Invoke(currentProfitability);
    }

    /// <summary>
    /// Sets the day duration in seconds.
    /// Minimum 60s prevents days so short the player cannot sort any documents —
    /// below that threshold even a single decay tick could end the day before the
    /// first document arrives.
    /// </summary>
    /// <param name="value">Desired day duration in seconds.</param>
    public void SetDayDuration(float value)
    {
        dayDuration = Mathf.Max(60f, value);
    }

    /// <summary>
    /// Sets the profitability decay rate in units per second.
    /// Minimum 0.5 prevents days where profitability never drops, removing all tension —
    /// without any decay the player can stop sorting after reaching 100 and simply wait.
    /// </summary>
    /// <param name="value">Desired decay rate in profitability points per second.</param>
    public void SetDecayRate(float value)
    {
        decayRatePerSecond = Mathf.Max(0.5f, value);
    }

    /// <summary>
    /// Returns the current profitability value (0–100 scale).
    /// Used by GameManager to seed the initial UI display after StartDay.
    /// </summary>
    /// <returns>The current profitability value.</returns>
    public float GetCurrentProfitability()
    {
        return currentProfitability;
    }

    /// <summary>
    /// Returns the configured fail threshold.
    /// Used by ProfitabilityBarUI.Initialize() to position the threshold marker once per day.
    /// </summary>
    /// <returns>The profitability value below which the player fails.</returns>
    public float GetFailThreshold()
    {
        return failThreshold;
    }

    /// <summary>
    /// Returns the configured day duration in seconds.
    /// GameManager can read this after applying a floor bonus to confirm the new value.
    /// </summary>
    /// <returns>Current day duration in seconds.</returns>
    public float GetDayDuration()
    {
        return dayDuration;
    }

    /// <summary>
    /// Returns the configured profitability decay rate in units per second.
    /// GameManager can read this after applying a floor bonus to confirm the new value.
    /// </summary>
    /// <returns>Current decay rate in profitability points per second.</returns>
    public float GetDecayRate()
    {
        return decayRatePerSecond;
    }

    /// <summary>
    /// Returns the normalised day progress as a value between 0 and 1.
    /// Can be used by UI elements to display a time-remaining indicator.
    /// </summary>
    /// <returns>Normalised elapsed time: 0 at day start, 1 at day end.</returns>
    public float GetDayProgress()
    {
        return Mathf.Clamp01(dayTimer / dayDuration);
    }

    /// <summary>
    /// Returns the seconds remaining in the current day, clamped to a minimum of zero.
    /// Called by GameManager on day start to seed DayTimerUI.ResetDisplay() with the
    /// correct full duration before the first onTimerUpdated fires.
    /// </summary>
    /// <returns>Remaining seconds in the day (always >= 0).</returns>
    public float GetRemainingSeconds()
    {
        return Mathf.Max(0f, dayDuration - dayTimer);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops dayCoroutine by stored reference if one is running.
    /// StopAllCoroutines is intentionally avoided so future coroutines on this
    /// MonoBehaviour are not inadvertently cancelled.
    /// </summary>
    private void StopDayCoroutineSafely()
    {
        if (dayCoroutine == null)
            return;

        StopCoroutine(dayCoroutine);
        dayCoroutine = null;
    }
}
