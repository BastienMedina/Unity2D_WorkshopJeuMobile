using System;

/// <summary>
/// Pure data container describing the difficulty parameters for a single game day.
/// Produced by DifficultyManager and consumed by GameManager to configure other systems.
/// Does NOT contain any computation logic, Unity lifecycle methods, or references to scene objects.
/// </summary>
[Serializable]
public class DifficultySettings
{
    /// <summary>
    /// Seconds between consecutive document spawns.
    /// Spawn rate is no longer difficulty-driven — the queue-size-based system ensures
    /// constant document availability by keeping the stack at targetStackSize at all times.
    /// Kept here to avoid breaking serialised assets that may still reference this field.
    /// </summary>
    public float spawnInterval;

    /// <summary>How many sorting rules are active during the day.</summary>
    public int numberOfRules;

    /// <summary>How many sorting bins are active during the day.</summary>
    public int numberOfBins;

    /// <summary>Maximum number of conditions allowed per generated rule (1–5).</summary>
    public int maxRuleComplexity;
}
