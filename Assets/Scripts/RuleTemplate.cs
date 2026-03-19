using System;

/// <summary>
/// Pure data container representing a single fill-in-the-blank sentence template
/// used to render a sorting rule in human-readable form.
/// Does NOT contain any substitution logic, game state, or Unity lifecycle methods.
/// </summary>
[Serializable]
public class RuleTemplate
{
    /// <summary>
    /// The sentence with {0} and optionally {1} as placeholders for specificities.
    /// Example (1 slot): "If the document has {0}, it must go here"
    /// Example (2 slots): "If {0} and {1} are present go here, otherwise other bin"
    /// Example (0 slots): "If no other rule applies, place the document here"
    /// </summary>
    public string templateText = string.Empty;

    /// <summary>
    /// Number of specificity slots this template expects.
    /// Stored explicitly so RuleGenerator can match a template to a rule's condition count
    /// without parsing the string — string parsing would be fragile and locale-sensitive.
    /// Slot counts per rule type:
    ///   PositiveForced           → 1  (conditionA)
    ///   PositiveExclusive        → 1  (conditionA)
    ///   ConditionalBranch        → 2  (conditionA + conditionB)
    ///   PositiveDouble           → 2  (conditionA + conditionB)
    ///   NegativeSimple           → 1  (conditionA)
    ///   PositiveWithNegative     → 2  (conditionA + conditionB)
    ///   ComplementPositiveForced     → 1
    ///   ComplementPositiveExclusive  → 1
    ///   ComplementNegativeSimple     → 1
    ///   ComplementPositiveWithNegative → 2
    /// </summary>
    public int requiredSpecificityCount = 1;

    /// <summary>
    /// The rule type this template is authored for.
    /// RuleGenerator must select templates that match the generated rule type — mixing templates
    /// across types would produce sentences that describe the wrong validation logic to the player.
    /// </summary>
    public RuleType ruleType;
}
