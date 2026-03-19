using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that acts as the single orchestrator for the entire game session.
/// Coordinates RuleGenerator, DocumentSpawner, DifficultyManager, BinLayoutManager,
/// DocumentStackManager, ProfitabilityManager, ProfitabilityBarUI, and DayTransitionManager
/// in the correct sequence, wiring all events together without containing any rule logic,
/// spawn logic, profitability logic, layout logic, or UI rendering logic.
/// Does NOT generate rules, spawn documents, compute difficulty, validate documents,
/// activate bins, manage the document stack, compute profitability, or render UI directly.
/// </summary>
public class GameManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned system references — core gameplay
    // -------------------------------------------------------------------------

    /// <summary>Generates the day's rule set from the SpecificityDatabase.</summary>
    [SerializeField] private RuleGenerator ruleGenerator;

    /// <summary>Controls document instantiation and the spawn loop.</summary>
    [SerializeField] private DocumentSpawner documentSpawner;

    /// <summary>Computes difficulty settings and day evolution type from the progression index.</summary>
    [SerializeField] private DifficultyManager difficultyManager;

    /// <summary>Activates and positions SortingBin slots based on the day's bin count.</summary>
    [SerializeField] private BinLayoutManager binLayoutManager;

    /// <summary>Manages the visual document stack, drag feedback, and document lifetime.</summary>
    [SerializeField] private DocumentStackManager documentStackManager;

    // -------------------------------------------------------------------------
    // Inspector-assigned system references — profitability and progression
    // -------------------------------------------------------------------------

    /// <summary>Tracks profitability, applies decay, and detects day success and failure.</summary>
    [SerializeField] private ProfitabilityManager profitabilityManager;

    /// <summary>Renders the profitability bar, threshold marker, and percentage text.</summary>
    [SerializeField] private ProfitabilityBarUI profitabilityBarUI;

    /// <summary>Displays the day-transition overlay between days.</summary>
    [SerializeField] private DayTransitionManager dayTransitionManager;

    /// <summary>Renders the MM:SS countdown timer below the profitability bar.</summary>
    [SerializeField] private DayTimerUI dayTimerUI;

    // -------------------------------------------------------------------------
    // Inspector-editable progression state
    // -------------------------------------------------------------------------

    /// <summary>Zero-based index of the current day within the current level.</summary>
    [SerializeField] private int currentDayIndex = 0;

    /// <summary>Zero-based index of the current level.</summary>
    [SerializeField] private int currentLevelIndex = 0;

    /// <summary>How many successful days must be completed before moving to the next level.</summary>
    [SerializeField] private int totalDaysPerLevel = 5;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        SubscribeToAllEvents();
        InitializeDay();
    }

    private void OnDestroy()
    {
        // Unsubscribe to prevent ghost callbacks if this object is destroyed while
        // ProfitabilityManager or DayTransitionManager coroutines are still pending.
        UnsubscribeFromAllEvents();
    }

    // -------------------------------------------------------------------------
    // Event wiring
    // -------------------------------------------------------------------------

    /// <summary>
    /// Subscribes GameManager to every event produced by the sub-systems.
    /// All wiring is centralised here so the data-flow graph is readable in one place
    /// and accidental double-subscription is easy to audit.
    /// </summary>
    private void SubscribeToAllEvents()
    {
        profitabilityManager.onProfitabilityChanged += OnProfitabilityChanged;
        profitabilityManager.onDaySuccess           += OnDaySuccess;
        profitabilityManager.onDayFailed            += OnDayFailed;
        profitabilityManager.onTimerUpdated         += OnTimerUpdated;
        dayTransitionManager.onTransitionComplete   += OnTransitionComplete;
    }

    /// <summary>
    /// Mirror of SubscribeToAllEvents — called in OnDestroy to avoid memory leaks
    /// from dangling delegates referencing a destroyed MonoBehaviour.
    /// </summary>
    private void UnsubscribeFromAllEvents()
    {
        profitabilityManager.onProfitabilityChanged -= OnProfitabilityChanged;
        profitabilityManager.onDaySuccess           -= OnDaySuccess;
        profitabilityManager.onDayFailed            -= OnDayFailed;
        profitabilityManager.onTimerUpdated         -= OnTimerUpdated;
        dayTransitionManager.onTransitionComplete   -= OnTransitionComplete;
    }

    // -------------------------------------------------------------------------
    // Event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by ProfitabilityManager every frame during an active day.
    /// Pushes the new profitability value to the UI without any computation in GameManager.
    /// </summary>
    /// <param name="profitabilityValue">Current profitability on the 0–100 scale.</param>
    private void OnProfitabilityChanged(float profitabilityValue)
    {
        profitabilityBarUI.UpdateDisplay(profitabilityValue);
    }

    /// <summary>
    /// Called by ProfitabilityManager every frame during an active day with seconds remaining.
    /// Relays the value directly to DayTimerUI so GameManager stays the single wiring point
    /// and DayTimerUI never needs a reference to ProfitabilityManager.
    /// </summary>
    /// <param name="remainingSeconds">Seconds left in the current day (always >= 0).</param>
    private void OnTimerUpdated(float remainingSeconds)
    {
        dayTimerUI.UpdateTimer(remainingSeconds);
    }

    /// <summary>
    /// Called by ProfitabilityManager when the day timer elapses without the player failing.
    /// Stops spawning, clears all stacks, advances the day or level counter,
    /// and starts the transition screen.
    /// </summary>
    private void OnDaySuccess()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        currentDayIndex++;

        // When all days in the current level are completed, move to the next level
        // and restart the day counter so the player progresses through the full arc.
        bool isLevelComplete = currentDayIndex >= totalDaysPerLevel;

        if (isLevelComplete)
        {
            currentDayIndex = 0;
            currentLevelIndex++;
        }

        dayTransitionManager.PlayTransition(currentDayIndex);
    }

    /// <summary>
    /// Called by ProfitabilityManager when profitability drops below the fail threshold.
    /// Stops spawning, clears all stacks, and starts the failure transition. dayIndex is reset
    /// to 0 so InitializeDay() restarts from the first day of the current level.
    /// The player retains level progress but must replay the day arc from the beginning.
    /// </summary>
    private void OnDayFailed()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        // Reset to day 1 of the current level, not the whole game —
        // the player failed this arc but has not lost all prior level progress.
        currentDayIndex = 0;

        // -1 is the failure sentinel understood by DayTransitionManager.PlayTransition(),
        // which displays "FAILED — Back to Day 1" regardless of the day index value.
        dayTransitionManager.PlayTransition(-1);
    }

    /// <summary>
    /// Called by DayTransitionManager after the overlay completes its display duration.
    /// Safe to initialise the next day here because profitability tracking and spawning
    /// were both stopped before PlayTransition was called.
    /// </summary>
    private void OnTransitionComplete()
    {
        InitializeDay();
    }

    // -------------------------------------------------------------------------
    // Day initialisation
    // -------------------------------------------------------------------------

    /// <summary>
    /// Bootstraps all systems for the current day in the correct dependency order:
    /// profitability start → UI initialisation → difficulty → bin activation →
    /// bin IDs → rule generation → rule distribution → spawner setup → stack clear → spawn start.
    /// </summary>
    public void InitializeDay()
    {
        profitabilityManager.StartDay();

        // ResetDisplay must be called after StartDay so GetRemainingSeconds returns the
        // correct full duration — before StartDay, dayTimer is still at its previous value.
        dayTimerUI.ResetDisplay();

        // Initialize UI with the fail threshold and initial profitability so the marker
        // is positioned and the bar is filled correctly before the first event fires.
        profitabilityBarUI.Initialize(
            profitabilityManager.GetFailThreshold(),
            profitabilityManager.GetCurrentProfitability());

        (DifficultySettings daySettings, DayEvolutionType evolutionType) =
            difficultyManager.OnNewDay(currentDayIndex, currentLevelIndex);

        // Activate the correct number of bin slots for today's difficulty before
        // extracting the active bin list — GetActiveBins must reflect the new count.
        binLayoutManager.SetActiveBinCount(daySettings.numberOfBins);

        List<SortingBin> activeBins = binLayoutManager.GetActiveBins();

        // Guard: zero active bins would cause divide-by-zero in the rules-per-bin calculation.
        if (activeBins.Count == 0)
        {
            Debug.LogError("[GameManager] No active bins returned by BinLayoutManager. " +
                           "Assign at least one BinSlot in the Inspector.");
            return;
        }

        List<string> activeBinIDs = ExtractBinIDs(activeBins);
        ruleGenerator.SetAvailableBins(activeBinIDs);

        // Divide total rules by bin count to obtain the per-bin value RuleGenerator expects.
        // RuleGenerator iterates per bin to guarantee full coverage, so the unit it consumes
        // is rules-per-bin, not a global total.
        int rulesPerBin = daySettings.numberOfRules / activeBins.Count;

        List<RuleData> generatedRules = ruleGenerator.GenerateRulesForDay(
            rulesPerBin,
            daySettings.maxRuleComplexity
        );

        DistributeRulesToBins(generatedRules, activeBins);

        // Inject the full cross-bin rule list into every bin immediately after distribution.
        // Bins need this only for Fallback validation — they must not query each other directly,
        // so GameManager is the correct point to push a single flat snapshot to all bins.
        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(generatedRules);

        // Subscribe to each active bin's onValidDrop event for this day.
        // Done here rather than once at startup because the set of active bins changes each day.
        SubscribeToBinEvents(activeBins);

        documentSpawner.UpdateActiveRules(generatedRules);

        // Clear any stale documents from the previous day before starting the new spawn loop.
        documentStackManager.ClearStack();

        // Apply the difficulty-driven interval before starting the loop so the
        // first spawn waits the correct duration rather than the previous day's value.
        documentSpawner.SetSpawnInterval(daySettings.spawnInterval);

        documentSpawner.StartDay();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Subscribes OnValidDrop to each active bin's onValidDrop event.
    /// Unsubscribes first to prevent accumulating duplicate subscriptions across days,
    /// which would fire OnValidDrop multiple times per drop and over-count profitability.
    /// </summary>
    /// <param name="activeBins">The bins active for the current day.</param>
    private void SubscribeToBinEvents(List<SortingBin> activeBins)
    {
        foreach (SortingBin bin in activeBins)
        {
            // Unsubscribe before subscribing to guarantee exactly one subscription per bin per day,
            // regardless of how many times InitializeDay has been called previously.
            bin.onValidDrop -= OnValidDrop;
            bin.onValidDrop += OnValidDrop;
        }
    }

    /// <summary>
    /// Called by any active SortingBin the moment a valid document drop is confirmed.
    /// Delegates to ProfitabilityManager so the bin never knows about the profitability system.
    /// </summary>
    private void OnValidDrop()
    {
        profitabilityManager.OnDocumentSorted();
    }

    /// <summary>
    /// Collects the string ID from each SortingBin in the provided list.
    /// Extracted into its own method to keep InitializeDay at a single level of abstraction.
    /// </summary>
    /// <param name="bins">The list of active SortingBin instances.</param>
    /// <returns>A list of bin ID strings in the same order as the input list.</returns>
    private List<string> ExtractBinIDs(List<SortingBin> bins)
    {
        List<string> binIDs = new List<string>();

        foreach (SortingBin bin in bins)
            binIDs.Add(bin.GetBinID());

        return binIDs;
    }

    /// <summary>
    /// Sends each generated rule to the SortingBin whose ID matches the rule's targetBinID.
    /// Rules whose targetBinID does not match any active bin are silently skipped —
    /// this guards against stale IDs that may no longer exist in the scene.
    /// </summary>
    /// <param name="rules">The full list of RuleData produced for the current day.</param>
    /// <param name="activeBins">The currently active SortingBin instances to distribute rules into.</param>
    private void DistributeRulesToBins(List<RuleData> rules, List<SortingBin> activeBins)
    {
        // Group rules by target bin first so each bin receives one AssignRules call,
        // avoiding multiple overwrites when several rules share the same targetBinID.
        Dictionary<string, List<RuleData>> rulesByBinID = GroupRulesByBinID(rules);

        foreach (SortingBin bin in activeBins)
        {
            string binID     = bin.GetBinID();
            bool hasBinRules = rulesByBinID.TryGetValue(binID, out List<RuleData> binRules);

            // Bins with no rules assigned still receive an empty list so they display
            // a "No rules assigned" message rather than showing stale rules from a previous day.
            bin.AssignRules(hasBinRules ? binRules : new List<RuleData>());
        }
    }

    /// <summary>
    /// Organises a flat rule list into a dictionary keyed by targetBinID.
    /// </summary>
    /// <param name="rules">The flat list of rules to group.</param>
    /// <returns>A dictionary mapping each bin ID to its associated rules.</returns>
    private Dictionary<string, List<RuleData>> GroupRulesByBinID(List<RuleData> rules)
    {
        Dictionary<string, List<RuleData>> groupedRules = new Dictionary<string, List<RuleData>>();

        foreach (RuleData rule in rules)
        {
            bool hasBinEntry = groupedRules.ContainsKey(rule.targetBinID);

            if (!hasBinEntry)
                groupedRules[rule.targetBinID] = new List<RuleData>();

            groupedRules[rule.targetBinID].Add(rule);
        }

        return groupedRules;
    }
}
