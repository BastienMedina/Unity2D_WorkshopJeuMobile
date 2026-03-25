/// <summary>
/// Enumerates all sorting rule types available in the game.
/// Each value maps to a distinct validation logic.
/// Does NOT contain any validation code — logic lives in SortingBin's private helpers.
/// </summary>
public enum RuleType
{
    /// <summary>
    /// Simple: document contains conditionA → valid regardless of any other specificities.
    /// Complexity base: 1.
    /// </summary>
    Simple,

    /// <summary>
    /// Multiple: document contains conditionA AND conditionB → valid.
    /// Complexity base: 2.
    /// </summary>
    Multiple,

    /// <summary>
    /// Branch: document contains conditionA but NOT conditionB → valid.
    /// Complexity base: 3.
    /// </summary>
    Branch,
}
