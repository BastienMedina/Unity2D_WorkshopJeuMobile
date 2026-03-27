using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// MonoBehaviour that acts as the single orchestrator for the entire game session.
/// Coordinates RuleGenerator, DocumentSpawner, DifficultyManager, BinLayoutManager,
/// DocumentStackManager, ProfitabilityManager, ProfitabilityBarUI, DayTransitionManager,
/// FloorProgressionManager, FloorSaveSystem, and FloorDifficultyProgression in the correct
/// sequence, wiring all events together.
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

    /// <summary>
    /// Plays the correct full-screen animation Canvas for each transition type
    /// (day end, day fail, floor complete). Fires onSequenceComplete when done
    /// so GameManager can advance the game state.
    /// Assign your Sleep Canvas to DayEnd, Elevator Canvas to FloorComplete, etc.
    /// </summary>
    [SerializeField] private AnimationSequenceManager animationSequenceManager;

    /// <summary>Renders the MM:SS countdown timer below the profitability bar.</summary>
    [SerializeField] private DayTimerUI dayTimerUI;

    /// <summary>
    /// Computes and returns per-floor difficulty bonuses (one random stat upgrade per new floor).
    /// Must never be called to modify ProfitabilityManager directly — all application goes
    /// through GameManager so the data-flow graph stays centralised.
    /// </summary>
    [SerializeField] private FloorProgressionManager floorProgressionManager;

    /// <summary>Persists and retrieves FloorSaveData to/from JSON files on disk.</summary>
    [SerializeField] private FloorSaveSystem floorSaveSystem;

    /// <summary>
    /// Computes floor parameters from base values and per-floor deltas.
    /// Used in Start() to derive the current floor's difficulty when no session data was passed,
    /// and in OnDaySuccess() to pre-compute the next floor's parameters immediately after saving.
    /// </summary>
    [SerializeField] private FloorDifficultyProgression floorDifficultyProgression;

    /// <summary>
    /// Shows the game over screen when the player fails a day.
    /// Drag the GAME OVER ANIMATION root GameObject here in the Inspector.
    /// </summary>
    [SerializeField] private GameOverController gameOverController;

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
    /// Set to true when the floor-complete animation is playing and SceneManager.LoadScene
    /// must be deferred until OnAnimationSequenceComplete fires.
    /// </summary>
    private bool pendingSceneLoad = false;

    /// <summary>
    /// Difficulty change summary built during InitializeDay and passed to PlayTransition
    /// inside OnDaySuccess. Built here — not in OnDaySuccess — because all changes
    /// happen during InitializeDay; by the time OnDaySuccess fires the summary must be complete.
    /// </summary>
    private DifficultyChangeSummary pendingSummary;

    /// <summary>
    /// The FloorSaveData snapshot for the floor currently being played.
    /// Set by InitializeFromFloorData() and kept in sync so SaveCurrentFloor() can read
    /// rulesPerBin and maxRuleComplexity from it without re-querying DifficultyManager.
    /// </summary>
    private FloorSaveData currentFloorSaveData;

    /// <summary>
    /// Tracks how many bins were active at the end of the previous day's InitializeDay call.
    /// Compared against the new count after SetActiveBinCount to detect when a new bin was added —
    /// reading BinLayoutManager.GetActiveBinCount() before and after in the same frame would always
    /// give equal results once the call updates the internal counter, so a persistent field is required.
    /// </summary>
    private int activeBinCount = 0;

    /// <summary>
    /// Stores the bin ID that received the most recently added rule via AddNewRule evolution.
    /// Excluded from the next random bin selection so the same bin never gets two rules in a row,
    /// ensuring rules distribute across all active bins rather than clustering on one.
    /// </summary>
    private string lastRuleAssignedBinID = "";

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        SubscribeToAllEvents();

        // Resolve FloorSaveSystem from the singleton when not assigned in Inspector.
        // This is the normal runtime path: FloorSaveSystemBootstrap in Menu_Principal
        // calls DontDestroyOnLoad so the instance is available here without a scene reference.
        if (floorSaveSystem == null)
            floorSaveSystem = FloorSaveSystem.Instance;

        // Read session data before clearing — Clear() must be called immediately so stale
        // values do not affect future sessions if GameScene is loaded again.
        FloorSaveData sessionFloor    = FloorSessionData.SelectedFloor;
        bool isReplayingFloor         = FloorSessionData.IsReplayingFloor;
        FloorSessionData.Clear();

        if (sessionFloor != null && isReplayingFloor)
        {
            // Player tapped a completed floor in TowerScene — restore it exactly.
            RestoreFloorFromSave(sessionFloor);
            return;
        }

        if (sessionFloor != null)
        {
            // Player tapped the current (incomplete) floor — use its pre-computed parameters
            // without running the replay restoration path (rules are generated fresh).
            InitializeFromFloorData(sessionFloor);
            return;
        }

        // No session data — the GameScene must always be launched from the main menu
        // by selecting a floor. Direct play from the Editor without passing through
        // FloorSessionData is not supported in story mode.
        Debug.LogError("[GameManager] Start: no FloorSessionData found. " +
                       "GameScene must be launched from the main menu by tapping a floor button. " +
                       "Direct Editor play is not supported — open Menu_Principal and press Play from there.");
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

        if (animationSequenceManager != null)
            animationSequenceManager.onSequenceComplete += OnAnimationSequenceComplete;
    }

    private void UnsubscribeFromAllEvents()
    {
        profitabilityManager.onProfitabilityChanged -= OnProfitabilityChanged;
        profitabilityManager.onDaySuccess           -= OnDaySuccess;
        profitabilityManager.onDayFailed            -= OnDayFailed;
        profitabilityManager.onTimerUpdated         -= OnTimerUpdated;
        dayTransitionManager.onTransitionComplete   -= OnTransitionComplete;

        if (animationSequenceManager != null)
            animationSequenceManager.onSequenceComplete -= OnAnimationSequenceComplete;
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
    /// When all days on a floor are completed, saves the floor, pre-computes the next floor's
    /// parameters, and returns to TowerScene.
    /// </summary>
    private void OnDaySuccess()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        currentDayIndex++;

        // 5 days completed = floor cleared — save, generate next floor data, return to TowerScene.
        bool isFloorComplete = currentDayIndex >= daysPerFloor;

        if (isFloorComplete)
        {
            SaveCurrentFloor();

            FloorSaveData nextFloorData = floorDifficultyProgression.GenerateNextFloorData(
                currentFloorSaveData);

            FloorSessionData.SelectedFloor    = nextFloorData;
            FloorSessionData.IsReplayingFloor = false;

            // Play the floor-complete animation (elevator) before loading TowerScene.
            // If no animationSequenceManager is assigned, load immediately as before.
            if (animationSequenceManager != null)
            {
                // Pass the new floor number to ElevatorSequenceController if one is present.
                ElevatorSequenceController elevator =
                    animationSequenceManager.GetComponentInChildren<ElevatorSequenceController>(true);

                if (elevator != null)
                    elevator.FloorNumber = currentFloorIndex + 2; // next floor is 1-based

                pendingSceneLoad = true;
                animationSequenceManager.PlaySequence(AnimationSequenceManager.TransitionType.FloorComplete);
                // OnAnimationSequenceComplete will call SceneManager.LoadScene when the sequence ends.
            }
            else
            {
                SceneManager.LoadScene("Menu_Principal");
            }
            return;
        }

        // Floor not yet complete — advance to the next day inside GameScene as before.
        // pendingSummary was built during the InitializeDay that started this day.
        dayTransitionManager.PlayTransition(currentDayIndex, currentFloorIndex, pendingSummary);

        // Play the day-end animation canvas (sleep/dodo sequence).
        // AnimationSequenceManager fires onSequenceComplete → OnAnimationSequenceComplete
        // when done, which is NOT wired to InitializeDay here — DayTransitionManager.onTransitionComplete
        // still drives InitializeDay so the existing timing contract is preserved.
        // If you want the animation to gate InitializeDay instead, remove the
        // dayTransitionManager subscription and route through OnAnimationSequenceComplete.
        if (animationSequenceManager != null)
            animationSequenceManager.PlaySequence(AnimationSequenceManager.TransitionType.DayEnd);
    }

    /// <summary>
    /// Called by ProfitabilityManager when profitability drops below the fail threshold.
    /// Stops all active gameplay systems and shows the Game Over screen.
    /// The screen waits for player input: Retry restarts the floor, Return goes to Menu_Principal.
    /// DayTransitionManager is intentionally bypassed here — the Game Over screen replaces
    /// the transition overlay and does NOT auto-advance to InitializeDay.
    /// </summary>
    private void OnDayFailed()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();

        if (gameOverController != null)
        {
            gameOverController.Show();
        }
        else
        {
            // Fallback when GameOverController is not assigned: behave as before
            // so the game is never stuck in a broken state during development.
            Debug.LogWarning("[GameManager] OnDayFailed: GameOverController is not assigned. " +
                             "Drag the GAME OVER ANIMATION root into the GameOverController slot on GameManager.");
            currentDayIndex = 0;
            dayTransitionManager.PlayTransition(-1, currentFloorIndex, null);
        }
    }

    /// <summary>
    /// Resets the day counter and restarts the current floor from day 1.
    /// Called by GameOverController when the player presses the Retry button.
    /// Floor bonuses applied earlier in the session are intentionally preserved —
    /// they were earned and must not be re-applied on retry (isFloorBonusApplied stays true).
    /// </summary>
    public void RestartCurrentFloor()
    {
        currentDayIndex = 0;
        InitializeDay();
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

    /// <summary>
    /// Called by AnimationSequenceManager when the active animation Canvas finishes.
    /// Currently used only for the FloorComplete path — loading TowerScene is deferred
    /// until the elevator sequence ends so the player sees the full animation.
    /// For DayEnd and DayFail, DayTransitionManager.onTransitionComplete still drives
    /// InitializeDay so the existing timing contract is preserved.
    /// </summary>
    private void OnAnimationSequenceComplete()
    {
        // Only act on FloorComplete — day transitions are handled by DayTransitionManager.
        if (pendingSceneLoad)
        {
            pendingSceneLoad = false;
            SceneManager.LoadScene("Menu_Principal");
        }
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

        // New floor starts fresh: no bin should be excluded on the very first rule assignment
        // because the previous floor's lastRuleAssignedBinID is stale and irrelevant here.
        if (isFirstDayOfFloor)
            lastRuleAssignedBinID = "";

        profitabilityManager.StartDay();

        // ResetDisplay must be called after StartDay so GetRemainingSeconds returns the
        // correct full duration — before StartDay, dayTimer is still at its previous value.
        dayTimerUI.ResetDisplay();

        // Initialize UI with the fail threshold and initial profitability so the marker
        // is positioned and the bar is filled correctly before the first event fires.
        profitabilityBarUI.Initialize(
            profitabilityManager.GetFailThreshold(),
            profitabilityManager.GetCurrentProfitability());

        // STEP 2 — Compute day data for failThreshold/dayDuration passthrough.
        // DifficultyManager is no longer authoritative for bin count or rule complexity in
        // story mode — those come from the floor JSON per night. currentDayData is kept to
        // avoid breaking any remaining callsite that may reference it (e.g. ApplyDayEvolution).
        currentDayData = difficultyManager.ComputeDayData(currentDayIndex, currentFloorIndex);

        // STEP 3 — Detect bin count change, then activate the correct number of slots.
        // previousBinCount is read from the persistent field BEFORE SetActiveBinCount so the
        // comparison is valid — BinLayoutManager.GetActiveBinCount() updates its internal counter
        // during SetActiveBinCount, making a before/after read within the same frame unreliable.
        int previousBinCount = activeBinCount;

        // OPTION B — use currentFloorSaveData.numberOfBins as the single source of truth for
        // how many bins are active on this floor. DifficultyManager.ComputeDayData is authoritative
        // for rulesPerBin and maxRuleComplexity only; its own ComputeNumberOfBins recalculates
        // from totalFloors * daysPerFloor independently and may diverge from FloorDifficultyProgression
        // which already encoded the correct bin count into currentFloorSaveData. Trusting two sources
        // in parallel guaranteed the third bin would never appear (DifficultyManager kept returning 2
        // while FloorSaveData.numberOfBins had already reached 3).
        // Falls back to currentDayData.numberOfBins for safety when no floor data is set
        // (e.g. direct Editor play without going through TowerScene).
        int targetBinCount = currentFloorSaveData != null
            ? currentFloorSaveData.numberOfBins
            : currentDayData.numberOfBins;

        binLayoutManager.SetActiveBinCount(targetBinCount);

        List<SortingBin> newActiveBins = binLayoutManager.GetActiveBins();
        int newBinCount                = newActiveBins.Count;
        bool isNewBinActivated         = newBinCount > previousBinCount;

        // Persist the current count so the next day's InitializeDay reads the correct previous value.
        activeBinCount = newBinCount;

        if (isNewBinActivated)
        {
            // The last bin in activation order is always the newly added one —
            // BinLayoutManager activates slots in a fixed order, so the highest index is newest.
            SortingBin newBin = newActiveBins[newBinCount - 1];
            string newBinID   = newBin.GetBinID();

            pendingSummary.newBinAdded       = true;
            pendingSummary.newBinDescription = "New bin: " + newBinID;
        }

        List<SortingBin> activeBins = newActiveBins;

        // Guard: zero active bins would cause divide-by-zero in the rules-per-bin calculation.
        if (activeBins.Count == 0)
        {
            Debug.LogError("[GameManager] No active bins returned by BinLayoutManager. " +
                           "Assign at least one BinSlot in the Inspector.");
            return;
        }

        List<string> activeBinIDs = ExtractBinIDs(activeBins);
        ruleGenerator.SetAvailableBins(activeBinIDs);

        Debug.Log($"[GameManager] STEP 3 — {activeBins.Count} active bins: [{string.Join(", ", activeBinIDs)}]");

        // STEP 4 — Load rules for the current night from the Floor Designer data.
        // The GameScene is exclusively driven by designer-authored floors — no procedural
        // generation ever runs here. Every night must have explicit per-bin rule assignments
        // saved in the floor JSON via the Floor Designer tool.
        bool hasNightData = currentFloorSaveData != null &&
                            currentFloorSaveData.nights != null &&
                            currentFloorSaveData.nights.Count > currentDayIndex;

        if (!hasNightData)
        {
            Debug.LogError($"[GameManager] STEP 4 — No night data found for night index {currentDayIndex} " +
                           $"in floor {currentFloorIndex}. Open the Floor Designer, configure all 5 nights " +
                           $"with rules, and re-save the floor before launching the game.");
            return;
        }

        NightSaveData nightData = currentFloorSaveData.nights[currentDayIndex];

        bool hasDesignerBinRules = nightData.bins != null &&
                                   nightData.bins.Count > 0 &&
                                   nightData.bins.Exists(b => b.rules != null && b.rules.Count > 0);

        if (!hasDesignerBinRules)
        {
            Debug.LogError($"[GameManager] STEP 4 — Night {currentDayIndex} of floor {currentFloorIndex} " +
                           $"has no bin rules. Open the Floor Designer, assign at least one rule to each bin " +
                           $"for every night, then re-save the floor.");
            return;
        }

        activeRules = RestoreRulesFromNight(nightData, activeBins);

        Debug.Log($"[GameManager] STEP 4 — Night {currentDayIndex + 1}: " +
                  $"loaded {activeRules.Count} designer-authored rules from floor JSON.");

        // STEP 4b — Activate the trash bin when this night requires it.
        // The trash bin is independent from the regular 2×2 layout; it never counts
        // toward numberOfBins and is always the bottom-centre slot.
        bool hasTrash = nightData.hasTrashedPrefab &&
                        nightData.trashedPrefabPaths != null &&
                        nightData.trashedPrefabPaths.Count > 0;

        binLayoutManager.SetTrashBinActive(hasTrash);

        if (hasTrash)
        {
            SortingBin trashBin = binLayoutManager.GetTrashBin();

            if (trashBin != null)
            {
                // Build one Prefab-type rule per trash prefab path and assign all of them
                // to the trash bin so ValidateDocument accepts any of the designated prefabs.
                List<RuleData> trashRules = BuildTrashRules(nightData.trashedPrefabPaths);
                trashBin.AssignRules(trashRules);
                trashBin.SetAllActiveRules(activeRules);

                // Append trash rules to activeRules so SetAllActiveRules on regular bins
                // and DocumentSpawner.UpdateActiveRules both have the complete picture.
                activeRules.AddRange(trashRules);

                Debug.Log($"[GameManager] STEP 4b — Trash bin activated with " +
                          $"{nightData.trashedPrefabPaths.Count} prefab(s).");
            }
            else
            {
                Debug.LogWarning("[GameManager] STEP 4b — hasTrashedPrefab is true but no trash bin " +
                                 "SortingBin found. Assign the trash bin slot in BinLayoutManager.");
            }
        }

        // STEP 5 — (Designer-mode only) Per-night bin count override.
        // The night may specify a different numberOfBins than the floor-level default.
        // Re-apply only when the night's value is explicitly set (> 0) and differs from
        // what was already applied in STEP 3, keeping activeBins in sync.
        if (nightData.numberOfBins > 0 && nightData.numberOfBins != newBinCount)
        {
            binLayoutManager.SetActiveBinCount(nightData.numberOfBins);
            activeBins   = binLayoutManager.GetActiveBins();
            newBinCount  = activeBins.Count;
            activeBinCount = newBinCount;

            Debug.Log($"[GameManager] STEP 5 — Night {currentDayIndex + 1} overrides bin count " +
                      $"to {nightData.numberOfBins}.");
        }

        // STEP 7 — Distribute rules to bins.
        Debug.Log($"[GameManager] STEP 7 — distributing {activeRules?.Count ?? 0} rules to {activeBins.Count} bins");
        DistributeRulesToBins(activeRules, activeBins);

        // Inject the full cross-bin rule list into every bin immediately after distribution.
        // Bins need this only for Fallback validation — they must not query each other directly,
        // so GameManager is the correct point to push a single flat snapshot to all bins.
        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(activeRules);

        // Active bins change each day — refresh all event subscriptions after the new bin set
        // is established so no stale subscriptions from previous days remain.
        // Pass hasTrash so the trash bin is wired or unwired correctly.
        RefreshBinSubscriptions(activeBins, hasTrash);

        documentSpawner.UpdateActiveRules(activeRules);

        // Inject trash prefabs into the spawner so they mix into the spawn pool
        // alongside regular prefabs for this night.
        List<string> trashPaths = hasTrash ? nightData.trashedPrefabPaths : null;
        documentSpawner.SetTrashPrefabs(trashPaths);

        // Clear any stale documents from the previous day before starting the new spawn loop.
        documentStackManager.ClearStack();

        // STEP 8 — Start day systems.
        documentSpawner.StartDay();
    }

    // -------------------------------------------------------------------------
    // Private helpers — floor and day progression
    // -------------------------------------------------------------------------

    /// <summary>
    /// Applies all parameters from floorData to the relevant sub-systems and then calls
    /// InitializeDay() to begin the first day on that floor.
    /// This is the single authoritative entry point for starting any floor — using it
    /// ensures no system is accidentally left with stale parameters from a previous floor.
    /// </summary>
    /// <param name="floorData">The floor whose parameters should be applied. Must not be null.</param>
    private void InitializeFromFloorData(FloorSaveData floorData)
    {
        if (floorData == null)
        {
            Debug.LogError("[GameManager] InitializeFromFloorData called with null floorData.");
            return;
        }

        currentFloorSaveData = floorData;
        currentFloorIndex    = floorData.floorIndex;
        currentDayIndex      = 0;
        isFloorBonusApplied  = true; // Parameters come from floorData — floor bonus must not re-apply.

        profitabilityManager.SetFailThreshold(floorData.failThreshold);
        profitabilityManager.SetDayDuration(floorData.dayDuration);
        profitabilityManager.SetDecayRate(floorData.decayRatePerSecond);

        // Bin count from floorData overrides whatever BinLayoutManager's default is —
        // the stored value is always the canonical bin count for this floor.
        binLayoutManager.SetActiveBinCount(floorData.numberOfBins);

        // OPTION A — synchronise activeBinCount immediately after applying the floor's bin count.
        // Without this, activeBinCount remains 0 when InitializeDay reads it as previousBinCount,
        // causing isNewBinActivated to be true on day 1 of every floor regardless of whether a
        // bin actually appeared (0 < floorData.numberOfBins is always true).
        // Reading GetActiveBinCount() here is safe: SetActiveBinCount above has already updated
        // BinLayoutManager's internal counter to the correct clamped value.
        activeBinCount = binLayoutManager.GetActiveBinCount();

        InitializeDay();
    }

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
            // Pick a random bin to receive the new rule, excluding the last bin that received one.
            // Excluding the last bin ensures rules distribute across all active bins and the
            // same bin never gets two rules in a row, keeping distribution fair and unpredictable.
            string targetBinID = PickRandomBinIDExcluding(lastRuleAssignedBinID);

            // Update lastRuleAssignedBinID immediately after selection so the next call
            // to PickRandomBinIDExcluding correctly excludes this bin.
            lastRuleAssignedBinID = targetBinID;

            Debug.Log("[GameManager] New rule assigned to: " + targetBinID +
                      " (excluded: " + lastRuleAssignedBinID + ")");

            // Pass activeRules so GenerateSingleRule can detect conflicts with the existing set
            // and return a resolution rule when one is needed.
            var (newRule, resolutionRule) = ruleGenerator.GenerateSingleRule(
                targetBinID,
                currentDayData.maxRuleComplexity,
                activeRules);

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

            // resolutionRule is non-null only when GenerateSingleRule detected a conflict —
            // assign it directly to the affected bin so the display updates immediately.
            if (resolutionRule != null)
            {
                activeRules.Add(resolutionRule);

                // Find the bin that owns the conflicting rule so we can push the resolution rule to it.
                SortingBin resolutionBin = activeBins.FirstOrDefault(
                    bin => bin.GetBinID() == resolutionRule.targetBinID);

                if (resolutionBin != null)
                {
                    // Collect all rules already assigned to that bin plus the new resolution rule
                    // so AssignRules replaces the full list rather than overwriting with just one entry.
                    List<RuleData> updatedBinRules = activeRules
                        .Where(rule => rule.targetBinID == resolutionRule.targetBinID)
                        .ToList();

                    resolutionBin.AssignRules(updatedBinRules);

                    Debug.Log("[GameManager] Resolution rule assigned to bin: " + resolutionRule.targetBinID);
                }
            }

            // Always rebuild the spawn pool after evolution — a new rule changes which combinations
            // are valid regardless of whether a conflict resolution rule was also added.
            // Moving this call outside the resolutionRule block prevents the pool from staying stale
            // when only a primary rule was added and no conflict was detected.
            documentSpawner.UpdateActiveRules(activeRules);

            Debug.Log("[GameManager] Spawn pool updated. Active rules: " + activeRules.Count +
                      " Valid combinations: " + documentSpawner.GetValidCombinationCount());
        }
    }

    /// <summary>
    /// Guarantees that the newly activated bin always has at least one rule assigned to it.
    /// Calls AssignRules directly on the bin immediately — not only via DistributeRulesToBins —
    /// because a bin with no rules displays nothing and may not activate correctly, which
    /// confuses the player and breaks document spawning for that bin.
    /// Called in the same InitializeDay step that detected the new bin, after evolution has run,
    /// so the bin gets a rule regardless of whether evolution happened to target it.
    /// </summary>
    /// <param name="newBin">The SortingBin that was just activated this day.</param>
    private void EnsureNewBinHasRule(SortingBin newBin)
    {
        string newBinID = newBin.GetBinID();

        // Check whether evolution already added a rule to the new bin —
        // if so, adding another one would over-populate it on day 2.
        bool newBinAlreadyHasRule = activeRules.Exists(rule => rule.targetBinID == newBinID);

        if (newBinAlreadyHasRule)
            return;

        RuleData ruleForNewBin;

        // Pass activeRules so conflict detection runs even for the forced new-bin rule.
        // resolutionRule is discarded here — EnsureNewBinHasRule fires before DistributeRulesToBins,
        // so any resolution rule generated now will be included in the Step 7 distribution.
        (ruleForNewBin, _) = ruleGenerator.GenerateSingleRule(
            newBinID,
            currentDayData.maxRuleComplexity,
            activeRules);

        if (ruleForNewBin == null)
        {
            Debug.LogWarning("[GameManager] EnsureNewBinHasRule: specificity pool exhausted — " +
                             "new bin " + newBinID + " received no rule.");
            return;
        }

        // Add to activeRules so DistributeRulesToBins (Step 7) and SetAllActiveRules include it.
        activeRules.Add(ruleForNewBin);

        // Assign directly to the bin immediately — new bin MUST have at least one rule
        // assigned on the same frame it activates so it displays correctly before Step 7 runs.
        List<RuleData> newBinRules = new List<RuleData> { ruleForNewBin };
        newBin.AssignRules(newBinRules);

        // Update the summary to reflect that the new bin also received a rule.
        // This overwrites the newBinDescription set in Step 3 to include the rule text,
        // giving the player full context: which bin appeared and what rule it carries.
        pendingSummary.newRuleAdded       = true;
        pendingSummary.newRuleDescription = "New bin: " + newBinID + " — " + ruleForNewBin.displayText;

        Debug.Log("[GameManager] New bin activated: " + newBinID +
                  " with rule: " + ruleForNewBin.displayText);
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

    /// <summary>
    /// Returns a random bin ID from the currently active bins, excluding the given bin ID.
    /// Exclusion prevents the same bin from receiving two rules in a row, distributing new
    /// rules fairly across all active bins over successive days.
    /// Falls back to any bin when only one is active — with a single bin, exclusion is
    /// impossible and that bin must be used regardless.
    /// </summary>
    /// <param name="excludedBinID">The bin ID to exclude from selection. Pass empty string to skip exclusion.</param>
    /// <returns>A randomly selected bin ID, never the excluded one unless it is the only option.</returns>
    private string PickRandomBinIDExcluding(string excludedBinID)
    {
        List<SortingBin> activeBins = binLayoutManager.GetActiveBins();

        List<string> eligibleBinIDs = activeBins
            .Select(bin => bin.GetBinID())
            .Where(id => id != excludedBinID)
            .ToList();

        // With only 1 bin, cannot exclude it — must use it regardless so the game never stalls.
        if (eligibleBinIDs.Count == 0)
            return activeBins[0].GetBinID();

        // Random selection excluding the last used bin ensures rules distribute across all bins
        // and the same bin never gets two rules in a row.
        return eligibleBinIDs[UnityEngine.Random.Range(0, eligibleBinIDs.Count)];
    }

    // -------------------------------------------------------------------------
    // Private helpers — event subscriptions
    // -------------------------------------------------------------------------

    /// <summary>
    /// Unsubscribes onValidDrop and onInvalidDrop from every bin managed by BinLayoutManager,
    /// then re-subscribes only to the currently active ones.
    /// When includeTrash is true, also wires the trash bin's events so correct/wrong drops
    /// on it are scored like any other bin.
    /// Called at the end of InitializeDay because the active bin set changes every day —
    /// without a full unsubscribe pass, bins deactivated mid-session would retain stale
    /// subscriptions and fire duplicate events the next time they become active.
    /// </summary>
    /// <param name="activeBins">The regular bins active for the current day.</param>
    /// <param name="includeTrash">When true, also subscribe to the trash bin's events.</param>
    private void RefreshBinSubscriptions(List<SortingBin> activeBins, bool includeTrash = false)
    {
        List<SortingBin> allBins = binLayoutManager.GetAllBins();

        foreach (SortingBin bin in allBins)
        {
            bin.onValidDrop   -= OnValidDrop;
            bin.onInvalidDrop -= OnInvalidDrop;
        }

        foreach (SortingBin bin in activeBins)
        {
            bin.onValidDrop   += OnValidDrop;
            bin.onInvalidDrop += OnInvalidDrop;
        }

        // Wire the trash bin separately so its events are always in a known state.
        if (includeTrash)
        {
            SortingBin trashBin = binLayoutManager.GetTrashBin();

            if (trashBin != null)
            {
                trashBin.onValidDrop   += OnValidDrop;
                trashBin.onInvalidDrop += OnInvalidDrop;
            }
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

    // -------------------------------------------------------------------------
    // Floor save / restore
    // -------------------------------------------------------------------------

    /// <summary>
    /// Persists progress when the player completes all daysPerFloor days on a floor.
    /// Branches on game mode to keep the two persistence systems completely isolated:
    ///   Procedural mode (TowerScene origin): writes only to PlayerPrefs — never touches floor_N.json.
    ///   Story mode (DesignerTowerScene origin): writes floor_N.json via FloorSaveSystem — never
    ///   touches PlayerPrefs.
    /// Called by OnDaySuccess exactly when all daysPerFloor have been completed.
    /// Keeps currentFloorSaveData in sync so GenerateNextFloorData() can read accurate values.
    /// Also resets progression counters so the tower scene starts the player on the next floor.
    /// </summary>
    private void SaveCurrentFloor()
    {
        // Read rulesPerBin and maxRuleComplexity from currentFloorSaveData if available,
        // otherwise fall back to currentDayData which reflects what was actually used this day.
        int savedRulesPerBin       = currentFloorSaveData != null
                                     ? currentFloorSaveData.rulesPerBin
                                     : currentDayData.rulesPerBin;

        int savedMaxRuleComplexity = currentFloorSaveData != null
                                     ? currentFloorSaveData.maxRuleComplexity
                                     : currentDayData.maxRuleComplexity;

        FloorSaveData floorData = new FloorSaveData
        {
            floorIndex         = currentFloorIndex,
            isCompleted        = true,
            wasGenerated       = currentFloorSaveData != null && currentFloorSaveData.wasGenerated,
            numberOfBins       = binLayoutManager.GetActiveBinCount(),
            failThreshold      = profitabilityManager.GetFailThreshold(),
            dayDuration        = profitabilityManager.GetDayDuration(),
            decayRatePerSecond = profitabilityManager.GetDecayRate(),
            rulesPerBin        = savedRulesPerBin,
            maxRuleComplexity  = savedMaxRuleComplexity,
            floorSeed          = string.Empty
        };

        // Convert each active RuleData to its serialisable SavedRuleData form.
        // RuleType enum values are stored as strings to keep JSON files human-readable
        // and to avoid breakage if the enum order changes in a future build.
        foreach (RuleData rule in activeRules)
        {
            SavedRuleData savedRule = new SavedRuleData
            {
                ruleType       = rule.ruleType.ToString(),
                conditionA     = rule.conditionA,
                conditionB     = rule.conditionB,
                targetBinID    = rule.targetBinID,
                secondaryBinID = rule.secondaryBinID,
                displayText    = rule.displayText,
                complexity     = rule.complexity,
                isComplement   = rule.isComplement
            };

            floorData.rules.Add(savedRule);

            // Collect all condition strings as specificities for replay reference.
            if (!string.IsNullOrEmpty(rule.conditionA) && !floorData.specificities.Contains(rule.conditionA))
                floorData.specificities.Add(rule.conditionA);

            if (!string.IsNullOrEmpty(rule.conditionB) && !floorData.specificities.Contains(rule.conditionB))
                floorData.specificities.Add(rule.conditionB);
        }

        // ── Mode branch ────────────────────────────────────────────────────────
        // wasGenerated == true means the floor was produced procedurally (TowerScene path).
        // wasGenerated == false means the floor was loaded from a designer JSON file (story path).
        // The two persistence systems must never cross: procedural uses PlayerPrefs,
        // story mode uses FloorSaveSystem JSON — mixing them would corrupt both modes.
        if (floorData.wasGenerated)
        {
            // Procedural mode — write progress to PlayerPrefs only.
            // The key mirrors the one in TowerManager so both read from the same source.
            // PlayerPrefs.Save() flushes immediately so progress survives a crash or force-quit.
            PlayerPrefs.SetInt("ProceduralHighestFloor", currentFloorIndex + 1);
            PlayerPrefs.Save();
            Debug.Log("[GameManager] Procedural floor " + currentFloorIndex
                + " completed — progress saved to PlayerPrefs.");
        }
        else
        {
            // Story mode — write floor_N.json via FloorSaveSystem.
            // Never writes to PlayerPrefs — story progress is tracked in JSON only.
            if (floorSaveSystem == null)
            {
                Debug.LogError("[GameManager] SaveCurrentFloor (story mode): floorSaveSystem is not assigned.");
            }
            else
            {
                floorSaveSystem.SaveFloor(floorData);
                Debug.Log("[GameManager] Story floor " + currentFloorIndex
                    + " completed — saved to disk via FloorSaveSystem.");
            }
        }

        // Keep currentFloorSaveData pointing at the just-saved snapshot so
        // OnDaySuccess can immediately pass it to GenerateNextFloorData().
        currentFloorSaveData = floorData;

        // Advance floor index and reset day counter for the next session.
        currentDayIndex     = 0;
        currentFloorIndex++;
        isFloorBonusApplied = false;
    }

    /// <summary>
    /// Restores the game state from a saved FloorSaveData so the player can replay a completed floor.
    /// Sets all profitability parameters, rebuilds the active rule list from serialised data,
    /// distributes rules to their matching bins, and starts the day.
    /// Exact restoration means the same rules, the same bins, and the same difficulty as the original run.
    /// </summary>
    /// <param name="saveData">The saved floor data to restore. Must not be null.</param>
    private void RestoreFloorFromSave(FloorSaveData saveData)
    {
        currentFloorIndex   = saveData.floorIndex;
        currentDayIndex     = 0;
        isFloorBonusApplied = true; // Floor bonus already encoded in saved parameters — do not re-apply.

        // Apply the saved difficulty parameters directly, bypassing FloorProgressionManager
        // so the restored session matches the original difficulty exactly.
        profitabilityManager.SetFailThreshold(saveData.failThreshold);
        profitabilityManager.SetDayDuration(saveData.dayDuration);
        profitabilityManager.SetDecayRate(saveData.decayRatePerSecond);

        profitabilityManager.StartDay();
        dayTimerUI.ResetDisplay();

        profitabilityBarUI.Initialize(
            profitabilityManager.GetFailThreshold(),
            profitabilityManager.GetCurrentProfitability());

        // Rebuild the active bin layout to match the saved bin count.
        currentDayData = difficultyManager.ComputeDayData(currentDayIndex, currentFloorIndex);
        binLayoutManager.SetActiveBinCount(saveData.numberOfBins);

        List<SortingBin> activeBins   = binLayoutManager.GetActiveBins();
        List<string> activeBinIDs = ExtractBinIDs(activeBins);
        ruleGenerator.SetAvailableBins(activeBinIDs);

        // Convert each SavedRuleData back to a full RuleData for validation and display.
        activeRules = new List<RuleData>();

        foreach (SavedRuleData savedRule in saveData.rules)
        {
            // Parse the string back to the enum safely; skip the entry if the string is unrecognised
            // (e.g. the enum was renamed in a later build) rather than crashing with an exception.
            bool isParsed = Enum.TryParse(savedRule.ruleType, out RuleType parsedType);

            if (!isParsed)
            {
                Debug.LogWarning($"[GameManager] RestoreFloorFromSave: unknown ruleType '{savedRule.ruleType}' — skipped.");
                continue;
            }

            RuleData rule = new RuleData
            {
                ruleType       = parsedType,
                conditionA     = savedRule.conditionA,
                conditionB     = savedRule.conditionB,
                targetBinID    = savedRule.targetBinID,
                secondaryBinID = savedRule.secondaryBinID,
                displayText    = savedRule.displayText,
                complexity     = savedRule.complexity,
                isComplement   = savedRule.isComplement
            };

            activeRules.Add(rule);
        }

        DistributeRulesToBins(activeRules, activeBins);

        foreach (SortingBin bin in activeBins)
            bin.SetAllActiveRules(activeRules);

        RefreshBinSubscriptions(activeBins);

        documentSpawner.UpdateActiveRules(activeRules);
        documentStackManager.ClearStack();
        documentSpawner.StartDay();

        pendingSummary = new DifficultyChangeSummary();
    }

    // -------------------------------------------------------------------------
    // Private helpers — trash bin
    // -------------------------------------------------------------------------

    /// <summary>
    /// Builds one Simple RuleData per trash prefab path, all targeting "bin_trash".
    /// Each rule uses the prefab path as conditionA so SortingBin.ValidateSimple can
    /// match it against DocumentData.specificities (which stores the prefab path tag).
    /// </summary>
    /// <param name="paths">Full Unity asset paths of the designated trash prefabs.</param>
    /// <returns>One RuleData per valid path, all with targetBinID "bin_trash".</returns>
    private List<RuleData> BuildTrashRules(List<string> paths)
    {
        List<RuleData> trashRules = new List<RuleData>();

        foreach (string path in paths)
        {
            if (string.IsNullOrEmpty(path))
                continue;

            string prefabName = System.IO.Path.GetFileNameWithoutExtension(path);

            trashRules.Add(new RuleData
            {
                ruleType    = RuleType.Simple,
                conditionA  = path,
                conditionB  = string.Empty,
                targetBinID = "bin_trash",
                displayText = $"🗑 {prefabName}",
                complexity  = 1,
                isComplement = false
            });
        }

        return trashRules;
    }

    // -------------------------------------------------------------------------
    // Night rule restoration — Floor Designer path
    // -------------------------------------------------------------------------

    /// <summary>
    /// Reconstructs a flat <see cref="RuleData"/> list from the per-bin rule assignments
    /// authored by the designer in the Floor Designer tool for a specific night.
    ///
    /// Each <see cref="BinSaveData"/> inside the night carries an explicit list of
    /// <see cref="SavedRuleData"/> entries. This method deserialises those entries and
    /// resolves the <c>targetBinID</c> from the bin's own <c>binID</c> field when the
    /// saved rule's targetBinID is empty — ensuring rules always reference a valid bin.
    ///
    /// Active bins are passed so unknown binIDs can be remapped to a live bin in order
    /// (first active bin for BinSaveData at index 0, second for index 1, etc.).
    /// Rules whose ruleType string is unrecognised are skipped with a warning rather than
    /// crashing, preserving backward compatibility with older save formats.
    /// </summary>
    /// <param name="nightData">The night whose BinSaveData should be deserialised.</param>
    /// <param name="activeBins">Currently active SortingBin instances used for binID remapping.</param>
    /// <returns>Flat list of all reconstructed RuleData across all bins for this night.</returns>
    private List<RuleData> RestoreRulesFromNight(NightSaveData nightData, List<SortingBin> activeBins)
    {
        List<RuleData> restoredRules = new List<RuleData>();

        if (nightData.bins == null || nightData.bins.Count == 0)
        {
            Debug.LogError("[GameManager] RestoreRulesFromNight: nightData.bins is null or empty.");
            return restoredRules;
        }

        // Diagnostic: log the exact binID/rule count coming from the JSON so mismatches
        // between the JSON and the scene's SortingBin.binID values are immediately visible.
        System.Text.StringBuilder diagLog = new System.Text.StringBuilder();
        diagLog.AppendLine($"[GameManager] RestoreRulesFromNight — night {nightData.nightIndex}, " +
                           $"{nightData.bins.Count} bins in JSON, {activeBins.Count} active bins in scene:");

        for (int i = 0; i < nightData.bins.Count; i++)
        {
            BinSaveData b = nightData.bins[i];
            diagLog.AppendLine($"  JSON bin[{i}] binID='{b.binID}'  rules={b.rules?.Count ?? 0}");
            if (b.rules != null)
                foreach (SavedRuleData r in b.rules)
                    diagLog.AppendLine($"    rule targetBinID='{r.targetBinID}'  condA='{r.conditionA}'  type='{r.ruleType}'");
        }

        diagLog.AppendLine("  Active scene bins:");
        for (int i = 0; i < activeBins.Count; i++)
            diagLog.AppendLine($"  scene bin[{i}] binID='{activeBins[i].GetBinID()}'");

        Debug.Log(diagLog.ToString());

        for (int binSaveIndex = 0; binSaveIndex < nightData.bins.Count; binSaveIndex++)
        {
            BinSaveData binSave = nightData.bins[binSaveIndex];

            if (binSave.rules == null || binSave.rules.Count == 0)
                continue;

            // Resolve the canonical bin ID for this slot:
            // 1. Use the binID stored in BinSaveData when non-empty.
            // 2. Fall back to the active bin at the matching position index.
            // 3. Final fallback: skip if no live bin can be mapped.
            string resolvedBinID = binSave.binID;

            if (string.IsNullOrEmpty(resolvedBinID))
            {
                if (binSaveIndex < activeBins.Count)
                    resolvedBinID = activeBins[binSaveIndex].GetBinID();
                else
                {
                    Debug.LogWarning($"[GameManager] RestoreRulesFromNight: " +
                                     $"BinSaveData at index {binSaveIndex} has no binID and no matching active bin — skipped.");
                    continue;
                }
            }

            foreach (SavedRuleData savedRule in binSave.rules)
            {
                bool isParsed = Enum.TryParse(savedRule.ruleType, out RuleType parsedType);

                if (!isParsed)
                {
                    Debug.LogWarning($"[GameManager] RestoreRulesFromNight: " +
                                     $"unknown ruleType '{savedRule.ruleType}' in bin '{resolvedBinID}' — skipped.");
                    continue;
                }

                // When targetBinID is empty in the saved rule, inherit the parent bin's ID.
                // Also override any stale targetBinID that doesn't match the parent BinSaveData.binID —
                // this corrects JSON files saved before the canonical ID fix (bin_A → bin_left_top, etc.)
                // so the player never gets empty bins due to an old format on disk.
                string targetBinID = string.IsNullOrEmpty(savedRule.targetBinID)
                    ? resolvedBinID
                    : savedRule.targetBinID;

                // Override stale IDs: if targetBinID doesn't match any active scene bin,
                // use the parent BinSaveData.binID (which was already corrected by the Floor Designer fix).
                bool targetExists = activeBins.Exists(b => b.GetBinID() == targetBinID);
                if (!targetExists)
                {
                    Debug.LogWarning($"[GameManager] RestoreRulesFromNight: " +
                                     $"rule targetBinID='{targetBinID}' not found in scene — " +
                                     $"overriding with parent binID='{resolvedBinID}'.");
                    targetBinID = resolvedBinID;
                }

                RuleData rule = new RuleData
                {
                    ruleType       = parsedType,
                    conditionA     = savedRule.conditionA,
                    conditionB     = savedRule.conditionB,
                    targetBinID    = targetBinID,
                    secondaryBinID = savedRule.secondaryBinID ?? string.Empty,
                    displayText    = savedRule.displayText,
                    complexity     = savedRule.complexity,
                    isComplement   = savedRule.isComplement
                };

                restoredRules.Add(rule);
            }
        }

        Debug.Log($"[GameManager] RestoreRulesFromNight — restored {restoredRules.Count} total rules.");
        return restoredRules;
    }
}