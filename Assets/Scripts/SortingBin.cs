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
            RuleType.PositiveForced    => ValidatePositiveForced(documentData, rule),
            RuleType.PositiveExclusive => ValidatePositiveExclusive(documentData, rule),
            RuleType.ConditionalBranch => ValidateConditionalBranch(documentData, rule),
            RuleType.Fallback          => ValidateFallback(documentData, activeRules),
            RuleType.NegativeSimple    => ValidateNegativeSimple(documentData, rule),
            RuleType.NegativeMultiple  => ValidateNegativeMultiple(documentData, rule),
            _                          => false
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
        bool hasConditionA    = documentData.specificities.Contains(rule.conditionA);
        bool hasOnlyOneEntry  = documentData.specificities.Count == 1;

        // Both checks are required: conditionA must be present AND it must be the only specificity.
        // Checking only the count would match any single-specificity document regardless of its value.
        return hasConditionA && hasOnlyOneEntry;
    }

    /// <summary>
    /// ConditionalBranch: routes to targetBinID when both conditionA and conditionB are present,
    /// or to secondaryBinID when conditionA is present but conditionB is absent.
    /// Returns false if conditionA is not present at all, or if the relevant bin ID is unresolved.
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
    /// Fallback: document is valid only when it matches no other rule across all active bins.
    /// Iterates allActiveRules, skipping other Fallback rules to prevent circular evaluation —
    /// a Fallback checking another Fallback would produce infinite mutual exclusion.
    /// </summary>
    private bool ValidateFallback(DocumentData documentData, List<RuleData> activeRules)
    {
        foreach (RuleData otherRule in activeRules)
        {
            // Skip other Fallback rules: Fallback must only check non-Fallback rules to determine
            // whether the document genuinely belongs nowhere else. Checking another Fallback
            // would create circular logic where two Fallbacks invalidate each other endlessly.
            if (otherRule.ruleType == RuleType.Fallback)
                continue;

            bool isMatchedByOtherRule = DispatchValidation(otherRule, documentData, activeRules);

            // If any non-Fallback rule matches, the document is not a "leftover" —
            // the Fallback bin should not accept it.
            if (isMatchedByOtherRule)
                return false;
        }

        return true;
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
    /// NegativeMultiple: document is valid if it contains NONE of the entries in conditionsList.
    /// All listed conditions must be absent — even one present condition disqualifies the document.
    /// </summary>
    private bool ValidateNegativeMultiple(DocumentData documentData, RuleData rule)
    {
        foreach (string bannedCondition in rule.conditionsList)
        {
            // Return false immediately on the first match — there is no need to check further
            // once we know the document contains at least one banned condition.
            if (documentData.specificities.Contains(bannedCondition))
                return false;
        }

        return true;
    }
}
