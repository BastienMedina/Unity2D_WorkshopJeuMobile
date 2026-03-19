using System;

/// <summary>
/// Pure data container passed from FloorProgressionManager to GameManager
/// to apply floor-level difficulty bonuses to ProfitabilityManager.
/// Does NOT contain any game logic, MonoBehaviour references, or Unity lifecycle methods.
/// Kept as a separate class because floor-level data (thresholds, timing, decay) is
/// conceptually distinct from day-level data (bin count, rules, complexity).
/// </summary>
[Serializable]
public class FloorDifficultyData
{
    /// <summary>
    /// Profitability level below which the player fails the day, scaled for this floor.
    /// Higher floors can raise this threshold to increase the minimum acceptable performance.
    /// </summary>
    public float failThreshold;

    /// <summary>
    /// Total duration of a single day in seconds, scaled for this floor.
    /// A shorter day at higher floors increases pressure without changing rule count.
    /// </summary>
    public float dayDuration;

    /// <summary>
    /// Amount by which profitability falls per second during an active day, scaled for this floor.
    /// Higher decay rates force the player to sort faster to maintain profitability.
    /// </summary>
    public float decayRatePerSecond;
}
