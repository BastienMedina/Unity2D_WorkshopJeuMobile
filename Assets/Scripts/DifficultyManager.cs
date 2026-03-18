using UnityEngine;

/// <summary>
/// Defines how the day's rule set should evolve compared to the previous day.
/// Returned by DifficultyManager.OnNewDay and interpreted by GameManager.
/// </summary>
public enum DayEvolutionType
{
    /// <summary>An existing rule should gain one additional condition.</summary>
    ComplexifyRule,

    /// <summary>A brand-new rule should be added to the active set.</summary>
    AddNewRule
}

/// <summary>
/// MonoBehaviour that computes DifficultySettings and DayEvolutionType from a progression index.
/// Translates abstract difficulty position (0–1) into concrete numeric parameters
/// and day-to-day evolution decisions, then exposes results for GameManager to consume.
/// Does NOT apply settings to any system, does NOT generate rules, does NOT spawn documents,
/// and holds NO references to RuleGenerator, DocumentSpawner, or SortingBin.
/// </summary>
public class DifficultyManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-exposed progression control
    // -------------------------------------------------------------------------

    [Header("Progression")]

    /// <summary>
    /// Abstract position in the full game arc. 0.0 = easiest, 1.0 = hardest.
    /// Can be set manually in the Inspector for playtesting, or computed automatically
    /// from level and day indices when useAutomaticProgression is enabled.
    /// </summary>
    [SerializeField] private float progressionIndex = 0f;

    /// <summary>
    /// When true, progressionIndex is recomputed from level/day numbers each day.
    /// When false, the manually assigned progressionIndex is used as-is.
    /// </summary>
    [SerializeField] private bool useAutomaticProgression = true;

    // -------------------------------------------------------------------------
    // Progression structure — bounds declared as fields, never hardcoded
    // -------------------------------------------------------------------------

    [Header("Automatic Progression Structure")]

    /// <summary>Total number of levels in the game. Used to normalise the progression index.</summary>
    [SerializeField] private int totalLevels = 5;

    /// <summary>Number of days per level. Used to normalise the progression index.</summary>
    [SerializeField] private int daysPerLevel = 5;

    // -------------------------------------------------------------------------
    // Spawn interval bounds
    // -------------------------------------------------------------------------

    [Header("Spawn Interval Bounds")]

    /// <summary>Slowest spawn rate in seconds, applied at the start of the game (easy).</summary>
    [SerializeField] private float maxSpawnInterval = 10f;

    /// <summary>Fastest spawn rate in seconds, applied at the end of the game (hard).</summary>
    [SerializeField] private float minSpawnInterval = 2f;

    // -------------------------------------------------------------------------
    // Rule count bounds
    // -------------------------------------------------------------------------

    [Header("Rule Count Bounds")]

    /// <summary>Minimum number of active rules (at the easiest difficulty).</summary>
    [SerializeField] private int minNumberOfRules = 1;

    /// <summary>Maximum number of active rules (at the hardest difficulty).</summary>
    [SerializeField] private int maxNumberOfRules = 6;

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

    /// <summary>Highest allowed maxRuleComplexity value (at the hardest difficulty).</summary>
    [SerializeField] private int maxRuleComplexity = 5;

    // -------------------------------------------------------------------------
    // Optional progression curve
    // -------------------------------------------------------------------------

    [Header("Progression Curve (optional)")]

    /// <summary>
    /// Optional AnimationCurve that remaps the linear 0–1 progression index before Lerp.
    /// Allows easing the difficulty ramp (e.g. slow start, steep mid-game spike).
    /// When left unassigned, interpolation remains linear.
    /// </summary>
    [SerializeField] private AnimationCurve progressionCurve;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>The most recently computed DifficultySettings, cached for retrieval by GameManager.</summary>
    private DifficultySettings currentSettings;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes a fully populated DifficultySettings object from the given progression index.
    /// If a progressionCurve is assigned, the index is remapped through it before interpolation.
    /// All settings are derived exclusively from the declared [SerializeField] bound fields —
    /// literal numbers inside the method body are forbidden to keep tuning centralised in
    /// the Inspector and avoid hidden magic values scattered across the codebase.
    /// </summary>
    /// <param name="index">
    /// A 0–1 value representing position in the full game progression.
    /// Values outside this range are clamped before use.
    /// </param>
    /// <returns>A DifficultySettings instance ready for consumption by GameManager.</returns>
    public DifficultySettings ComputeSettings(float index)
    {
        // Clamp before any computation: Mathf.Lerp and AnimationCurve.Evaluate both
        // produce out-of-range results when given values outside [0, 1], which would
        // silently break spawn intervals and rule counts in edge cases.
        float clampedIndex = Mathf.Clamp01(index);

        float curvedIndex = EvaluateProgressionCurve(clampedIndex);

        DifficultySettings computedSettings = new DifficultySettings
        {
            // Spawn interval decreases as difficulty rises, so Lerp goes from max to min.
            spawnInterval    = Mathf.Clamp(Mathf.Lerp(maxSpawnInterval, minSpawnInterval, curvedIndex),
                                           minSpawnInterval, maxSpawnInterval),

            numberOfRules    = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minNumberOfRules, maxNumberOfRules, curvedIndex)),
                                           minNumberOfRules, maxNumberOfRules),

            numberOfBins     = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minNumberOfBins, maxNumberOfBins, curvedIndex)),
                                           minNumberOfBins, maxNumberOfBins),

            maxRuleComplexity = Mathf.Clamp(Mathf.RoundToInt(Mathf.Lerp(minRuleComplexity, maxRuleComplexity, curvedIndex)),
                                            minRuleComplexity, maxRuleComplexity)
        };

        currentSettings = computedSettings;
        return computedSettings;
    }

    /// <summary>
    /// Processes the start of a new day: optionally recomputes progressionIndex from
    /// level and day positions, then determines how the rule set should evolve.
    /// Results are returned to the caller (GameManager) — this method applies nothing directly.
    /// </summary>
    /// <param name="dayIndex">Zero-based index of the current day within its level (0 to daysPerLevel-1).</param>
    /// <param name="levelIndex">Zero-based index of the current level (0 to totalLevels-1).</param>
    /// <returns>
    /// A tuple containing the computed DifficultySettings and the DayEvolutionType
    /// that GameManager should apply when building the day's rule set.
    /// </returns>
    public (DifficultySettings settings, DayEvolutionType evolutionType) OnNewDay(int dayIndex, int levelIndex)
    {
        if (useAutomaticProgression)
        {
            // Normalise the day's absolute position across the full game arc.
            // Formula: (levelIndex * daysPerLevel + dayIndex) / (totalLevels * daysPerLevel)
            // This maps day 0 of level 0 to 0.0 and the final day of the final level to ~1.0,
            // producing a uniform difficulty curve regardless of level/day structure changes.
            int absoluteDayPosition = levelIndex * daysPerLevel + dayIndex;
            int totalDayCount       = totalLevels * daysPerLevel;
            progressionIndex        = (float)absoluteDayPosition / totalDayCount;
        }

        DifficultySettings computedSettings = ComputeSettings(progressionIndex);

        // Alternate the evolution type by day parity to create a predictable rhythm:
        // even days deepen existing rules (more conditions), odd days widen the rule set
        // (more rules). This avoids two consecutive days of the same challenge type.
        DayEvolutionType evolutionType = (dayIndex % 2 == 0)
            ? DayEvolutionType.ComplexifyRule
            : DayEvolutionType.AddNewRule;

        return (computedSettings, evolutionType);
    }

    /// <summary>
    /// Returns the DifficultySettings produced by the most recent ComputeSettings call.
    /// Allows GameManager to retrieve the current state without triggering a recomputation.
    /// Returns null if ComputeSettings has never been called.
    /// </summary>
    /// <returns>The last computed DifficultySettings, or null if none exist yet.</returns>
    public DifficultySettings GetCurrentSettings()
    {
        return currentSettings;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Remaps a 0–1 index through progressionCurve if one is assigned.
    /// Falls back to the original index silently when the curve is null or has no keys,
    /// so the system remains functional without mandatory curve configuration.
    /// </summary>
    /// <param name="linearIndex">The pre-clamped linear progression index to evaluate.</param>
    /// <returns>The remapped index, still in 0–1 range when the curve is configured correctly.</returns>
    private float EvaluateProgressionCurve(float linearIndex)
    {
        // A curve with no keys is treated as unassigned — Evaluate would return 0
        // for all inputs, silently locking difficulty to its minimum.
        bool hasCurve = progressionCurve != null && progressionCurve.length > 0;

        if (!hasCurve)
            return linearIndex;

        return progressionCurve.Evaluate(linearIndex);
    }
}
