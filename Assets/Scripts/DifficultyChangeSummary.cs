using System;

/// <summary>
/// Pure data container that describes every difficulty change that occurred during
/// a single InitializeDay call. Built by GameManager and passed to DayTransitionManager
/// so the transition overlay can show the player a human-readable summary of what changed.
/// Does NOT contain any logic, MonoBehaviour references, or Unity lifecycle methods.
/// Keeping it separate from both systems prevents coupling — GameManager builds it,
/// DayTransitionManager reads it, and neither knows the internal details of the other.
/// </summary>
[Serializable]
public class DifficultyChangeSummary
{
    /// <summary>True when BinLayoutManager activated a new bin slot this day.</summary>
    public bool newBinAdded;

    /// <summary>True when RuleGenerator added a new rule to an existing or new bin this day.</summary>
    public bool newRuleAdded;

    /// <summary>True when RuleGenerator complexified (upgraded) an existing rule this day.</summary>
    public bool ruleComplexified;

    /// <summary>True when FloorProgressionManager applied a floor bonus at the start of this floor.</summary>
    public bool floorBonusApplied;

    /// <summary>
    /// Which stat was boosted by the floor bonus. Only meaningful when floorBonusApplied is true.
    /// Stored here so DayTransitionManager can display the bonus without calling back into
    /// FloorProgressionManager, which would create a hidden reverse dependency.
    /// </summary>
    public FloorBonusType floorBonusType;

    /// <summary>
    /// Human-readable description of which rule was complexified, shown with a "~" prefix.
    /// Example: "Rule on bin_left_top is now harder"
    /// </summary>
    public string complexifiedRuleDescription;

    /// <summary>
    /// Human-readable description of the new rule that was added, shown with a "+" prefix.
    /// Example: "New rule added to bin_right_top"
    /// </summary>
    public string newRuleDescription;

    /// <summary>
    /// Human-readable description of the bin that was activated, shown with a "+" prefix.
    /// Example: "New bin: bin_left_bottom"
    /// </summary>
    public string newBinDescription;

    /// <summary>
    /// Human-readable description of the floor bonus that was applied.
    /// Example: "Productivity decays faster"
    /// </summary>
    public string floorBonusDescription;
}
