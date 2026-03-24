using UnityEngine;

/// <summary>
/// MonoBehaviour that computes floor N parameters by applying a configurable percentage delta
/// to floor N-1 parameters. All base values and all deltas are exposed in the Inspector so
/// designers can tune the difficulty curve without touching code.
/// Does NOT save data to disk, does NOT load scenes, and does NOT manage UI of any kind.
/// </summary>
public class FloorDifficultyProgression : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Base floor 0 parameters — starting values before any progression is applied
    // -------------------------------------------------------------------------

    [Header("Base Floor 1 Parameters")]

    /// <summary>Profitability percentage the player must stay above on floor 0.</summary>
    [SerializeField] private float baseFailThreshold = 20f;

    /// <summary>Duration in seconds of a single day on floor 0.
    /// Base value matches ProfitabilityManager default so floor 1 starts at 60s consistently.
    /// </summary>
    [SerializeField] private float baseDayDuration = 60f;

    /// <summary>Profitability points lost per second on floor 0.</summary>
    [SerializeField] private float baseDecayRate = 2f;

    /// <summary>Number of rules assigned to each bin on floor 0.</summary>
    [SerializeField] private int baseRulesPerBin = 1;

    /// <summary>Number of active sorting bins on floor 0.</summary>
    [SerializeField] private int baseNumberOfBins = 2;

    /// <summary>Maximum rule complexity allowed during generation on floor 0.</summary>
    [SerializeField] private int baseMaxRuleComplexity = 1;

    // -------------------------------------------------------------------------
    // Per-floor delta configuration — float fields use a percentage multiplier;
    // integer fields use a step-based counter because fractional bin/rule counts
    // are meaningless and rounding would introduce inconsistencies.
    // -------------------------------------------------------------------------

    [Header("Delta Per Floor (percentage increase)")]

    /// <summary>Fraction by which failThreshold grows each floor (e.g. 0.08 = +8%).</summary>
    [SerializeField] private float failThresholdDeltaPct = 0.08f;

    /// <summary>Fraction by which dayDuration grows each floor (e.g. 0.10 = +10%).</summary>
    [SerializeField] private float dayDurationDeltaPct = 0.10f;

    /// <summary>Fraction by which decayRatePerSecond grows each floor (e.g. 0.12 = +12%).</summary>
    [SerializeField] private float decayRateDeltaPct = 0.12f;

    /// <summary>
    /// One extra rule per bin is added every N floors.
    /// Integer values cannot meaningfully use a percentage delta — step-based increase
    /// keeps the count an exact integer and avoids rounding drift over many floors.
    /// </summary>
    [SerializeField] private int rulesPerBinDeltaEveryN = 2;

    /// <summary>
    /// One extra bin is activated every N floors, capped at maxBins.
    /// Step-based so the player always experiences a whole-number bin count.
    /// </summary>
    [SerializeField] private int binsMaxDeltaEveryN = 3;

    /// <summary>Maximum rule complexity increases by 1 every N floors.</summary>
    [SerializeField] private int complexityDeltaEveryN = 2;

    // -------------------------------------------------------------------------
    // Absolute caps — prevent any parameter from reaching an unplayable value
    // -------------------------------------------------------------------------

    [Header("Absolute Caps")]

    /// <summary>
    /// Hard ceiling for failThreshold. Uncapped values would eventually force the player
    /// to maintain near-perfect productivity at all times, making the game unwinnable.
    /// </summary>
    [SerializeField] private float maxFailThreshold = 60f;

    /// <summary>Hard ceiling for dayDuration in seconds. Beyond this the day feels endless.</summary>
    [SerializeField] private float maxDayDuration = 300f;

    /// <summary>
    /// Hard ceiling for decayRatePerSecond. Beyond this the profitability bar empties
    /// in seconds, making the game functionally unplayable regardless of skill.
    /// </summary>
    [SerializeField] private float maxDecayRate = 8f;

    /// <summary>Maximum number of simultaneously active bins. Hard-capped to 5.</summary>
    [SerializeField] private int maxBins = 5;

    /// <summary>Maximum rules per bin. Beyond 4, the display becomes unreadable on mobile.</summary>
    [SerializeField] private int maxRulesPerBin = 4;

    /// <summary>Maximum rule complexity. Mirrors the global complexity ceiling in RuleGenerator.</summary>
    [SerializeField] private int maxComplexity = 5;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates and returns a FloorSaveData populated entirely with base floor 0 values.
    /// Floor 0 always uses these values — it is never derived from a previous floor,
    /// so wasGenerated is false and the rules list is left empty for runtime generation.
    /// </summary>
    /// <returns>A FloorSaveData for floor 0 with all base parameters set.</returns>
    public FloorSaveData GenerateFloor1Data()
    {
        return new FloorSaveData
        {
            floorIndex         = 0,
            isCompleted        = false,
            wasGenerated       = false,
            failThreshold      = baseFailThreshold,
            dayDuration        = baseDayDuration,
            decayRatePerSecond = baseDecayRate,
            rulesPerBin        = baseRulesPerBin,
            numberOfBins       = baseNumberOfBins,
            maxRuleComplexity  = baseMaxRuleComplexity,
            floorSeed          = string.Empty
            // rules left as the default empty list — generated at runtime by RuleGenerator
        };
    }

    /// <summary>
    /// Computes and returns the parameters for the floor immediately after previousFloor,
    /// applying the configured percentage deltas and step-based integer increments.
    /// Falls back to GenerateFloor1Data() when previousFloor is null.
    /// Never saves the result to disk — saving is exclusively GameManager's responsibility.
    /// </summary>
    /// <param name="previousFloor">The completed floor whose parameters seed the computation.</param>
    /// <returns>A new FloorSaveData with all parameters computed for the next floor.</returns>
    public FloorSaveData GenerateNextFloorData(FloorSaveData previousFloor)
    {
        // Null guard: if called without a valid previous floor (e.g. first session edge case),
        // return base data rather than crashing — the caller gets a valid result in all paths.
        if (previousFloor == null)
            return GenerateFloor1Data();

        int nextFloorIndex = previousFloor.floorIndex + 1;

        // Float parameters: multiply by (1 + delta) each floor, then clamp to hard cap.
        // Multiplication compounds correctly over many floors without arithmetic drift.
        float nextFailThreshold = Mathf.Clamp(
            previousFloor.failThreshold * (1f + failThresholdDeltaPct),
            0f,
            maxFailThreshold);

        // Longer days require more sustained attention — capped so a session never becomes
        // impractically long on a mobile device where play sessions are typically short.
        float nextDayDuration = Mathf.Clamp(
            previousFloor.dayDuration * (1f + dayDurationDeltaPct),
            0f,
            maxDayDuration);

        // Faster decay means less margin for slow sorting; cap prevents instant-fail scenarios.
        float nextDecayRate = Mathf.Clamp(
            previousFloor.decayRatePerSecond * (1f + decayRateDeltaPct),
            0f,
            maxDecayRate);

        // Integer parameters: add 1 every N floors using modulo on the new floor index.
        // Modulo on nextFloorIndex (not previousFloor.floorIndex) ensures the step fires
        // correctly at the intended floor number regardless of where the chain started.
        int nextRulesPerBin = Mathf.Clamp(
            previousFloor.rulesPerBin + (nextFloorIndex % rulesPerBinDeltaEveryN == 0 ? 1 : 0),
            1,
            maxRulesPerBin);

        // New bin appears every N floors, giving the player time to adapt before the next
        // bin is added — front-loading bins would overwhelm players on early floors.
        int nextNumberOfBins = Mathf.Clamp(
            previousFloor.numberOfBins + (nextFloorIndex % binsMaxDeltaEveryN == 0 ? 1 : 0),
            1,
            maxBins);

        int nextMaxRuleComplexity = Mathf.Clamp(
            previousFloor.maxRuleComplexity + (nextFloorIndex % complexityDeltaEveryN == 0 ? 1 : 0),
            1,
            maxComplexity);

        return new FloorSaveData
        {
            floorIndex         = nextFloorIndex,
            isCompleted        = false,
            wasGenerated       = true,
            failThreshold      = nextFailThreshold,
            dayDuration        = nextDayDuration,
            decayRatePerSecond = nextDecayRate,
            rulesPerBin        = nextRulesPerBin,
            numberOfBins       = nextNumberOfBins,
            maxRuleComplexity  = nextMaxRuleComplexity,
            floorSeed          = string.Empty
            // rules left empty — generated at runtime by RuleGenerator using these parameters
        };
    }

    /// <summary>
    /// Returns the FloorSaveData for the requested floor index, loading it from disk when a save
    /// exists, or computing it by walking back through the inheritance chain when it does not.
    /// Recursion terminates unconditionally at floor 0, which always returns base data.
    /// Recursion depth equals floorIndex — for reasonable floor counts this is never a concern.
    /// </summary>
    /// <param name="floorIndex">Zero-based index of the floor whose data is needed.</param>
    /// <param name="saveSystem">The save system used to check and load persisted floors.</param>
    /// <returns>
    /// The exact saved FloorSaveData if the floor was previously completed,
    /// or a freshly computed FloorSaveData derived from the inheritance chain.
    /// </returns>
    public FloorSaveData GetOrGenerateFloorData(int floorIndex, FloorSaveSystem saveSystem)
    {
        Debug.Log("[FloorProgression] GetOrGenerateFloorData called for index: " + floorIndex);

        // null saveSystem = procedural mode (TowerScene). Skip all file-loading entirely
        // and fall straight through to the generation logic below.
        // Non-null saveSystem = story mode (DesignerTowerScene / GameScene replay path).
        // Saved data takes absolute priority there — returning it ensures replays are identical
        // to the original run and no re-computation overrides what the player experienced.
        if (saveSystem != null && saveSystem.FloorExists(floorIndex))
            return saveSystem.LoadFloor(floorIndex);

        // Base case: floor 0 always returns fixed base values, ending the recursion.
        if (floorIndex == 0)
            return GenerateFloor1Data();

        // Recursive case: build the previous floor's data first, then derive the current floor.
        // Walking backwards ensures the chain is correct even when intermediate floors
        // were never saved (e.g. after a DeleteAllSaves() reset or in procedural mode).
        FloorSaveData previousFloorData = GetOrGenerateFloorData(floorIndex - 1, saveSystem);

        // Null previous data means the recursive chain broke — fall back to floor 1 base data
        // rather than propagating a null into GenerateNextFloorData and crashing there.
        if (previousFloorData == null)
        {
            Debug.LogError("[FloorProgression] Previous floor data is null for index: " + (floorIndex - 1));
            return GenerateFloor1Data();
        }

        return GenerateNextFloorData(previousFloorData);
    }
}
