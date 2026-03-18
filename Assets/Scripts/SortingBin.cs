using System.Collections.Generic;
using System.Linq;
using System.Text;
using TMPro;
using UnityEngine;

/// <summary>
/// MonoBehaviour attached to a sorting bin UI element in the scene.
/// Stores the rules assigned to this bin, validates incoming documents against those rules,
/// and renders the rule list in its label text.
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
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Rules assigned to this bin for the current game day.</summary>
    private List<RuleData> assignedRules = new List<RuleData>();

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
            string conditionList = string.Join(", ", rule.conditions);
            displayBuilder.AppendLine($"• {conditionList}");
        }

        rulesDisplayText.text = displayBuilder.ToString().TrimEnd();
    }

    /// <summary>
    /// Checks whether the given document satisfies at least one of the assigned rules.
    /// A rule is satisfied when the document's specificities contain ALL of the rule's conditions.
    /// </summary>
    /// <param name="documentData">The document dropped onto this bin by the player.</param>
    /// <returns>
    /// True if the document matches at least one rule; false if no rule matched.
    /// </returns>
    public bool ValidateDocument(DocumentData documentData)
    {
        foreach (RuleData rule in assignedRules)
        {
            bool isRuleMatched = rule.conditions.All(condition =>
                documentData.specificities.Contains(condition));

            // Return on the first successful match rather than collecting all matches.
            // The game design requires "at least one rule" to be satisfied, not "all rules" —
            // a document legitimately belongs to a bin if it fits any single sorting criterion,
            // allowing multiple overlapping rule sets without forcing strict global consistency.
            if (isRuleMatched)
                return true;
        }

        return false;
    }
}
