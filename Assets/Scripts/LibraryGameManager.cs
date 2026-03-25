using System;
using System.Collections.Generic;
using System.IO;
using TMPro;
using UnityEngine;

/// <summary>
/// Orchestrates the Library Rule Test Scene.
/// Reads the Rule Library JSON, passes entries to <see cref="LibraryRuleAssigner"/>,
/// wires bin events to the score/profitability display, and controls the day lifecycle.
///
/// Pipeline on StartDay():
///   1. Load RuleLibraryFile from disk.
///   2. Build the list of active bin IDs from the active SortingBin instances.
///   3. Call LibraryRuleAssigner.AssignFromLibrary() — which fires OnRulesAssigned
///      so LibraryDocumentSpawner rebuilds its pool automatically.
///   4. Start LibraryDocumentSpawner.
///
/// Does NOT generate rules procedurally, compute floor progression, manage saves,
/// or reference any scene-transition code from the main game.
/// </summary>
public class LibraryGameManager : MonoBehaviour
{
    // ─── Inspector ────────────────────────────────────────────────────────────────

    [SerializeField] private LibraryRuleAssigner ruleAssigner;
    [SerializeField] private LibraryDocumentSpawner documentSpawner;
    [SerializeField] private BinLayoutManager binLayoutManager;
    [SerializeField] private DocumentStackManager documentStackManager;
    [SerializeField] private ProfitabilityManager profitabilityManager;
    [SerializeField] private ProfitabilityBarUI profitabilityBarUI;
    [SerializeField] private DayTimerUI dayTimerUI;

    /// <summary>
    /// Optional upgrader component. When assigned, rules are upgraded after the
    /// initial library assignment using the <see cref="upgradeTargetComplexity"/> ceiling.
    /// Leave unassigned to skip the upgrade pass entirely.
    /// </summary>
    [SerializeField] private RuleComplexityUpgrader ruleComplexityUpgrader;

    /// <summary>
    /// Complexity ceiling used when <see cref="ruleComplexityUpgrader"/> runs.
    /// Corresponds to the current floor's maxRuleComplexity.
    /// Rules below this ceiling are eligible for upgrade if a matching higher-complexity
    /// candidate exists in the Rule Library.
    /// </summary>
    [SerializeField] private int upgradeTargetComplexity = 2;

    /// <summary>Number of sorting bins to activate in the test scene.</summary>
    [SerializeField] private int numberOfBins = 2;

    /// <summary>Day duration in seconds for the test session.</summary>
    [SerializeField] private float dayDuration = 120f;

    /// <summary>Profitability fail threshold (0–100).</summary>
    [SerializeField] private float failThreshold = 30f;

    /// <summary>Profitability decay per second.</summary>
    [SerializeField] private float decayRate = 1f;

    /// <summary>Optional status text shown in the test scene HUD.</summary>
    [SerializeField] private TextMeshProUGUI statusText;

    /// <summary>Optional panel displayed when the day ends (success or failure).</summary>
    [SerializeField] private GameObject endOfDayPanel;

    /// <summary>Optional label inside the end-of-day panel.</summary>
    [SerializeField] private TextMeshProUGUI endOfDayLabel;

    // ─── Constants ────────────────────────────────────────────────────────────────

    private static string LibraryFilePath =>
        Path.Combine(Application.dataPath, "Editor", "RuleLibraryData.json");

    // ─── Runtime state ────────────────────────────────────────────────────────────

    private List<SortingBin> activeBins = new List<SortingBin>();
    private bool isDayActive;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────────

    private void Start()
    {
        if (endOfDayPanel != null)
            endOfDayPanel.SetActive(false);

        SubscribeToEvents();
        StartDay();
    }

    private void OnDestroy()
    {
        UnsubscribeFromEvents();
    }

    // ─── Public API ───────────────────────────────────────────────────────────────

    /// <summary>
    /// Initialises and starts a new test day.
    /// Reloads the Rule Library from disk each call so changes saved in the editor
    /// tool are immediately reflected without restarting Unity.
    /// </summary>
    public void StartDay()
    {
        if (endOfDayPanel != null)
            endOfDayPanel.SetActive(false);

        // 1 — Configure difficulty.
        profitabilityManager.SetFailThreshold(failThreshold);
        profitabilityManager.SetDayDuration(dayDuration);
        profitabilityManager.SetDecayRate(decayRate);

        // 2 — Activate bins.
        binLayoutManager.SetActiveBinCount(numberOfBins);
        activeBins = binLayoutManager.GetActiveBins();

        List<string> activeBinIDs = new List<string>();
        foreach (SortingBin bin in activeBins)
            activeBinIDs.Add(bin.GetBinID());

        // 3 — Load library and assign rules.
        RuleLibraryFile libraryFile = LoadLibrary();
        ruleAssigner.AssignFromLibrary(libraryFile, activeBins, activeBinIDs);

        // 3b — Optional complexity upgrade pass.
        // Runs after the initial assignment so upgrades operate on fully resolved rules.
        // OnRulesAssigned (→ LibraryDocumentSpawner) fires inside AssignFromLibrary;
        // the upgrader calls AssignRules again on each bin, which calls DisplayRules but
        // does NOT re-fire OnRulesAssigned. The spawner is therefore rebuilt manually
        // after the upgrade so its pool reflects the upgraded conditions.
        if (ruleComplexityUpgrader != null)
        {
            ruleComplexityUpgrader.TryUpgradeBins(activeBins, upgradeTargetComplexity);

            // Notify the spawner so it rebuilds its valid-combination pool from the upgraded rules.
            List<RuleData> upgradedRules = new List<RuleData>();
            foreach (SortingBin bin in activeBins)
                upgradedRules.AddRange(bin.GetAssignedRules());

            documentSpawner.RebuildPool(upgradedRules);
        }

        // 4 — Wire bin events.
        RefreshBinSubscriptions();

        // 5 — Start spawner and profitability.
        documentStackManager.ClearStack();
        documentSpawner.StartDay();

        profitabilityManager.StartDay();

        if (profitabilityBarUI != null)
            profitabilityBarUI.Initialize(
                profitabilityManager.GetFailThreshold(),
                profitabilityManager.GetCurrentProfitability());

        if (dayTimerUI != null)
            dayTimerUI.ResetDisplay();

        isDayActive = true;
        SetStatus("Jour en cours — triez les documents !");

        Debug.Log("[LibraryGameManager] Day started.");
    }

    /// <summary>Restarts the day — called by the Retry button in the end-of-day panel.</summary>
    public void RestartDay()
    {
        documentSpawner.StopDay();
        documentSpawner.ClearAllDocuments();
        documentStackManager.ClearStack();
        StartDay();
    }

    // ─── Event wiring ─────────────────────────────────────────────────────────────

    private void SubscribeToEvents()
    {
        profitabilityManager.onProfitabilityChanged += OnProfitabilityChanged;
        profitabilityManager.onDaySuccess           += OnDaySuccess;
        profitabilityManager.onDayFailed            += OnDayFailed;
        profitabilityManager.onTimerUpdated         += OnTimerUpdated;
    }

    private void UnsubscribeFromEvents()
    {
        profitabilityManager.onProfitabilityChanged -= OnProfitabilityChanged;
        profitabilityManager.onDaySuccess           -= OnDaySuccess;
        profitabilityManager.onDayFailed            -= OnDayFailed;
        profitabilityManager.onTimerUpdated         -= OnTimerUpdated;

        UnsubscribeFromBins();
    }

    private void RefreshBinSubscriptions()
    {
        UnsubscribeFromBins();

        foreach (SortingBin bin in activeBins)
        {
            bin.onValidDrop   += OnValidDrop;
            bin.onInvalidDrop += OnInvalidDrop;
        }
    }

    private void UnsubscribeFromBins()
    {
        // Use the cached activeBins list rather than calling binLayoutManager.GetAllBins()
        // to avoid a MissingReferenceException during OnDestroy: when Unity exits Play Mode,
        // scene objects are destroyed in an undefined order, so binSlots RectTransforms inside
        // BinLayoutManager may already be null by the time OnDestroy runs here.
        foreach (SortingBin bin in activeBins)
        {
            if (bin == null)
                continue;

            bin.onValidDrop   -= OnValidDrop;
            bin.onInvalidDrop -= OnInvalidDrop;
        }
    }

    // ─── Event handlers ───────────────────────────────────────────────────────────

    private void OnProfitabilityChanged(float value)
    {
        if (profitabilityBarUI != null)
            profitabilityBarUI.UpdateDisplay(value);
    }

    private void OnTimerUpdated(float remaining)
    {
        if (dayTimerUI != null)
            dayTimerUI.UpdateTimer(remaining);
    }

    private void OnDaySuccess()
    {
        if (!isDayActive)
            return;

        isDayActive = false;
        documentSpawner.StopDay();
        documentStackManager.ClearStack();
        ShowEndOfDay(success: true);
        Debug.Log("[LibraryGameManager] Day succeeded.");
    }

    private void OnDayFailed()
    {
        if (!isDayActive)
            return;

        isDayActive = false;
        documentSpawner.StopDay();
        documentStackManager.ClearStack();
        ShowEndOfDay(success: false);
        Debug.Log("[LibraryGameManager] Day failed.");
    }

    private void OnValidDrop()
    {
        profitabilityManager.OnDocumentSorted();
    }

    private void OnInvalidDrop()
    {
        profitabilityManager.ApplyWrongBinPenalty();
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────────

    private RuleLibraryFile LoadLibrary()
    {
        if (!File.Exists(LibraryFilePath))
        {
            Debug.LogWarning("[LibraryGameManager] RuleLibraryData.json not found — starting with empty library.");
            return new RuleLibraryFile();
        }

        try
        {
            string json = File.ReadAllText(LibraryFilePath);
            RuleLibraryFile file = JsonUtility.FromJson<RuleLibraryFile>(json);
            Debug.Log($"[LibraryGameManager] Loaded {file?.entries?.Count ?? 0} rule entries from library.");
            return file ?? new RuleLibraryFile();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[LibraryGameManager] Failed to load library: {exception.Message}");
            return new RuleLibraryFile();
        }
    }

    private void ShowEndOfDay(bool success)
    {
        if (endOfDayPanel != null)
            endOfDayPanel.SetActive(true);

        string message = success
            ? "Journée réussie !\nCliquez sur Recommencer pour rejouer."
            : "Journée échouée.\nCliquez sur Recommencer pour réessayer.";

        if (endOfDayLabel != null)
            endOfDayLabel.text = message;

        SetStatus(success ? "Succès !" : "Échec.");
    }

    private void SetStatus(string message)
    {
        if (statusText != null)
            statusText.text = message;
    }
}
