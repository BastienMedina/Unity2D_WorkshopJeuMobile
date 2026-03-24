// ANALYZED VALUES — extracted directly from source code, commit: analysis pass
//
// Day 1 binCount:       2    [DifficultyManager.minNumberOfBins = 2]
//                            [FloorDifficultyProgression.baseNumberOfBins = 2]
// Day 1 rulesPerBin:    1    [DifficultyManager.minRulesPerBin = 1]
//                            [FloorDifficultyProgression.baseRulesPerBin = 1]
// Day 1 maxComplexity:  1    [DifficultyManager.minRuleComplexity = 1]
//                            [FloorDifficultyProgression.baseMaxRuleComplexity = 1]
//
// Day evolution options (DifficultyManager.SelectDayEvolution):
//   - ComplexifyRule  (50%)  → RuleGenerator.ComplexifyExistingRule()
//   - AddNewRule      (50%)  → RuleGenerator.GenerateSingleRule()
//   NOTE: "Add new bin" is NOT a random daily evolution in the real code.
//         Bin count is determined by DifficultyManager.ComputeNumberOfBins() via a
//         continuous Lerp over the full game arc — it is NOT one of the three random
//         evolution options described in the design doc.
//   // DISCREPANCY: Design doc specifies 3 evolutions (ComplexifyRule, AddNewRule, AddBin).
//   //              Real code SelectDayEvolution() only has 2 (ComplexifyRule, AddNewRule).
//   //              AddBin is implicit via ComputeNumberOfBins Lerp, not a random pick.
//   //              Simulation uses design doc intent (3-way random) as a conscious override.
//   //              ⚠️ DESIGN MISMATCH flagged in report.
//
// Gain per correct document: 5f  [ProfitabilityManager.gainPerDocument = 5f, [SerializeField]]
// Initial profitability:     60f [ProfitabilityManager.initialProfitability = 60f, [SerializeField]]
//   // DISCREPANCY: Design doc / FIX 4 spec says start at 50%. Real code starts at 60%.
//   //              Simulation uses 60f from code. ⚠️ DESIGN MISMATCH flagged in report.
// Default decay rate:        2f  [ProfitabilityManager.decayRatePerSecond = 2f (private field)]
//                                [FloorDifficultyProgression.baseDecayRate = 2f]
// Default fail threshold:    20f [ProfitabilityManager.failThreshold = 20f (private field)]
//                                [FloorDifficultyProgression.baseFailThreshold = 20f]
// Default day duration:      60f [ProfitabilityManager.dayDuration = 60f, [SerializeField]]
//                                [FloorDifficultyProgression.baseDayDuration = 60f]
//
// Floor delta parameters (FloorDifficultyProgression):
//   failThresholdDeltaPct  = 0.08f  (+8% per floor, cap 60f)
//   dayDurationDeltaPct    = 0.10f  (+10% per floor, cap 300f)
//   decayRateDeltaPct      = 0.12f  (+12% per floor, cap 8f)
//   rulesPerBinDeltaEveryN = 2      (+1 rule/bin every 2 floors, max 4)
//   binsMaxDeltaEveryN     = 3      (+1 bin every 3 floors, max 5)
//   complexityDeltaEveryN  = 2      (+1 complexity every 2 floors, max 5)
//
// BinLayoutManager.maximumBinCount = 5 (Inspector default)
//   // DISCREPANCY: Design rule says "Max bin count is 3".
//   //              Real code caps at 5 via BinLayoutManager.maximumBinCount and
//   //              FloorDifficultyProgression.maxBins = 5.
//   //              ⚠️ DESIGN MISMATCH flagged in report.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Editor-only window that simulates a full game session without entering Play Mode.
/// Replicates the pure logic of DifficultyManager, FloorDifficultyProgression, and
/// RuleGenerator inline so no MonoBehaviour context is required.
/// All simulation state and report rendering are self-contained in this class.
/// Place in Assets/Editor — uses UnityEditor APIs that are stripped from player builds.
/// </summary>
public class PlaytestSimulatorWindow : EditorWindow
{
    // ─── Constants ──────────────────────────────────────────────────────────────

    private const float LeftPanelWidthRatio      = 0.30f;
    private const int   MaxDaysToSimulate        = 30;
    private const int   TotalFloors              = 5;
    private const int   DaysPerFloor             = 5;

    // DifficultyManager defaults — mirrored from Inspector defaults for headless simulation.
    private const int   MinRulesPerBin           = 1;
    private const int   MaxRulesPerBinCap        = 4;
    private const int   MinNumberOfBins          = 2;
    private const int   MaxNumberOfBins          = 5;
    private const int   MinRuleComplexity        = 1;
    private const float ComplexityIncreasePerDay = 0.5f;

    // FloorDifficultyProgression defaults — mirrored from Inspector defaults.
    private const float BaseFailThreshold        = 20f;
    private const float BaseDayDuration          = 60f;
    private const float BaseDecayRate            = 2f;
    private const int   BaseRulesPerBin          = 1;
    private const int   BaseNumberOfBins         = 2;
    private const int   BaseMaxRuleComplexity    = 1;
    private const float FailThresholdDeltaPct    = 0.08f;
    private const float DayDurationDeltaPct      = 0.10f;
    private const float DecayRateDeltaPct        = 0.12f;
    private const int   RulesPerBinDeltaEveryN   = 2;
    private const int   BinsMaxDeltaEveryN       = 3;
    private const int   ComplexityDeltaEveryN    = 2;
    private const float MaxFailThreshold         = 60f;
    private const float MaxDayDuration           = 300f;
    private const float MaxDecayRate             = 8f;
    private const int   MaxBins                  = 5;
    private const int   MaxRulesPerBin           = 4;
    private const int   MaxComplexity            = 5;

    private const int   MinimumComplexity        = 1;
    private const int   MaximumComplexity        = 5;

    // ProfitabilityManager defaults — extracted from source (real code values).
    // FALLBACK: SpawnInterval not directly readable from DocumentSpawner (demand-driven spawning);
    //           using estimated 5s as fallback matching original simulator constant.
    private const float InitialProfitability     = 60f;   // ProfitabilityManager.initialProfitability
    private const float GainPerDocument          = 5f;    // ProfitabilityManager.gainPerDocument
    private const float DefaultSpawnIntervalSecs = 5f;    // FALLBACK: demand-driven, not timer-based

    // ─── Design mismatch flags ───────────────────────────────────────────────────

    // DISCREPANCY: SelectDayEvolution() has only 2 evolutions (ComplexifyRule, AddNewRule).
    //              The simulation uses 3-way (add ComplexifyRule/AddNewRule/AddBin) per design doc.
    private const bool EvolutionDesignMismatch = true;
    // DISCREPANCY: initialProfitability = 60f in real code, design doc says 50%.
    private const bool ProfitabilityStartMismatch = true;
    // DISCREPANCY: maximumBinCount = 5 in real code, design doc says 3.
    private const bool MaxBinDesignMismatch = true;

    // ─── UI State ───────────────────────────────────────────────────────────────

    private int  floorIndex         = 1;
    private int  daysToSimulate     = 5;
    private int  randomSeed         = 42;
    private bool isSimulationRunning;

    private Vector2 reportScrollPosition;

    // ─── Cached GUIStyles — null-checked in OnGUI to survive domain reload ──────

    private GUIStyle reportStyle;
    private GUIStyle headerStyle;
    private GUIStyle runButtonStyle;

    // ─── Simulation result ──────────────────────────────────────────────────────

    private SimulationReport lastReport;
    private string           reportText = string.Empty;

    // ─── Design mismatch helpers ─────────────────────────────────────────────────

    private static List<string> BuildDesignMismatchList()
    {
        List<string> mismatches = new List<string>();

        if (EvolutionDesignMismatch)
            mismatches.Add("SelectDayEvolution() has only 2 options (ComplexifyRule, AddNewRule). " +
                           "Design doc specifies 3 (+ AddBin). Simulation uses 3-way random split.");

        if (ProfitabilityStartMismatch)
            mismatches.Add("initialProfitability = 60f in code. Design doc / FIX 4 spec says 50%. " +
                           "Simulation uses 60f (real code value).");

        if (MaxBinDesignMismatch)
            mismatches.Add("BinLayoutManager.maximumBinCount = 5 in code. Design doc says max is 3. " +
                           "Simulation respects code value (5).");

        return mismatches;
    }

    // ─── Entry point ────────────────────────────────────────────────────────────

    [MenuItem("Tools/Playtest Simulator")]
    public static void OpenWindow()
    {
        PlaytestSimulatorWindow window = GetWindow<PlaytestSimulatorWindow>("Playtest Simulator");
        window.minSize = new Vector2(800f, 500f);
        window.Show();
    }

    // ─── GUI ────────────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        // GUIStyles must be created inside OnGUI and cached with a null-check.
        // Creating them in constructors or field initialisers causes NullReferenceException
        // on domain reload because the Unity GUI skin is not yet available at that point.
        EnsureStylesInitialised();

        float totalWidth  = position.width;
        float leftWidth   = totalWidth * LeftPanelWidthRatio;
        float rightWidth  = totalWidth * (1f - LeftPanelWidthRatio);

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel(leftWidth);
        DrawRightPanel(rightWidth);
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Initialises all cached GUIStyles if they have not yet been created or were lost on domain reload.
    /// Safe to call every OnGUI frame — creates only when the reference is null.
    /// </summary>
    private void EnsureStylesInitialised()
    {
        if (reportStyle == null)
        {
            reportStyle = new GUIStyle(EditorStyles.label)
            {
                // Fixed-width font ensures columns in the report align correctly
                // regardless of the platform or editor font scaling setting.
                font      = EditorStyles.label.font,
                fontSize  = 11,
                wordWrap  = true,
                richText  = true
            };
            reportStyle.normal.textColor = EditorStyles.label.normal.textColor;
        }

        if (headerStyle == null)
        {
            headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 12
            };
        }

        if (runButtonStyle == null)
        {
            runButtonStyle = new GUIStyle(GUI.skin.button)
            {
                fontSize         = 14,
                fontStyle        = FontStyle.Bold,
                fixedHeight      = 42f,
                normal           = { textColor = Color.white }
            };

            // Green background for the run button — gives it clear visual affordance
            // so the designer knows immediately what the primary action is.
            Texture2D greenTex = new Texture2D(1, 1);
            greenTex.SetPixel(0, 0, new Color(0.22f, 0.60f, 0.22f));
            greenTex.Apply();
            runButtonStyle.normal.background   = greenTex;
            runButtonStyle.hover.background    = greenTex;
            runButtonStyle.active.background   = greenTex;
        }
    }

    /// <summary>Renders the left configuration panel with all input fields and action buttons.</summary>
    private void DrawLeftPanel(float panelWidth)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(panelWidth));
        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField("Simulation Settings", headerStyle);
        EditorGUILayout.Space(4f);

        floorIndex = Mathf.Max(0, EditorGUILayout.IntField("Floor Index", floorIndex));

        daysToSimulate = Mathf.Clamp(
            EditorGUILayout.IntField("Days to Simulate", daysToSimulate),
            1,
            MaxDaysToSimulate);

        // Seed row: field + Randomize button on the same line.
        EditorGUILayout.BeginHorizontal();
        randomSeed = EditorGUILayout.IntField("Random Seed", randomSeed);
        if (GUILayout.Button("Randomize", GUILayout.Width(80f)))
            randomSeed = UnityEngine.Random.Range(0, int.MaxValue);
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.Space(10f);

        // Disable the run button while a simulation is in progress to prevent re-entrant calls.
        EditorGUI.BeginDisabledGroup(isSimulationRunning);
        if (GUILayout.Button("▶  RUN SIMULATION", runButtonStyle))
            RunSimulation();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.Space(6f);
        DrawHorizontalSeparator();
        EditorGUILayout.Space(6f);

        // Export button is disabled until at least one simulation has been run.
        EditorGUI.BeginDisabledGroup(lastReport == null);
        if (GUILayout.Button("📋  Export Report to .txt"))
            ExportReport();
        EditorGUI.EndDisabledGroup();

        EditorGUILayout.EndVertical();
    }

    /// <summary>Renders the right scrollable report panel.</summary>
    private void DrawRightPanel(float panelWidth)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(panelWidth));
        EditorGUILayout.Space(6f);

        EditorGUILayout.LabelField("Simulation Report", headerStyle);
        EditorGUILayout.Space(4f);

        reportScrollPosition = EditorGUILayout.BeginScrollView(reportScrollPosition, GUILayout.ExpandHeight(true));

        if (string.IsNullOrEmpty(reportText))
        {
            EditorGUILayout.LabelField("Run a simulation to see results here.", EditorStyles.wordWrappedLabel);
        }
        else
        {
            // SelectableLabel allows the designer to copy text from the report.
            EditorGUILayout.SelectableLabel(reportText, reportStyle, GUILayout.ExpandHeight(true));
        }

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    /// <summary>Draws a 1px horizontal separator line using Unity's EditorGUI box.</summary>
    private static void DrawHorizontalSeparator()
    {
        Rect separatorRect = EditorGUILayout.GetControlRect(false, 1f);
        EditorGUI.DrawRect(separatorRect, new Color(0.5f, 0.5f, 0.5f, 0.4f));
    }

    // ─── Simulation ─────────────────────────────────────────────────────────────

    /// <summary>
    /// Entry point for the simulation. Wraps the full run in a try/catch so any exception
    /// is surfaced inline in the report panel rather than silently crashing the Editor.
    /// </summary>
    private void RunSimulation()
    {
        isSimulationRunning = true;

        try
        {
            SpecificityDatabase database = LoadSpecificityDatabase();

            if (database == null)
            {
                reportText  = "❌ ERROR: No SpecificityDatabase asset found in the project.\n" +
                              "Create one via Assets > Create > Game > Specificity Database.";
                lastReport  = null;
                return;
            }

            lastReport = SimulateSession(database, floorIndex, daysToSimulate, randomSeed);
            reportText  = BuildReportText(lastReport);
        }
        catch (Exception exception)
        {
            reportText = "❌ SIMULATION ERROR:\n" + exception.Message + "\n\n" + exception.StackTrace;
            lastReport = null;
        }
        finally
        {
            isSimulationRunning = false;
            Repaint();
        }
    }

    /// <summary>
    /// Loads the first SpecificityDatabase asset found in the project via AssetDatabase.
    /// Returns null when none exists — callers must null-check before use.
    /// AssetDatabase is used rather than Resources.Load because the database may be placed
    /// anywhere in the project and does not have to live in a Resources folder.
    /// </summary>
    private static SpecificityDatabase LoadSpecificityDatabase()
    {
        string[] guids = AssetDatabase.FindAssets("t:SpecificityDatabase");

        if (guids.Length == 0)
            return null;

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        return AssetDatabase.LoadAssetAtPath<SpecificityDatabase>(assetPath);
    }

    /// <summary>
    /// Runs the full simulation for the requested number of days on the given floor.
    /// Uses a seeded System.Random so results are deterministic and reproducible.
    /// Replicates DifficultyManager, FloorDifficultyProgression, and RuleGenerator logic
    /// inline — none of those classes can be instantiated without a MonoBehaviour context.
    /// </summary>
    private SimulationReport SimulateSession(
        SpecificityDatabase database,
        int targetFloorIndex,
        int totalDays,
        int seed)
    {
        System.Random rng = new System.Random(seed);

        // Build floor data using the inline replica of FloorDifficultyProgression.
        SimFloorData floorData = ComputeFloorData(targetFloorIndex);

        SimulationReport report = new SimulationReport
        {
            floorIndex = targetFloorIndex,
            seed       = seed,
            dayReports = new List<DayReport>()
        };

        // FIX 1: usedSpecificities cleared once for the floor — mirrors ResetForNewLevel().
        // NEVER cleared between days.
        List<string> usedSpecificities = new List<string>();

        // FIX 2: activeRules persists across ALL days. Never reset between days.
        List<RuleData> activeRules   = new List<RuleData>();
        int activeBinCount           = MinNumberOfBins;
        List<string> activeBinIDs   = GenerateBinIDs(activeBinCount);

        List<string> designMismatches = BuildDesignMismatchList();

        for (int dayIndex = 0; dayIndex < totalDays; dayIndex++)
        {
            try
            {
                DayReport dayReport = SimulateDay(
                    dayIndex,
                    targetFloorIndex,
                    floorData,
                    database,
                    usedSpecificities,
                    ref activeRules,
                    ref activeBinCount,
                    ref activeBinIDs,
                    rng);

                dayReport.designMismatches = dayIndex == 0 ? designMismatches : new List<string>();
                report.dayReports.Add(dayReport);
            }
            catch (Exception ex)
            {
                DayReport errorReport = new DayReport
                {
                    dayNumber          = dayIndex + 1,
                    difficultyData     = new DayDifficultyData(),
                    rulesByBin         = new Dictionary<string, List<RuleData>>(),
                    validCombinations  = new List<List<string>>(),
                    combinationCounts  = new Dictionary<string, int>(),
                    estimatedDocuments = 0,
                    difficultyChanges  = new List<string> { "❌ ERROR: " + ex.Message },
                    potentialIssues    = new List<string>(),
                    designMismatches   = new List<string>(),
                    floorData          = floorData
                };
                report.dayReports.Add(errorReport);
            }
        }

        return report;
    }

    /// <summary>
    /// Simulates a single day.
    /// FIX 1 (Day 1): Generates 1 primary rule per bin + complement. activeRules seeded fresh.
    /// FIX 2 (Day 2+): Picks ONE evolution via 3-way random. activeRules mutated in-place.
    /// FIX 3: Uses subset-based valid combination generation.
    /// FIX 4: Profitability simulated with real code values (start=60, gain=5, decay from floor).
    /// </summary>
    private DayReport SimulateDay(
        int dayIndex,
        int targetFloorIndex,
        SimFloorData floorData,
        SpecificityDatabase database,
        List<string> usedSpecificities,
        ref List<RuleData> activeRules,
        ref int activeBinCount,
        ref List<string> activeBinIDs,
        System.Random rng)
    {
        DayDifficultyData dayData        = ComputeDayDifficulty(dayIndex, targetFloorIndex);
        List<string> difficultyChanges  = new List<string>();

        if (dayIndex == 0)
        {
            // FIX 1: Day 1 initialisation from real code values.
            activeBinCount = MinNumberOfBins;           // 2 — DifficultyManager.minNumberOfBins
            activeBinIDs   = GenerateBinIDs(activeBinCount);

            activeRules = GenerateRulesForDay(
                rulesPerBin:       BaseRulesPerBin,       // 1 — real code
                complexityTarget:  BaseMaxRuleComplexity, // 1 — real code
                binIDs:            activeBinIDs,
                database:          database,
                usedSpecificities: usedSpecificities,
                rng:               rng);

            difficultyChanges.Add($"Day 1 initialised:");
            difficultyChanges.Add($"  activeBins: {activeBinCount}  [minNumberOfBins={MinNumberOfBins}]");
            difficultyChanges.Add($"  rulesPerBin: {BaseRulesPerBin}  [baseRulesPerBin={BaseRulesPerBin}]");
            difficultyChanges.Add($"  maxComplexity: {BaseMaxRuleComplexity}  [baseMaxRuleComplexity={BaseMaxRuleComplexity}]");
            difficultyChanges.Add($"  floorBase: fail={floorData.failThreshold:F1}%, " +
                                   $"duration={floorData.dayDuration:F0}s, decay={floorData.decayRatePerSecond:F2}/s");
        }
        else
        {
            // FIX 2: Day 2+ — pick ONE evolution via seeded 3-way random.
            // ⚠️ DESIGN MISMATCH: Real SelectDayEvolution() only has 2 paths. See ANALYZED VALUES.
            int evolutionRoll  = rng.Next(0, 3); // 0=Complexify, 1=AddRule, 2=AddBin
            string evolutionName;

            if (evolutionRoll == 0)
            {
                evolutionName = "ComplexifyRule";
                SimComplexifyExistingRule(activeRules, dayData.maxRuleComplexity,
                    database, usedSpecificities, rng);
            }
            else if (evolutionRoll == 1)
            {
                evolutionName = "AddNewRule";
                string targetBin = activeBinIDs[rng.Next(0, activeBinIDs.Count)];
                SimGenerateSingleRule(activeRules, targetBin, dayData.maxRuleComplexity,
                    activeBinIDs, database, usedSpecificities, rng);
            }
            else
            {
                if (activeBinCount < MaxBins)
                {
                    evolutionName = "AddBin";
                    activeBinCount++;
                    activeBinIDs = GenerateBinIDs(activeBinCount);
                    string newBinID = activeBinIDs[activeBinCount - 1];
                    SimGenerateSingleRule(activeRules, newBinID, dayData.maxRuleComplexity,
                        activeBinIDs, database, usedSpecificities, rng);
                }
                else
                {
                    evolutionName = "AddBin (max reached — fallback: AddNewRule)";
                    string targetBin = activeBinIDs[rng.Next(0, activeBinIDs.Count)];
                    SimGenerateSingleRule(activeRules, targetBin, dayData.maxRuleComplexity,
                        activeBinIDs, database, usedSpecificities, rng);
                }
            }

            difficultyChanges.Add($"Evolution: {evolutionName}");
            difficultyChanges.Add($"  activeBins: {activeBinCount} | totalRules: {activeRules.Count}");
        }

        Dictionary<string, List<RuleData>> rulesByBin = GroupRulesByBin(activeRules, activeBinIDs);

        // FIX 3: Subset-based valid combination generation.
        List<List<string>> validCombinations = BuildValidCombinationsForSimulation(activeRules, activeBinIDs);

        float dayDuration        = floorData.dayDuration;
        int   estimatedDocuments = Mathf.Max(1, Mathf.RoundToInt(dayDuration / DefaultSpawnIntervalSecs));

        // FIX 4: Profitability with real code values.
        ProfitabilityResult profitResult = SimulateProfitability(
            dayDuration,
            floorData.decayRatePerSecond,
            floorData.failThreshold,
            estimatedDocuments);

        List<string> potentialIssues = DetectPotentialIssues(activeRules, validCombinations, activeBinIDs);

        Dictionary<string, int> combinationCounts = SimulateDocumentDistribution(
            validCombinations, estimatedDocuments, rng);

        int maxComplexityReached = activeRules.Count > 0 ? activeRules.Max(r => r.complexity) : 1;

        return new DayReport
        {
            dayNumber            = dayIndex + 1,
            difficultyData       = dayData,
            rulesByBin           = rulesByBin,
            validCombinations    = validCombinations,
            combinationCounts    = combinationCounts,
            estimatedDocuments   = estimatedDocuments,
            difficultyChanges    = difficultyChanges,
            potentialIssues      = potentialIssues,
            designMismatches     = new List<string>(),
            floorData            = floorData,
            profitabilityResult  = profitResult,
            maxComplexityReached = maxComplexityReached,
            activeBinCount       = activeBinCount
        };
    }

    // ─── Inline DifficultyManager replica ───────────────────────────────────────

    /// <summary>
    /// Replicates DifficultyManager.ComputeDayData using the same formulas.
    /// Called without a MonoBehaviour instance because DifficultyManager requires one.
    /// All constants mirror the Inspector defaults defined in DifficultyManager.cs.
    /// </summary>
    private static DayDifficultyData ComputeDayDifficulty(int dayIndex, int floorIndex)
    {
        // Mirrors DifficultyManager.ComputeDayData formula exactly.
        int computedRulesPerBin = MinRulesPerBin + Mathf.FloorToInt(dayIndex * 0.5f);
        int clampedRulesPerBin  = Mathf.Clamp(computedRulesPerBin, MinRulesPerBin, MaxRulesPerBinCap);

        int computedComplexity = Mathf.RoundToInt(MinRuleComplexity + dayIndex * ComplexityIncreasePerDay);
        int clampedComplexity  = Mathf.Clamp(computedComplexity, 1, 5);

        int numberOfBins = ComputeNumberOfBins(dayIndex, floorIndex);

        return new DayDifficultyData
        {
            rulesPerBin       = clampedRulesPerBin,
            maxRuleComplexity = clampedComplexity,
            numberOfBins      = numberOfBins
        };
    }

    /// <summary>Replicates DifficultyManager.ComputeNumberOfBins with linear progression (no curve).</summary>
    private static int ComputeNumberOfBins(int dayIndex, int floorIndex)
    {
        int totalDayCount       = TotalFloors * DaysPerFloor;
        int absoluteDayPosition = floorIndex * DaysPerFloor + dayIndex;

        float progressionIndex = totalDayCount > 0
            ? (float)absoluteDayPosition / totalDayCount
            : 0f;

        // No AnimationCurve in editor simulation — falls back to linear, matching the default.
        float clampedIndex = Mathf.Clamp01(progressionIndex);

        return Mathf.Clamp(
            Mathf.RoundToInt(Mathf.Lerp(MinNumberOfBins, MaxNumberOfBins, clampedIndex)),
            MinNumberOfBins,
            MaxNumberOfBins);
    }

    // ─── Inline FloorDifficultyProgression replica ──────────────────────────────

    /// <summary>
    /// Replicates FloorDifficultyProgression.GetOrGenerateFloorData by walking the inheritance chain.
    /// Recursion terminates at floor 0 which always returns base values — mirrors the original exactly.
    /// </summary>
    private static SimFloorData ComputeFloorData(int targetFloorIndex)
    {
        if (targetFloorIndex <= 0)
            return BuildBaseFloorData();

        SimFloorData previousFloor = ComputeFloorData(targetFloorIndex - 1);
        return ComputeNextFloorData(previousFloor, targetFloorIndex);
    }

    /// <summary>Returns floor 0 base parameters — mirrors FloorDifficultyProgression.GenerateFloor1Data.</summary>
    private static SimFloorData BuildBaseFloorData()
    {
        return new SimFloorData
        {
            floorIndex         = 0,
            failThreshold      = BaseFailThreshold,
            dayDuration        = BaseDayDuration,
            decayRatePerSecond = BaseDecayRate,
            rulesPerBin        = BaseRulesPerBin,
            numberOfBins       = BaseNumberOfBins,
            maxRuleComplexity  = BaseMaxRuleComplexity
        };
    }

    /// <summary>Applies per-floor deltas — mirrors FloorDifficultyProgression.GenerateNextFloorData.</summary>
    private static SimFloorData ComputeNextFloorData(SimFloorData previous, int nextFloorIndex)
    {
        return new SimFloorData
        {
            floorIndex         = nextFloorIndex,
            failThreshold      = Mathf.Clamp(previous.failThreshold * (1f + FailThresholdDeltaPct), 0f, MaxFailThreshold),
            dayDuration        = Mathf.Clamp(previous.dayDuration  * (1f + DayDurationDeltaPct),   0f, MaxDayDuration),
            decayRatePerSecond = Mathf.Clamp(previous.decayRatePerSecond * (1f + DecayRateDeltaPct), 0f, MaxDecayRate),
            rulesPerBin        = Mathf.Clamp(previous.rulesPerBin + (nextFloorIndex % RulesPerBinDeltaEveryN == 0 ? 1 : 0), 1, MaxRulesPerBin),
            numberOfBins       = Mathf.Clamp(previous.numberOfBins + (nextFloorIndex % BinsMaxDeltaEveryN == 0 ? 1 : 0), 1, MaxBins),
            maxRuleComplexity  = Mathf.Clamp(previous.maxRuleComplexity + (nextFloorIndex % ComplexityDeltaEveryN == 0 ? 1 : 0), 1, MaxComplexity)
        };
    }

    // ─── Inline RuleGenerator replica ───────────────────────────────────────────

    /// <summary>
    /// Replicates RuleGenerator.GenerateRulesForDay using a seeded System.Random instead of
    /// UnityEngine.Random so rule generation is reproducible from the UI seed field.
    /// FIX 1: called only on Day 1 with rulesPerBin=1, complexityTarget=1.
    /// </summary>
    private static List<RuleData> GenerateRulesForDay(
        int rulesPerBin,
        int complexityTarget,
        List<string> binIDs,
        SpecificityDatabase database,
        List<string> usedSpecificities,
        System.Random rng)
    {
        List<RuleData> allRules = new List<RuleData>();
        int totalBinCount       = binIDs.Count;

        foreach (string binID in binIDs)
        {
            List<string> pool = BuildAvailablePool(database, usedSpecificities);

            for (int ruleIndex = 0; ruleIndex < rulesPerBin; ruleIndex++)
            {
                RuleType selectedType = SelectRuleType(complexityTarget, rng);
                RuleData primaryRule  = BuildRule(selectedType, binID, pool, totalBinCount, binIDs, usedSpecificities, rng);

                if (primaryRule == null)
                    break;

                allRules.Add(primaryRule);

                string complementBinID  = PickDifferentBinID(binID, binIDs, rng);
                RuleData complementRule = GenerateComplementRule(primaryRule, complementBinID);

                if (complementRule != null)
                {
                    allRules.Add(complementRule);
                    primaryRule.complementBinID = complementBinID;
                }
            }
        }

        return allRules;
    }

    /// <summary>
    /// FIX 2: Replicates RuleGenerator.ComplexifyExistingRule in-place on the shared activeRules list.
    /// Picks a random non-complement rule below newComplexityTarget and upgrades it.
    /// Syncs the paired complement rule after any change.
    /// </summary>
    private static void SimComplexifyExistingRule(
        List<RuleData> activeRules,
        int newComplexityTarget,
        SpecificityDatabase database,
        List<string> usedSpecificities,
        System.Random rng)
    {
        List<RuleData> candidates = activeRules
            .FindAll(rule => !rule.isComplement && rule.complexity < newComplexityTarget);

        if (candidates.Count == 0)
            return;

        RuleData targetRule = candidates[rng.Next(0, candidates.Count)];
        List<string> pool   = BuildAvailablePool(database, usedSpecificities);

        bool hasOneCondition = !string.IsNullOrEmpty(targetRule.conditionA) &&
                               string.IsNullOrEmpty(targetRule.conditionB);

        if (hasOneCondition && pool.Count > 0)
        {
            int idx = rng.Next(0, pool.Count);
            string secondCondition = pool[idx];
            pool.RemoveAt(idx);
            usedSpecificities.Add(secondCondition);

            targetRule.conditionB  = secondCondition;
            targetRule.complexity  = Mathf.Min(targetRule.complexity + 1, MaximumComplexity);
            targetRule.displayText = ResolveDisplayText(targetRule.ruleType, targetRule);
            SimSyncComplementRule(targetRule, activeRules);
            return;
        }

        RuleType? upgradedType = GetUpgradedRuleType(targetRule.ruleType);
        if (upgradedType == null)
            return;

        targetRule.ruleType    = upgradedType.Value;
        targetRule.complexity  = Mathf.Min(targetRule.complexity + 1, MaximumComplexity);
        targetRule.displayText = ResolveDisplayText(targetRule.ruleType, targetRule);
        SimSyncComplementRule(targetRule, activeRules);
    }

    /// <summary>
    /// FIX 2: Replicates RuleGenerator.GenerateSingleRule — generates one new primary rule
    /// for the given bin and appends it (plus complement) to activeRules.
    /// activeRules is mutated in-place. Never recreated.
    /// </summary>
    private static void SimGenerateSingleRule(
        List<RuleData> activeRules,
        string targetBinID,
        int complexityTarget,
        List<string> binIDs,
        SpecificityDatabase database,
        List<string> usedSpecificities,
        System.Random rng)
    {
        List<string> pool     = BuildAvailablePool(database, usedSpecificities);
        RuleType selectedType = SelectRuleType(complexityTarget, rng);
        RuleData newRule      = BuildRule(selectedType, targetBinID, pool, binIDs.Count, binIDs, usedSpecificities, rng);

        if (newRule == null)
            return;

        activeRules.Add(newRule);

        string complementBinID  = PickDifferentBinID(targetBinID, binIDs, rng);
        RuleData complementRule = GenerateComplementRule(newRule, complementBinID);

        if (complementRule != null)
        {
            activeRules.Add(complementRule);
            newRule.complementBinID = complementBinID;
        }
    }

    /// <summary>
    /// Syncs the complement rule's conditions and type to match the updated primary.
    /// Mirrors RuleGenerator.SyncComplementRule.
    /// </summary>
    private static void SimSyncComplementRule(RuleData primaryRule, List<RuleData> activeRules)
    {
        RuleData pairedComplement = activeRules.Find(rule =>
            rule.isComplement && rule.conditionA == primaryRule.conditionA);

        if (pairedComplement == null)
            return;

        RuleType newComplementType     = ResolveComplementType(primaryRule.ruleType);
        pairedComplement.ruleType      = newComplementType;
        pairedComplement.conditionB    = primaryRule.conditionB;
        pairedComplement.complexity    = primaryRule.complexity;
        pairedComplement.displayText   = ResolveDisplayText(newComplementType, pairedComplement);
    }

    private static RuleType? GetUpgradedRuleType(RuleType currentType)
    {
        return currentType switch
        {
            RuleType.PositiveExclusive => RuleType.PositiveForced,
            RuleType.PositiveForced    => RuleType.NegativeSimple,
            _                          => null
        };
    }

    private static List<string> BuildAvailablePool(SpecificityDatabase database, List<string> usedSpecificities)
    {
        return database.allSpecificities
            .Where(specificity => !usedSpecificities.Contains(specificity))
            .ToList();
    }

    /// <summary>Mirrors RuleGenerator.SelectRuleType using System.Random for determinism.</summary>
    private static RuleType SelectRuleType(int complexityTarget, System.Random rng)
    {
        if (complexityTarget <= 1)
            return RuleType.PositiveExclusive;

        if (complexityTarget == 2)
        {
            RuleType[] mediumTypes = { RuleType.PositiveForced, RuleType.NegativeSimple };
            return mediumTypes[rng.Next(0, mediumTypes.Length)];
        }

        RuleType[] hardTypes = { RuleType.ConditionalBranch, RuleType.PositiveDouble, RuleType.PositiveWithNegative };
        return hardTypes[rng.Next(0, hardTypes.Length)];
    }

    /// <summary>Mirrors RuleGenerator.BuildRule, consuming specificities from the shared pool.</summary>
    private static RuleData BuildRule(
        RuleType ruleType,
        string binID,
        List<string> pool,
        int totalBinCount,
        List<string> binIDs,
        List<string> usedSpecificities,
        System.Random rng)
    {
        RuleData rule = new RuleData { ruleType = ruleType, targetBinID = binID };

        switch (ruleType)
        {
            case RuleType.PositiveExclusive:
            case RuleType.PositiveForced:
            case RuleType.NegativeSimple:
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string singleCondition))
                    return null;
                rule.conditionA = singleCondition;
                break;

            case RuleType.ConditionalBranch:
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condBranchA)) return null;
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condBranchB)) return null;
                rule.conditionA     = condBranchA;
                rule.conditionB     = condBranchB;
                rule.secondaryBinID = PickDifferentBinID(binID, binIDs, rng);
                break;

            case RuleType.PositiveDouble:
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condDoubleA)) return null;
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condDoubleB)) return null;
                rule.conditionA     = condDoubleA;
                rule.conditionB     = condDoubleB;
                rule.secondaryBinID = PickDifferentBinID(binID, binIDs, rng);
                break;

            case RuleType.PositiveWithNegative:
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condWithA)) return null;
                if (!TryConsumeSpecificity(pool, usedSpecificities, rng, out string condWithB)) return null;
                rule.conditionA = condWithA;
                rule.conditionB = condWithB;
                break;
        }

        rule.displayText = ResolveDisplayText(ruleType, rule);
        rule.complexity  = ComputeComplexity(ruleType, totalBinCount);

        return rule;
    }

    private static bool TryConsumeSpecificity(
        List<string> pool,
        List<string> usedSpecificities,
        System.Random rng,
        out string result)
    {
        if (pool.Count == 0)
        {
            result = string.Empty;
            return false;
        }

        int randomIndex = rng.Next(0, pool.Count);
        result = pool[randomIndex];
        pool.RemoveAt(randomIndex);
        usedSpecificities.Add(result);
        return true;
    }

    private static string PickDifferentBinID(string primaryBinID, List<string> binIDs, System.Random rng)
    {
        List<string> alternates = binIDs.Where(id => id != primaryBinID).ToList();

        if (alternates.Count == 0)
            return primaryBinID;

        return alternates[rng.Next(0, alternates.Count)];
    }

    /// <summary>
    /// Mirrors RuleGenerator.GenerateComplementRule, mapping primary types to complement types.
    /// Returns null for self-complementary types (ConditionalBranch, PositiveDouble).
    /// </summary>
    private static RuleData GenerateComplementRule(RuleData primaryRule, string complementBinID)
    {
        if (primaryRule.isComplement)
            return null;

        // Self-complementary types already cover both routing outcomes — no complement needed.
        if (primaryRule.ruleType == RuleType.ConditionalBranch ||
            primaryRule.ruleType == RuleType.PositiveDouble)
            return null;

        RuleType complementType = ResolveComplementType(primaryRule.ruleType);

        // No mapping exists for this type — skip silently to match RuleGenerator behaviour.
        if (complementType == primaryRule.ruleType)
            return null;

        RuleData complementRule = new RuleData
        {
            isComplement  = true,
            ruleType      = complementType,
            targetBinID   = complementBinID,
            conditionA    = primaryRule.conditionA,
            conditionB    = primaryRule.conditionB,
            complexity    = primaryRule.complexity
        };

        complementRule.displayText = ResolveDisplayText(complementType, complementRule);

        return complementRule;
    }

    private static RuleType ResolveComplementType(RuleType primaryType)
    {
        return primaryType switch
        {
            RuleType.PositiveForced       => RuleType.ComplementPositiveForced,
            RuleType.PositiveExclusive    => RuleType.ComplementPositiveExclusive,
            RuleType.NegativeSimple       => RuleType.ComplementNegativeSimple,
            RuleType.PositiveWithNegative => RuleType.ComplementPositiveWithNegative,
            _                             => primaryType
        };
    }

    private static int ComputeComplexity(RuleType ruleType, int binCount)
    {
        int baseScore = ruleType switch
        {
            RuleType.PositiveExclusive              => 1,
            RuleType.PositiveForced                 => 2,
            RuleType.NegativeSimple                 => 2,
            RuleType.ConditionalBranch              => 3,
            RuleType.PositiveDouble                 => 3,
            RuleType.PositiveWithNegative           => 3,
            RuleType.ComplementPositiveForced       => 2,
            RuleType.ComplementPositiveExclusive    => 1,
            RuleType.ComplementNegativeSimple       => 2,
            RuleType.ComplementPositiveWithNegative => 3,
            _                                       => 1
        };

        int binPenalty = Mathf.Max(0, binCount - 1);
        return Mathf.Clamp(baseScore + binPenalty, MinimumComplexity, MaximumComplexity);
    }

    /// <summary>Produces a readable rule sentence using the built-in fallback templates from RuleGenerator.</summary>
    private static string ResolveDisplayText(RuleType ruleType, RuleData rule)
    {
        string template = ruleType switch
        {
            RuleType.PositiveExclusive              => "If the document has only {0}, place it here",
            RuleType.PositiveForced                 => "If the document has {0}, it must go here",
            RuleType.NegativeSimple                 => "If the document does not have {0}, place it here",
            RuleType.ConditionalBranch              => "If {0} is present: check for {1} — yes: here, no: other bin",
            RuleType.PositiveDouble                 => "If {0} and {1} are present go here, otherwise other bin",
            RuleType.PositiveWithNegative           => "If {0} is present but {1} is not, place it here",
            RuleType.ComplementPositiveForced       => "If the document does NOT have {0}, place it here",
            RuleType.ComplementPositiveExclusive    => "If {0} is present with other specificities, place here",
            RuleType.ComplementNegativeSimple       => "If the document has {0}, place it here",
            RuleType.ComplementPositiveWithNegative => "If {0} and {1} are both present, place it here",
            _                                       => "Send documents here"
        };

        return template
            .Replace("{0}", rule.conditionA)
            .Replace("{1}", rule.conditionB);
    }

    // ─── FIX 3: Valid combination generation (subset-based) ─────────────────────

    /// <summary>
    /// FIX 3: Generates valid document combinations via explicit subset enumeration.
    ///   1. Collect all conditionA and conditionB strings from activeRules.
    ///   2. Build all subsets of size 1–3.
    ///   3. For each subset, count distinct bins that accept it (ValidateExactCombination).
    ///   4. Keep only subsets accepted by exactly 1 bin.
    ///   5. Discard if 0 bins (unplaceable) or 2+ bins (ambiguous).
    /// </summary>
    private static List<List<string>> BuildValidCombinationsForSimulation(
        List<RuleData> allRules,
        List<string> activeBinIDs)
    {
        // Step 1: Collect all specificities referenced in active rules.
        HashSet<string> specificitySet = new HashSet<string>();
        foreach (RuleData rule in allRules)
        {
            if (!string.IsNullOrEmpty(rule.conditionA)) specificitySet.Add(rule.conditionA);
            if (!string.IsNullOrEmpty(rule.conditionB)) specificitySet.Add(rule.conditionB);
        }

        List<string> allSpecificities = specificitySet.ToList();
        int n = allSpecificities.Count;

        // Step 2: Build all subsets of size 1–3.
        List<List<string>> allSubsets = new List<List<string>>();

        for (int i = 0; i < n; i++)
            allSubsets.Add(new List<string> { allSpecificities[i] });

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                allSubsets.Add(new List<string> { allSpecificities[i], allSpecificities[j] });

        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                for (int k = j + 1; k < n; k++)
                    allSubsets.Add(new List<string> { allSpecificities[i], allSpecificities[j], allSpecificities[k] });

        List<List<string>> validCombinations = new List<List<string>>();

        // Steps 3–5: Keep only subsets accepted by exactly one bin.
        foreach (List<string> subset in allSubsets)
        {
            string acceptingBin   = null;
            int    acceptingCount = 0;

            foreach (RuleData rule in allRules)
            {
                if (!ValidateExactCombination(rule, subset))
                    continue;

                if (acceptingBin == null)
                {
                    acceptingBin   = rule.targetBinID;
                    acceptingCount = 1;
                }
                else if (rule.targetBinID != acceptingBin)
                {
                    acceptingCount++;
                    break; // Ambiguous — stop early.
                }
            }

            if (acceptingCount == 1)
                validCombinations.Add(subset);
        }

        return validCombinations;
    }

    private static bool ValidateExactCombination(RuleData rule, List<string> specificities)
    {
        switch (rule.ruleType)
        {
            case RuleType.PositiveForced:
                return specificities.Contains(rule.conditionA);

            case RuleType.PositiveExclusive:
                return specificities.Contains(rule.conditionA) && specificities.Count == 1;

            case RuleType.ConditionalBranch:
            case RuleType.PositiveDouble:
                bool isYesBranch = rule.targetBinID != rule.secondaryBinID;
                if (isYesBranch)
                    return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);
                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.NegativeSimple:
                return !specificities.Contains(rule.conditionA);

            case RuleType.PositiveWithNegative:
                return specificities.Contains(rule.conditionA) && !specificities.Contains(rule.conditionB);

            case RuleType.ComplementPositiveForced:
                return !specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveExclusive:
                return specificities.Contains(rule.conditionA) && specificities.Count > 1;

            case RuleType.ComplementNegativeSimple:
                return specificities.Contains(rule.conditionA);

            case RuleType.ComplementPositiveWithNegative:
                return specificities.Contains(rule.conditionA) && specificities.Contains(rule.conditionB);

            default:
                return false;
        }
    }

    // ─── FIX 4: Profitability simulation ────────────────────────────────────────

    /// <summary>
    /// FIX 4: Simulates profitability using real code values from ProfitabilityManager.
    /// Start: 60f (ProfitabilityManager.initialProfitability — NOT 50% as in design doc).
    /// Decay: totalDecay = decayRate * dayDuration (framerate-independent, matches Update()).
    /// Gains: documentCount * gainPerDocument (bot always correct — no wrong-bin penalty).
    /// Tracks minimum profitability to detect threshold crossing at any point in the day.
    /// </summary>
    private static ProfitabilityResult SimulateProfitability(
        float dayDuration,
        float decayRate,
        float failThreshold,
        int documentCount)
    {
        float startProfitability = InitialProfitability; // 60f from real code
        float spawnInterval      = dayDuration / Mathf.Max(1, documentCount);
        float profitability      = startProfitability;
        float minProfitability   = profitability;
        float elapsed            = 0f;
        bool  failed             = false;
        int   docsSpawned        = 0;

        // 1-second simulation steps — 30 days × 60s = 1800 iterations maximum, under 1ms.
        const float stepDelta = 1f;

        while (elapsed < dayDuration && !failed)
        {
            float step = Mathf.Min(stepDelta, dayDuration - elapsed);
            profitability -= decayRate * step;
            profitability  = Mathf.Clamp(profitability, 0f, 100f);
            elapsed       += step;

            // Award gain for each document that should have been sorted by now.
            while (docsSpawned < documentCount)
            {
                float docTime = (docsSpawned + 1) * spawnInterval;
                if (docTime > elapsed) break;
                profitability += GainPerDocument;
                profitability  = Mathf.Clamp(profitability, 0f, 100f);
                docsSpawned++;
            }

            if (profitability < minProfitability)
                minProfitability = profitability;

            if (profitability < failThreshold)
                failed = true;
        }

        return new ProfitabilityResult
        {
            startProfitability = startProfitability,
            endProfitability   = Mathf.Clamp(profitability, 0f, 100f),
            minProfitability   = minProfitability,
            totalDecay         = decayRate * dayDuration,
            totalGain          = docsSpawned * GainPerDocument,
            documentCount      = docsSpawned,
            gainPerDocument    = GainPerDocument,
            failThreshold      = failThreshold,
            isVictory          = !failed
        };
    }

    // ─── Document distribution simulation ───────────────────────────────────────

    /// <summary>
    /// Distributes estimated documents across valid combinations using uniform random picks.
    /// Returns a dictionary mapping combination key → count.
    /// </summary>
    private static Dictionary<string, int> SimulateDocumentDistribution(
        List<List<string>> validCombinations,
        int totalDocuments,
        System.Random rng)
    {
        Dictionary<string, int> counts = new Dictionary<string, int>();

        if (validCombinations.Count == 0)
            return counts;

        for (int i = 0; i < totalDocuments; i++)
        {
            List<string> picked = validCombinations[rng.Next(0, validCombinations.Count)];
            string key          = ComboKey(picked);

            if (!counts.ContainsKey(key))
                counts[key] = 0;

            counts[key]++;
        }

        return counts;
    }

    // ─── Helpers ────────────────────────────────────────────────────────────────

    private static List<string> GenerateBinIDs(int count)
    {
        List<string> ids = new List<string>();
        for (int i = 0; i < count; i++)
            ids.Add("bin_" + (i + 1));
        return ids;
    }

    private static Dictionary<string, List<RuleData>> GroupRulesByBin(
        List<RuleData> allRules,
        List<string> binIDs)
    {
        Dictionary<string, List<RuleData>> result = new Dictionary<string, List<RuleData>>();
        foreach (string binID in binIDs)
            result[binID] = new List<RuleData>();
        foreach (RuleData rule in allRules)
            if (result.ContainsKey(rule.targetBinID))
                result[rule.targetBinID].Add(rule);
        return result;
    }

    private static string ComboKey(List<string> combo)
    {
        List<string> sorted = new List<string>(combo);
        sorted.Sort();
        return string.Join(", ", sorted);
    }

    private static string TruncatePad(string s, int maxLen)
    {
        if (s.Length > maxLen) s = s.Substring(0, maxLen - 3) + "...";
        return s.PadRight(maxLen);
    }

    /// <summary>
    /// Runs static analysis on the generated rules and spawn pool to identify potential gameplay problems.
    /// Reports: conflicts between bins, sparse spawn pools, missing complements.
    /// </summary>
    private static List<string> DetectPotentialIssues(
        List<RuleData> allRules,
        List<List<string>> validCombinations,
        List<string> binIDs)
    {
        List<string> issues = new List<string>();

        // Warn when the spawn pool is critically small — the player will see the same
        // document type on almost every spawn, making the day feel repetitive.
        if (validCombinations.Count <= 1)
            issues.Add("Only 1 valid document combination available — risk of repetition");

        // Check every primary rule for a missing complement.
        foreach (RuleData rule in allRules)
        {
            if (rule.isComplement)
                continue;

            if (rule.ruleType == RuleType.ConditionalBranch || rule.ruleType == RuleType.PositiveDouble)
                continue; // Self-complementary — no separate complement expected.

            bool hasComplement = allRules.Exists(candidate =>
                candidate.isComplement &&
                candidate.conditionA == rule.conditionA &&
                candidate.targetBinID != rule.targetBinID);

            if (!hasComplement)
                issues.Add($"No complement rule found for {rule.ruleType} in {rule.targetBinID} (conditionA: {rule.conditionA})");
        }

        // Detect cross-bin conflicts: two rules on different bins both accepting the same combination.
        foreach (List<string> combo in validCombinations)
        {
            List<string> acceptingBins = new List<string>();

            foreach (RuleData rule in allRules)
            {
                if (ValidateExactCombination(rule, combo) && !acceptingBins.Contains(rule.targetBinID))
                    acceptingBins.Add(rule.targetBinID);
            }

            if (acceptingBins.Count > 1)
                issues.Add($"Rule conflict: [{string.Join(", ", combo)}] accepted by {string.Join(" AND ", acceptingBins)}");
        }

        // Warn when a bin has no primary rules — it will display nothing to the player.
        foreach (string binID in binIDs)
        {
            bool hasPrimaryRule = allRules.Exists(rule => rule.targetBinID == binID && !rule.isComplement);

            if (!hasPrimaryRule)
                issues.Add($"Bin {binID} has no primary rules — it will appear empty");
        }

        return issues;
    }

    // ─── Report text builder ─────────────────────────────────────────────────────

    /// <summary>
    /// Converts a SimulationReport into a formatted string displayed in the report panel.
    /// STEP 4: Begins with the summary box, then appends per-day reports.
    /// </summary>
    private static string BuildReportText(SimulationReport report)
    {
        StringBuilder builder = new StringBuilder();
        AppendSummary(builder, report);
        builder.AppendLine();

        foreach (DayReport day in report.dayReports)
        {
            AppendDayReport(builder, day, report.floorIndex);
            builder.AppendLine();
        }

        return builder.ToString();
    }

    /// <summary>STEP 4: Renders the summary box at the top of the full report.</summary>
    private static void AppendSummary(StringBuilder builder, SimulationReport report)
    {
        int totalDays      = report.dayReports.Count;
        int victories      = report.dayReports.Count(d => d.profitabilityResult != null && d.profitabilityResult.isVictory);
        int defeats        = totalDays - victories;
        int totalDocuments = report.dayReports.Sum(d => d.estimatedDocuments);
        int maxBinsReached = report.dayReports.Count > 0 ? report.dayReports.Max(d => d.activeBinCount) : 0;
        int maxComplexity  = report.dayReports.Count > 0 ? report.dayReports.Max(d => d.maxComplexityReached) : 0;

        HashSet<string> ruleTypesUsed = new HashSet<string>();
        foreach (DayReport day in report.dayReports)
            foreach (KeyValuePair<string, List<RuleData>> pair in day.rulesByBin)
                foreach (RuleData rule in pair.Value)
                    ruleTypesUsed.Add(rule.ruleType.ToString());

        List<string> criticalIssues = new List<string>();
        foreach (DayReport day in report.dayReports)
        {
            foreach (string issue in day.potentialIssues)
                if (issue.StartsWith("Rule conflict") || issue.StartsWith("No complement") || issue.StartsWith("Bin "))
                    criticalIssues.Add($"Day {day.dayNumber}: {issue}");

            if (day.designMismatches != null)
                foreach (string mismatch in day.designMismatches)
                    criticalIssues.Add($"⚠️ DESIGN MISMATCH: {mismatch}");
        }

        string ruleTypeList = string.Join(", ", ruleTypesUsed.Take(4));
        if (ruleTypesUsed.Count > 4) ruleTypeList += $" (+{ruleTypesUsed.Count - 4} more)";

        builder.AppendLine("╔═══════════════════════════════════════════════╗");
        builder.AppendLine("║           SIMULATION SUMMARY                  ║");
        builder.AppendLine("╠═══════════════════════════════════════════════╣");
        builder.AppendLine($"║ Floor: {report.floorIndex,-3} | Days: {totalDays,-3} | Seed: {report.seed,-13}║");
        builder.AppendLine($"║ Victories: {victories}/{totalDays,-2} | Defeats: {defeats}/{totalDays,-22}║");
        builder.AppendLine($"║ Total documents processed: {totalDocuments,-20}║");
        builder.AppendLine($"║ Rule types: {TruncatePad(ruleTypeList, 35)}║");
        builder.AppendLine($"║ Max bins reached: {maxBinsReached,-29}║");
        builder.AppendLine($"║ Max complexity reached: {maxComplexity,-23}║");
        builder.AppendLine("╠═══════════════════════════════════════════════╣");
        builder.AppendLine($"║ ⚠️  CRITICAL ISSUES: {criticalIssues.Count,-26}║");

        if (criticalIssues.Count == 0)
        {
            builder.AppendLine("║ No critical issues detected                   ║");
        }
        else
        {
            foreach (string issue in criticalIssues.Take(5))
                builder.AppendLine("║ • " + TruncatePad(issue, 44) + "║");

            if (criticalIssues.Count > 5)
                builder.AppendLine($"║ ... and {criticalIssues.Count - 5} more (see per-day reports)         ║");
        }

        builder.AppendLine("╚═══════════════════════════════════════════════╝");
    }

    private static void AppendDayReport(StringBuilder builder, DayReport day, int floorIndex)
    {
        builder.AppendLine("═══════════════════════════════════════════════");
        builder.AppendLine($"DAY {day.dayNumber} — Floor {floorIndex}");
        builder.AppendLine("═══════════════════════════════════════════════");
        builder.AppendLine();

        // ── Design mismatches (Day 1 only) ────────────────────────────────────
        if (day.designMismatches != null && day.designMismatches.Count > 0)
        {
            builder.AppendLine("⚠️ DESIGN MISMATCHES:");
            foreach (string mismatch in day.designMismatches)
                builder.AppendLine("  ⚠️ DESIGN MISMATCH: " + mismatch);
            builder.AppendLine();
        }

        // ── Difficulty changes ─────────────────────────────────────────────────
        builder.AppendLine("📦 DIFFICULTY CHANGES:");

        if (day.difficultyChanges == null || day.difficultyChanges.Count == 0)
            builder.AppendLine("  No difficulty change this day");
        else
            foreach (string change in day.difficultyChanges)
                builder.AppendLine("  • " + change);

        builder.AppendLine();

        // ── Bins & rules ───────────────────────────────────────────────────────
        builder.AppendLine("🗂️ BINS & RULES:");
        int binNumber = 1;

        foreach (KeyValuePair<string, List<RuleData>> pair in day.rulesByBin)
        {
            builder.AppendLine($"  Bin {binNumber} [{pair.Key}]:");

            if (pair.Value.Count == 0)
            {
                builder.AppendLine("    (no rules assigned)");
            }
            else
            {
                foreach (RuleData rule in pair.Value)
                {
                    string complementTag = rule.isComplement ? " [complement]" : string.Empty;
                    builder.AppendLine($"    - {rule.displayText}");
                    builder.AppendLine($"      (Type: {rule.ruleType}{complementTag}, Complexity: {rule.complexity})");
                }
            }

            binNumber++;
        }

        builder.AppendLine();

        // ── Documents spawned ──────────────────────────────────────────────────
        builder.AppendLine("📄 DOCUMENTS SPAWNED:");
        builder.AppendLine($"  Total estimated: {day.estimatedDocuments}");

        if (day.validCombinations.Count == 0)
        {
            builder.AppendLine("  ⚠️ No valid combinations — no documents can be spawned");
        }
        else
        {
            builder.AppendLine("  Breakdown by combination:");
            List<KeyValuePair<string, int>> sortedCounts = day.combinationCounts
                .OrderByDescending(pair => pair.Value)
                .ToList();

            foreach (KeyValuePair<string, int> entry in sortedCounts)
                builder.AppendLine($"    - [{entry.Key}] × {entry.Value}");

            if (sortedCounts.Count > 0)
                builder.AppendLine($"  Most frequent: [{sortedCounts[0].Key}]");

            builder.AppendLine($"  Unique combinations: {day.validCombinations.Count}");
        }

        builder.AppendLine();

        // ── Profitability (FIX 4) ─────────────────────────────────────────────
        if (day.profitabilityResult != null)
        {
            ProfitabilityResult p = day.profitabilityResult;
            builder.AppendLine("📊 PROFITABILITY:");
            builder.AppendLine($"  Start: {p.startProfitability:F1}% → End: {p.endProfitability:F1}%");
            builder.AppendLine($"  Decay: -{p.totalDecay:F1}% over {day.floorData.dayDuration:F0}s  " +
                                $"(rate: {day.floorData.decayRatePerSecond:F2}/s)");
            builder.AppendLine($"  Gains: +{p.totalGain:F1}%  " +
                                $"({p.documentCount} docs × {p.gainPerDocument:F1}% each)");
            builder.AppendLine($"  Fail threshold: {p.failThreshold:F1}%");
            builder.AppendLine($"  Min profitability: {p.minProfitability:F1}%");
            builder.AppendLine($"  Outcome: {(p.isVictory ? "Victory ✅" : "Defeat ❌")}");
            builder.AppendLine();
        }

        // ── Potential issues ───────────────────────────────────────────────────
        builder.AppendLine("⚠️ POTENTIAL ISSUES:");

        if (day.potentialIssues == null || day.potentialIssues.Count == 0)
            builder.AppendLine("  ✅ No issues detected");
        else
            foreach (string issue in day.potentialIssues)
                builder.AppendLine("  • " + issue);
    }

    // ─── Export ──────────────────────────────────────────────────────────────────

    /// <summary>
    /// Writes the current report text to a timestamped .txt file under Assets/PlaytestReports/.
    /// Creates the directory if it does not exist — fails gracefully with an EditorUtility dialog.
    /// </summary>
    private void ExportReport()
    {
        if (lastReport == null || string.IsNullOrEmpty(reportText))
            return;

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        string directory = Path.Combine(Application.dataPath, "PlaytestReports");
        string fileName  = $"playtest_floor{lastReport.floorIndex}_{timestamp}.txt";
        string fullPath  = Path.Combine(directory, fileName);

        try
        {
            if (!Directory.Exists(directory))
                Directory.CreateDirectory(directory);

            File.WriteAllText(fullPath, reportText, Encoding.UTF8);
            AssetDatabase.Refresh();
            EditorUtility.DisplayDialog(
                "Export Successful",
                "Report saved to:\nAssets/PlaytestReports/" + fileName,
                "OK");
        }
        catch (Exception exception)
        {
            EditorUtility.DisplayDialog(
                "Export Failed",
                "Could not write report file:\n" + exception.Message,
                "OK");
        }
    }

    // ─── Data structures ─────────────────────────────────────────────────────────

    /// <summary>Floor parameters computed once per simulation run — not persisted to disk.</summary>
    private class SimFloorData
    {
        public int   floorIndex;
        public float failThreshold;
        public float dayDuration;
        public float decayRatePerSecond;
        public int   rulesPerBin;
        public int   numberOfBins;
        public int   maxRuleComplexity;
    }

    /// <summary>Full simulation result containing one DayReport per day.</summary>
    private class SimulationReport
    {
        public int             floorIndex;
        public int             seed;
        public List<DayReport> dayReports;
    }

    /// <summary>Profitability outcome for a single simulated day (FIX 4).</summary>
    private class ProfitabilityResult
    {
        public float startProfitability;
        public float endProfitability;
        public float minProfitability;
        public float totalDecay;
        public float totalGain;
        public int   documentCount;
        public float gainPerDocument;
        public float failThreshold;
        public bool  isVictory;
    }

    /// <summary>All data produced for a single simulated day — consumed only by the report renderer.</summary>
    private class DayReport
    {
        public int                                dayNumber;
        public DayDifficultyData                  difficultyData;
        public Dictionary<string, List<RuleData>> rulesByBin;
        public List<List<string>>                 validCombinations;
        public Dictionary<string, int>            combinationCounts;
        public int                                estimatedDocuments;
        public List<string>                       difficultyChanges;
        public List<string>                       potentialIssues;
        public List<string>                       designMismatches;
        public SimFloorData                       floorData;
        public ProfitabilityResult                profitabilityResult;
        public int                                maxComplexityReached;
        public int                                activeBinCount;
    }
}
