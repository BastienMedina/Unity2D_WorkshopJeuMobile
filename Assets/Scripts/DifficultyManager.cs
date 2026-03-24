using UnityEngine;

/// <summary>
/// Defines how the day's rule set should evolve compared to the previous day.
/// Declared at file scope so GameManager can reference DayEvolutionType without a class prefix,
/// keeping call sites readable when the type is compared or logged from outside DifficultyManager.
/// </summary>
public enum DayEvolutionType
{
    /// <summary>An existing rule should gain one additional condition or be upgraded to a harder type.</summary>
    ComplexifyRule,

    /// <summary>A brand-new rule should be added to the active set.</summary>
    AddNewRule
}

/// <summary>
/// MonoBehaviour that computes DayDifficultyData and DayEvolutionType from floor and day indices.
/// Translates abstract progression position into concrete numeric parameters (rules per bin,
/// complexity, bin count) and day-to-day evolution decisions, then exposes results for
/// GameManager to consume.
/// Does NOT apply settings to any system, does NOT generate rules, does NOT spawn documents,
/// and holds NO references to RuleGenerator, DocumentSpawner, or SortingBin.
/// </summary>
public class DifficultyManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Progression structure — bounds declared as fields, never hardcoded
    // -------------------------------------------------------------------------

    [Header("Automatic Progression Structure")]

    /// <summary>Total number of floors in the game. Used to normalise the progression index.</summary>
    [SerializeField] private int totalFloors = 5;

    /// <summary>
    /// Number of days per floor. Used to normalise the progression index.
    /// "floor" reflects the vertical progression metaphor of the game rather than
    /// a flat "level" concept — each floor is a distinct vertical stage with its own bonus.
    /// </summary>
    [SerializeField] private int daysPerFloor = 5;

    // -------------------------------------------------------------------------
    // Base values — day 1 / floor 1 defaults
    // -------------------------------------------------------------------------

    [Header("Base Values (Floor 1, Day 1 defaults)")]

    /// <summary>
    /// Profitability fail threshold before any floor bonus is applied.
    /// Base values are the day 1 floor 1 defaults — all scaling is computed
    /// relative to these so the Inspector remains the single source of truth.
    /// </summary>
    // CS0414 suppressed: field is Inspector-assigned and reserved for future scaling logic.
    // Removing it would lose the serialized value and the [Header] grouping in the Inspector.
#pragma warning disable CS0414
    [SerializeField] private float baseFailThreshold = 20f;

    /// <summary>
    /// Day duration in seconds before any floor bonus is applied.
    /// Base values are the day 1 floor 1 defaults — all scaling is computed
    /// relative to these so the Inspector remains the single source of truth.
    /// </summary>
    [SerializeField] private float baseDayDuration = 120f;

    /// <summary>
    /// Profitability decay per second before any floor bonus is applied.
    /// Base values are the day 1 floor 1 defaults — all scaling is computed
    /// relative to these so the Inspector remains the single source of truth.
    /// </summary>
    [SerializeField] private float baseDecayRate = 2f;
#pragma warning restore CS0414

    // -------------------------------------------------------------------------
    // Rules-per-bin bounds
    // -------------------------------------------------------------------------

    [Header("Rules Per Bin Bounds")]

    /// <summary>
    /// Minimum number of rules assigned to each bin at the easiest difficulty.
    /// Per-bin rather than global total guarantees every bin has at least one rule
    /// at all difficulty levels, preventing empty bins.
    /// </summary>
    [SerializeField] private int minRulesPerBin = 1;

    /// <summary>
    /// Maximum rules any single bin can hold regardless of day or floor index.
    /// Cap prevents bins from becoming unreadable walls of text that the player
    /// cannot parse within the available day time.
    /// </summary>
    [SerializeField] private int maxRulesPerBinCap = 4;

    // -------------------------------------------------------------------------
    // Bin count bounds
    // -------------------------------------------------------------------------

    [Header("Bin Count Bounds")]

    /// <summary>Minimum number of active bins (at the easiest difficulty).</summary>
    [SerializeField] private int minNumberOfBins = 2;

    /// <summary>Maximum number of active bins (at the hardest difficulty).</summary>
    [SerializeField] private int maxNumberOfBins = 5;

    // -------------------------------------------------------------------------
    // Rule complexity bounds
    // -------------------------------------------------------------------------

    [Header("Complexity Bounds")]

    /// <summary>Lowest allowed maxRuleComplexity value (at the easiest difficulty).</summary>
    [SerializeField] private int minRuleComplexity = 1;

    /// <summary>
    /// Amount added to maxRuleComplexity for each day that passes within a floor.
    /// Incremental growth gives the player one day to learn existing rules before new
    /// complexity is introduced, rather than front-loading all difficulty on day 1.
    /// </summary>
    [SerializeField] private float complexityIncreasePerDay = 0.5f;

    // -------------------------------------------------------------------------
    // Optional progression curve
    // -------------------------------------------------------------------------

    [Header("Progression Curve (optional)")]

    /// <summary>
    /// Optional AnimationCurve that remaps the linear 0–1 progression index before Lerp.
    /// Allows easing the difficulty ramp (e.g. slow start, steep mid-game spike).
    /// When left unassigned, bin count interpolation remains linear.
    /// </summary>
    [SerializeField] private AnimationCurve progressionCurve;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes a fully populated DayDifficultyData from the given day and floor indices.
    /// rulesPerBin grows every two days so the player has one day to adapt before new rules appear.
    /// maxRuleComplexity grows each day via complexityIncreasePerDay.
    /// numberOfBins is derived from the absolute day position across the full game arc.
    /// </summary>
    /// <param name="dayIndex">Zero-based index of the current day within its floor (0 to daysPerFloor-1).</param>
    /// <param name="floorIndex">Zero-based index of the current floor (0 to totalFloors-1).</param>
    /// <returns>A DayDifficultyData instance ready for consumption by GameManager.</returns>
    public DayDifficultyData ComputeDayData(int dayIndex, int floorIndex)
    {
        // rulesPerBin grows every 2 days so the player has time to adapt before new rules appear.
        // FloorToInt ensures the cap steps discretely (day 0→1, day 2→2, day 4→3 rules per bin).
        int computedRulesPerBin = minRulesPerBin + Mathf.FloorToInt(dayIndex * 0.5f);
        int clampedRulesPerBin  = Mathf.Clamp(computedRulesPerBin, minRulesPerBin, maxRulesPerBinCap);

        // maxRuleComplexity uses FloorToInt — not RoundToInt — for the same reason as rulesPerBin:
        // RoundToInt applies banker's rounding (round-half-to-even), which causes 1.5 → 2 but
        // 2.5 → 2, making the complexity target stagnate for two consecutive days and preventing
        // ComplexifyExistingRule from finding any candidate (rule.complexity < newComplexityTarget
        // is false when the target hasn't moved). FloorToInt increments the tier at predictable,
        // evenly spaced day boundaries so the target strictly increases every 1/complexityIncreasePerDay days.
        int computedComplexity = minRuleComplexity + Mathf.FloorToInt(dayIndex * complexityIncreasePerDay);
        int clampedComplexity  = Mathf.Clamp(computedComplexity, 1, 5);

        int numberOfBins = ComputeNumberOfBins(dayIndex, floorIndex);

        return new DayDifficultyData
        {
            rulesPerBin       = clampedRulesPerBin,
            maxRuleComplexity = clampedComplexity,
            numberOfBins      = numberOfBins
        };
    }

    /// <summary>
    /// Randomly returns either ComplexifyRule or AddNewRule.
    /// Random selection means the player cannot predict whether to expect a new rule or
    /// a harder version of an existing one, keeping each day feel surprising.
    /// </summary>
    /// <returns>A randomly selected DayEvolutionType.</returns>
    public DayEvolutionType SelectDayEvolution()
    {
        // Two possible values — a 50/50 split keeps either outcome equally likely
        // without imposing an alternating pattern the player could memorise.
        bool isComplexify = Random.value < 0.5f;
        return isComplexify ? DayEvolutionType.ComplexifyRule : DayEvolutionType.AddNewRule;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Derives the bin count from the absolute day position across the full game arc,
    /// optionally remapped through progressionCurve, then clamped to [minNumberOfBins, maxNumberOfBins].
    /// </summary>
    /// <param name="dayIndex">Zero-based day index within the current floor.</param>
    /// <param name="floorIndex">Zero-based floor index.</param>
    /// <returns>Number of active bins clamped to the configured bounds.</returns>
    private int ComputeNumberOfBins(int dayIndex, int floorIndex)
    {
        int totalDayCount       = totalFloors * daysPerFloor;
        int absoluteDayPosition = floorIndex * daysPerFloor + dayIndex;

        // Guard against division by zero when totalDayCount is misconfigured in the Inspector.
        float progressionIndex = totalDayCount > 0
            ? (float)absoluteDayPosition / totalDayCount
            : 0f;

        float clampedIndex = Mathf.Clamp01(progressionIndex);
        float curvedIndex  = EvaluateProgressionCurve(clampedIndex);

        return Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(minNumberOfBins, maxNumberOfBins, curvedIndex)),
            minNumberOfBins,
            maxNumberOfBins);
    }

    /// <summary>
    /// Remaps a 0–1 index through progressionCurve if one is assigned.
    /// Falls back to the original index when the curve is null or has no keys,
    /// keeping the system functional without mandatory curve configuration.
    /// </summary>
    /// <param name="linearIndex">The pre-clamped linear progression index.</param>
    /// <returns>The remapped index (still 0–1 when the curve is configured correctly).</returns>
    private float EvaluateProgressionCurve(float linearIndex)
    {
        // A curve with no keys returns 0 for all inputs, silently locking difficulty to minimum —
        // treat it as unassigned to prevent invisible difficulty clamping during playtesting.
        bool hasCurve = progressionCurve != null && progressionCurve.length > 0;

        if (!hasCurve)
            return linearIndex;

        return progressionCurve.Evaluate(linearIndex);
    }
}
