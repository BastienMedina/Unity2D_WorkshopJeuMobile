using UnityEngine;

/// <summary>
/// Enum declaring which difficulty stat receives the bonus when a new floor begins.
/// Declared at file scope — not nested inside FloorProgressionManager — so GameManager
/// can reference FloorBonusType directly without a class-name prefix, keeping call sites
/// readable when the type is compared or logged from outside this file.
/// </summary>
public enum FloorBonusType
{
    /// <summary>The fail threshold is raised, reducing the player's safety margin.</summary>
    FailThreshold,

    /// <summary>The day duration is shortened, increasing time pressure.</summary>
    DayDuration,

    /// <summary>The profitability decay rate is increased, forcing faster sorting.</summary>
    DecayRate
}

/// <summary>
/// MonoBehaviour that computes and returns per-floor difficulty bonuses.
/// On each new floor one stat (FailThreshold, DayDuration, or DecayRate) is randomly
/// selected and scaled upward relative to the floor index.
/// Returns a FloorDifficultyData to GameManager, which applies it to ProfitabilityManager.
/// Does NOT manage daily rules, does NOT handle UI, and does NOT modify ProfitabilityManager
/// directly — all application is delegated to GameManager so data flow stays centralised.
/// </summary>
public class FloorProgressionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-exposed scaling control
    // -------------------------------------------------------------------------

    [Header("Floor Scaling — auto-computed from floor index")]

    /// <summary>
    /// Fractional multiplier added per floor when computing the bonus magnitude.
    /// Default 0.15 means each floor adds 15% of the base value on top of the base,
    /// creating an exponential-feeling difficulty ramp: the absolute bonus grows
    /// floor over floor even though the per-floor rate stays constant.
    /// </summary>
    [SerializeField] private float baseFloorScaling = 0.15f;

    // -------------------------------------------------------------------------
    // Base values — day 1 / floor 1 defaults
    // -------------------------------------------------------------------------

    [Header("Base Values (Floor 1, Day 1 defaults)")]

    /// <summary>
    /// Profitability fail threshold before any floor bonus is applied.
    /// All floor scaling is computed relative to this value so the Inspector
    /// remains the single source of truth for baseline tuning.
    /// </summary>
    [SerializeField] private float baseFailThreshold = 20f;

    /// <summary>
    /// Day duration in seconds before any floor bonus is applied.
    /// Shortened on floors where DayDuration is selected as the bonus type.
    /// </summary>
    [SerializeField] private float baseDayDuration = 60f;

    /// <summary>
    /// Profitability decay per second before any floor bonus is applied.
    /// Increased on floors where DecayRate is selected as the bonus type.
    /// </summary>
    [SerializeField] private float baseDecayRate = 2f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// The bonus stat selected for the current floor.
    /// Randomly assigned once per floor in OnNewFloor so each run feels different
    /// and forces the player to adapt rather than memorise a fixed difficulty pattern.
    /// </summary>
    private FloorBonusType currentFloorBonus;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Selects a random FloorBonusType for the new floor, stores it, then computes
    /// and returns the corresponding FloorDifficultyData.
    /// Call this once at the start of each floor (when dayIndex == 0).
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor that just started.</param>
    /// <returns>A FloorDifficultyData with one stat scaled up and the others at base value.</returns>
    public FloorDifficultyData OnNewFloor(int floorIndex)
    {
        // Random selection ensures each run feels different and keeps the player adapting
        // rather than memorising a fixed sequence of which stat changes each floor.
        FloorBonusType[] allBonusTypes = (FloorBonusType[])System.Enum.GetValues(typeof(FloorBonusType));
        currentFloorBonus = allBonusTypes[Random.Range(0, allBonusTypes.Length)];

        FloorDifficultyData floorData = ComputeFloorData(floorIndex);

        Debug.Log($"[Difficulty] Floor {floorIndex + 1} bonus → {currentFloorBonus} | " +
                  $"FailThreshold: {floorData.failThreshold:F1} | " +
                  $"DayDuration: {floorData.dayDuration:F1}s | " +
                  $"DecayRate: {floorData.decayRatePerSecond:F2}/s");

        return floorData;
    }

    /// <summary>
    /// Builds a FloorDifficultyData where only the stat matching currentFloorBonus
    /// is scaled up; all other stats stay at their base values.
    /// Keeping only one stat per floor ensures the player can identify what changed
    /// and is not overwhelmed by simultaneous changes across multiple dimensions.
    /// </summary>
    /// <param name="floorIndex">Zero-based index used to compute the bonus magnitude.</param>
    /// <returns>A FloorDifficultyData ready for GameManager to apply to ProfitabilityManager.</returns>
    public FloorDifficultyData ComputeFloorData(int floorIndex)
    {
        // Magnitude grows with floorIndex so early floors are forgiving (small bonus)
        // and later floors are brutal (large bonus) — a constant magnitude would feel
        // flat and would not reward the player for progressing through the arc.
        float magnitude = baseFloorScaling * floorIndex;

        FloorDifficultyData data = new FloorDifficultyData
        {
            // Only ONE stat changes per floor; the others stay at base to avoid
            // overwhelming the player with simultaneous multi-dimensional difficulty spikes.
            failThreshold    = baseFailThreshold,
            dayDuration      = baseDayDuration,
            decayRatePerSecond = baseDecayRate
        };

        switch (currentFloorBonus)
        {
            case FloorBonusType.FailThreshold:
                data.failThreshold = baseFailThreshold * (1f + magnitude);
                break;

            case FloorBonusType.DayDuration:
                data.dayDuration = baseDayDuration * (1f + magnitude);
                break;

            case FloorBonusType.DecayRate:
                data.decayRatePerSecond = baseDecayRate * (1f + magnitude);
                break;
        }

        return data;
    }

    /// <summary>
    /// Returns the FloorBonusType that was randomly selected at the start of the current floor.
    /// GameManager uses this to display which stat changed in the transition UI.
    /// </summary>
    /// <returns>The bonus type active for the current floor.</returns>
    public FloorBonusType GetCurrentFloorBonus()
    {
        return currentFloorBonus;
    }
}
