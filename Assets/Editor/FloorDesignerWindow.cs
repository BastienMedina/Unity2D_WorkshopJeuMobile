using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Unity EditorWindow that exposes a two-panel floor design tool.
/// Left panel: scrollable tower overview with floor list and add controls.
/// Right panel: full floor editor with parameters, rules, save actions, and comparison.
/// Does NOT run at runtime — Editor only.
/// Placing this file outside an Editor folder causes Android build failures
/// because UnityEditor.dll is not available in player builds.
/// </summary>
public class FloorDesignerWindow : EditorWindow
{
    // ─── Constants ────────────────────────────────────────────────────────────

    private const float LeftPanelWidthRatio  = 0.30f;
    private const float FloorEntryHeight     = 40f;
    private const int   MaxLoadedFloorIndex  = 99;

    private const float FailThresholdMin   = 0f;
    private const float FailThresholdMax   = 80f;
    private const float DayDurationMin     = 60f;
    private const float DayDurationMax     = 300f;
    private const float DecayRateMin       = 0.5f;
    private const float DecayRateMax       = 8f;

    private const int NumberOfBinsMin      = 1;
    private const int NumberOfBinsMax      = 5;
    private const int RulesPerBinMin       = 1;
    private const int RulesPerBinMax       = 4;
    private const int MaxRuleComplexityMin = 1;
    private const int MaxRuleComplexityMax = 5;

    private const float DeltaMin          = 0f;
    private const float DeltaMax          = 0.5f;

    // Default base values used when creating the very first floor (index 0).
    private const float BaseFailThreshold      = 50f;
    private const float BaseDayDuration        = 120f;
    private const float BaseDecayRatePerSecond = 1f;
    private const int   BaseNumberOfBins       = 2;
    private const int   BaseRulesPerBin        = 2;
    private const int   BaseMaxRuleComplexity  = 2;

    // ─── State ────────────────────────────────────────────────────────────────

    /// <summary>All floors currently loaded in the tool during this editor session.</summary>
    private List<FloorDesignerData> loadedFloors = new List<FloorDesignerData>();

    /// <summary>Index into loadedFloors identifying the floor currently being edited.</summary>
    private int selectedFloorIndex = -1;

    /// <summary>Scroll position for the left tower panel floor list.</summary>
    private Vector2 towerScrollPos;

    /// <summary>Scroll position for the right floor editor panel.</summary>
    private Vector2 editScrollPos;

    // ─── Delta settings — editable per-session so the designer can tune the
    //     progression curve before generating the entire tower. ────────────────

    [SerializeField] private float failThresholdDeltaPct = 0.08f;
    [SerializeField] private float dayDurationDeltaPct   = 0.10f;
    [SerializeField] private float decayRateDeltaPct     = 0.12f;

    // Cached array of all RuleType enum names for dropdown rendering.
    private string[] ruleTypeNames;

    // ─── Cached GUIStyles — created once per OnEnable to avoid per-frame alloc ─

    private GUIStyle sectionLabelStyle;
    private bool isStylesInitialized;

    /// <summary>
    /// Tracks whether the Night Progression comparison table is expanded.
    /// Default false keeps the comparison section compact until the designer needs it.
    /// </summary>
    private bool showNightComparison;

    // ─── Menu Entry ───────────────────────────────────────────────────────────

    /// <summary>
    /// Opens the Floor Designer window from the Unity top menu.
    /// MenuItem tag: accessible from Tools → Floor Designer.
    /// </summary>
    [MenuItem("Tools/Floor Designer")]
    public static void OpenWindow()
    {
        FloorDesignerWindow window = GetWindow<FloorDesignerWindow>("Floor Designer");
        window.minSize = new Vector2(800f, 500f);
        window.Show();
    }

    // ─── Lifecycle ────────────────────────────────────────────────────────────

    /// <summary>
    /// Called when the window is opened or the Editor reloads scripts.
    /// Loads all floors from disk so the tool always reflects current save state
    /// and the designer never starts with stale data from a previous session.
    /// </summary>
    private void OnEnable()
    {
        ruleTypeNames     = Enum.GetNames(typeof(RuleType));
        isStylesInitialized = false;
        LoadAllFloors();
    }

    /// <summary>
    /// Called when the window is closed or the Editor reloads scripts.
    /// If any floor has unsaved changes, prompts the designer before discarding work,
    /// preventing accidental loss of an entire session of edits.
    /// </summary>
    private void OnDisable()
    {
        if (!HasAnyDirtyFloor())
            return;

        int choice = EditorUtility.DisplayDialogComplex(
            "Unsaved Changes",
            "You have unsaved changes. Save before closing?",
            "Save All",
            "Discard",
            "Cancel");

        // choice 0 = Save All, 1 = Discard, 2 = Cancel
        if (choice == 0)
            SaveAllFloors();
        // Discard and Cancel both let the window close — Cancel cannot prevent OnDisable.
    }

    // ─── Main Draw ────────────────────────────────────────────────────────────

    private void OnGUI()
    {
        EnsureStylesInitialized();

        float totalWidth  = position.width;
        float leftWidth   = totalWidth * LeftPanelWidthRatio;
        float rightWidth  = totalWidth - leftWidth;

        EditorGUILayout.BeginHorizontal();
        DrawLeftPanel(leftWidth);
        DrawRightPanel(rightWidth);
        EditorGUILayout.EndHorizontal();
    }

    // ─── Left Panel ───────────────────────────────────────────────────────────

    /// <summary>Draws the tower overview panel with floor list and toolbar.</summary>
    private void DrawLeftPanel(float width)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(width));

        EditorGUILayout.LabelField("Floor Tower", sectionLabelStyle);
        EditorGUILayout.Space(4f);

        DrawTowerToolbar();

        EditorGUILayout.Space(4f);

        towerScrollPos = EditorGUILayout.BeginScrollView(towerScrollPos);
        DrawFloorList();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Renders the three-row toolbar in the left panel.
    /// Row 1: [Load All] — full width.
    /// Row 2: [+ Add New Floor] and [Inherit From Below] side-by-side.
    /// Row 3: [🗑 Destroy All Floors] — red background, full width, destructive action.
    /// </summary>
    private void DrawTowerToolbar()
    {
        // Row 1 — Load All
        if (GUILayout.Button("Load All"))
            LoadAllFloors();

        // Row 2 — Add New Floor + Inherit From Below
        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("+ Add New Floor"))
            AddNewFloor();

        bool canInherit = HasValidSelection() && loadedFloors[selectedFloorIndex].floorIndex > 0;
        using (new EditorGUI.DisabledScope(!canInherit))
        {
            if (GUILayout.Button("Inherit From Below"))
                InheritFromPreviousFloor();
        }

        EditorGUILayout.EndHorizontal();

        // Row 3 — Destroy All Floors (red to signal destructive action)
        // Color reset immediately after so subsequent UI elements are unaffected.
        GUI.backgroundColor = Color.red;
        if (GUILayout.Button("🗑 Destroy All Floors"))
            DestroyAllFloors();
        GUI.backgroundColor = Color.white;
    }

    /// <summary>
    /// Renders one styled button per loaded floor using runtime-built GUIStyles.
    /// Button color encodes the floor state at a glance so the designer never has
    /// to open a floor to see whether it is dirty, selected, completed, or active.
    /// </summary>
    private void DrawFloorList()
    {
        for (int i = 0; i < loadedFloors.Count; i++)
        {
            FloorDesignerData floor = loadedFloors[i];

            GUIStyle buttonStyle = new GUIStyle(GUI.skin.button)
            {
                fixedHeight = 50f,
                fontSize    = 16,
                fontStyle   = FontStyle.Bold,
                alignment   = TextAnchor.MiddleCenter
            };

            // Color priority: dirty+selected > dirty > selected > completed > active.
            // Each state has a distinct color so the designer reads floor state without
            // opening it — this avoids the need for per-floor status labels.
            if (floor.isDirty && i == selectedFloorIndex)
                // Bright yellow: selected AND has unsaved changes — highest urgency.
                GUI.backgroundColor = new Color(1f, 0.85f, 0f);
            else if (floor.isDirty)
                // Dark yellow: unsaved but not currently selected.
                GUI.backgroundColor = new Color(0.9f, 0.7f, 0f);
            else if (i == selectedFloorIndex)
                // Blue: currently selected and saved.
                GUI.backgroundColor = new Color(0.3f, 0.6f, 1f);
            else if (floor.isCompleted)
                // Dark green: floor was completed by the player.
                GUI.backgroundColor = new Color(0.2f, 0.6f, 0.2f);
            else
                // Light green: floor is active / in progress.
                GUI.backgroundColor = new Color(0.4f, 0.75f, 0.4f);

            // Asterisk in the label signals unsaved changes at a glance without
            // requiring the designer to open the floor to notice the dirty state.
            string label = floor.displayName + (floor.isDirty ? "  *" : string.Empty);

            if (GUILayout.Button(label, buttonStyle, GUILayout.ExpandWidth(true)))
                // Clicking a button selects that floor for editing in the right panel.
                selectedFloorIndex = i;

            // Always reset background color after each colored button so subsequent
            // Unity EditorGUI elements are not tinted by the last color set here.
            // Unity EditorGUI shares GUI.backgroundColor state globally across the frame.
            GUI.backgroundColor = Color.white;
        }
    }

    /// <summary>
    /// Renders the [+ Add Next Floor] button.
    /// Always generates from the previous floor to maintain the difficulty inheritance chain.
    /// If no floors exist yet, creates floor index 0 with base parameters.
    /// </summary>
    private void DrawAddNextFloorButton()
    {
        if (!GUILayout.Button("+ Add Next Floor"))
            return;

        if (loadedFloors.Count == 0)
        {
            CreateBaseFloor();
            return;
        }

        // Always derive from the last floor so difficulty never resets mid-tower.
        FloorDesignerData lastFloor = loadedFloors[loadedFloors.Count - 1];
        FloorDesignerData newFloor  = FloorDesignerSaveUtils.GenerateFromPrevious(
            lastFloor,
            failThresholdDeltaPct,
            dayDurationDeltaPct,
            decayRateDeltaPct);

        loadedFloors.Add(newFloor);
        selectedFloorIndex = loadedFloors.Count - 1;
        Repaint();
    }

    // ─── Right Panel ──────────────────────────────────────────────────────────

    /// <summary>Draws the full floor editor panel for the currently selected floor.</summary>
    private void DrawRightPanel(float width)
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(width));

        if (!HasValidSelection())
        {
            EditorGUILayout.LabelField("Select a floor to edit.", EditorStyles.centeredGreyMiniLabel);
            EditorGUILayout.EndVertical();
            return;
        }

        FloorDesignerData floor = loadedFloors[selectedFloorIndex];

        editScrollPos = EditorGUILayout.BeginScrollView(editScrollPos);

        DrawFloorInfoSection(floor);
        DrawProfitabilitySection(floor);
        DrawRuleParametersSection(floor);
        DrawDifficultyDeltaSection();
        DrawNightsSection(floor);
        DrawExistingSavesSection(floor);
        DrawSaveActionsSection(floor);
        DrawComparisonSection(floor);

        EditorGUILayout.EndScrollView();
        EditorGUILayout.EndVertical();
    }

    // ─── Right Panel Sections ─────────────────────────────────────────────────

    /// <summary>Renders the floor name and index fields.</summary>
    private void DrawFloorInfoSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── FLOOR INFO ──", sectionLabelStyle);

        EditorGUI.BeginChangeCheck();
        string newName = EditorGUILayout.TextField("Display Name", floor.displayName);
        if (EditorGUI.EndChangeCheck())
        {
            floor.displayName = newName;
            floor.isDirty     = true;
        }

        EditorGUILayout.LabelField("Floor Index", floor.floorIndex.ToString());
        EditorGUILayout.Space(6f);
    }

    /// <summary>Renders the profitability parameters with clamped range sliders.</summary>
    private void DrawProfitabilitySection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── PROFITABILITY PARAMETERS ──", sectionLabelStyle);

        EditorGUI.BeginChangeCheck();

        float newFailThreshold     = EditorGUILayout.Slider("Fail Threshold", floor.failThreshold, FailThresholdMin, FailThresholdMax);
        float newDayDuration       = EditorGUILayout.Slider("Day Duration (s)", floor.dayDuration, DayDurationMin, DayDurationMax);
        float newDecayRate         = EditorGUILayout.Slider("Decay Rate /s", floor.decayRatePerSecond, DecayRateMin, DecayRateMax);

        if (EditorGUI.EndChangeCheck())
        {
            floor.failThreshold      = newFailThreshold;
            floor.dayDuration        = newDayDuration;
            floor.decayRatePerSecond = newDecayRate;
            floor.isDirty            = true;
        }

        EditorGUILayout.Space(6f);
    }

    /// <summary>Renders the integer rule parameter fields with clamped bounds.</summary>
    private void DrawRuleParametersSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── RULE PARAMETERS ──", sectionLabelStyle);

        EditorGUI.BeginChangeCheck();

        int newNumberOfBins     = EditorGUILayout.IntSlider("Number Of Bins", floor.numberOfBins, NumberOfBinsMin, NumberOfBinsMax);
        int newRulesPerBin      = EditorGUILayout.IntSlider("Rules Per Bin", floor.rulesPerBin, RulesPerBinMin, RulesPerBinMax);
        int newMaxComplexity    = EditorGUILayout.IntSlider("Max Rule Complexity", floor.maxRuleComplexity, MaxRuleComplexityMin, MaxRuleComplexityMax);

        if (EditorGUI.EndChangeCheck())
        {
            floor.numberOfBins    = newNumberOfBins;
            floor.rulesPerBin     = newRulesPerBin;
            floor.maxRuleComplexity = newMaxComplexity;
            floor.isDirty         = true;
        }

        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Renders the delta percentage fields used when generating the next floor.
    /// Editable per-session so the designer can tune the difficulty curve
    /// before generating a batch of floors, without editing each floor manually.
    /// </summary>
    private void DrawDifficultyDeltaSection()
    {
        EditorGUILayout.LabelField("── DIFFICULTY DELTA (Add Next Floor) ──", sectionLabelStyle);

        failThresholdDeltaPct = EditorGUILayout.Slider("Fail Threshold Delta", failThresholdDeltaPct, DeltaMin, DeltaMax);
        dayDurationDeltaPct   = EditorGUILayout.Slider("Day Duration Delta",   dayDurationDeltaPct,   DeltaMin, DeltaMax);
        decayRateDeltaPct     = EditorGUILayout.Slider("Decay Rate Delta",     decayRateDeltaPct,     DeltaMin, DeltaMax);

        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Renders the NIGHTS (5 Days) section.
    /// Each night shows a collapsible colored header with summary stats, and when expanded,
    /// three parameter sliders that RuleGenerator will read at runtime to create the rules.
    /// </summary>
    private void DrawNightsSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── NIGHTS (5 Days) ──", sectionLabelStyle);

        for (int i = 0; i < floor.nights.Count; i++)
            DrawNightPanel(floor, floor.nights[i], i);

        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Renders one night entry: a colored clickable header button and, when expanded,
    /// three parameter sliders (numberOfBins, rulesPerBin, maxRuleComplexity).
    /// </summary>
    /// <param name="floor">The parent floor whose isDirty flag is set on any change.</param>
    /// <param name="night">The night data to render.</param>
    /// <param name="nightIndex">Zero-based position of this night in the floor's nights list.</param>
    private void DrawNightPanel(FloorDesignerData floor, NightDesignerData night, int nightIndex)
    {
        // ── Night Header Button ───────────────────────────────────────────────
        GUIStyle headerStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        // Blue = manually customized night; gray = auto-inherited from previous night.
        // Visual distinction helps the designer quickly identify which nights have
        // custom rule parameters vs which are simply following the previous night.
        if (night.wasManuallyEdited)
            GUI.backgroundColor = new Color(0.4f, 0.7f, 1f);
        else
            GUI.backgroundColor = new Color(0.5f, 0.5f, 0.5f);

        // ✎ icon = manually edited night; ↓ icon = inherited from previous night.
        // Status suffix in the header lets the designer scan all 5 nights at a glance.
        string editMarker = night.wasManuallyEdited ? "  ✎" : "  ↓";
        string headerLabel = $"Night {nightIndex + 1}  |  {night.numberOfBins} bins"
                           + $"  |  {night.rulesPerBin} rules/bin"
                           + $"  |  complexity {night.maxRuleComplexity}"
                           + editMarker;

        if (GUILayout.Button(headerLabel, headerStyle, GUILayout.ExpandWidth(true)))
            night.isExpanded = !night.isExpanded;

        // Always reset after the colored header button so subsequent UI is not tinted.
        GUI.backgroundColor = Color.white;

        if (!night.isExpanded)
            return;

        // ── Night Sub-Panel ───────────────────────────────────────────────────
        EditorGUI.indentLevel++;

        EditorGUILayout.LabelField($"BIN & RULE SETTINGS — Night {nightIndex + 1}", EditorStyles.boldLabel);

        // Number of Bins slider — independent per night; not propagated.
        EditorGUI.BeginChangeCheck();
        int newNumberOfBins = EditorGUILayout.IntSlider(
            "Bins active this night", night.numberOfBins, NumberOfBinsMin, NumberOfBinsMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.numberOfBins    = newNumberOfBins;
            night.wasManuallyEdited = true;
            floor.isDirty         = true;
        }

        // Rules Per Bin slider — propagated forward to non-manually-edited nights.
        EditorGUI.BeginChangeCheck();
        int newRulesPerBin = EditorGUILayout.IntSlider(
            "Rules per bin this night", night.rulesPerBin, RulesPerBinMin, RulesPerBinMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.rulesPerBin       = newRulesPerBin;
            night.wasManuallyEdited = true;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty           = true;
        }

        // Max Rule Complexity slider — propagated forward to non-manually-edited nights.
        EditorGUI.BeginChangeCheck();
        int newMaxComplexity = EditorGUILayout.IntSlider(
            "Max rule complexity this night", night.maxRuleComplexity, MaxRuleComplexityMin, MaxRuleComplexityMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.maxRuleComplexity = newMaxComplexity;
            night.wasManuallyEdited = true;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty           = true;
        }

        // Status label — explains whether these values are custom or inherited,
        // helping the designer understand the source of each night's configuration.
        GUIStyle statusLabelStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
        string statusText;

        if (night.wasManuallyEdited)
            statusText = "✎ Manually customized";
        else if (nightIndex == 0)
            statusText = "↓ Base night — edit to customize";
        else
            // Shows which night this one is inheriting from, so the designer can trace the chain.
            statusText = $"↓ Inherited from Night {nightIndex}";

        EditorGUILayout.LabelField(statusText, statusLabelStyle);

        // Reset to inherited — only meaningful for nights that have a predecessor and were edited.
        bool canReset = nightIndex > 0 && night.wasManuallyEdited;
        if (canReset && GUILayout.Button($"Reset this night to inherited values"))
        {
            NightDesignerData previousNight = floor.nights[nightIndex - 1];
            night.rulesPerBin       = previousNight.rulesPerBin;
            night.maxRuleComplexity = previousNight.maxRuleComplexity;
            // Clear the manual flag so propagation can freely update this night again.
            night.wasManuallyEdited = false;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty = true;
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    /// <summary>
    /// Renders the existing save versions list for the current floor and exposes the
    /// advanced "Save as new version" action for designers who want versioned snapshots.
    /// Displayed so the designer can see which versions already exist before deciding
    /// whether to overwrite or create a new version.
    /// </summary>
    private void DrawExistingSavesSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── EXISTING SAVES ──", sectionLabelStyle);

        List<string> versions = FloorDesignerSaveUtils.GetExistingSaveVersions(floor.floorIndex);

        if (versions.Count == 0)
        {
            EditorGUILayout.LabelField("No saves found for this floor.", EditorStyles.miniLabel);
        }
        else
        {
            foreach (string fileName in versions)
                EditorGUILayout.LabelField(fileName, EditorStyles.miniLabel);
        }

        // Advanced versioning — kept here for designers who need parallel save snapshots.
        // "Save Floor" in the Save Actions section is the primary action for daily use.
        if (GUILayout.Button("Save as new version"))
        {
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: false);
            string lastSaved = GetLastSavedVersionName(floor.floorIndex);
            EditorUtility.DisplayDialog("Saved", $"Saved as {lastSaved}", "OK");
            Repaint();
        }

        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Renders the primary save and reset buttons for the selected floor.
    /// Two focused actions replace the previous three-button row to reduce cognitive load:
    /// [Save Floor] writes the canonical floor_N.json; [Reset] restores the last saved state.
    /// The advanced "Save as new version" action remains in the EXISTING SAVES section below.
    /// </summary>
    private void DrawSaveActionsSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── SAVE ACTIONS ──", sectionLabelStyle);

        EditorGUILayout.BeginHorizontal();

        // ── Save Floor ────────────────────────────────────────────────────────
        GUI.backgroundColor = Color.green;
        if (GUILayout.Button("💾 Save Floor"))
        {
            // Always writes to floor_N.json (overwrite: true) because "Save Floor" is the
            // canonical daily action — the designer saves the current authoritative version.
            // Versioning (v2, v3) is available via "Save as new version" in the Existing Saves
            // section for designers who want to keep multiple snapshots in parallel.
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: true);
            floor.isDirty = false;
            EditorUtility.DisplayDialog("Saved", $"floor_{floor.floorIndex}.json saved.", "OK");
            AddFloorBlockToDesignerScene();
            Repaint();
        }
        // Reset color immediately after drawing the colored button.
        // Unity EditorGUI shares GUI.backgroundColor — not resetting taints all subsequent UI.
        GUI.backgroundColor = Color.white;

        // ── Reset to Base Values ──────────────────────────────────────────────
        GUI.backgroundColor = new Color(1f, 0.5f, 0f);
        if (GUILayout.Button("↺ Reset to Base Values"))
        {
            bool confirmed = EditorUtility.DisplayDialog(
                "Reset Floor",
                "Reset floor to base values?\nAll current edits will be lost.",
                "Reset",
                "Cancel");

            if (confirmed)
                ResetSelectedFloorToBaseValues(floor);
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Renders a side-by-side comparison of the previous floor and the current floor,
    /// followed by a collapsible Night Progression Summary table.
    /// Displayed only when a previous floor exists in the loaded list.
    /// Visual comparison helps the designer verify the difficulty curve feels right
    /// before committing to a save.
    /// </summary>
    private void DrawComparisonSection(FloorDesignerData floor)
    {
        if (floor.floorIndex == 0 || selectedFloorIndex == 0)
            return;

        FloorDesignerData previousFloor = FindPreviousFloor(floor.floorIndex);

        if (previousFloor == null)
            return;

        EditorGUILayout.LabelField("── COMPARISON ──", sectionLabelStyle);

        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Parameter",         EditorStyles.boldLabel, GUILayout.Width(160f));
        EditorGUILayout.LabelField("Previous Floor",    EditorStyles.boldLabel, GUILayout.Width(120f));
        EditorGUILayout.LabelField("This Floor",        EditorStyles.boldLabel, GUILayout.Width(120f));
        EditorGUILayout.EndHorizontal();

        DrawComparisonRow("Fail Threshold",   previousFloor.failThreshold.ToString("F2"),      floor.failThreshold.ToString("F2"));
        DrawComparisonRow("Day Duration",     previousFloor.dayDuration.ToString("F2"),        floor.dayDuration.ToString("F2"));
        DrawComparisonRow("Decay Rate",       previousFloor.decayRatePerSecond.ToString("F3"), floor.decayRatePerSecond.ToString("F3"));
        DrawComparisonRow("Number Of Bins",   previousFloor.numberOfBins.ToString(),           floor.numberOfBins.ToString());

        EditorGUILayout.Space(6f);

        // ── Night Progression Summary ─────────────────────────────────────────
        // Collapsible table so the designer can verify the 5-night difficulty ramp
        // at a glance without cluttering the panel when not needed.
        showNightComparison = EditorGUILayout.Foldout(showNightComparison, "Night Progression Summary", toggleOnLabelClick: true);

        if (!showNightComparison)
            return;

        // Header row for the night progression table.
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Night",      EditorStyles.boldLabel, GUILayout.Width(50f));
        EditorGUILayout.LabelField("Bins",       EditorStyles.boldLabel, GUILayout.Width(50f));
        EditorGUILayout.LabelField("Rules/Bin",  EditorStyles.boldLabel, GUILayout.Width(75f));
        EditorGUILayout.LabelField("Complexity", EditorStyles.boldLabel, GUILayout.Width(80f));
        EditorGUILayout.LabelField("Status",     EditorStyles.boldLabel, GUILayout.Width(80f));
        EditorGUILayout.EndHorizontal();

        // One row per night — lets the designer verify the full difficulty ramp across all 5 nights.
        for (int i = 0; i < floor.nights.Count; i++)
        {
            NightDesignerData night = floor.nights[i];
            string status = night.wasManuallyEdited ? "Custom" : "Inherited";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField($"Night {i + 1}", GUILayout.Width(50f));
            EditorGUILayout.LabelField(night.numberOfBins.ToString(),      GUILayout.Width(50f));
            EditorGUILayout.LabelField(night.rulesPerBin.ToString(),       GUILayout.Width(75f));
            EditorGUILayout.LabelField(night.maxRuleComplexity.ToString(),  GUILayout.Width(80f));
            EditorGUILayout.LabelField(status,                             GUILayout.Width(80f));
            EditorGUILayout.EndHorizontal();
        }

        EditorGUILayout.Space(6f);
    }

    /// <summary>Renders one labelled row in the comparison table.</summary>
    private void DrawComparisonRow(string parameterLabel, string previousValue, string currentValue)
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField(parameterLabel, GUILayout.Width(160f));
        EditorGUILayout.LabelField(previousValue,  GUILayout.Width(120f));
        EditorGUILayout.LabelField(currentValue,   GUILayout.Width(120f));
        EditorGUILayout.EndHorizontal();
    }

    // ─── Data Operations ──────────────────────────────────────────────────────

    /// <summary>
    /// Iterates floor indices 0 to MaxLoadedFloorIndex and loads each from disk.
    /// Stops at the first missing index to avoid loading non-sequential floors
    /// that would produce gaps in the tower list and confuse the difficulty chain.
    /// </summary>
    private void LoadAllFloors()
    {
        loadedFloors.Clear();
        selectedFloorIndex = -1;

        for (int i = 0; i <= MaxLoadedFloorIndex; i++)
        {
            FloorDesignerData loaded = FloorDesignerSaveUtils.LoadFloorIntoDesigner(i);

            // Stop at first missing index — gaps mean non-sequential saves.
            if (loaded == null)
                break;

            loadedFloors.Add(loaded);
        }

        Repaint();
    }

    /// <summary>
    /// Generates and adds a new floor derived from the last loaded floor.
    /// If no floors are loaded yet, creates floor index 0 with base parameters.
    /// Automatically calls AddFloorBlockToDesignerScene so the visual tower updates
    /// immediately without the designer needing to click Refresh manually.
    /// </summary>
    private void AddNewFloor()
    {
        if (loadedFloors.Count == 0)
        {
            CreateBaseFloor();
            return;
        }

        // Always derive from the last floor to maintain difficulty inheritance chain.
        FloorDesignerData lastFloor = loadedFloors[loadedFloors.Count - 1];
        FloorDesignerData newFloor  = FloorDesignerSaveUtils.GenerateFromPrevious(
            lastFloor,
            failThresholdDeltaPct,
            dayDurationDeltaPct,
            decayRateDeltaPct);

        loadedFloors.Add(newFloor);
        selectedFloorIndex = loadedFloors.Count - 1;

        // Block appears in DesignerTowerScene as soon as the floor is created in the tool.
        AddFloorBlockToDesignerScene();
        Repaint();
    }

    /// <summary>
    /// Copies all profitability and rule parameters from the floor below the selected one.
    /// Does NOT change floorIndex — only parameter values are overwritten.
    /// Sets isDirty = true so the designer is reminded to save after the inherit operation.
    /// </summary>
    private void InheritFromPreviousFloor()
    {
        if (!HasValidSelection())
            return;

        FloorDesignerData selected = loadedFloors[selectedFloorIndex];
        FloorDesignerData previous = FindPreviousFloor(selected.floorIndex);

        if (previous == null)
            return;

        selected.failThreshold      = previous.failThreshold;
        selected.dayDuration        = previous.dayDuration;
        selected.decayRatePerSecond = previous.decayRatePerSecond;
        selected.numberOfBins       = previous.numberOfBins;
        selected.rulesPerBin        = previous.rulesPerBin;
        selected.maxRuleComplexity  = previous.maxRuleComplexity;
        selected.isDirty            = true;

        Repaint();
    }

    /// <summary>
    /// Deletes every floor_N*.json file from disk after confirming with the designer.
    /// Clears the in-memory floor list and refreshes DesignerTowerScene to show the empty state.
    /// Requires explicit confirmation because this action cannot be undone.
    /// </summary>
    private void DestroyAllFloors()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Destroy All Floors",
            "Delete ALL saved floor files? This cannot be undone.",
            "Delete All",
            "Cancel");

        if (!confirmed)
            return;

        string saveFolder = System.IO.Path.Combine(
            UnityEngine.Application.persistentDataPath, "floors");

        if (System.IO.Directory.Exists(saveFolder))
        {
            string[] allFiles = System.IO.Directory.GetFiles(saveFolder, "floor_*.json");
            foreach (string filePath in allFiles)
                System.IO.File.Delete(filePath);
        }

        loadedFloors.Clear();
        selectedFloorIndex = -1;

        // After deleting all floors, DesignerTowerScene must update to show the empty-tower message.
        ClearDesignerTowerScene();
        Repaint();
    }

    /// <summary>
    /// Creates floor index 0 with base parameters when no floors exist.
    /// Calls InitializeNights so the floor immediately has 5 valid night entries.
    /// </summary>
    private void CreateBaseFloor()
    {
        if (loadedFloors.Count > 0)
            return;

        FloorDesignerData baseFloor = new FloorDesignerData
        {
            floorIndex         = 0,
            displayName        = "Floor 1",
            failThreshold      = BaseFailThreshold,
            dayDuration        = BaseDayDuration,
            decayRatePerSecond = BaseDecayRatePerSecond,
            numberOfBins       = BaseNumberOfBins,
            rulesPerBin        = BaseRulesPerBin,
            maxRuleComplexity  = BaseMaxRuleComplexity,
            isDirty            = true
        };
        baseFloor.InitializeNights();

        loadedFloors.Add(baseFloor);
        selectedFloorIndex = 0;
        Repaint();
    }

    // ─── Designer Scene Helpers ───────────────────────────────────────────────

    /// <summary>
    /// Finds the DesignerTowerPreview component in the open DesignerTowerScene and calls
    /// LoadAndDisplayFloors() on it so the visual tower reflects the newly added floor.
    /// Uses EditorSceneManager (not SceneManager) because this runs in Edit mode, not at runtime.
    /// Logs a warning and returns early if the scene is not currently open in the Editor.
    /// </summary>
    private void AddFloorBlockToDesignerScene()
    {
        DesignerTowerPreview preview = FindDesignerTowerPreview();

        if (preview == null)
            return;

        // Refreshes the visual tower immediately after a new floor is added —
        // no need for the designer to manually click Refresh in the scene.
        preview.LoadAndDisplayFloors();
    }

    /// <summary>
    /// Finds the DesignerTowerPreview component in the open DesignerTowerScene and calls
    /// LoadAndDisplayFloors() so the scene shows the empty-tower message after all floors
    /// have been deleted from disk.
    /// </summary>
    private void ClearDesignerTowerScene()
    {
        DesignerTowerPreview preview = FindDesignerTowerPreview();

        if (preview == null)
            return;

        // After deleting all floors, the scene must update to show the empty-tower guidance message.
        preview.LoadAndDisplayFloors();
    }

    /// <summary>
    /// Looks up the DesignerTowerScene by name using EditorSceneManager and searches its
    /// root GameObjects for a DesignerTowerPreview component.
    /// Returns null and logs a warning if the scene is not loaded or the component is not found.
    /// </summary>
    /// <returns>The DesignerTowerPreview component, or null.</returns>
    private DesignerTowerPreview FindDesignerTowerPreview()
    {
        // EditorSceneManager works in Edit mode; SceneManager only works at runtime.
        Scene designerScene = EditorSceneManager.GetSceneByName("DesignerTowerScene");

        if (!designerScene.IsValid() || !designerScene.isLoaded)
        {
            Debug.LogWarning("[FloorDesigner] Open DesignerTowerScene to see blocks update.");
            return null;
        }

        foreach (GameObject root in designerScene.GetRootGameObjects())
        {
            DesignerTowerPreview preview = root.GetComponentInChildren<DesignerTowerPreview>();
            if (preview != null)
                return preview;
        }

        Debug.LogWarning("[FloorDesigner] DesignerTowerPreview component not found in DesignerTowerScene.");
        return null;
    }

    /// <summary>
    /// Resets the selected floor to its base values without affecting other floors.
    /// If a save file exists on disk, reloads from disk (last saved version — not factory defaults).
    /// If no save file exists and this is floor 0, rebuilds from FloorDifficultyProgression base values.
    /// If no save file exists for any other floor index, rebuilds by applying deltas from the previous floor.
    /// Sets isDirty = false after reset because the floor is now consistent with its saved state.
    /// </summary>
    /// <param name="floor">The floor to reset in-place.</param>
    private void ResetSelectedFloorToBaseValues(FloorDesignerData floor)
    {
        bool floorExistsOnDisk = FloorDesignerSaveUtils.FloorExists(floor.floorIndex);

        if (floorExistsOnDisk)
        {
            // Reload from disk so the reset restores the last saved version,
            // not factory defaults — the designer always saves intentionally.
            FloorDesignerData reloaded = FloorDesignerSaveUtils.LoadFloorIntoDesigner(floor.floorIndex);

            if (reloaded != null)
            {
                loadedFloors[selectedFloorIndex] = reloaded;
                reloaded.isDirty = false;
            }
        }
        else if (floor.floorIndex == 0)
        {
            // No save exists and this is the base floor — rebuild from hardcoded base values.
            floor.failThreshold      = BaseFailThreshold;
            floor.dayDuration        = BaseDayDuration;
            floor.decayRatePerSecond = BaseDecayRatePerSecond;
            floor.numberOfBins       = BaseNumberOfBins;
            floor.rulesPerBin        = BaseRulesPerBin;
            floor.maxRuleComplexity  = BaseMaxRuleComplexity;
            floor.nights.Clear();
            floor.InitializeNights();
            floor.isDirty = false;
        }
        else
        {
            // No save exists for a non-zero floor — rebuild from the previous floor + deltas
            // to restore the original generated state before any manual edits were made.
            FloorDesignerData previousFloor = FindPreviousFloor(floor.floorIndex);

            if (previousFloor == null)
            {
                Debug.LogWarning($"[FloorDesigner] Cannot reset floor {floor.floorIndex}: previous floor not loaded.");
                return;
            }

            FloorDesignerData rebuilt = FloorDesignerSaveUtils.GenerateFromPrevious(
                previousFloor,
                failThresholdDeltaPct,
                dayDurationDeltaPct,
                decayRateDeltaPct);

            rebuilt.floorIndex  = floor.floorIndex;
            rebuilt.displayName = floor.displayName;
            rebuilt.isDirty     = false;

            loadedFloors[selectedFloorIndex] = rebuilt;
        }

        Repaint();
    }

    /// <summary>Reloads the selected floor from disk, discarding all in-memory changes.</summary>
    private void ReloadSelectedFloorFromDisk()
    {
        if (!HasValidSelection())
            return;

        int floorIndex = loadedFloors[selectedFloorIndex].floorIndex;
        FloorDesignerData reloaded = FloorDesignerSaveUtils.LoadFloorIntoDesigner(floorIndex);

        if (reloaded == null)
        {
            Debug.LogWarning($"[FloorDesigner] No save file found for floor {floorIndex} — cannot reload.");
            return;
        }

        loadedFloors[selectedFloorIndex] = reloaded;
        Repaint();
    }

    /// <summary>Saves all loaded floors regardless of dirty state.</summary>
    private void SaveAllFloors()
    {
        foreach (FloorDesignerData floor in loadedFloors)
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: true);
    }

    // ─── Query Helpers ────────────────────────────────────────────────────────

    /// <summary>Returns true when selectedFloorIndex points to a valid entry in loadedFloors.</summary>
    private bool HasValidSelection()
    {
        return selectedFloorIndex >= 0 && selectedFloorIndex < loadedFloors.Count;
    }

    /// <summary>Returns true when at least one floor in loadedFloors has isDirty set to true.</summary>
    private bool HasAnyDirtyFloor()
    {
        foreach (FloorDesignerData floor in loadedFloors)
        {
            if (floor.isDirty)
                return true;
        }
        return false;
    }

    /// <summary>
    /// Finds the loaded floor whose floorIndex equals targetFloorIndex - 1.
    /// Returns null if no such floor is currently loaded in the tool.
    /// </summary>
    private FloorDesignerData FindPreviousFloor(int targetFloorIndex)
    {
        int previousIndex = targetFloorIndex - 1;
        foreach (FloorDesignerData floor in loadedFloors)
        {
            if (floor.floorIndex == previousIndex)
                return floor;
        }
        return null;
    }

    /// <summary>
    /// Returns the file name of the most recently created versioned save for the given floor index.
    /// Used in the "Saved as" confirmation dialog after a non-overwrite save.
    /// </summary>
    private string GetLastSavedVersionName(int floorIndex)
    {
        List<string> versions = FloorDesignerSaveUtils.GetExistingSaveVersions(floorIndex);
        return versions.Count > 0 ? versions[versions.Count - 1] : $"floor_{floorIndex}.json";
    }

    // ─── Style Initialization ─────────────────────────────────────────────────

    /// <summary>
    /// Builds GUIStyle instances once per enable cycle.
    /// Called lazily from OnGUI rather than OnEnable because GUISkin is not
    /// available in OnEnable and accessing it there causes Unity errors.
    /// </summary>
    private void EnsureStylesInitialized()
    {
        if (isStylesInitialized)
            return;

        sectionLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize = 11
        };

        isStylesInitialized = true;
    }
}
