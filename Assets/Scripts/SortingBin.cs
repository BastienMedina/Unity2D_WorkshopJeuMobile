using System;
using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// MonoBehaviour attached to a sorting bin UI element in the scene.
/// Stores the rules assigned to this bin, validates incoming documents against those rules
/// by dispatching to per-type private helpers, and renders the rule list in its label text.
/// Does NOT generate rules, spawn documents, handle drag events, or perform raycasting.
/// Does NOT use Fallback or NegativeMultiple — both are replaced by the complement rule system
/// which guarantees every document always has exactly one valid destination.
/// </summary>
public class SortingBin : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// Unique identifier for this bin. Must match one of the IDs supplied to RuleGenerator
    /// via availableBinIDs so that generated rules can resolve to a real scene object.
    /// </summary>
    [SerializeField] private string binID;

    /// <summary>Text component that displays the rules currently assigned to this bin.</summary>
    [SerializeField] private TextMeshProUGUI rulesDisplayText;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired immediately after a document drop is confirmed as valid.
    /// SortingBin must not reference ScoreManager directly — using an event keeps
    /// the two systems fully decoupled, allowing GameManager to wire them together
    /// without either class knowing the other exists.
    /// </summary>
    public event Action onValidDrop;

    /// <summary>
    /// Fired immediately after a document drop is confirmed as invalid.
    /// SortingBin must not reference ProfitabilityManager directly — the event keeps
    /// systems decoupled so GameManager is the sole orchestrator of penalty application.
    /// Only fires when a bin IS detected under the pointer; missed drops (no bin found)
    /// never reach ValidateDocument and therefore never fire this event.
    /// </summary>
    public event Action onInvalidDrop;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Rules assigned to this bin for the current game day.</summary>
    private List<RuleData> assignedRules = new List<RuleData>();

    /// <summary>
    /// Full rule set across all bins for the current day.
    /// Required exclusively by Fallback validation, which must check every rule in every bin —
    /// SortingBin must not query other bins directly, so GameManager injects this list.
    /// </summary>
    private List<RuleData> allActiveRules = new List<RuleData>();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the unique identifier of this bin.
    /// Used by external systems (e.g. GameManager) to match bins against generated rules.
    /// </summary>
    /// <returns>The bin's string ID as configured in the Inspector.</returns>
    public string GetBinID() => binID;

    /// <summary>
    /// Returns a copy of the rules currently assigned to this bin.
    /// Used by <see cref="RuleComplexityUpgrader"/> to inspect the current rule set
    /// without exposing the internal list directly.
    /// </summary>
    public List<RuleData> GetAssignedRules() => new List<RuleData>(assignedRules);

    /// <summary>
    /// Stores the provided rules as the active rule set for this bin and refreshes the display.
    /// Called by GameManager at the start of each game day.
    /// </summary>
    /// <param name="rules">The list of RuleData objects to assign to this bin.</param>
    public void AssignRules(List<RuleData> rules)
    {
        assignedRules = new List<RuleData>(rules);
        DisplayRules();
    }

    /// <summary>
    /// Stores the full cross-bin rule list needed for Fallback validation.
    /// SortingBin needs the complete rule set only for Fallback — it must not query
    /// other SortingBin instances directly, so GameManager injects the flat list here.
    /// Called by GameManager after distributing rules to all bins.
    /// </summary>
    /// <param name="allRules">Every active RuleData across all bins this day.</param>
    public void SetAllActiveRules(List<RuleData> allRules)
    {
        allActiveRules = new List<RuleData>(allRules);
    }

    /// <summary>
    /// Updates the rulesDisplayText with a human-readable summary of all assigned rules.
    /// Called internally after AssignRules; not intended for direct external calls.
    /// </summary>
    public void DisplayRules()
    {
        if (rulesDisplayText == null)
            return; // Guard: bin may be configured without a label in early prototyping.

        if (assignedRules.Count == 0)
        {
            rulesDisplayText.text = "No rules assigned.";
            return;
        }

        StringBuilder displayBuilder = new StringBuilder();

        foreach (RuleData rule in assignedRules)
        {
            // Display the pre-resolved human-readable sentence rather than the raw conditions.
            // conditions are internal logic data used for validation; displayText is what the player reads.
            // Re-resolving the template here every frame would duplicate work already done by RuleGenerator.
            displayBuilder.AppendLine($"• {rule.displayText}");
        }

        rulesDisplayText.text = displayBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// Checks whether the given document satisfies at least one of this bin's assigned rules.
    /// Dispatches validation to a private helper per RuleType to keep this method flat.
    /// Returns true on the first matching rule — "at least one rule" is sufficient.
    /// Fires onValidDrop when the document is accepted so GameManager can award a point
    /// without SortingBin needing to know ScoreManager exists.
    /// </summary>
    /// <param name="documentData">The document dropped onto this bin by the player.</param>
    /// <param name="activeRules">
    /// All rules across all bins this day. Required for Fallback validation,
    /// which must verify no other rule matches before accepting the document.
    /// </param>
    /// <returns>True if the document matches at least one rule; false otherwise.</returns>
    public bool ValidateDocument(DocumentData documentData, List<RuleData> activeRules)
    {
        foreach (RuleData rule in assignedRules)
        {
            bool isRuleMatched = DispatchValidation(rule, documentData, activeRules);

            // Return on the first successful match — "at least one rule" is the design intent.
            // A document legitimately belongs here if it satisfies any single criterion,
            // allowing overlapping rule sets without forcing strict global consistency.
            if (!isRuleMatched)
                continue;

            // Notify listeners (GameManager → ScoreManager) that a point was earned.
            // Fired here rather than in DraggableDocument so that the bin — the authority
            // on validity — is the source of truth for scoring events.
            onValidDrop?.Invoke();
            return true;
        }

        // No rule matched — the document was dropped in the wrong bin.
        // Fire onInvalidDrop before returning so GameManager can apply the penalty in the same frame.
        // onInvalidDrop only fires here, inside ValidateDocument, which is only called when a bin IS
        // detected — missed drops (pointer in empty space) never call this method, so the penalty
        // is never triggered by a cancelled drag or a miss. Only wrong-bin drops are penalised.
        onInvalidDrop?.Invoke();
        return false;
    }

    // -------------------------------------------------------------------------
    // Validation dispatch
    // -------------------------------------------------------------------------

    /// <summary>
    /// Routes validation to the correct private helper based on the rule's type.
    /// Centralising dispatch here keeps ValidateDocument free of type-switch logic.
    /// </summary>
    private bool DispatchValidation(RuleData rule, DocumentData documentData, List<RuleData> activeRules)
    {
        return rule.ruleType switch
        {
            RuleType.PositiveForced                 => ValidatePositiveForced(documentData, rule),
            RuleType.PositiveExclusive              => ValidatePositiveExclusive(documentData, rule),
            RuleType.ConditionalBranch              => ValidateConditionalBranch(documentData, rule),
            RuleType.PositiveDouble                 => ValidatePositiveDouble(documentData, rule),
            RuleType.NegativeSimple                 => ValidateNegativeSimple(documentData, rule),
            RuleType.PositiveWithNegative           => ValidatePositiveWithNegative(documentData, rule),
            RuleType.ComplementPositiveForced       => ValidateComplementPositiveForced(documentData, rule),
            RuleType.ComplementPositiveExclusive    => ValidateComplementPositiveExclusive(documentData, rule),
            RuleType.ComplementNegativeSimple       => ValidateComplementNegativeSimple(documentData, rule),
            RuleType.ComplementPositiveWithNegative => ValidateComplementPositiveWithNegative(documentData, rule),
            RuleType.PositiveOr                     => ValidatePositiveOr(documentData, rule),
            RuleType.ComplementPositiveOr           => ValidateComplementPositiveOr(documentData, rule),
            _                                       => false
        };
    }

    // -------------------------------------------------------------------------
    // Per-type validation helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// PositiveForced: document is valid if it contains conditionA, regardless of any other specificities.
    /// "Forced" means presence of the one condition overrides all other checks — the simplest positive match.
    /// </summary>
    private bool ValidatePositiveForced(DocumentData documentData, RuleData rule)
    {
        return documentData.specificities.Contains(rule.conditionA);
    }

    /// <summary>
    /// PositiveExclusive: document is valid only if it contains conditionA AND has no other specificities.
    /// The document must have exactly this one trait — additional specificities disqualify it.
    /// </summary>
    private bool ValidatePositiveExclusive(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA   = documentData.specificities.Contains(rule.conditionA);
        bool hasOnlyOneEntry = documentData.specificities.Count == 1;

        // Both checks are required: conditionA must be present AND it must be the only specificity.
        // Checking only the count would match any single-specificity document regardless of its value.
        return hasConditionA && hasOnlyOneEntry;
    }

    /// <summary>
    /// ConditionalBranch: routes to targetBinID when both conditionA and conditionB are present,
    /// or to secondaryBinID when conditionA is present but conditionB is absent.
    /// Returns false if conditionA is not present at all, or if the relevant bin ID is unresolved.
    /// ConditionalBranch is self-complementary — both outcomes are handled within this single rule.
    /// </summary>
    private bool ValidateConditionalBranch(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA = documentData.specificities.Contains(rule.conditionA);

        // conditionA is the entry condition — without it, neither branch applies.
        if (!hasConditionA)
            return false;

        bool hasConditionB = documentData.specificities.Contains(rule.conditionB);

        if (hasConditionB)
            return binID == rule.targetBinID;

        // conditionA present but conditionB absent → routes to secondaryBinID.
        // Guard against a missing secondaryBinID: a missing ID would silently route to no bin,
        // producing a document the player can never sort correctly.
        if (string.IsNullOrEmpty(rule.secondaryBinID))
        {
            Debug.LogWarning($"[SortingBin] ConditionalBranch rule has no secondaryBinID. Rule targetBinID: {rule.targetBinID}");
            return false;
        }

        return binID == rule.secondaryBinID;
    }

    /// <summary>
    /// PositiveDouble: document is valid in this bin if it contains BOTH conditionA AND conditionB.
    ///
    /// When secondaryBinID is empty (library-converter path — "Et" / "Sauf" branch):
    ///   The rule is a plain two-condition AND gate targeting only targetBinID.
    ///   The document must have both conditions to be valid here; having only conditionA
    ///   is handled by a sibling PositiveWithNegative rule on a different bin.
    ///
    /// When secondaryBinID is set (legacy self-complementary path):
    ///   conditionA + conditionB → targetBinID
    ///   conditionA without conditionB → secondaryBinID
    ///   Both branches are handled by this single rule with two target bins.
    /// </summary>
    private bool ValidatePositiveDouble(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA = documentData.specificities.Contains(rule.conditionA);

        // conditionA must be present — PositiveDouble is meaningless without the primary condition.
        if (!hasConditionA)
            return false;

        bool hasConditionB = documentData.specificities.Contains(rule.conditionB);

        // Library-converter path: no secondaryBinID means this is a plain AND gate.
        // The document must have both conditions; conditionA-alone is not accepted here.
        if (string.IsNullOrEmpty(rule.secondaryBinID))
            return hasConditionB && binID == rule.targetBinID;

        // Legacy self-complementary path: both branches are handled here.
        if (hasConditionB)
            return binID == rule.targetBinID;

        return binID == rule.secondaryBinID;
    }

    /// <summary>
    /// NegativeSimple: document is valid if it does NOT contain conditionA.
    /// Absence of the condition is the sorting trigger — the inverse of a positive check.
    /// </summary>
    private bool ValidateNegativeSimple(DocumentData documentData, RuleData rule)
    {
        return !documentData.specificities.Contains(rule.conditionA);
    }

    /// <summary>
    /// PositiveWithNegative: document is valid if it contains conditionA AND does NOT contain conditionB.
    /// The document must have the primary trait but explicitly lack the secondary one.
    /// This pair with ComplementPositiveWithNegative guarantees any document with conditionA
    /// always has exactly one valid destination.
    /// </summary>
    private bool ValidatePositiveWithNegative(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA = documentData.specificities.Contains(rule.conditionA);

        // conditionA must be present — without it this rule does not apply at all.
        if (!hasConditionA)
            return false;

        // conditionB must be absent — its presence routes the document to the complement bin instead.
        bool hasConditionB = documentData.specificities.Contains(rule.conditionB);
        return !hasConditionB;
    }

    /// <summary>
    /// ComplementPositiveForced: document is valid if it does NOT contain conditionA.
    /// This is the complement of PositiveForced — together they cover all possible documents.
    /// </summary>
    private bool ValidateComplementPositiveForced(DocumentData documentData, RuleData rule)
    {
        // Complement of PositiveForced: the absence of conditionA is the routing trigger.
        return !documentData.specificities.Contains(rule.conditionA);
    }

    /// <summary>
    /// ComplementPositiveExclusive: document is valid if it contains conditionA
    /// AND has at least one other specificity (Count > 1).
    /// Complement means conditionA is present but the document is not exclusive —
    /// it carries additional specificities beyond conditionA.
    /// </summary>
    private bool ValidateComplementPositiveExclusive(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA           = documentData.specificities.Contains(rule.conditionA);
        bool hasAdditionalSpecificity = documentData.specificities.Count > 1;

        // conditionA must be present and there must be at least one additional specificity —
        // a document with only conditionA belongs in the primary PositiveExclusive bin, not here.
        return hasConditionA && hasAdditionalSpecificity;
    }

    /// <summary>
    /// ComplementNegativeSimple: document is valid if it contains conditionA.
    /// Complement of NegativeSimple is simply having conditionA — the two rules together cover all cases.
    /// </summary>
    private bool ValidateComplementNegativeSimple(DocumentData documentData, RuleData rule)
    {
        // Simply having conditionA is the inverse of the NegativeSimple rule which requires its absence.
        return documentData.specificities.Contains(rule.conditionA);
    }

    /// <summary>
    /// ComplementPositiveWithNegative: document is valid if it contains BOTH conditionA AND conditionB.
    /// Covers the case where both conditions are present — the primary PositiveWithNegative rule
    /// handles conditionA-without-conditionB, and this complement handles conditionA-with-conditionB,
    /// guaranteeing every document that has conditionA always has exactly one valid destination.
    /// </summary>
    private bool ValidateComplementPositiveWithNegative(DocumentData documentData, RuleData rule)
    {
        bool hasConditionA = documentData.specificities.Contains(rule.conditionA);
        bool hasConditionB = documentData.specificities.Contains(rule.conditionB);

        // Both conditions must be present — the complement covers only the "both present" case.
        return hasConditionA && hasConditionB;
    }

    /// <summary>
    /// PositiveOr: document is valid if it contains conditionA OR conditionB (at least one).
    /// Introduced to support the "Ou" connector from the Rule Library structured editor.
    /// </summary>
    private bool ValidatePositiveOr(DocumentData documentData, RuleData rule)
    {
        return documentData.specificities.Contains(rule.conditionA)
            || documentData.specificities.Contains(rule.conditionB);
    }

    /// <summary>
    /// ComplementPositiveOr: document is valid if it contains NEITHER conditionA NOR conditionB.
    /// The complement of PositiveOr — together the pair guarantees every document has a destination.
    /// </summary>
    private bool ValidateComplementPositiveOr(DocumentData documentData, RuleData rule)
    {
        return !documentData.specificities.Contains(rule.conditionA)
            && !documentData.specificities.Contains(rule.conditionB);
    }
}
