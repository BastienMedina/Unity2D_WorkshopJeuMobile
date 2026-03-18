/// <summary>
/// Enumerates all sorting rule types available in the game.
/// Each value maps to a distinct validation logic and a distinct complexity base score.
/// Does NOT contain any validation code — logic lives in SortingBin's private helpers.
/// </summary>
public enum RuleType
{
    /// <summary>
    /// Document contains conditionA → always valid, regardless of any other specificities.
    /// Complexity base: 2.
    /// </summary>
    PositiveForced,

    /// <summary>
    /// Document contains conditionA AND has no other specificities → valid.
    /// Complexity base: 1.
    /// </summary>
    PositiveExclusive,

    /// <summary>
    /// Document contains conditionA AND conditionB → targetBinID.
    /// Document contains conditionA BUT NOT conditionB → secondaryBinID.
    /// Complexity base: 3.
    /// </summary>
    ConditionalBranch,

    /// <summary>
    /// Document does not match ANY rule of ANY active bin → valid.
    /// Complexity base: 2.
    /// </summary>
    Fallback,

    /// <summary>
    /// Document does NOT contain conditionA → valid.
    /// Complexity base: 2.
    /// </summary>
    NegativeSimple,

    /// <summary>
    /// Document contains NONE of the entries in conditionsList → valid.
    /// Complexity base: 3.
    /// </summary>
    NegativeMultiple
}
