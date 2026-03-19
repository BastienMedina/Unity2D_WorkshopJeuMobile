using System;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for tracking the player's current score during a game day
/// and broadcasting changes to any subscriber via an event.
/// Does NOT handle UI rendering, does NOT know about objectives or win/fail conditions,
/// and does NOT communicate with any other system beyond firing onScoreChanged.
/// </summary>
public class ScoreManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// Current cumulative score for the active day.
    /// Serialized so the live value is visible in the Inspector during Play Mode for debugging.
    /// </summary>
    [SerializeField] private int currentScore;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired every time the score changes, carrying the new score value.
    /// Event-based so ObjectiveManager and ScoreUI react independently —
    /// ScoreManager never needs to know who is listening, keeping all three systems decoupled.
    /// </summary>
    public event Action<int> onScoreChanged;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Increments the current score by one point and notifies subscribers.
    /// Called by GameManager when a valid document drop is confirmed by a SortingBin.
    /// </summary>
    public void AddPoint()
    {
        currentScore++;
        onScoreChanged?.Invoke(currentScore);
    }

    /// <summary>
    /// Resets the current score to zero and notifies subscribers.
    /// Called by GameManager at the start of each new day so score tracking is clean.
    /// </summary>
    public void ResetScore()
    {
        currentScore = 0;
        onScoreChanged?.Invoke(currentScore);
    }

    /// <summary>
    /// Returns the current score without modifying it.
    /// Used by GameManager to pass a snapshot to ObjectiveManager or ScoreUI on demand.
    /// </summary>
    /// <returns>The player's current score for this day.</returns>
    public int GetCurrentScore()
    {
        return currentScore;
    }
}
