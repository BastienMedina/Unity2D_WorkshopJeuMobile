using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for managing the objective target (the moving maximum),
/// detecting win and fail conditions, and notifying GameManager via events.
/// Does NOT modify the score, does NOT update any UI directly,
/// and does NOT call ScoreManager — it receives score values via OnScoreUpdated()
/// called by GameManager when ScoreManager fires onScoreChanged.
/// </summary>
public class ObjectiveManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned configuration
    // -------------------------------------------------------------------------

    /// <summary>Seconds between each automatic increase of the objective maximum.</summary>
    [SerializeField] private float objectiveIncreaseInterval = 30f;

    /// <summary>Starting objective maximum used on day 1 before any day-based scaling.</summary>
    [SerializeField] private int initialObjectiveMax = 10;

    /// <summary>
    /// Base amount added to the objective maximum per increase interval.
    /// This value also scales with the day index at increase time, creating progressive pressure:
    /// higher days demand faster sorting because each increase pushes the target further ahead.
    /// </summary>
    [SerializeField] private float increaseAmountPerDay = 5f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>The current objective maximum that the player must reach to win the day.</summary>
    private int currentObjectiveMax;

    /// <summary>Counts how many automatic increases have occurred this day.</summary>
    private int maxIncreaseCount;

    /// <summary>Zero-based index of the active day, stored so ObjectiveIncreaseLoop can read it.</summary>
    private int activeDayIndex;

    /// <summary>Reference to the running increase coroutine; stored so it can be stopped safely by reference.</summary>
    private Coroutine objectiveIncreaseCoroutine;

    /// <summary>Most recent score value received from GameManager via OnScoreUpdated.</summary>
    private int lastKnownScore;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired when the player's score reaches or exceeds currentObjectiveMax.
    /// GameManager subscribes to trigger the end-of-day success flow.
    /// </summary>
    public event Action onObjectiveReached;

    /// <summary>
    /// Fired when the third automatic increase occurs and the player's score
    /// is below 25% of the current maximum, signalling an unrecoverable failure.
    /// GameManager subscribes to trigger the day-restart flow.
    /// </summary>
    public event Action onObjectiveFailed;

    /// <summary>
    /// Fired whenever currentObjectiveMax increases, carrying the new max value.
    /// ScoreUI subscribes to this to keep the slider maximum in sync at runtime.
    /// </summary>
    public event Action<int> onObjectiveMaxChanged;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Initialises state for a new day, computes the starting objective maximum,
    /// and launches the automatic increase coroutine.
    /// Objective max scales with day index so early days are more forgiving than later ones —
    /// this ensures the game difficulty curve is felt even before the increases kick in.
    /// </summary>
    /// <param name="dayIndex">Zero-based index of the day being started.</param>
    /// <param name="currentScore">The player's score at the moment the day begins (used to seed lastKnownScore).</param>
    public void StartDay(int dayIndex, int currentScore)
    {
        activeDayIndex       = dayIndex;
        lastKnownScore       = currentScore;
        maxIncreaseCount     = 0;

        // Objective max starts higher on later days so players cannot coast through advanced days
        // using strategies that worked on early ones.
        currentObjectiveMax = initialObjectiveMax + (int)(dayIndex * increaseAmountPerDay);

        StopIncreaseCoroutineSafely();
        objectiveIncreaseCoroutine = StartCoroutine(ObjectiveIncreaseLoop());
    }

    /// <summary>
    /// Stops the automatic increase coroutine safely.
    /// Called by GameManager when the day ends — either by success or failure —
    /// to prevent the coroutine from firing events after the transition begins.
    /// </summary>
    public void StopDay()
    {
        StopIncreaseCoroutineSafely();
    }

    /// <summary>
    /// Receives the latest score value from GameManager and checks the win condition.
    /// ObjectiveManager never calls ScoreManager directly; GameManager acts as the bridge.
    /// Checking on every score update ensures the win condition is detected the instant it is met,
    /// rather than waiting for the next coroutine tick which could delay the transition.
    /// </summary>
    /// <param name="newScore">The player's current score after the most recent point was added.</param>
    public void OnScoreUpdated(int newScore)
    {
        lastKnownScore = newScore;

        bool hasReachedObjective = newScore >= currentObjectiveMax;

        if (!hasReachedObjective)
            return;

        StopIncreaseCoroutineSafely();
        onObjectiveReached?.Invoke();
    }

    /// <summary>
    /// Returns the current objective maximum.
    /// Used by GameManager to pass an up-to-date max value to ScoreUI.UpdateDisplay.
    /// </summary>
    /// <returns>The current objective maximum for this day.</returns>
    public int GetCurrentMax()
    {
        return currentObjectiveMax;
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Repeatedly waits objectiveIncreaseInterval seconds, then increases currentObjectiveMax.
    /// The increase multiplier grows with activeDayIndex to make later days progressively harder:
    /// each tick pushes the target further away, compressing the time window the player has to score.
    /// After the third increase, checks whether the player is below 25% of the new maximum;
    /// the fail check only triggers on the third increase to give the player three full intervals
    /// (three chances) before the game decides the day is unrecoverable.
    /// </summary>
    private IEnumerator ObjectiveIncreaseLoop()
    {
        while (true)
        {
            yield return new WaitForSeconds(objectiveIncreaseInterval);

            maxIncreaseCount++;

            // Multiplier grows with day index so later days apply larger jumps per tick,
            // creating steeper pressure on advanced days without changing the base interval.
            float increaseMultiplier = 1f + activeDayIndex * 0.2f;
            int increaseAmount       = Mathf.RoundToInt(increaseAmountPerDay * increaseMultiplier);

            currentObjectiveMax += increaseAmount;
            onObjectiveMaxChanged?.Invoke(currentObjectiveMax);

            // Fail check only on the third increase: three intervals give the player 90 seconds
            // (at the default 30 s interval) before the game evaluates whether the run is lost.
            // Checking earlier would punish players who start slowly but accelerate.
            bool isThirdIncrease = maxIncreaseCount == 3;

            if (!isThirdIncrease)
                continue;

            float failThreshold              = currentObjectiveMax * 0.25f;
            bool hasFailedObjective          = lastKnownScore < failThreshold;

            if (!hasFailedObjective)
                continue;

            StopIncreaseCoroutineSafely();
            onObjectiveFailed?.Invoke();
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops objectiveIncreaseCoroutine by stored reference if one is running.
    /// StopAllCoroutines is intentionally avoided to avoid cancelling unrelated coroutines
    /// that may exist on this MonoBehaviour in the future.
    /// </summary>
    private void StopIncreaseCoroutineSafely()
    {
        if (objectiveIncreaseCoroutine == null)
            return;

        StopCoroutine(objectiveIncreaseCoroutine);
        objectiveIncreaseCoroutine = null;
    }
}
