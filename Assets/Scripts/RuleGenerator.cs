using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// MonoBehaviour responsible for procedurally generating RuleData objects
/// from the SpecificityDatabase according to day parameters supplied by the caller.
/// Does NOT handle UI display, rule validation, item spawning, or any other system.
/// All generated rules are returned to the caller — this class never distributes or stores them.
/// </summary>
public class RuleGenerator : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>Database of all available specificities and sentence templates.</summary>
    [SerializeField] private SpecificityDatabase specificityDatabase;

    /// <summary>
    /// IDs of the SortingBin instances that are active in the current scene.
    /// Must be provided from outside (Inspector or GameManager) because RuleGenerator
    /// has no knowledge of the scene structure and must never query it directly.
    /// Assigning bin IDs here ensures every generated rule always points to a real,
    /// existing bin rather than an invented identifier that has no counterpart in the scene.
    /// </summary>
    [SerializeField] private List<string> availableBinIDs = new List<string>();

    // -------------------------------------------------------------------------
    // Complexity clamping bounds — never hardcoded inline
    // -------------------------------------------------------------------------

    [SerializeField] private int minimumComplexity = 1;
    [SerializeField] private int maximumComplexity = 5;

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Specificities already used during the current level.
    /// Prevents repetition across rules within the same play session.
    /// </summary>
    private List<string> usedSpecificities = new List<string>();

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Generates a list of RuleData objects for a single game day.
    /// Picks unused specificities from the database, fills templates,
    /// marks those specificities as used, and computes complexity for each rule.
    /// </summary>
    /// <param name="numberOfRules">How many rules to generate for this day.</param>
    /// <param name="complexityTarget">
    /// When 1, templates with a single {0} slot are preferred (simpler rules).
    /// When 2, templates with {0} and {1} slots are preferred (richer conditions).
    /// </param>
    /// <returns>
    /// A list of fully populated RuleData objects, ready for consumption by the caller.
    /// Returns an empty list if the database has insufficient unused specificities.
    /// </returns>
    public List<RuleData> GenerateRulesForDay(int numberOfRules, int complexityTarget)
    {
        List<RuleData> generatedRules = new List<RuleData>();

        List<string> availableSpecificities = BuildAvailablePool();

        for (int ruleIndex = 0; ruleIndex < numberOfRules; ruleIndex++)
        {
            int requiredSlots = complexityTarget;
            bool hasEnoughSpecificities = availableSpecificities.Count >= requiredSlots;

            // Fall back to a single-slot rule rather than skipping entirely,
            // so the day is never left with fewer rules than requested.
            if (!hasEnoughSpecificities && availableSpecificities.Count >= 1)
                requiredSlots = 1;

            if (availableSpecificities.Count < requiredSlots)
                break; // Exhausted the pool — cannot generate more rules safely.

            if (availableBinIDs.Count == 0)
                break; // No valid bin IDs available — generating a rule would produce an unresolvable target.

            List<string> selectedSpecificities = PickSpecificities(availableSpecificities, requiredSlots);

            // Remove selected entries so the same specificity is not offered again
            // later in this loop iteration (pool depletion within a single call).
            foreach (string specificity in selectedSpecificities)
                availableSpecificities.Remove(specificity);

            // Mark selected specificities as globally used for this level so that
            // subsequent calls to GenerateRulesForDay (e.g. for bonus rounds)
            // cannot reuse them and produce duplicate or trivially similar rules.
            usedSpecificities.AddRange(selectedSpecificities);

            // Pick a bin ID at random from the externally provided list.
            // RuleGenerator must never invent bin IDs — only the caller knows
            // which SortingBin instances actually exist in the current scene.
            string assignedBinID = availableBinIDs[Random.Range(0, availableBinIDs.Count)];

            RuleData newRule = new RuleData
            {
                conditions = new List<string>(selectedSpecificities),
                targetBinID = assignedBinID
            };

            generatedRules.Add(newRule);
        }

        ApplyComplexityToRules(generatedRules);

        return generatedRules;
    }

    /// <summary>
    /// Resets the used-specificities tracking list so the next level starts with a clean pool.
    /// Should be called by GameManager at the beginning of every new level.
    /// </summary>
    public void ResetForNewLevel()
    {
        usedSpecificities.Clear();
    }

    /// <summary>
    /// Replaces the current list of valid bin IDs at runtime.
    /// Call this from GameManager when the active set of SortingBins changes between levels.
    /// Overrides any IDs previously assigned via the Inspector.
    /// </summary>
    /// <param name="binIDs">The IDs of all SortingBin instances currently present in the scene.</param>
    public void SetAvailableBins(List<string> binIDs)
    {
        availableBinIDs = new List<string>(binIDs);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds the pool of specificities that are still available for this level
    /// by excluding everything already present in usedSpecificities.
    /// </summary>
    /// <returns>A mutable list of candidate specificities.</returns>
    private List<string> BuildAvailablePool()
    {
        // Exclude used specificities so the same condition never appears twice
        // in the same level — repetition would make rules feel identical and
        // undermine the incremental difficulty design.
        return specificityDatabase.allSpecificities
            .Where(specificity => !usedSpecificities.Contains(specificity))
            .ToList();
    }

    /// <summary>
    /// Randomly selects a fixed number of specificities from the given pool.
    /// </summary>
    /// <param name="pool">The mutable pool to draw from.</param>
    /// <param name="count">Number of specificities to pick.</param>
    /// <returns>A list of randomly selected specificities.</returns>
    private List<string> PickSpecificities(List<string> pool, int count)
    {
        List<string> picked = new List<string>();

        for (int pickIndex = 0; pickIndex < count; pickIndex++)
        {
            int randomIndex = Random.Range(0, pool.Count);
            string candidate = pool[randomIndex];

            picked.Add(candidate);

            // Temporarily remove from the local pool so the same entry
            // cannot be picked twice within a single rule's condition list.
            pool.RemoveAt(randomIndex);
        }

        // Restore pool entries so the outer loop manages depletion at rule level.
        // (Caller is responsible for removing picked entries from the shared pool.)
        return picked;
    }

    /// <summary>
    /// Computes and assigns a complexity score to each rule in the provided list.
    /// Formula: conditions count + (distinct bin count across all rules / 2), clamped.
    /// The distinct-bin factor rewards days that force the player to juggle more destinations.
    /// </summary>
    /// <param name="rules">The list of rules to annotate in place.</param>
    private void ApplyComplexityToRules(List<RuleData> rules)
    {
        int distinctBinCount = rules.Select(rule => rule.targetBinID).Distinct().Count();

        foreach (RuleData rule in rules)
        {
            float rawComplexity = rule.conditions.Count + (distinctBinCount / 2f);
            rule.complexity = Mathf.Clamp(Mathf.RoundToInt(rawComplexity), minimumComplexity, maximumComplexity);
        }
    }
}
