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
    private const int NumberOfBinsMax      = 4;
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

    // Cached array of all RuleType enum names — used internally for serialisation.
    private string[] ruleTypeNames;

    // Cached array of human-readable rule labels built from database templates.
    // Same length and order as ruleTypeNames — index N in both arrays refers to the same RuleType.
    private string[] ruleTypeLabels;

    // ─── Cached GUIStyles — created once per OnEnable to avoid per-frame alloc ─

    private GUIStyle sectionLabelStyle;
    private bool isStylesInitialized;

    // ─── Per-night dropdown selection index (0–4), one per night.
    // Index into the SpecificityDatabase.allSpecificities array for the Popup widget.
    // Not serialised — resets on editor reload, acceptable for a transient selection state.
    private readonly int[] specificityDropdownIndex = new int[5];

    /// <summary>Cached specificity names loaded from the SpecificityDatabase for the Popup widget.</summary>
    private string[] cachedSpecificityNames;

    /// <summary>Cached reference to the SpecificityDatabase asset found in the project.</summary>
    private SpecificityDatabase cachedSpecificityDatabase;

    /// <summary>
    /// Tracks whether the Night Progression comparison table is expanded.
    /// Default false keeps the comparison section compact until the designer needs it.
    /// </summary>
    private bool showNightComparison;

    // ─── Rule Library state (chargée depuis RuleLibraryData.json) ────────────

    /// <summary>Toutes les entrées de la Rule Library chargées depuis le JSON.</summary>
    private List<RuleLibraryEntry> libraryEntries = new List<RuleLibraryEntry>();

    /// <summary>
    /// Labels affichés dans les popups du formulaire d'ajout/remplacement.
    /// Format : "[complexité★] label de l'entrée"
    /// </summary>
    private string[] libraryEntryLabels = new string[0];

    /// <summary>
    /// Per-night, per-bin: index de l'entrée sélectionnée dans le dropdown d'ajout.
    /// Key = nightIndex * 10 + binIndex.
    /// </summary>
    private Dictionary<int, int> addLibraryEntryIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-night, per-bin: index de la corbeille secondaire choisie pour les règles Ou/Sauf.
    /// Key = nightIndex * 10 + binIndex.
    /// </summary>
    private Dictionary<int, int> addSecondaryBinIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-night, per-bin, per-rule: index de l'entrée sélectionnée dans le dropdown de remplacement.
    /// Key = nightIndex * 1000 + binIndex * 100 + ruleIndex.
    /// </summary>
    private Dictionary<int, int> replaceLibraryEntryIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-night, per-bin, per-rule: index de la corbeille secondaire pour les remplacements Ou/Sauf.
    /// Key = nightIndex * 1000 + binIndex * 100 + ruleIndex.
    /// </summary>
    private Dictionary<int, int> replaceSecondaryBinIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-night: index sélectionné dans le dropdown du prefab trash.
    /// Key = nightIndex.
    /// </summary>
    private Dictionary<int, int> trashPrefabDropdownIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-rule: index du dropdown "Corbeille 1 → bin physique" pour les règles Branch.
    /// Key = nightIndex * 1000 + binIndex * 100 + ruleIndex.
    /// </summary>
    private Dictionary<int, int> branchSlot1DropdownIndex = new Dictionary<int, int>();

    /// <summary>
    /// Per-rule: index du dropdown "Corbeille 2 → bin physique" pour les règles Branch.
    /// Key = nightIndex * 1000 + binIndex * 100 + ruleIndex.
    /// </summary>
    private Dictionary<int, int> branchSlot2DropdownIndex = new Dictionary<int, int>();

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
        ruleTypeNames       = Enum.GetNames(typeof(RuleType));
        isStylesInitialized = false;
        LoadSpecificityDatabase();
        LoadAllFloors();
        BuildRuleTypeLabels();
        LoadRuleLibrary();
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

    // ─── Specificity Database Loader ──────────────────────────────────────────

    /// <summary>
    /// Finds and caches the first SpecificityDatabase asset in the project using AssetDatabase.
    /// Builds cachedSpecificityNames from allSpecificities so the Popup widget can render
    /// all available specificities without querying AssetDatabase every frame.
    /// Called once in OnEnable and whenever the database reference is invalidated.
    /// Logs a warning if no database is found — Pinned Specificities dropdowns will
    /// display a single "No database found" entry and remain non-functional until one is created.
    /// </summary>
    private void LoadSpecificityDatabase()
    {
        string[] guids = AssetDatabase.FindAssets("t:SpecificityDatabase");

        if (guids.Length == 0)
        {
            Debug.LogWarning("[FloorDesigner] No SpecificityDatabase asset found in project. Pinned Specificities dropdowns will be disabled.");
            cachedSpecificityDatabase = null;
            cachedSpecificityNames    = new string[] { "(No database found)" };
            return;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        cachedSpecificityDatabase = AssetDatabase.LoadAssetAtPath<SpecificityDatabase>(assetPath);

        if (cachedSpecificityDatabase == null || cachedSpecificityDatabase.allSpecificities == null ||
            cachedSpecificityDatabase.allSpecificities.Count == 0)
        {
            cachedSpecificityNames = new string[] { "(Database is empty)" };
            return;
        }

        cachedSpecificityNames = cachedSpecificityDatabase.allSpecificities.ToArray();
    }

    /// <summary>
    /// Builds ruleTypeLabels in the same order as ruleTypeNames (Enum.GetNames order).
    /// Each label uses the templateText from the SpecificityDatabase for that RuleType,
    /// keeping {0} and {1} visible as placeholders so the designer understands what
    /// condition slots the rule expects. Falls back to the enum name when no template exists.
    /// Called after LoadSpecificityDatabase so the database is guaranteed to be loaded first.
    /// </summary>
    private void BuildRuleTypeLabels()
    {
        RuleType[] allTypes = (RuleType[])Enum.GetValues(typeof(RuleType));
        ruleTypeLabels = new string[allTypes.Length];

        for (int i = 0; i < allTypes.Length; i++)
        {
            RuleType type      = allTypes[i];
            string   enumName  = type.ToString();
            string   label     = enumName; // fallback

            if (cachedSpecificityDatabase?.templates != null)
            {
                foreach (RuleTemplate t in cachedSpecificityDatabase.templates)
                {
                    if (t.ruleType == type)
                    {
                        label = t.templateText;
                        break;
                    }
                }
            }

            // If still on enum name, use the hard-coded French fallbacks from FloorDesignerRuleGen.
            if (label == enumName)
            {
                label = type switch
                {
                    RuleType.Simple   => "Si {0} → ici",
                    RuleType.Multiple => "Si {0} et {1} → ici",
                    RuleType.Branch   => "Si {0} mais pas {1} → ici",
                    _                 => enumName
                };
            }

            ruleTypeLabels[i] = label;
        }
    }

    // ─── Rule Library Loader ──────────────────────────────────────────────────

    /// <summary>
    /// Chemin vers le JSON de la Rule Library — identique au chemin utilisé par RuleLibraryWindow.
    /// </summary>
    private static string RuleLibraryFilePath =>
        System.IO.Path.Combine(Application.dataPath, "Editor", "RuleLibraryData.json");

    /// <summary>
    /// Charge toutes les entrées de la Rule Library depuis le JSON et reconstruit les labels
    /// affichés dans les dropdowns d'ajout/remplacement de règle.
    ///
    /// Format du label : "[★ × complexité] 📦 nomPrefab" (mode prefab)
    ///                ou "[★ × complexité] [connecteur] label" (mode conditions legacy)
    /// Sont incluses : les entrées prefab ET les entrées structurées (conditions).
    /// Les manuscrits purs sans prefab sont exclus — aucune logique d'assignation.
    /// </summary>
    private void LoadRuleLibrary()
    {
        libraryEntries = new List<RuleLibraryEntry>();

        if (!System.IO.File.Exists(RuleLibraryFilePath))
        {
            Debug.LogWarning("[FloorDesigner] RuleLibraryData.json introuvable — aucune règle de bibliothèque disponible.");
            libraryEntryLabels = new string[] { "(Bibliothèque vide)" };
            return;
        }

        try
        {
            string json = System.IO.File.ReadAllText(RuleLibraryFilePath);
            RuleLibraryFile file = JsonUtility.FromJson<RuleLibraryFile>(json);

            if (file == null || file.entries == null)
            {
                libraryEntryLabels = new string[] { "(Bibliothèque vide)" };
                return;
            }

            // Inclure les entrées prefab ET les entrées avec conditions structurées (legacy).
            foreach (RuleLibraryEntry entry in file.entries)
            {
                bool hasPrefab     = !string.IsNullOrEmpty(entry.prefabPath);
                bool hasConditions = entry.conditions != null && entry.conditions.Count > 0;

                if (hasPrefab || hasConditions)
                    libraryEntries.Add(entry);
            }

            // Construction des labels pour le popup.
            libraryEntryLabels = new string[libraryEntries.Count];
            for (int i = 0; i < libraryEntries.Count; i++)
            {
                RuleLibraryEntry e     = libraryEntries[i];
                string           stars = new string('★', e.complexity);

                if (!string.IsNullOrEmpty(e.prefabPath))
                {
                    string prefabName      = System.IO.Path.GetFileNameWithoutExtension(e.prefabPath);
                    libraryEntryLabels[i]  = $"[{stars}] 📦 {prefabName}";
                }
                else
                {
                    string connector      = e.conditions.Count > 1 ? $" [{e.conditions[0].connector}]" : string.Empty;
                    libraryEntryLabels[i] = $"[{stars}]{connector} {e.label}";
                }
            }

            Debug.Log($"[FloorDesigner] {libraryEntries.Count} règles chargées depuis la bibliothèque.");
        }
        catch (System.Exception ex)
        {
            Debug.LogError($"[FloorDesigner] Erreur de chargement de la bibliothèque : {ex.Message}");
            libraryEntryLabels = new string[] { "(Erreur de chargement)" };
        }
    }

    // ─── Left Panel ───────────────────────────────────────────────────────────

    /// <summary>Draws the tower overview panel with floor list and toolbar.</summary>
    private void DrawLeftPanel(float width)    {
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
    /// When the floor is not yet saved: shows difficulty sliders per night.
    /// When the floor has been saved: shows the per-bin rule editor for each night.
    /// </summary>
    private void DrawNightsSection(FloorDesignerData floor)
    {
        EditorGUILayout.LabelField("── NIGHTS (5 Days) ──", sectionLabelStyle);

        for (int i = 0; i < floor.nights.Count; i++)
        {
            if (floor.isSaved)
                DrawNightPanelRuleEditor(floor, floor.nights[i], i);
            else
                DrawNightPanelCreation(floor, floor.nights[i], i);
        }

        EditorGUILayout.Space(6f);
    }

    /// <summary>
    /// Creation mode night panel: difficulty sliders only (numberOfBins, rulesPerBin, maxRuleComplexity).
    /// Shown before the first save so the designer sets difficulty without being distracted by rule details.
    /// </summary>
    private void DrawNightPanelCreation(FloorDesignerData floor, NightDesignerData night, int nightIndex)
    {
        // ── Night Header Button ───────────────────────────────────────────────
        GUIStyle headerStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        GUI.backgroundColor = night.wasManuallyEdited
            ? new Color(0.4f, 0.7f, 1f)
            : new Color(0.5f, 0.5f, 0.5f);

        string editMarker  = night.wasManuallyEdited ? "  ✎" : "  ↓";
        string headerLabel = $"Nuit {nightIndex + 1}  |  {night.numberOfBins} corbeilles"
                           + $"  |  {night.rulesPerBin} règles/corbeille"
                           + $"  |  complexité {night.maxRuleComplexity}"
                           + editMarker;

        if (GUILayout.Button(headerLabel, headerStyle, GUILayout.ExpandWidth(true)))
            night.isExpanded = !night.isExpanded;

        GUI.backgroundColor = Color.white;

        if (!night.isExpanded)
            return;

        EditorGUI.indentLevel++;

        EditorGUILayout.LabelField($"DIFFICULTÉ — Nuit {nightIndex + 1}", EditorStyles.boldLabel);

        // Number of Bins
        EditorGUI.BeginChangeCheck();
        int newBins = EditorGUILayout.IntSlider("Corbeilles actives", night.numberOfBins, NumberOfBinsMin, NumberOfBinsMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.numberOfBins      = newBins;
            night.wasManuallyEdited = true;
            floor.isDirty           = true;
        }

        // Rules Per Bin
        EditorGUI.BeginChangeCheck();
        int newRulesPerBin = EditorGUILayout.IntSlider("Règles par corbeille", night.rulesPerBin, RulesPerBinMin, RulesPerBinMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.rulesPerBin       = newRulesPerBin;
            night.wasManuallyEdited = true;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty           = true;
        }

        // Max Rule Complexity
        EditorGUI.BeginChangeCheck();
        int newMaxComplexity = EditorGUILayout.IntSlider("Difficulté de règle (max)", night.maxRuleComplexity, MaxRuleComplexityMin, MaxRuleComplexityMax);
        if (EditorGUI.EndChangeCheck())
        {
            night.maxRuleComplexity = newMaxComplexity;
            night.wasManuallyEdited = true;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty           = true;
        }

        GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
        string statusText = night.wasManuallyEdited
            ? "✎ Personnalisé manuellement"
            : nightIndex == 0
                ? "↓ Nuit de base — modifier pour personnaliser"
                : $"↓ Hérité de la Nuit {nightIndex}";
        EditorGUILayout.LabelField(statusText, statusStyle);

        // Reset to inherited button
        bool canReset = nightIndex > 0 && night.wasManuallyEdited;
        if (canReset && GUILayout.Button("Réinitialiser aux valeurs héritées"))
        {
            NightDesignerData previousNight = floor.nights[nightIndex - 1];
            night.rulesPerBin       = previousNight.rulesPerBin;
            night.maxRuleComplexity = previousNight.maxRuleComplexity;
            night.wasManuallyEdited = false;
            floor.PropagateNightRules(nightIndex);
            floor.isDirty = true;
        }

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    /// <summary>
    /// Rule editor mode night panel: shows one collapsible bin panel per corbeille,
    /// each listing its assigned rules with add / remove / replace controls.
    /// Also provides [+ Corbeille] and [− Corbeille] buttons to adjust the bin count per night.
    /// Shown after the first save — the difficulty sliders are no longer displayed.
    /// </summary>
    private void DrawNightPanelRuleEditor(FloorDesignerData floor, NightDesignerData night, int nightIndex)
    {
        // ── Night Header Button ───────────────────────────────────────────────
        GUIStyle headerStyle = new GUIStyle(GUI.skin.button)
        {
            fontSize  = 12,
            fontStyle = FontStyle.Bold,
            alignment = TextAnchor.MiddleLeft
        };

        int totalRules = 0;
        foreach (BinDesignerData bin in night.bins)
            totalRules += bin.rules.Count;

        GUI.backgroundColor = new Color(0.35f, 0.65f, 0.95f);
        string nightHeader = $"Nuit {nightIndex + 1}  |  {night.numberOfBins} corbeilles  |  {totalRules} règles au total";

        if (GUILayout.Button(nightHeader, headerStyle, GUILayout.ExpandWidth(true)))
            night.isExpanded = !night.isExpanded;

        GUI.backgroundColor = Color.white;

        if (!night.isExpanded)
            return;

        EditorGUI.indentLevel++;

        // ── One bin panel per corbeille ───────────────────────────────────────
        for (int binIdx = 0; binIdx < night.bins.Count; binIdx++)
            DrawBinRulePanel(floor, night, nightIndex, night.bins[binIdx], binIdx);

        // ── Bin count controls ────────────────────────────────────────────────
        EditorGUILayout.Space(4f);
        EditorGUILayout.BeginHorizontal();

        bool canAdd    = night.numberOfBins < NumberOfBinsMax;
        bool canRemove = night.numberOfBins > NumberOfBinsMin;

        using (new EditorGUI.DisabledScope(!canAdd))
        {
            GUI.backgroundColor = canAdd ? new Color(0.4f, 0.85f, 0.4f) : Color.white;
            if (GUILayout.Button("+ Corbeille", GUILayout.Width(110f)))
            {
                night.numberOfBins++;
                // SyncBins appends a new empty bin and keeps binIndex/binID consistent.
                night.SyncBins(night.numberOfBins);
                floor.isDirty = true;
            }
            GUI.backgroundColor = Color.white;
        }

        using (new EditorGUI.DisabledScope(!canRemove))
        {
            GUI.backgroundColor = canRemove ? new Color(1f, 0.4f, 0.4f) : Color.white;
            if (GUILayout.Button("− Corbeille", GUILayout.Width(110f)))
            {
                if (EditorUtility.DisplayDialog(
                    "Supprimer la corbeille",
                    $"Supprimer la Corbeille {FloorDesignerData.GetBinDisplayName(FloorDesignerData.GetBinID(night.numberOfBins - 1))} de la Nuit {nightIndex + 1} ?\n" +
                    "Les règles qu'elle contient seront perdues.",
                    "Supprimer", "Annuler"))
                {
                    night.numberOfBins--;
                    night.SyncBins(night.numberOfBins);
                    floor.isDirty = true;
                }
            }
            GUI.backgroundColor = Color.white;
        }

        GUIStyle binCountStyle = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Italic };
        EditorGUILayout.LabelField($"({night.numberOfBins}/{NumberOfBinsMax})", binCountStyle);

        EditorGUILayout.EndHorizontal();

        // ── Section Corbeille Poubelle ────────────────────────────────────────
        EditorGUILayout.Space(6f);
        DrawTrashBinSection(floor, night, nightIndex);

        EditorGUI.indentLevel--;
        EditorGUILayout.Space(4f);
    }

    /// <summary>
    /// Draws the collapsible rule editor panel for one bin within a night.
    /// Lists assigned rules with [×] remove and [↕] replace actions, plus an "Add Rule" form.
    /// </summary>
    private void DrawBinRulePanel(FloorDesignerData floor, NightDesignerData night, int nightIndex,
                                  BinDesignerData bin, int binIdx)
    {
        int stateKey = nightIndex * 10 + binIdx;

        // ── Bin Header ───────────────────────────────────────────────────────
        GUIStyle binHeaderStyle = new GUIStyle(EditorStyles.foldout)
        {
            fontStyle = FontStyle.Bold,
            fontSize  = 11
        };

        GUI.backgroundColor = new Color(0.55f, 0.85f, 0.55f);
        string binDisplayName = FloorDesignerData.GetBinDisplayName(bin.binID);
        string binLabel = $"Corbeille {binDisplayName}  ({bin.rules.Count} règle{(bin.rules.Count != 1 ? "s" : "")})";

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        bin.isExpanded = EditorGUILayout.Foldout(bin.isExpanded, binLabel, toggleOnLabelClick: true, binHeaderStyle);
        GUI.backgroundColor = Color.white;

        if (bin.isExpanded)
        {
            EditorGUI.indentLevel++;

            // ── Existing rules ────────────────────────────────────────────────
            if (bin.rules.Count == 0)
            {
                EditorGUILayout.LabelField("Aucune règle assignée.", EditorStyles.miniLabel);
            }
            else
            {
                for (int ruleIdx = 0; ruleIdx < bin.rules.Count; ruleIdx++)
                    DrawRuleRow(floor, night, nightIndex, bin, binIdx, ruleIdx, stateKey);
            }

            EditorGUILayout.Space(4f);

            // ── Add Rule form ─────────────────────────────────────────────────
            DrawAddRuleForm(floor, night, nightIndex, bin, binIdx, stateKey);

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
        EditorGUILayout.Space(2f);
    }

    /// <summary>
    /// Dessine une ligne pour une règle existante : texte de résumé, bouton [×] suppression,
    /// et formulaire inline de remplacement piochant dans la Rule Library.
    /// Pour les règles doubles (Ou/Sauf), affiche un sélecteur de corbeille secondaire.
    /// </summary>
    private void DrawRuleRow(FloorDesignerData floor, NightDesignerData night, int nightIndex,
                             BinDesignerData bin, int binIdx, int ruleIdx, int stateKey)
    {
        DesignerRuleEntry rule      = bin.rules[ruleIdx];
        int               replaceKey = nightIndex * 1000 + binIdx * 100 + ruleIdx;

        EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        EditorGUILayout.BeginHorizontal();

        string ruleLabel = BuildStructuredLabel(rule);
        EditorGUILayout.LabelField(ruleLabel, GUILayout.ExpandWidth(true));

        GUI.backgroundColor = new Color(1f, 0.4f, 0.4f);
        if (GUILayout.Button("×", GUILayout.Width(24f)))
        {
            bin.rules.RemoveAt(ruleIdx);
            floor.isDirty = true;
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            EditorGUILayout.EndVertical();
            return;
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        // ── Dropdowns Branch (slot 1 / slot 2 → corbeille physique) ──────────
        bool isBranchRule = rule.ruleTypeString == RuleType.Branch.ToString()
                         && !string.IsNullOrEmpty(rule.conditionB);
        if (isBranchRule)
            DrawBranchSlotAssignment(floor, night, nightIndex, bin, binIdx, ruleIdx, replaceKey, rule);

        // ── Formulaire de remplacement ────────────────────────────────────────
        if (!replaceLibraryEntryIndex.ContainsKey(replaceKey))
            replaceLibraryEntryIndex[replaceKey] = 0;

        bool libraryReady = libraryEntries.Count > 0;

        EditorGUILayout.LabelField("Remplacer par (bibliothèque) :", EditorStyles.miniLabel);

        using (new EditorGUI.DisabledScope(!libraryReady))
        {
            replaceLibraryEntryIndex[replaceKey] = EditorGUILayout.Popup(
                replaceLibraryEntryIndex[replaceKey], libraryEntryLabels, GUILayout.ExpandWidth(true));
        }

        int              repIdx   = Mathf.Clamp(replaceLibraryEntryIndex[replaceKey], 0, libraryEntries.Count - 1);
        RuleLibraryEntry repEntry = libraryReady ? libraryEntries[repIdx] : null;
        bool             repDouble = repEntry != null && IsDoubleConnector(repEntry);

        BinDesignerData repSecondaryBin = null;
        if (repDouble)
            repSecondaryBin = DrawSecondaryBinSelector(night, bin, replaceKey, replaceSecondaryBinIndex);

        // Aperçu du remplacement
        if (repEntry != null)
        {
            string repPreview = BuildDoublePreview(repEntry, bin.binID, repSecondaryBin?.binID ?? "?");
            GUIStyle miniPreview = new GUIStyle(EditorStyles.miniLabel) { wordWrap = true, fontStyle = FontStyle.Italic };
            EditorGUILayout.LabelField($"→ {repPreview}", miniPreview);
        }

        bool canReplace = libraryReady && (!repDouble || repSecondaryBin != null);

        EditorGUILayout.BeginHorizontal();
        GUILayout.FlexibleSpace();
        GUI.backgroundColor = new Color(1f, 0.75f, 0.2f);
        using (new EditorGUI.DisabledScope(!canReplace))
        {
            if (GUILayout.Button("↕ Remplacer", GUILayout.Width(110f)))
            {
                // Remplace la règle courante sur ce bin par la règle primaire du nouvel entry.
                ApplyLibraryEntryToBin(repEntry, bin, rule, isPrimary: true);

                // Si double : ajoute aussi la règle secondaire sur l'autre corbeille.
                if (repDouble && repSecondaryBin != null)
                {
                    DesignerRuleEntry secondary = new DesignerRuleEntry();
                    ApplyLibraryEntryToBin(repEntry, repSecondaryBin, secondary, isPrimary: false);
                    repSecondaryBin.rules.Add(secondary);
                }

                floor.isDirty = true;
            }
        }
        GUI.backgroundColor = Color.white;
        EditorGUILayout.EndHorizontal();

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Dessine le formulaire d'ajout de règle au bas d'un panneau de corbeille.
    ///
    /// Pour les règles simples (condition seule, Et) : un seul popup bibliothèque suffit.
    /// Pour les règles doubles (Ou, Sauf) : affiche en plus un sélecteur de corbeille secondaire
    /// parmi les autres corbeilles de la même nuit. Au clic Ajouter :
    ///   - Ou  : condA → corbeille courante  /  condB → corbeille secondaire
    ///   - Sauf: condA sans condB → corbeille courante  /  condA+condB → corbeille secondaire
    ///
    /// Inclut un bouton [↻] pour recharger la bibliothèque sans fermer la fenêtre.
    /// </summary>
    private void DrawAddRuleForm(FloorDesignerData floor, NightDesignerData night, int nightIndex,
                                 BinDesignerData bin, int binIdx, int stateKey)
    {
        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("── Ajouter depuis la bibliothèque ──", EditorStyles.boldLabel);
        if (GUILayout.Button("↻", GUILayout.Width(24f)))
            LoadRuleLibrary();
        EditorGUILayout.EndHorizontal();

        bool libraryReady = libraryEntries.Count > 0;

        if (!libraryReady)
        {
            EditorGUILayout.HelpBox(
                "Aucune règle trouvée dans la bibliothèque.\nOuvrez Tools → Rule Library pour créer des règles.",
                MessageType.Warning);
            return;
        }

        if (!addLibraryEntryIndex.ContainsKey(stateKey))
            addLibraryEntryIndex[stateKey] = 0;

        addLibraryEntryIndex[stateKey] = EditorGUILayout.Popup(
            "Règle", addLibraryEntryIndex[stateKey], libraryEntryLabels);

        int              selectedIdx   = Mathf.Clamp(addLibraryEntryIndex[stateKey], 0, libraryEntries.Count - 1);
        RuleLibraryEntry selectedEntry = libraryEntries[selectedIdx];
        bool             isDouble      = IsDoubleConnector(selectedEntry);

        // ── Sélecteur de corbeille secondaire (Ou / Sauf uniquement) ─────────
        BinDesignerData secondaryBin = null;

        if (isDouble)
        {
            secondaryBin = DrawSecondaryBinSelector(night, bin, stateKey, addSecondaryBinIndex);
        }

        // ── Aperçu ────────────────────────────────────────────────────────────
        string bin1Label = bin.binID;
        string bin2Label = secondaryBin != null ? secondaryBin.binID : "?";
        string preview   = BuildDoublePreview(selectedEntry, bin1Label, bin2Label);

        GUIStyle previewStyle = new GUIStyle(EditorStyles.helpBox) { wordWrap = true, fontStyle = FontStyle.Italic };
        EditorGUILayout.LabelField($"Aperçu : {preview}", previewStyle);

        // ── Bouton Ajouter ────────────────────────────────────────────────────
        bool canAdd = !isDouble || secondaryBin != null;

        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        using (new EditorGUI.DisabledScope(!canAdd))
        {
            if (GUILayout.Button("+ Ajouter la règle"))
            {
                AddRuleFromLibraryEntry(selectedEntry, bin, secondaryBin, floor);
            }
        }
        GUI.backgroundColor = Color.white;

        if (isDouble && secondaryBin == null)
            EditorGUILayout.HelpBox("Sélectionnez une corbeille secondaire pour cette règle.", MessageType.Info);
    }

    /// <summary>
    /// Builds a structured French label from a DesignerRuleEntry's fields.
    /// In prefab mode: shows the prefab name and the target bin.
    /// In condition mode: shows the rule logic.
    /// </summary>
    private static string BuildStructuredLabel(DesignerRuleEntry rule)
    {
        string bin = string.IsNullOrEmpty(rule.targetBinID) ? "?" : rule.targetBinID;

        // Prefab mode: the prefabPath is stored in conditionA for display purposes.
        if (!string.IsNullOrEmpty(rule.conditionA) && rule.conditionA.StartsWith("Assets/"))
        {
            string prefabName = System.IO.Path.GetFileNameWithoutExtension(rule.conditionA);
            return $"📦 {prefabName} → {bin}";
        }

        string a = string.IsNullOrEmpty(rule.conditionA) ? "?" : rule.conditionA;
        string b = string.IsNullOrEmpty(rule.conditionB) ? "?" : rule.conditionB;

        if (!System.Enum.TryParse(rule.ruleTypeString, out RuleType type))
            return $"{a} → {bin}";

        return type switch
        {
            RuleType.Simple   => $"Si {a} → {bin}",
            RuleType.Multiple => $"Si {a} et {b} → {bin}",
            RuleType.Branch   => $"Si {a} sauf si {b} → {bin}",
            _                 => $"{a} → {bin}"
        };
    }

    /// <summary>
    /// Construit le texte d'affichage d'une règle à partir des champs structurés résolus
    /// (ruleType, conditionA, conditionB, binID).
    /// Utilisé par <see cref="ApplyLibraryEntryToBin"/> pour garantir que le texte affiché
    /// correspond toujours à la logique réelle de la règle issue de la Rule Library.
    /// </summary>
    private static string BuildStructuredDisplayText(RuleType ruleType, string condA, string condB, string binID)
    {
        string a = string.IsNullOrEmpty(condA) ? "?" : condA;
        string b = string.IsNullOrEmpty(condB) ? "?" : condB;

        return ruleType switch
        {
            RuleType.Simple   => $"Si le document contient {a}, posez-le ici",
            RuleType.Multiple => $"Si le document contient {a} et {b}, posez-le ici",
            RuleType.Branch   => $"Si le document contient {a} mais pas {b}, posez-le ici",
            _                 => "Posez le document ici"
        };
    }

    /// <summary>
    /// Construit un texte d'aperçu lisible pour une entrée de la bibliothèque.
    /// Mode prefab : affiche "📦 nomPrefab → binID".
    /// Mode structuré : utilise le manuscriptText si disponible, sinon génère depuis les conditions.
    /// </summary>
    private string BuildLibraryEntryPreview(RuleLibraryEntry entry, string binID)
    {
        // Mode prefab.
        if (!string.IsNullOrEmpty(entry.prefabPath))
        {
            string prefabName = System.IO.Path.GetFileNameWithoutExtension(entry.prefabPath);
            return $"📦 {prefabName} → {binID}";
        }

        // Mode manuscrit.
        if (!string.IsNullOrEmpty(entry.manuscriptText))
            return entry.manuscriptText;

        if (entry.conditions == null || entry.conditions.Count == 0)
            return $"{entry.label} → {binID}";

        if (entry.conditions.Count == 1)
            return $"Si {entry.conditions[0].specificity} → {binID}";

        string condA      = entry.conditions[0].specificity;
        string connector  = entry.conditions[0].connector;
        string condB      = entry.conditions[1].specificity;

        return connector switch
        {
            "Et"   => $"Si {condA} et {condB} → {binID}",
            "Ou"   => $"Si {condA} ou {condB} → {binID} / corbeille 2",
            "Sauf" => $"Si {condA} sauf si {condB} → {binID} / corbeille 2",
            _      => $"Si {condA} [{connector}] {condB} → {binID}"
        };
    }

    // ─── Branch slot assignment ───────────────────────────────────────────────

    /// <summary>
    /// Dessine deux dropdowns sous une règle Branch pour laisser le designer assigner
    /// quel bin physique correspond à "Corbeille 1" (prefab A) et "Corbeille 2" (prefab B).
    ///
    /// Les dropdowns listent tous les bins actifs de la nuit avec leurs noms positionnels.
    /// La sélection est stockée directement dans rule.branchSlot1BinID et rule.branchSlot2BinID.
    /// Un avertissement s'affiche si les deux slots pointent vers le même bin.
    /// </summary>
    private void DrawBranchSlotAssignment(
        FloorDesignerData floor,
        NightDesignerData night,
        int               nightIndex,
        BinDesignerData   bin,
        int               binIdx,
        int               ruleIdx,
        int               stateKey,
        DesignerRuleEntry rule)
    {
        // Build arrays from active bins.
        string[] binIDs      = new string[night.bins.Count];
        string[] binLabels   = new string[night.bins.Count];
        for (int i = 0; i < night.bins.Count; i++)
        {
            binIDs[i]    = night.bins[i].binID;
            binLabels[i] = FloorDesignerData.GetBinDisplayName(night.bins[i].binID);
        }

        int slot1Key = stateKey;
        int slot2Key = stateKey + 50000; // distinct key space from replaceSecondaryBinIndex

        // Initialise dropdown indices from stored values.
        if (!branchSlot1DropdownIndex.ContainsKey(slot1Key))
        {
            int stored = Array.IndexOf(binIDs, rule.branchSlot1BinID);
            branchSlot1DropdownIndex[slot1Key] = stored >= 0 ? stored : 0;
        }

        if (!branchSlot2DropdownIndex.ContainsKey(slot2Key))
        {
            int stored = Array.IndexOf(binIDs, rule.branchSlot2BinID);
            branchSlot2DropdownIndex[slot2Key] = stored >= 0 ? stored : (binIDs.Length > 1 ? 1 : 0);
        }

        EditorGUILayout.Space(2f);
        EditorGUILayout.BeginVertical(new GUIStyle(EditorStyles.helpBox)
        {
            padding = new RectOffset(6, 6, 4, 4)
        });

        GUIStyle slotLabel = new GUIStyle(EditorStyles.miniLabel) { fontStyle = FontStyle.Bold };
        EditorGUILayout.LabelField("Assignation des corbeilles (Branch)", slotLabel);

        // Slot 1 — Prefab A
        EditorGUI.BeginChangeCheck();
        branchSlot1DropdownIndex[slot1Key] = EditorGUILayout.Popup(
            "Corbeille 1 (Prefab A)",
            branchSlot1DropdownIndex[slot1Key],
            binLabels);
        if (EditorGUI.EndChangeCheck())
        {
            int idx = Mathf.Clamp(branchSlot1DropdownIndex[slot1Key], 0, binIDs.Length - 1);
            rule.branchSlot1BinID = binIDs[idx];
            floor.isDirty = true;
        }

        // Slot 2 — Prefab B
        EditorGUI.BeginChangeCheck();
        branchSlot2DropdownIndex[slot2Key] = EditorGUILayout.Popup(
            "Corbeille 2 (Prefab B)",
            branchSlot2DropdownIndex[slot2Key],
            binLabels);
        if (EditorGUI.EndChangeCheck())
        {
            int idx = Mathf.Clamp(branchSlot2DropdownIndex[slot2Key], 0, binIDs.Length - 1);
            rule.branchSlot2BinID = binIDs[idx];
            floor.isDirty = true;
        }

        // Avertissement si les deux slots pointent vers le même bin.
        if (rule.branchSlot1BinID == rule.branchSlot2BinID && !string.IsNullOrEmpty(rule.branchSlot1BinID))
        {
            EditorGUILayout.HelpBox(
                "Corbeille 1 et Corbeille 2 pointent vers le même bin.\n" +
                "Assignez deux corbeilles différentes.",
                MessageType.Warning);
        }

        EditorGUILayout.EndVertical();
    }

    // ─── Trash bin section ────────────────────────────────────────────────────

    /// <summary>
    /// Dessine la section "Corbeille Poubelle" en bas d'un panneau de nuit.
    ///
    /// Un toggle active ou désactive la poubelle (5e corbeille, bas-centre).
    /// Quand elle est active, une liste de prefabs peut être assignée ; chaque prefab
    /// sélectionné sera injecté dans le pool de spawn de cette nuit en plus des
    /// prefabs des corbeilles normales.
    /// Seuls les prefabs de la Rule Library absents des règles de la nuit sont proposés.
    /// </summary>
    private void DrawTrashBinSection(FloorDesignerData floor, NightDesignerData night, int nightIndex)
    {
        EditorGUILayout.BeginVertical(EditorStyles.helpBox);

        // Toggle d'activation
        EditorGUI.BeginChangeCheck();
        bool newHasTrash = EditorGUILayout.ToggleLeft("🗑 Corbeille Poubelle (bas-centre)", night.hasTrashedPrefab, EditorStyles.boldLabel);
        if (EditorGUI.EndChangeCheck())
        {
            night.hasTrashedPrefab = newHasTrash;
            if (!newHasTrash)
                night.trashedPrefabPaths.Clear();
            floor.isDirty = true;
        }

        if (!night.hasTrashedPrefab)
        {
            EditorGUILayout.EndVertical();
            return;
        }

        // Collecte des prefabs libres (non assignés aux corbeilles normales de cette nuit).
        List<string> freePrefabPaths = GetFreePrefabPaths(night);

        if (freePrefabPaths.Count == 0)
        {
            EditorGUILayout.HelpBox(
                "Tous les prefabs sont déjà assignés à une corbeille normale.\n" +
                "Ajoutez un prefab supplémentaire dans la Rule Library.",
                MessageType.Warning);
            EditorGUILayout.EndVertical();
            return;
        }

        EditorGUILayout.LabelField("Documents poubelle :", EditorStyles.boldLabel);

        // Liste des prefabs déjà assignés à la poubelle — avec bouton de suppression.
        for (int i = night.trashedPrefabPaths.Count - 1; i >= 0; i--)
        {
            string assignedPath = night.trashedPrefabPaths[i];
            string label        = $"📦 {System.IO.Path.GetFileNameWithoutExtension(assignedPath)}";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label);

            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button("✕", GUILayout.Width(28f)))
            {
                night.trashedPrefabPaths.RemoveAt(i);
                floor.isDirty = true;
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // Prefabs libres non encore assignés à la poubelle.
        List<string> availablePaths = new List<string>();
        foreach (string path in freePrefabPaths)
        {
            if (!night.trashedPrefabPaths.Contains(path))
                availablePaths.Add(path);
        }

        if (availablePaths.Count > 0)
        {
            string[] dropdownLabels = new string[availablePaths.Count + 1];
            dropdownLabels[0] = "— Ajouter un prefab poubelle —";
            for (int i = 0; i < availablePaths.Count; i++)
                dropdownLabels[i + 1] = $"📦 {System.IO.Path.GetFileNameWithoutExtension(availablePaths[i])}";

            if (!trashPrefabDropdownIndex.ContainsKey(nightIndex))
                trashPrefabDropdownIndex[nightIndex] = 0;

            EditorGUI.BeginChangeCheck();
            trashPrefabDropdownIndex[nightIndex] = EditorGUILayout.Popup(
                trashPrefabDropdownIndex[nightIndex],
                dropdownLabels);

            if (EditorGUI.EndChangeCheck() && trashPrefabDropdownIndex[nightIndex] > 0)
            {
                int selectedIdx = trashPrefabDropdownIndex[nightIndex] - 1;
                night.trashedPrefabPaths.Add(availablePaths[selectedIdx]);
                trashPrefabDropdownIndex[nightIndex] = 0;
                floor.isDirty = true;
            }
        }
        else
        {
            EditorGUILayout.HelpBox("Tous les prefabs libres ont été ajoutés.", MessageType.Info);
        }

        EditorGUILayout.EndVertical();
    }

    /// <summary>
    /// Retourne la liste des prefabPaths présents dans la Rule Library
    /// qui NE figurent dans AUCUNE règle déjà assignée aux corbeilles de la nuit donnée.
    ///
    /// Un prefabPath est considéré "utilisé" si conditionA d'au moins une règle
    /// dans n'importe quelle BinDesignerData de la nuit lui est égal.
    /// </summary>
    private List<string> GetFreePrefabPaths(NightDesignerData night)
    {
        // Collecte des paths déjà assignés à cette nuit.
        HashSet<string> usedPaths = new HashSet<string>();
        foreach (BinDesignerData bin in night.bins)
        {
            foreach (DesignerRuleEntry rule in bin.rules)
            {
                // En mode prefab, conditionA stocke le prefabPath (commence par "Assets/").
                if (!string.IsNullOrEmpty(rule.conditionA) && rule.conditionA.StartsWith("Assets/"))
                    usedPaths.Add(rule.conditionA);
            }
        }

        // Tous les prefabs de la bibliothèque non présents dans usedPaths.
        List<string> free = new List<string>();
        foreach (RuleLibraryEntry entry in libraryEntries)
        {
            if (string.IsNullOrEmpty(entry.prefabPath))
                continue;

            if (!usedPaths.Contains(entry.prefabPath))
                free.Add(entry.prefabPath);
        }

        return free;
    }

    // ─── Double-connector helpers ─────────────────────────────────────────────

    /// <summary>
    /// Retourne true si l'entrée utilise un connecteur Ou ou Sauf — c'est-à-dire
    /// qu'elle génère deux règles sur deux corbeilles distinctes.
    /// Les entrées prefab ne sont jamais doubles.
    /// </summary>
    private static bool IsDoubleConnector(RuleLibraryEntry entry)
    {
        if (!string.IsNullOrEmpty(entry.prefabPath))
            return false;

        if (entry.conditions == null || entry.conditions.Count < 2)
            return false;

        string conn = entry.conditions[0].connector;
        return conn == "Ou" || conn == "Sauf";
    }

    /// <summary>
    /// Dessine un Popup "Corbeille secondaire" parmi les corbeilles de la même nuit,
    /// en excluant la corbeille courante.
    /// Retourne la <see cref="BinDesignerData"/> choisie, ou null si aucune autre corbeille.
    /// </summary>
    private BinDesignerData DrawSecondaryBinSelector(
        NightDesignerData night,
        BinDesignerData   currentBin,
        int               stateKey,
        Dictionary<int, int> indexDict)
    {
        // Corbeilles disponibles = toutes sauf la corbeille courante.
        List<BinDesignerData> otherBins   = new List<BinDesignerData>();
        List<string>          otherLabels = new List<string>();

        foreach (BinDesignerData b in night.bins)
        {
            if (b.binID == currentBin.binID)
                continue;
            otherBins.Add(b);
            otherLabels.Add(FloorDesignerData.GetBinDisplayName(b.binID));
        }

        if (otherBins.Count == 0)
        {
            EditorGUILayout.HelpBox("Aucune autre corbeille disponible dans cette nuit.", MessageType.Warning);
            return null;
        }

        if (!indexDict.ContainsKey(stateKey))
            indexDict[stateKey] = 0;

        indexDict[stateKey] = EditorGUILayout.Popup(
            "Corbeille secondaire", indexDict[stateKey], otherLabels.ToArray());

        int safeIdx = Mathf.Clamp(indexDict[stateKey], 0, otherBins.Count - 1);
        return otherBins[safeIdx];
    }

    /// <summary>
    /// Construit un texte d'aperçu lisible qui montre les deux branches d'une règle double.
    ///
    /// Ou   : "Si condA → bin1 | Si condB → bin2"
    /// Sauf : "Si condA (sans condB) → bin1 | Si condA + condB → bin2"
    /// Autres : délègue à BuildLibraryEntryPreview.
    /// </summary>
    private string BuildDoublePreview(RuleLibraryEntry entry, string bin1, string bin2)
    {
        if (!IsDoubleConnector(entry))
            return BuildLibraryEntryPreview(entry, bin1);

        string condA = entry.conditions[0].specificity;
        string condB = entry.conditions[1].specificity;
        string conn  = entry.conditions[0].connector;

        return conn switch
        {
            "Ou"   => $"Si {condA} → {bin1}  |  Si {condB} → {bin2}",
            "Sauf" => $"Si {condA} sans {condB} → {bin1}  |  Si {condA} + {condB} → {bin2}",
            _      => BuildLibraryEntryPreview(entry, bin1)
        };
    }

    /// <summary>
    /// Crée et enregistre les DesignerRuleEntry depuis une entrée de bibliothèque.
    ///
    /// Règle simple (condition seule, Et) : une règle sur <paramref name="primaryBin"/>.
    /// Règle double (Ou, Sauf)            : une règle primaire sur <paramref name="primaryBin"/>
    ///                                      + une règle secondaire sur <paramref name="secondaryBin"/>.
    /// </summary>
    private void AddRuleFromLibraryEntry(
        RuleLibraryEntry entry,
        BinDesignerData  primaryBin,
        BinDesignerData  secondaryBin,
        FloorDesignerData floor)
    {
        // Règle primaire → corbeille courante.
        DesignerRuleEntry primary = new DesignerRuleEntry();
        ApplyLibraryEntryToBin(entry, primaryBin, primary, isPrimary: true);
        primaryBin.rules.Add(primary);

        // Règle secondaire → corbeille secondaire (Ou/Sauf uniquement).
        if (IsDoubleConnector(entry) && secondaryBin != null)
        {
            DesignerRuleEntry secondary = new DesignerRuleEntry();
            ApplyLibraryEntryToBin(entry, secondaryBin, secondary, isPrimary: false);
            secondaryBin.rules.Add(secondary);
        }

        floor.isDirty = true;
    }

    /// <summary>
    /// Traduit une <see cref="RuleLibraryEntry"/> en une <see cref="DesignerRuleEntry"/>
    /// et l'applique à la corbeille cible.
    ///
    /// Mode prefab : conditionA reçoit le prefabPath ; ruleType = Simple.
    /// Mode conditions : résolution selon le connecteur (Et / Ou / Sauf).
    /// </summary>
    private void ApplyLibraryEntryToBin(
        RuleLibraryEntry  entry,
        BinDesignerData   bin,
        DesignerRuleEntry target,
        bool              isPrimary = true)
    {
        // ── Mode prefab ───────────────────────────────────────────────────────
        if (!string.IsNullOrEmpty(entry.prefabPath))
        {
            string prefabName = System.IO.Path.GetFileNameWithoutExtension(entry.prefabPath);
            target.ruleTypeString = RuleType.Simple.ToString();
            // conditionA stores the prefab path so the runtime and save system can reference it.
            target.conditionA     = entry.prefabPath;
            target.conditionB     = string.Empty;
            target.targetBinID    = bin.binID;
            target.displayText    = $"📦 {prefabName} → {bin.binID}";
            target.complexity     = entry.complexity;
            target.isComplement   = false;
            return;
        }

        // ── Mode conditions (legacy) ──────────────────────────────────────────
        if (entry.conditions == null || entry.conditions.Count == 0)
        {
            target.ruleTypeString = RuleType.Simple.ToString();
            target.conditionA     = string.Empty;
            target.conditionB     = string.Empty;
            target.targetBinID    = bin.binID;
            target.displayText    = string.IsNullOrEmpty(entry.manuscriptText) ? entry.label : entry.manuscriptText;
            target.complexity     = entry.complexity;
            target.isComplement   = false;
            return;
        }

        string condA = entry.conditions[0].specificity;
        string condB = entry.conditions.Count > 1 ? entry.conditions[1].specificity : string.Empty;
        string conn  = entry.conditions.Count > 1 ? entry.conditions[0].connector   : string.Empty;

        RuleType resolvedType;
        string   usedCondA;
        string   usedCondB;

        switch (conn)
        {
            case "Et":
                resolvedType = RuleType.Multiple;
                usedCondA    = condA;
                usedCondB    = condB;
                break;

            case "Ou":
                resolvedType = RuleType.Simple;
                usedCondA    = isPrimary ? condA : condB;
                usedCondB    = string.Empty;
                break;

            case "Sauf":
                if (isPrimary)
                {
                    resolvedType = RuleType.Branch;
                    usedCondA    = condA;
                    usedCondB    = condB;
                }
                else
                {
                    resolvedType = RuleType.Multiple;
                    usedCondA    = condA;
                    usedCondB    = condB;
                }
                break;

            default:
                resolvedType = RuleType.Simple;
                usedCondA    = condA;
                usedCondB    = string.Empty;
                break;
        }

        string displayText = BuildStructuredDisplayText(resolvedType, usedCondA, usedCondB, bin.binID);

        target.ruleTypeString = resolvedType.ToString();
        target.conditionA     = usedCondA;
        target.conditionB     = usedCondB;
        target.targetBinID    = bin.binID;
        target.displayText    = displayText;
        target.complexity     = entry.complexity;
        target.isComplement   = false;
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
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: false,
                database: cachedSpecificityDatabase, libraryEntries: libraryEntries);
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
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: true,
                database: cachedSpecificityDatabase, libraryEntries: libraryEntries);
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
    /// Distributes specificities from the cached SpecificityDatabase across all 5 nights
    /// of the given floor. Each night receives rulesPerBin specificities (one per primary rule
    /// the generator will create). Specificities are drawn without replacement across nights
    /// to avoid repetition — night 2 never reuses a specificity already assigned to night 1.
    /// When the database runs out of unused specificities, the pool is recycled from the start.
    /// This method is called once at floor creation time so every night immediately has a
    /// concrete starting rule set the designer can review and modify without entering Play mode.
    /// Does nothing if the database is not loaded or contains no specificities.
    /// </summary>
    /// <param name="floor">The floor whose nights will receive auto-assigned specificities.</param>
    private void AutoAssignPinnedSpecificities(FloorDesignerData floor)
    {
        if (cachedSpecificityDatabase == null ||
            cachedSpecificityDatabase.allSpecificities == null ||
            cachedSpecificityDatabase.allSpecificities.Count == 0)
            return;

        // Shuffle a copy of the pool so each floor gets a different starting assignment.
        List<string> pool = new List<string>(cachedSpecificityDatabase.allSpecificities);
        ShuffleList(pool);

        int poolIndex = 0;

        foreach (NightDesignerData night in floor.nights)
        {
            night.pinnedSpecificities.Clear();

            // Assign one specificity per primary rule slot — rulesPerBin primary rules per bin,
            // times numberOfBins bins would be the full set, but we use rulesPerBin as the count
            // for simplicity so each bin has one clear conditionA to start with.
            int toAssign = Mathf.Clamp(night.rulesPerBin * night.numberOfBins, 1, pool.Count);

            for (int i = 0; i < toAssign; i++)
            {
                // Recycle the pool when exhausted so every night gets a full assignment.
                if (poolIndex >= pool.Count)
                    poolIndex = 0;

                night.pinnedSpecificities.Add(pool[poolIndex]);
                poolIndex++;
            }
        }
    }

    /// <summary>
    /// Fisher–Yates shuffle for a List&lt;string&gt;.
    /// Used to randomise the specificity pool before distributing across nights
    /// so each floor creation produces a different default assignment.
    /// </summary>
    private static void ShuffleList(List<string> list)
    {
        System.Random rng = new System.Random();
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
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

        // Auto-assign pinned specificities so the designer sees a starting rule set immediately.
        AutoAssignPinnedSpecificities(newFloor);

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
    /// Calls InitializeNights so the floor immediately has 5 valid night entries,
    /// then auto-assigns pinned specificities from the database across all nights so
    /// the designer immediately sees a concrete rule set to review and modify.
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

        // Auto-assign pinned specificities so the designer sees a starting rule set immediately.
        AutoAssignPinnedSpecificities(baseFloor);

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
            FloorDesignerSaveUtils.SaveFloor(floor, overwrite: true,
                database: cachedSpecificityDatabase, libraryEntries: libraryEntries);
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
