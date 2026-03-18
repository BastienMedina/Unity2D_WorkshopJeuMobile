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
    /// The sentence with {0} and optionally {1} or {2} as placeholders for specificities.
    /// Example (1 slot):  "If the document has {0}, send it here"
    /// Example (2 slots): "Documents that are {0} and {1} go here"
    /// Example (0 slots): "If no other rule applies, place the document here"
    /// </summary>
    public string templateText = string.Empty;

    /// <summary>
    /// Number of specificity slots this template expects.
    /// Stored explicitly so RuleGenerator can match a template to a rule's condition count without
    /// parsing the string itself — string parsing would be fragile and locale-sensitive.
    /// ConditionalBranch uses 2 (conditionA + conditionB), NegativeMultiple uses 3, Fallback uses 0.
    /// </summary>
    public int requiredSpecificityCount = 1;

    /// <summary>
    /// The rule type this template is authored for.
    /// RuleGenerator must select templates that match the generated rule type — mixing templates
    /// across types would produce sentences that describe the wrong validation logic to the player.
    /// </summary>
    public RuleType ruleType;
}
