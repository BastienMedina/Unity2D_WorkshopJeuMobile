using System;

/// <summary>
/// Pure data container describing the rule-level difficulty parameters for a single game day.
/// Produced by DifficultyManager.ComputeDayData and consumed by GameManager to configure
/// RuleGenerator and BinLayoutManager.
/// Does NOT contain any game logic, MonoBehaviour references, or Unity lifecycle methods.
/// Separates daily rule data from floor-level profitability data so each system only
/// receives the values it actually needs.
/// </summary>
[Serializable]
public class DayDifficultyData
{
    /// <summary>
    /// How many rules are assigned to each active bin for this day.
    /// Grows every two days so the player has time to adapt before new rules appear.
    /// </summary>
    public int rulesPerBin;

    /// <summary>
    /// The highest complexity tier any rule may reach on this day (range 1–5).
    /// Rules at higher complexity tiers use harder logical structures (negation, branching).
    /// </summary>
    public int maxRuleComplexity;

    /// <summary>
    /// How many sorting bins are active during this day.
    /// More bins mean more destinations to consider simultaneously, increasing cognitive load.
    /// </summary>
    public int numberOfBins;
}
