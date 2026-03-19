using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that acts as the single orchestrator for the entire game session.
/// Coordinates RuleGenerator, DocumentSpawner, DifficultyManager, BinLayoutManager,
/// DocumentStackManager, ProfitabilityManager, ProfitabilityBarUI, DayTransitionManager,
/// and FloorProgressionManager in the correct sequence, wiring all events together.
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

    /// <summary>Computes DayDifficultyData and DayEvolutionType from floor and day indices.</summary>
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

    /// <summary>
    /// Computes and returns per-floor difficulty bonuses (one random stat upgrade per new floor).
    /// Must never be called to modify ProfitabilityManager directly — all application goes
    /// through GameManager so the data-flow graph stays centralised.
    /// </summary>
    [SerializeField] private FloorProgressionManager floorProgressionManager;

    // -------------------------------------------------------------------------
    // Inspector-editable progression state
    // -------------------------------------------------------------------------

    /// <summary>Zero-based index of the current day within the current floor.</summary>
    [SerializeField] private int currentDayIndex = 0;

    /// <summary>
    /// Zero-based index of the current floor.
    /// "floor" better reflects the vertical progression metaphor of the game
    /// than "level", which implies a flat sequence without a spatial anchor.
    /// </summary>
    [SerializeField] private int currentFloorIndex = 0;

    /// <summary>
    /// How many successful days must be completed before moving to the next floor.
    /// "daysPerFloor" matches the floor metaphor — each floor is a distinct vertical stage.
    /// </summary>
    [SerializeField] private int daysPerFloor = 5;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Floor-level difficulty data received from FloorProgressionManager.
    /// Applied once per floor to ProfitabilityManager; persists for all days on that floor.
    /// </summary>
    private FloorDifficultyData currentFloorData;

    /// <summary>
    /// Day-level difficulty data computed fresh each day by DifficultyManager.
    /// Controls rule count, max complexity, and bin count for the current day.
    /// </summary>
    private DayDifficultyData currentDayData;

    /// <summary>
    /// Flat list of all active rules across all bins for the current day.
    /// Kept in sync after every ComplexifyRule or AddNewRule operation so bins
    /// always receive an accurate snapshot for Fallback validation.
    /// </summary>
    private List<RuleData> activeRules = new List<RuleData>();

    /// <summary>
    /// Guard that prevents OnNewFloor from being called more than once per floor.
    /// Without this guard, any path that calls InitializeDay while dayIndex == 0
    /// (e.g. a retry) would re-apply the floor bonus, compounding it incorrectly.
    /// </summary>
    private bool isFloorBonusApplied = false;

    /// <summary>
    /// Difficulty change summary built during InitializeDay and passed to PlayTransition
    /// inside OnDaySuccess. Built here — not in OnDaySuccess — because all changes
    /// happen during InitializeDay; by the time OnDaySuccess fires the summary must be complete.
    /// </summary>
    private DifficultyChangeSummary pendingSummary;

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
    /// Stops spawning, clears all stacks, advances the day or floor counter,
    /// and starts the transition screen.
    /// </summary>
    private void OnDaySuccess()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        currentDayIndex++;

        // 5 days completed = floor cleared — advance to the next floor and reset the day counter.
        bool isFloorComplete = currentDayIndex >= daysPerFloor;

        if (isFloorComplete)
        {
            currentDayIndex   = 0;
            currentFloorIndex++;

            // Allow OnNewFloor to run for the new floor when InitializeDay is next called.
            isFloorBonusApplied = false;
        }

        // pendingSummary was built during the InitializeDay that started this day —
        // all changes have already been recorded, so the summary is ready to display.
        dayTransitionManager.PlayTransition(currentDayIndex, currentFloorIndex, pendingSummary);
    }

    /// <summary>
    /// Called by ProfitabilityManager when profitability drops below the fail threshold.
    /// Stops spawning, clears all stacks, resets to day 1 of the current floor,
    /// and starts the failure transition. Floor index is NOT reset — the player retains
    /// floor progress but must replay the day arc from the beginning.
    /// Floor bonuses persist (they were earned), but rules are regenerated so the player
    /// does not memorise the exact same rule set on retry.
    /// </summary>
    private void OnDayFailed()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        // Player restarts floor from day 1; floor bonuses persist (they were earned).
        currentDayIndex = 0;

        // Fresh rules on retry so the player does not memorise the exact same rule set —
        // retrying with identical rules would remove the adaptation challenge.
        ruleGenerator.ResetForNewLevel();

        // -1 is the failure sentinel understood by DayTransitionManager.PlayTransition(),
        // which displays "FAILED — Back to Day 1" regardless of day index.
        // Null summary on failure — no difficulty changes to display on a failed day.
        dayTransitionManager.PlayTransition(-1, currentFloorIndex, null);
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
    /// Bootstraps all systems for the current day in the correct dependency order.
    /// Step 0 — Reset the pending summary so stale flags from the previous day are never shown.
    /// Step 1 — Apply floor bonus if this is day 1 of a new floor.
    /// Step 2 — Compute day data (must precede rule generation and evolution).
    /// Step 3 — Detect bin count change, activate bins, register IDs with RuleGenerator.
    /// Step 4 — Generate base rules (day 1 only) — must run BEFORE evolution on day 2+.
    /// Step 5 — Apply daily rule evolution (day 2+).
    /// Step 6 — Ensure new bin has a rule (whenever a bin was added this step).
    /// Step 7 — Distribute rules to bins.
    /// Step 8 — Start day systems.
    /// </summary>
    public void InitializeDay()
    {
        // Diagnostic — POINT 1: confirms InitializeDay is reached after a transition.
        // If this log never appears, OnTransitionComplete is not wired or not firing.
        Debug.Log("[GameManager] InitializeDay called — day: " + currentDayIndex + " floor: " + currentFloorIndex);

        // STEP 0 — Fresh summary for this day.
        // Built here because all difficulty changes happen inside InitializeDay;
        // by the time OnDaySuccess fires the summary must already be complete.
        pendingSummary = new DifficultyChangeSummary();

        // STEP 1 — Apply floor bonus once at day 1 of each floor.
        // Floor bonuses apply at the start of floor day 1 and persist for all 5 days.
        // The guard prevents the bonus from stacking if InitializeDay is called multiple
        // times while currentDayIndex == 0 (e.g. during a retry within the same floor).

        // Diagnostic — POINT 2: confirms currentDayIndex value when the floor check is evaluated.
        // If this never shows 0, the counter was not reset correctly before this call.
        Debug.Log("[GameManager] dayIndex check: " + currentDayIndex + " floorIndex: " + currentFloorIndex);

        bool isFirstDayOfFloor = currentDayIndex == 0;

        if (isFirstDayOfFloor && !isFloorBonusApplied)
        {
            ApplyFloorBonus();
            isFloorBonusApplied = true;
        }

        profitabilityManager.StartDay();

        // ResetDisplay must be called after StartDay so GetRemainingSeconds returns the
        // correct full duration — before StartDay, dayTimer is still at its previous value.
        dayTimerUI.ResetDisplay();

        // Initialize UI with the fail threshold and initial profitability so the marker
        // is positioned and the bar is filled correctly before the first event fires.
        profitabilityBarUI.Initialize(
            profitabilityManager.GetFailThreshold(),
            profitabilityManager.GetCurrentProfitability());

        // STEP 2 — Compute day data before anything that depends on rulesPerBin or maxRuleComplexity.
        // Placed here so both base rule generation (Step 4) and evolution (Step 5) use the
        // same currentDayData instance with no risk of stale values from the previous day.
        currentDayData = difficultyManager.ComputeDayData(currentDayIndex, currentFloorIndex);

        // STEP 3 — Detect bin count change, then activate the correct number of slots.
        // previousBinCount is read BEFORE SetActiveBinCount so the comparison is valid —
        // reading it after would always produce equal counts.
        int previousBinCount = binLayoutManager.GetActiveBinCount();

        binLayoutManager.SetActiveBinCount(currentDayData.numberOfBins);

        int newBinCount         = binLayoutManager.GetActiveBinCount();
        bool isNewBinActivated  = newBinCount > previousBinCount;

        if (isNewBinActivated)
        {
            string newBinID = binLayoutManager.GetLastActivatedBinID();

            pendingSummary.newBinAdded       = true;
            pendingSummary.newBinDescription = "New bin: " + newBinID;
        }

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

        // STEP 4 — Generate base rules on day 1 only.
        // This block MUST run before the evolution check below so that activeRules is always
        // populated on day 2. Previously, evolution ran before generation, meaning activeRules
        // was still empty when ComplexifyExistingRule was called — it would find zero candidates
        // and return early, silently skipping all difficulty upgrades for the entire run.
        if (isFirstDayOfFloor)
        {
            ruleGenerator.ResetForNewLevel();

            activeRules = ruleGenerator.GenerateRulesForDay(
                currentDayData.rulesPerBin,
                currentDayData.maxRuleComplexity);
        }

        // STEP 5 — Apply daily rule evolution starting from day 2.
        // Day 1 uses base rules so the player has one day to learn the initial rule set
        // before evolution begins — front-loading changes would overwhelm new players.
        // Evolution is placed AFTER base rule generation so activeRules is guaranteed
        // to be non-empty regardless of which day is being initialised.

        // Diagnostic — POINT 3: confirms whether the evolution branch is being reached.
        // If dayIndex is always 0 here, OnDaySuccess is not incrementing the counter.
        Debug.Log("[GameManager] ApplyDayEvolution check — dayIndex: " + currentDayIndex);

        // Diagnostic — POINT 4: confirms activeRules is populated before evolution runs.
        // If count is 0 here on day 2+, rule generation on day 1 failed or activeRules was cleared.
        Debug.Log("[GameManager] activeRules count before evolution: " +
                  (activeRules != null ? activeRules.Count : -1));

        bool isDayAfterFirst = currentDayIndex > 0;

        if (isDayAfterFirst)
        {
            ApplyDayEvolution(activeBins);
        }

        // STEP 6 — Ensure every newly activated bin receives at least one rule.
        // A bin with no rules is confusing and unplayable — the player sees a bin but has
        // no information about what to sort into it, regardless of the evolution outcome.
        if (isNewBinActivated)
        {
            EnsureNewBinHasRule(activeBins);
        }

        // STEP 7 — Distribute rules to bins.
        DistributeRulesToBins(activeRules, activeBins);

        // Inject the full cross-bin rule list into every bin immediately after distribution.
        // Bins need this only for Fallback validation — they must not query each other directly,
        // so GameManager is the correct point to push a single flat snapshot to all bins.
        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(activeRules);

        // Active bins change each day — refresh all event subscriptions after the new bin set
        // is established so no stale subscriptions from previous days remain.
        RefreshBinSubscriptions(activeBins);

        documentSpawner.UpdateActiveRules(activeRules);

        // Clear any stale documents from the previous day before starting the new spawn loop.
        documentStackManager.ClearStack();

        // STEP 8 — Start day systems.
        documentSpawner.StartDay();
    }

    // -------------------------------------------------------------------------
    // Private helpers — floor and day progression
    // -------------------------------------------------------------------------

    /// <summary>
    /// Requests FloorDifficultyData from FloorProgressionManager and applies it to
    /// ProfitabilityManager. Called once per floor when dayIndex == 0.
    /// FloorProgressionManager never touches ProfitabilityManager directly — this method
    /// is the exclusive bridge between the two systems.
    /// Also records the floor bonus details in pendingSummary so the transition overlay
    /// can display which stat changed without querying FloorProgressionManager itself.
    /// </summary>
    private void ApplyFloorBonus()
    {
        currentFloorData = floorProgressionManager.OnNewFloor(currentFloorIndex);

        profitabilityManager.SetFailThreshold(currentFloorData.failThreshold);
        profitabilityManager.SetDayDuration(currentFloorData.dayDuration);
        profitabilityManager.SetDecayRate(currentFloorData.decayRatePerSecond);

        // Floor bonus description is built here where GameManager knows which stat was upgraded —
        // DayTransitionManager must never call back into FloorProgressionManager to discover it.
        FloorBonusType selectedBonus = floorProgressionManager.GetCurrentFloorBonus();

        pendingSummary.floorBonusApplied     = true;
        pendingSummary.floorBonusType        = selectedBonus;
        pendingSummary.floorBonusDescription = BuildFloorBonusDescription(selectedBonus);
    }

    /// <summary>
    /// Asks DifficultyManager to select a random evolution type, then applies it
    /// to the active rule set by either complexifying an existing rule or adding a new one.
    /// activeRules is updated in place so all bins receive an accurate snapshot.
    /// Also populates the relevant fields in pendingSummary.
    /// </summary>
    /// <param name="activeBins">The bins active for the current day, used when inserting a new rule.</param>
    private void ApplyDayEvolution(List<SortingBin> activeBins)
    {
        DayEvolutionType evolutionType = difficultyManager.SelectDayEvolution();

        if (evolutionType == DayEvolutionType.ComplexifyRule)
        {
            // Capture list count before the call to detect whether complexification actually ran —
            // ComplexifyExistingRule modifies in place and logs a warning when it finds no candidate,
            // but it does not return a success flag; using a sentinel check on the target rule's
            // displayText is fragile, so instead we track the call and always set the flag.
            // The RuleGenerator warning in console is sufficient signal when no candidate exists.
            ruleGenerator.ComplexifyExistingRule(activeRules, currentDayData.maxRuleComplexity);

            // Find the rule that was just complexified to build the description.
            // The most recently modified rule is the one with the highest complexity that
            // matches the target — using the last complexified rule reported via the Debug.Log
            // in RuleGenerator is the authoritative record; here we approximate by picking the
            // highest-complexity rule for the description string only.
            RuleData mostComplexRule = FindHighestComplexityRule(activeRules);
            string complexifiedBinID = mostComplexRule != null ? mostComplexRule.targetBinID : "unknown";

            pendingSummary.ruleComplexified              = true;
            pendingSummary.complexifiedRuleDescription   = "Rule on " + complexifiedBinID + " is now harder";
        }
        else
        {
            // Pick a random bin to receive the new rule.
            // Random selection keeps the rule distribution unpredictable across days.
            SortingBin targetBin = activeBins[Random.Range(0, activeBins.Count)];
            string targetBinID   = targetBin.GetBinID();

            RuleData newRule = ruleGenerator.GenerateSingleRule(
                targetBinID,
                currentDayData.maxRuleComplexity);

            // newRule can be null when the specificity pool is exhausted — skip silently
            // rather than adding a null entry that would crash validation downstream.
            if (newRule != null)
            {
                activeRules.Add(newRule);

                pendingSummary.newRuleAdded       = true;
                pendingSummary.newRuleDescription = "New rule added to " + targetBinID;

                Debug.Log($"[Difficulty] New rule added → " +
                          $"bin: {newRule.targetBinID} | " +
                          $"type: {newRule.ruleType} | " +
                          $"condition: {newRule.conditionA} | " +
                          $"complexity: {newRule.complexity}");
            }
        }
    }

    /// <summary>
    /// Guarantees that the newly activated bin always has at least one rule assigned to it.
    /// Called in the same InitializeDay step that detected the new bin, after evolution has run —
    /// this ensures the bin gets a rule regardless of whether evolution targeted it or not.
    /// A bin with no rules is confusing and unplayable; this method is the single enforcement point.
    /// </summary>
    /// <param name="activeBins">The bins active for the current day.</param>
    private void EnsureNewBinHasRule(List<SortingBin> activeBins)
    {
        string newBinID = binLayoutManager.GetLastActivatedBinID();

        // Guard: no ID means BinLayoutManager did not record a newly activated slot this call.
        if (string.IsNullOrEmpty(newBinID))
            return;

        // Check whether evolution already added a rule to the new bin —
        // if so, adding another one would over-populate it on day 2.
        bool newBinAlreadyHasRule = activeRules.Exists(rule => rule.targetBinID == newBinID);

        if (newBinAlreadyHasRule)
            return;

        RuleData ruleForNewBin = ruleGenerator.GenerateSingleRule(newBinID, currentDayData.maxRuleComplexity);

        if (ruleForNewBin == null)
        {
            Debug.LogWarning("[GameManager] EnsureNewBinHasRule: specificity pool exhausted — " +
                             "new bin " + newBinID + " received no rule.");
            return;
        }

        activeRules.Add(ruleForNewBin);

        // Update the summary to reflect that the new bin also received a rule.
        // This overwrites the newBinDescription set in Step 3 to include the rule text,
        // giving the player full context: which bin appeared and what rule it carries.
        pendingSummary.newRuleAdded       = true;
        pendingSummary.newRuleDescription = "New bin: " + newBinID + " — " + ruleForNewBin.displayText;

        Debug.Log($"[Difficulty] Rule assigned to new bin → " +
                  $"bin: {newBinID} | " +
                  $"type: {ruleForNewBin.ruleType} | " +
                  $"condition: {ruleForNewBin.conditionA}");
    }

    /// <summary>
    /// Returns a human-readable sentence describing the gameplay impact of the given floor bonus type.
    /// Uses gameplay-language descriptions rather than technical field names so the player
    /// understands the impact without needing to know the underlying data model.
    /// </summary>
    /// <param name="bonusType">The FloorBonusType selected for the current floor.</param>
    /// <returns>A short description string suitable for display on the transition overlay.</returns>
    private string BuildFloorBonusDescription(FloorBonusType bonusType)
    {
        switch (bonusType)
        {
            case FloorBonusType.FailThreshold:
                return "Minimum productivity increased";

            case FloorBonusType.DayDuration:
                return "Days are now longer";

            case FloorBonusType.DecayRate:
                return "Productivity decays faster";

            default:
                return "Difficulty increased";
        }
    }

    /// <summary>
    /// Returns the rule with the highest complexity value from the given list,
    /// or null when the list is empty.
    /// Used by ApplyDayEvolution to identify which rule was most recently complexified
    /// for building the summary description — this is an approximation; the RuleGenerator
    /// Debug.Log is the authoritative record of which rule was actually modified.
    /// </summary>
    /// <param name="rules">The flat list of active rules to search.</param>
    /// <returns>The RuleData with the greatest complexity, or null.</returns>
    private RuleData FindHighestComplexityRule(List<RuleData> rules)
    {
        if (rules == null || rules.Count == 0)
            return null;

        RuleData highestComplexityRule = rules[0];

        foreach (RuleData rule in rules)
        {
            if (rule.complexity > highestComplexityRule.complexity)
                highestComplexityRule = rule;
        }

        return highestComplexityRule;
    }

    // -------------------------------------------------------------------------
    // Private helpers — event subscriptions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Unsubscribes onValidDrop and onInvalidDrop from every bin managed by BinLayoutManager,
    /// then re-subscribes only to the currently active ones.
    /// Called at the end of InitializeDay because the active bin set changes every day —
    /// without a full unsubscribe pass, bins deactivated mid-session would retain stale
    /// subscriptions and fire duplicate events the next time they become active.
    /// </summary>
    /// <param name="activeBins">The bins active for the current day.</param>
    private void RefreshBinSubscriptions(List<SortingBin> activeBins)
    {
        List<SortingBin> allBins = binLayoutManager.GetAllBins();

        // Unsubscribe from ALL bins (active and inactive) to eliminate any stale delegates
        // accumulated from previous days. Unsubscribing a delegate that was never subscribed
        // is a no-op in C#, so this is safe regardless of each bin's previous state.
        foreach (SortingBin bin in allBins)
        {
            bin.onValidDrop   -= OnValidDrop;
            bin.onInvalidDrop -= OnInvalidDrop;
        }

        // Re-subscribe only to active bins — inactive bins must not fire events because
        // they hold no rules this day and any drop on them is outside the intended game flow.
        foreach (SortingBin bin in activeBins)
        {
            bin.onValidDrop   += OnValidDrop;
            bin.onInvalidDrop += OnInvalidDrop;
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
    /// Called by any active SortingBin when a document is dropped into the wrong bin.
    /// Delegates penalty application to ProfitabilityManager — GameManager is the sole
    /// orchestrator, connecting bin events to the profitability system without either
    /// knowing the other exists directly.
    /// </summary>
    private void OnInvalidDrop()
    {
        profitabilityManager.ApplyWrongBinPenalty();
    }

    // -------------------------------------------------------------------------
    // Private helpers — data extraction
    // -------------------------------------------------------------------------

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
