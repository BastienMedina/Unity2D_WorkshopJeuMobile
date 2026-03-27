using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;

/// <summary>
/// Unity EditorWindow that lets designers create, edit, and organise rules in a persistent library.
/// Left panel: scrollable list of all authored rules, grouped by RuleType.
/// Right panel: editing form with manuscript mode (free-text) or structured mode (condition chain).
/// Persists all entries to a JSON file so the library survives editor restarts.
/// Does NOT generate rules at runtime, does NOT reference MonoBehaviours, and does NOT modify the scene.
/// </summary>
public class RuleLibraryWindow : EditorWindow
{
    // ─── Menu entry ───────────────────────────────────────────────────────────

    [MenuItem("Tools/Rule Library")]
    public static void Open()
    {
        RuleLibraryWindow window = GetWindow<RuleLibraryWindow>("Rule Library");
        window.minSize = new Vector2(820f, 500f);
        window.Show();
    }

    // ─── Layout constants ─────────────────────────────────────────────────────

    private const float LeftPanelWidth   = 260f;
    private const float PanelPadding     = 8f;
    private const float EntryHeight      = 36f;
    private const float ComplexityWidth  = 32f;

    // ─── Persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Path to the JSON library file.
    /// Placed inside the project so it is version-controlled with the other designer data.
    /// </summary>
    private static string LibraryFilePath =>
        Path.Combine(Application.dataPath, "Editor", "RuleLibraryData.json");

    // ─── State ────────────────────────────────────────────────────────────────

    /// <summary>All rules currently loaded in the library, sorted by RuleType group.</summary>
    private List<RuleLibraryEntry> allEntries = new List<RuleLibraryEntry>();

    /// <summary>
    /// GUID of the rule currently selected for editing in the right panel.
    /// Null / empty means no selection — the right panel shows a placeholder.
    /// </summary>
    private string selectedEntryGuid = string.Empty;

    /// <summary>Scroll position of the left panel rule list.</summary>
    private Vector2 leftScrollPos;

    /// <summary>Scroll position of the right panel editor.</summary>
    private Vector2 rightScrollPos;

    // ─── Right panel working copy ──────────────────────────────────────────────

    /// <summary>
    /// Shallow working copy of the currently selected entry, edited in the right panel.
    /// Changes are not persisted to allEntries until the designer clicks "Sauvegarder".
    /// Kept as a separate object to support discard-changes behaviour.
    /// </summary>
    private RuleLibraryEntry editingEntry;

    /// <summary>True when the working copy differs from the persisted entry.</summary>
    private bool hasPendingChanges;

    // ─── Cached data ──────────────────────────────────────────────────────────

    /// <summary>
    /// Cached prefab GameObjects chargés depuis editingEntry.prefabPaths.
    /// La liste est synchronisée avec prefabPaths à chaque frame par SyncPrefabObjectsFromPaths.
    /// </summary>
    private List<GameObject> editingPrefabObjects = new List<GameObject>();

    /// <summary>
    /// Cached prefab GameObjects loaded from editingEntry.prefabBPaths (Prefab B list, Branch only).
    /// Rebuilt each time an entry is selected or when any path changes.
    /// </summary>
    private List<GameObject> editingPrefabBObjects = new List<GameObject>();

    /// <summary>All RuleType values displayed in the type dropdown.</summary>
    private static readonly RuleType[] PrimaryRuleTypes =
    {
        RuleType.Simple,
        RuleType.Multiple,
        RuleType.Branch,
    };

    /// <summary>Human-readable labels aligned with PrimaryRuleTypes array.</summary>
    private static readonly string[] RuleTypeLabels =
    {
        "Simple (contient A)",
        "Multiple (contient A et B)",
        "Branche (A mais pas B)",
    };

    // ─── Styles (created once per OnEnable) ───────────────────────────────────

    private GUIStyle groupHeaderStyle;
    private GUIStyle selectedEntryStyle;
    private GUIStyle normalEntryStyle;
    private GUIStyle complexityLabelStyle;
    private bool areStylesReady;

    // ─── Unity lifecycle ──────────────────────────────────────────────────────

    private void OnEnable()
    {
        LoadLibrary();
    }

    private void OnGUI()
    {
        EnsureStylesInitialized();

        EditorGUILayout.BeginHorizontal();

        DrawLeftPanel();

        // Thin vertical separator between the two panels.
        Rect separatorRect = EditorGUILayout.GetControlRect(false, GUILayout.Width(1f),
                                                             GUILayout.ExpandHeight(true));
        EditorGUI.DrawRect(separatorRect, new Color(0.12f, 0.12f, 0.12f));

        DrawRightPanel();

        EditorGUILayout.EndHorizontal();
    }

    // ─── Left panel ───────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the left panel: toolbar with "New Rule" button, then the grouped scrollable rule list.
    /// </summary>
    private void DrawLeftPanel()
    {
        EditorGUILayout.BeginVertical(GUILayout.Width(LeftPanelWidth));

        DrawLeftToolbar();

        leftScrollPos = EditorGUILayout.BeginScrollView(leftScrollPos, GUIStyle.none, GUI.skin.verticalScrollbar);
        DrawGroupedRuleList();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    /// <summary>Draws the "New Rule" and "Refresh DB" buttons at the top of the left panel.</summary>
    private void DrawLeftToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        if (GUILayout.Button("+ Nouvelle règle", EditorStyles.toolbarButton))
            CreateNewEntry();

        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Draws the rule list grouped by RuleType.
    /// Each group header is followed by the rules of that type in creation order.
    /// Groups with no entries are hidden to keep the list compact.
    /// </summary>
    private void DrawGroupedRuleList()
    {
        // Build groups: map each PrimaryRuleType to the entries that belong to it.
        foreach (RuleType ruleType in PrimaryRuleTypes)
        {
            List<RuleLibraryEntry> group = new List<RuleLibraryEntry>();
            foreach (RuleLibraryEntry e in allEntries)
            {
                if (e.ruleTypeString == ruleType.ToString())
                    group.Add(e);
            }

            if (group.Count == 0)
                continue;

            DrawGroupHeader(GetFriendlyTypeName(ruleType));

            foreach (RuleLibraryEntry entry in group)
                DrawEntryRow(entry);
        }

        // Entries with no type assigned yet fall into an "uncategorised" group.
        List<RuleLibraryEntry> untyped = new List<RuleLibraryEntry>();
        foreach (RuleLibraryEntry e in allEntries)
        {
            if (string.IsNullOrEmpty(e.ruleTypeString))
                untyped.Add(e);
        }

        if (untyped.Count > 0)
        {
            DrawGroupHeader("Sans type");
            foreach (RuleLibraryEntry entry in untyped)
                DrawEntryRow(entry);
        }

        // Extra bottom padding so the last entry is not clipped.
        GUILayout.Space(12f);
    }

    /// <summary>Draws a section header row for one rule type group.</summary>
    private void DrawGroupHeader(string label)
    {
        GUILayout.Space(4f);
        Rect headerRect = EditorGUILayout.GetControlRect(false, 20f);
        EditorGUI.DrawRect(headerRect, new Color(0.18f, 0.18f, 0.18f));
        EditorGUI.LabelField(headerRect, "  " + label, groupHeaderStyle);
    }

    /// <summary>Draws one clickable row in the rule list with its label and complexity badge.</summary>
    private void DrawEntryRow(RuleLibraryEntry entry)
    {
        bool isSelected = entry.guid == selectedEntryGuid;
        GUIStyle rowStyle = isSelected ? selectedEntryStyle : normalEntryStyle;

        Rect rowRect = EditorGUILayout.GetControlRect(false, EntryHeight);

        if (GUI.Button(rowRect, GUIContent.none, rowStyle))
            SelectEntry(entry);

        // Complexity badge on the right.
        Rect badgeRect = new Rect(rowRect.xMax - ComplexityWidth - 4f,
                                  rowRect.y + (rowRect.height - 18f) * 0.5f,
                                  ComplexityWidth, 18f);
        Color badgeColor = ComplexityColor(entry.complexity);
        EditorGUI.DrawRect(badgeRect, badgeColor);
        GUI.Label(badgeRect, entry.complexity.ToString(), complexityLabelStyle);

        // Entry label, clipped to avoid overlap with the badge.
        Rect labelRect = new Rect(rowRect.x + 6f, rowRect.y,
                                  rowRect.width - ComplexityWidth - 14f, rowRect.height);
        string displayLabel = string.IsNullOrEmpty(entry.label) ? "(sans nom)" : entry.label;
        string modePrefix   = entry.isManuscript ? "✎ " : "⚙ ";
        GUI.Label(labelRect, modePrefix + displayLabel, EditorStyles.label);
    }

    // ─── Right panel ──────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the right panel: editor form when a rule is selected, placeholder otherwise.
    /// All layout Begin/End pairs are always matched regardless of editingEntry state,
    /// which prevents Invalid GUILayout state errors when the entry is deleted mid-frame.
    /// </summary>
    private void DrawRightPanel()
    {
        EditorGUILayout.BeginVertical();

        if (editingEntry == null)
        {
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Sélectionnez ou créez une règle.",
                                       EditorStyles.centeredGreyMiniLabel);
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndVertical();
            return;
        }

        // Snapshot the reference so mid-frame nullification cannot reach the draw calls below.
        RuleLibraryEntry snapshot = editingEntry;

        DrawRightToolbar();

        // After the toolbar, editingEntry may have been nullified by Delete.
        // If so, close layouts cleanly and bail out.
        if (editingEntry == null)
        {
            EditorGUILayout.EndVertical();
            return;
        }

        rightScrollPos = EditorGUILayout.BeginScrollView(rightScrollPos);
        EditorGUILayout.BeginVertical(new GUIStyle { padding = new RectOffset(10, 10, 8, 8) });

        DrawSharedMetadata();
        EditorGUILayout.Space(8f);
        DrawAuthoringModeToggle();
        EditorGUILayout.Space(6f);

        if (snapshot.isManuscript)
            DrawManuscriptEditor();
        else
            DrawPrefabEditor();

        EditorGUILayout.EndVertical();
        EditorGUILayout.EndScrollView();

        EditorGUILayout.EndVertical();
    }

    /// <summary>Draws Save / Discard / Delete buttons at the top of the right panel.</summary>
    private void DrawRightToolbar()
    {
        EditorGUILayout.BeginHorizontal(EditorStyles.toolbar);

        GUI.enabled = hasPendingChanges;
        if (GUILayout.Button("✓ Sauvegarder", EditorStyles.toolbarButton))
            CommitChanges();

        if (GUILayout.Button("✕ Annuler", EditorStyles.toolbarButton))
            DiscardChanges();
        GUI.enabled = true;

        GUILayout.FlexibleSpace();

        GUI.backgroundColor = new Color(0.8f, 0.2f, 0.2f);
        if (GUILayout.Button("🗑 Supprimer", EditorStyles.toolbarButton, GUILayout.Width(88f)))
        {
            // Reset backgroundColor before calling Delete, which triggers a dialog
            // and may null editingEntry — any subsequent GUI call must see the default color.
            GUI.backgroundColor = Color.white;
            EditorGUILayout.EndHorizontal();
            DeleteSelectedEntry();
            return;
        }
        GUI.backgroundColor = Color.white;

        EditorGUILayout.EndHorizontal();
    }

    // ─── Shared metadata section ──────────────────────────────────────────────

    /// <summary>Draws the Label, RuleType dropdown, and Complexity slider shared by both modes.</summary>
    private void DrawSharedMetadata()
    {
        EditorGUILayout.LabelField("Métadonnées", EditorStyles.boldLabel);

        // Label
        string newLabel = EditorGUILayout.TextField("Nom de la règle", editingEntry.label);
        if (newLabel != editingEntry.label)
        {
            editingEntry.label = newLabel;
            MarkDirty();
        }

        // Rule type dropdown
        int currentTypeIndex = Array.FindIndex(PrimaryRuleTypes,
                                               t => t.ToString() == editingEntry.ruleTypeString);
        if (currentTypeIndex < 0)
            currentTypeIndex = 0;

        int newTypeIndex = EditorGUILayout.Popup("Type de règle", currentTypeIndex, RuleTypeLabels);
        if (newTypeIndex != currentTypeIndex)
        {
            editingEntry.ruleTypeString = PrimaryRuleTypes[newTypeIndex].ToString();
            MarkDirty();
        }

        // Complexity slider 1–5
        int newComplexity = EditorGUILayout.IntSlider("Complexité (1–5)", editingEntry.complexity, 1, 5);
        if (newComplexity != editingEntry.complexity)
        {
            editingEntry.complexity = newComplexity;
            MarkDirty();
        }
    }

    // ─── Authoring mode toggle ────────────────────────────────────────────────

    /// <summary>Draws the toggle that switches between manuscript and prefab modes.</summary>
    private void DrawAuthoringModeToggle()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Mode d'édition", GUILayout.Width(EditorGUIUtility.labelWidth));

        bool newIsManuscript = GUILayout.Toggle(editingEntry.isManuscript,
                                                " ✎ Manuscrit",
                                                EditorStyles.miniButtonLeft,
                                                GUILayout.Height(22f));
        bool newIsPrefab = GUILayout.Toggle(!editingEntry.isManuscript,
                                                " 📦 Prefab",
                                                EditorStyles.miniButtonRight,
                                                GUILayout.Height(22f));

        bool toggledToManuscript = newIsManuscript && !editingEntry.isManuscript;
        bool toggledToPrefab     = newIsPrefab     &&  editingEntry.isManuscript;

        if (toggledToManuscript)
        {
            editingEntry.isManuscript = true;
            MarkDirty();
        }
        else if (toggledToPrefab)
        {
            editingEntry.isManuscript = false;
            MarkDirty();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ─── Manuscript editor ────────────────────────────────────────────────────

    /// <summary>
    /// Draws the free-text manuscript editor with a large multi-line text area.
    /// The designer writes the rule as a natural-language sentence.
    /// </summary>
    private void DrawManuscriptEditor()
    {
        EditorGUILayout.LabelField("Texte manuscrit", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Écrivez la règle en langage naturel.\n" +
            "Ex : « Si le document contient un tampon rouge, le mettre dans Corbeille A. »",
            MessageType.None);

        string newText = EditorGUILayout.TextArea(editingEntry.manuscriptText,
                                                  GUILayout.MinHeight(100f));
        if (newText != editingEntry.manuscriptText)
        {
            editingEntry.manuscriptText = newText;
            MarkDirty();
        }
    }

    // ─── Prefab editor ────────────────────────────────────────────────────────

    /// <summary>
    /// Draws the prefab mode editor.
    ///
    /// Simple / Multiple : une liste de prefabs A (autant que souhaité) + dropdown "Corbeille 1 ou 2".
    ///                     Boutons "+ Ajouter un prefab" et "✕" par ligne pour gérer la liste.
    /// Branch            : liste de prefabs A + un Prefab B unique, chacun avec son slot.
    ///
    /// Les slots (1 ou 2) sont résolus en corbeilles physiques dans le Floor Designer.
    /// </summary>
    private void DrawPrefabEditor()
    {
        bool isBranch = editingEntry.ruleTypeString == RuleType.Branch.ToString();

        EditorGUILayout.LabelField("Prefab(s) du document", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            isBranch
                ? "Branche : assignez un ou plusieurs Prefabs A (condition principale) et un Prefab B.\n" +
                  "Chaque groupe est routé vers l'une des deux corbeilles du Floor Designer."
                : "Ajoutez un ou plusieurs prefabs pour cette règle.\n" +
                  "Tous les prefabs listés seront acceptés par la corbeille assignée et injectés dans le pool de spawn.",
            MessageType.None);

        EditorGUILayout.Space(4f);

        SyncPrefabObjectsFromPaths();

        // ── Liste des Prefabs A ───────────────────────────────────────────────
        EditorGUILayout.LabelField(isBranch ? "Prefabs A" : "Prefabs", EditorStyles.boldLabel);

        if (editingEntry.prefabPaths == null)
            editingEntry.prefabPaths = new List<string>();

        // Affiche chaque prefab de la liste avec un ObjectField + bouton ✕.
        for (int i = 0; i < editingEntry.prefabPaths.Count; i++)
        {
            // Assure que le cache des GameObjects est suffisamment grand.
            while (editingPrefabObjects.Count <= i)
                editingPrefabObjects.Add(null);

            string     storedPath = editingEntry.prefabPaths[i];
            GameObject cachedObj  = editingPrefabObjects[i];

            // Synchronise le cache si le path a changé.
            string cachedPath = cachedObj != null ? AssetDatabase.GetAssetPath(cachedObj) : string.Empty;
            if (cachedPath != storedPath)
            {
                cachedObj = string.IsNullOrEmpty(storedPath)
                    ? null
                    : AssetDatabase.LoadAssetAtPath<GameObject>(storedPath);
                editingPrefabObjects[i] = cachedObj;
            }

            EditorGUILayout.BeginHorizontal();

            // ObjectField pour ce prefab.
            EditorGUI.BeginChangeCheck();
            GameObject newPrefab = (GameObject)EditorGUILayout.ObjectField(
                $"Prefab {i + 1}", cachedObj, typeof(GameObject), allowSceneObjects: false);
            if (EditorGUI.EndChangeCheck())
            {
                editingPrefabObjects[i]    = newPrefab;
                editingEntry.prefabPaths[i] = newPrefab != null
                    ? AssetDatabase.GetAssetPath(newPrefab)
                    : string.Empty;
                // prefabPath miroir pour la rétrocompatibilité.
                if (i == 0)
                    editingEntry.prefabPath = editingEntry.prefabPaths[0];
                MarkDirty();
            }

            // Bouton suppression.
            GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
            if (GUILayout.Button("✕", GUILayout.Width(26f)))
            {
                editingEntry.prefabPaths.RemoveAt(i);
                editingPrefabObjects.RemoveAt(i);
                // prefabPath miroir.
                editingEntry.prefabPath = editingEntry.prefabPaths.Count > 0
                    ? editingEntry.prefabPaths[0]
                    : string.Empty;
                MarkDirty();
                GUI.backgroundColor = Color.white;
                EditorGUILayout.EndHorizontal();
                break; // Recalcule la liste au prochain frame.
            }
            GUI.backgroundColor = Color.white;

            EditorGUILayout.EndHorizontal();
        }

        // Dropdown "corbeille" partagé pour tous les Prefabs A.
        int currentSlotIdx = Mathf.Clamp(editingEntry.prefabASlot - 1, 0, SlotLabels.Length - 1);
        EditorGUI.BeginChangeCheck();
        int newSlotIdx = EditorGUILayout.Popup("Corbeille (slot)", currentSlotIdx, SlotLabels);
        if (EditorGUI.EndChangeCheck())
        {
            editingEntry.prefabASlot = newSlotIdx + 1;
            MarkDirty();
        }

        // Bouton "+ Ajouter un prefab".
        EditorGUILayout.Space(2f);
        GUI.backgroundColor = new Color(0.4f, 0.85f, 0.4f);
        if (GUILayout.Button("+ Ajouter un prefab"))
        {
            editingEntry.prefabPaths.Add(string.Empty);
            editingPrefabObjects.Add(null);
            MarkDirty();
        }
        GUI.backgroundColor = Color.white;

        // ── Prefab B (Branch uniquement) ──────────────────────────────────────
        if (isBranch)
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("Prefabs B", EditorStyles.boldLabel);

            if (editingEntry.prefabBPaths == null)
                editingEntry.prefabBPaths = new List<string>();

            // Affiche chaque prefab B avec un ObjectField + bouton ✕.
            for (int i = 0; i < editingEntry.prefabBPaths.Count; i++)
            {
                while (editingPrefabBObjects.Count <= i)
                    editingPrefabBObjects.Add(null);

                string     storedBPath = editingEntry.prefabBPaths[i];
                GameObject cachedBObj  = editingPrefabBObjects[i];

                // Synchronise le cache si le path a changé.
                string cachedBPath = cachedBObj != null ? AssetDatabase.GetAssetPath(cachedBObj) : string.Empty;
                if (cachedBPath != storedBPath)
                {
                    cachedBObj = string.IsNullOrEmpty(storedBPath)
                        ? null
                        : AssetDatabase.LoadAssetAtPath<GameObject>(storedBPath);
                    editingPrefabBObjects[i] = cachedBObj;
                }

                EditorGUILayout.BeginHorizontal();

                // ObjectField pour ce prefab B.
                EditorGUI.BeginChangeCheck();
                GameObject newBPrefab = (GameObject)EditorGUILayout.ObjectField(
                    $"Prefab B{i + 1}", cachedBObj, typeof(GameObject), allowSceneObjects: false);
                if (EditorGUI.EndChangeCheck())
                {
                    editingPrefabBObjects[i]      = newBPrefab;
                    editingEntry.prefabBPaths[i]  = newBPrefab != null
                        ? AssetDatabase.GetAssetPath(newBPrefab)
                        : string.Empty;
                    // prefabBPath miroir pour la rétrocompatibilité.
                    if (i == 0)
                        editingEntry.prefabBPath = editingEntry.prefabBPaths[0];
                    MarkDirty();
                }

                // Bouton suppression B.
                GUI.backgroundColor = new Color(1f, 0.35f, 0.35f);
                if (GUILayout.Button("✕", GUILayout.Width(26f)))
                {
                    editingEntry.prefabBPaths.RemoveAt(i);
                    editingPrefabBObjects.RemoveAt(i);
                    // prefabBPath miroir.
                    editingEntry.prefabBPath = editingEntry.prefabBPaths.Count > 0
                        ? editingEntry.prefabBPaths[0]
                        : string.Empty;
                    MarkDirty();
                    GUI.backgroundColor = Color.white;
                    EditorGUILayout.EndHorizontal();
                    break;
                }
                GUI.backgroundColor = Color.white;

                EditorGUILayout.EndHorizontal();
            }

            // Dropdown "corbeille" partagé pour tous les Prefabs B.
            int currentBSlotIdx = Mathf.Clamp(editingEntry.prefabBSlot - 1, 0, SlotLabels.Length - 1);
            EditorGUI.BeginChangeCheck();
            int newBSlotIdx = EditorGUILayout.Popup("Corbeille B (slot)", currentBSlotIdx, SlotLabels);
            if (EditorGUI.EndChangeCheck())
            {
                editingEntry.prefabBSlot = newBSlotIdx + 1;
                MarkDirty();
            }

            // Bouton "+ Ajouter un prefab B".
            EditorGUILayout.Space(2f);
            GUI.backgroundColor = new Color(0.4f, 0.65f, 0.95f);
            if (GUILayout.Button("+ Ajouter un prefab B"))
            {
                editingEntry.prefabBPaths.Add(string.Empty);
                editingPrefabBObjects.Add(null);
                MarkDirty();
            }
            GUI.backgroundColor = Color.white;

            if (editingEntry.prefabASlot == editingEntry.prefabBSlot)
            {
                EditorGUILayout.HelpBox(
                    "Prefabs A et Prefabs B sont assignés au même slot.\n" +
                    "Ils doivent être sur des corbeilles différentes.",
                    MessageType.Warning);
            }
        }
    }

    private static readonly string[] SlotLabels = { "Corbeille 1", "Corbeille 2" };

    /// <summary>
    /// Synchronise editingPrefabObjects et editingPrefabBObjects avec les paths stockés dans editingEntry.
    /// Assure que les listes de caches ont la bonne taille; les synchronisations individuelles se font inline.
    /// </summary>
    private void SyncPrefabObjectsFromPaths()
    {
        if (editingEntry.prefabPaths == null)
            editingEntry.prefabPaths = new List<string>();

        // Redimensionne la liste de caches A si nécessaire.
        while (editingPrefabObjects.Count < editingEntry.prefabPaths.Count)
            editingPrefabObjects.Add(null);
        while (editingPrefabObjects.Count > editingEntry.prefabPaths.Count)
            editingPrefabObjects.RemoveAt(editingPrefabObjects.Count - 1);

        // Migrate prefabBPath → prefabBPaths si nécessaire.
        if (editingEntry.prefabBPaths == null)
            editingEntry.prefabBPaths = new List<string>();

        if (editingEntry.prefabBPaths.Count == 0 && !string.IsNullOrEmpty(editingEntry.prefabBPath))
            editingEntry.prefabBPaths = new List<string> { editingEntry.prefabBPath };

        // Redimensionne la liste de caches B si nécessaire.
        while (editingPrefabBObjects.Count < editingEntry.prefabBPaths.Count)
            editingPrefabBObjects.Add(null);
        while (editingPrefabBObjects.Count > editingEntry.prefabBPaths.Count)
            editingPrefabBObjects.RemoveAt(editingPrefabBObjects.Count - 1);
    }

    // ─── Entry management ─────────────────────────────────────────────────────

    /// <summary>
    /// Creates a new blank entry, adds it to the library, selects it for editing,
    /// and immediately saves so it persists even if the window is closed without saving.
    /// </summary>
    private void CreateNewEntry()
    {
        RuleLibraryEntry newEntry = new RuleLibraryEntry
        {
            guid           = Guid.NewGuid().ToString(),
            label          = "Nouvelle règle",
            isManuscript   = false,
            ruleTypeString = RuleType.Simple.ToString(),
            complexity     = 1,
            conditions     = new List<ConditionNode> { new ConditionNode() }
        };

        allEntries.Add(newEntry);
        SaveLibrary();
        SelectEntry(newEntry);
        Repaint();
    }

    /// <summary>
    /// Selects an entry for editing by building a deep copy into editingEntry.
    /// Deep copy prevents live mutations of the persisted data until CommitChanges() is called.
    /// </summary>
    private void SelectEntry(RuleLibraryEntry entry)
    {
        // Warn about unsaved changes before switching selection.
        if (hasPendingChanges && !string.IsNullOrEmpty(selectedEntryGuid))
        {
            bool shouldDiscard = EditorUtility.DisplayDialog(
                "Modifications non sauvegardées",
                "Des modifications sont en cours. Les abandonner ?",
                "Oui, ignorer", "Non, rester");

            if (!shouldDiscard)
                return;
        }

        selectedEntryGuid    = entry.guid;
        editingEntry         = DeepCopyEntry(entry);
        editingPrefabObjects = new List<GameObject>();

        // Charge le cache des Prefabs A depuis prefabPaths (avec migration depuis prefabPath).
        if (editingEntry.prefabPaths == null || editingEntry.prefabPaths.Count == 0)
        {
            if (!string.IsNullOrEmpty(editingEntry.prefabPath))
                editingEntry.prefabPaths = new List<string> { editingEntry.prefabPath };
            else
                editingEntry.prefabPaths = new List<string>();
        }

        foreach (string p in editingEntry.prefabPaths)
        {
            editingPrefabObjects.Add(string.IsNullOrEmpty(p)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(p));
        }

        // Charge le cache des Prefabs B depuis prefabBPaths (avec migration depuis prefabBPath).
        editingPrefabBObjects = new List<GameObject>();

        if (editingEntry.prefabBPaths == null || editingEntry.prefabBPaths.Count == 0)
        {
            if (!string.IsNullOrEmpty(editingEntry.prefabBPath))
                editingEntry.prefabBPaths = new List<string> { editingEntry.prefabBPath };
            else
                editingEntry.prefabBPaths = new List<string>();
        }

        foreach (string p in editingEntry.prefabBPaths)
        {
            editingPrefabBObjects.Add(string.IsNullOrEmpty(p)
                ? null
                : AssetDatabase.LoadAssetAtPath<GameObject>(p));
        }

        hasPendingChanges    = false;
        rightScrollPos       = Vector2.zero;
        Repaint();
    }

    /// <summary>
    /// Applies the working copy (editingEntry) back into allEntries and saves to disk.
    /// Auto-generates a label from the conditions when the designer left it as default.
    /// </summary>
    private void CommitChanges()
    {
        int persistedIndex = allEntries.FindIndex(e => e.guid == editingEntry.guid);
        if (persistedIndex < 0)
            return;

        AutoFillLabel(editingEntry);
        allEntries[persistedIndex] = DeepCopyEntry(editingEntry);
        hasPendingChanges          = false;

        SaveLibrary();
        Repaint();
    }

    /// <summary>Discards working-copy changes by reloading the persisted version into editingEntry.</summary>
    private void DiscardChanges()
    {
        RuleLibraryEntry persisted = allEntries.Find(e => e.guid == selectedEntryGuid);
        if (persisted != null)
            editingEntry = DeepCopyEntry(persisted);

        hasPendingChanges = false;
        Repaint();
    }

    /// <summary>
    /// Removes the currently selected entry from the library after a confirmation dialog.
    /// Clears the right panel on confirmation.
    /// </summary>
    private void DeleteSelectedEntry()
    {
        bool confirmed = EditorUtility.DisplayDialog(
            "Supprimer la règle",
            $"Supprimer « {editingEntry.label} » définitivement ?",
            "Supprimer", "Annuler");

        if (!confirmed)
            return;

        allEntries.RemoveAll(e => e.guid == selectedEntryGuid);
        selectedEntryGuid = string.Empty;
        editingEntry      = null;
        hasPendingChanges = false;

        SaveLibrary();
        Repaint();
    }

    /// <summary>
    /// Marks the working copy as having unsaved changes.
    /// Called by every UI control that modifies editingEntry.
    /// </summary>
    private void MarkDirty()
    {
        hasPendingChanges = true;
        Repaint();
    }

    // ─── Persistence ──────────────────────────────────────────────────────────

    /// <summary>
    /// Serialises allEntries to the JSON library file.
    /// Logs an error when the write fails but does not throw — the window remains usable.
    /// </summary>
    private void SaveLibrary()
    {
        try
        {
            RuleLibraryFile file = new RuleLibraryFile { entries = allEntries };
            string json = JsonUtility.ToJson(file, prettyPrint: true);
            File.WriteAllText(LibraryFilePath, json);
            AssetDatabase.Refresh();
        }
        catch (Exception exception)
        {
            Debug.LogError($"[RuleLibrary] Failed to save library: {exception.Message}");
        }
    }

    /// <summary>
    /// Loads the library from the JSON file into allEntries.
    /// Starts with an empty list when no file exists yet (first use).
    /// </summary>
    private void LoadLibrary()
    {
        allEntries.Clear();

        if (!File.Exists(LibraryFilePath))
            return;

        try
        {
            string json = File.ReadAllText(LibraryFilePath);
            RuleLibraryFile file = JsonUtility.FromJson<RuleLibraryFile>(json);

            if (file?.entries != null)
                allEntries = file.entries;
        }
        catch (Exception exception)
        {
            Debug.LogError($"[RuleLibrary] Failed to load library: {exception.Message}");
        }
    }

    // ─── Helpers ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a short French-friendly label for a RuleType.
    /// Used as group section headers in the left panel.
    /// </summary>
    private static string GetFriendlyTypeName(RuleType ruleType)
    {
        return ruleType switch
        {
            RuleType.Simple   => "Simple (contient A)",
            RuleType.Multiple => "Multiple (contient A et B)",
            RuleType.Branch   => "Branche (A mais pas B)",
            _                 => ruleType.ToString()
        };
    }

    /// <summary>
    /// Returns a colour that visually encodes the difficulty level (green → red scale).
    /// </summary>
    private static Color ComplexityColor(int complexity)
    {
        return complexity switch
        {
            1 => new Color(0.18f, 0.55f, 0.18f),
            2 => new Color(0.35f, 0.60f, 0.20f),
            3 => new Color(0.75f, 0.65f, 0.05f),
            4 => new Color(0.80f, 0.40f, 0.10f),
            _ => new Color(0.75f, 0.15f, 0.10f)
        };
    }

    /// <summary>
    /// Fills in the entry label automatically when the designer left it as the default.
    /// In manuscript mode: first 40 characters of the text.
    /// In prefab mode: the prefab file name without extension.
    /// </summary>
    private static void AutoFillLabel(RuleLibraryEntry entry)
    {
        bool isDefaultLabel = string.IsNullOrEmpty(entry.label) ||
                              entry.label == "Nouvelle règle";
        if (!isDefaultLabel)
            return;

        if (entry.isManuscript)
        {
            string trimmed = entry.manuscriptText?.Trim() ?? string.Empty;
            entry.label = trimmed.Length <= 40 ? trimmed : trimmed.Substring(0, 40) + "…";
            return;
        }

        // Prefab mode: derive label from all prefab names joined with ", ".
        if (entry.prefabPaths != null && entry.prefabPaths.Count > 0)
        {
            var names = new System.Text.StringBuilder();
            foreach (string p in entry.prefabPaths)
            {
                if (string.IsNullOrEmpty(p)) continue;
                if (names.Length > 0) names.Append(", ");
                names.Append(System.IO.Path.GetFileNameWithoutExtension(p));
            }
            string combined = names.ToString();
            entry.label = combined.Length <= 48 ? combined : combined.Substring(0, 48) + "…";
            return;
        }

        // Legacy fallback: single prefabPath.
        if (!string.IsNullOrEmpty(entry.prefabPath))
        {
            string fileName = System.IO.Path.GetFileNameWithoutExtension(entry.prefabPath);
            entry.label = fileName.Length <= 48 ? fileName : fileName.Substring(0, 48) + "…";
        }
    }

    /// <summary>
    /// Creates a full deep copy of a RuleLibraryEntry so the editing panel never
    /// mutates the persisted list directly until CommitChanges() is called.
    /// </summary>
    private static RuleLibraryEntry DeepCopyEntry(RuleLibraryEntry source)
    {
        RuleLibraryEntry copy = new RuleLibraryEntry
        {
            guid             = source.guid,
            label            = source.label,
            isManuscript     = source.isManuscript,
            manuscriptText   = source.manuscriptText,
            ruleTypeString   = source.ruleTypeString,
            complexity       = source.complexity,
            // prefabPaths : copie profonde de la liste complète.
            prefabPaths      = source.prefabPaths != null
                ? new List<string>(source.prefabPaths)
                : new List<string>(),
            // prefabPath miroir du premier élément pour la rétrocompatibilité JSON.
            prefabPath       = source.prefabPaths != null && source.prefabPaths.Count > 0
                ? source.prefabPaths[0]
                : source.prefabPath,
            prefabASlot      = source.prefabASlot,
            // prefabBPaths : copie profonde de la liste complète.
            prefabBPaths     = source.prefabBPaths != null
                ? new List<string>(source.prefabBPaths)
                : new List<string>(),
            // prefabBPath miroir du premier élément pour la rétrocompatibilité JSON.
            prefabBPath      = source.prefabBPaths != null && source.prefabBPaths.Count > 0
                ? source.prefabBPaths[0]
                : source.prefabBPath,
            prefabBSlot      = source.prefabBSlot,
            secondaryBinSlot = source.secondaryBinSlot,
            resolvedBin1     = source.resolvedBin1,
            resolvedBin2     = source.resolvedBin2,
            conditions       = new List<ConditionNode>()
        };

        if (source.conditions != null)
        {
            foreach (ConditionNode node in source.conditions)
            {
                copy.conditions.Add(new ConditionNode
                {
                    specificity   = node.specificity,
                    connector     = node.connector,
                    targetBinSlot = node.targetBinSlot
                });
            }
        }

        return copy;
    }

    // ─── Style initialisation ─────────────────────────────────────────────────

    /// <summary>
    /// Creates all GUIStyles once and caches them.
    /// Called at the start of every OnGUI to guarantee the styles exist before any draw call.
    /// Guarded by areStylesReady so allocation happens only on first paint or after domain reload.
    /// </summary>
    private void EnsureStylesInitialized()
    {
        if (areStylesReady)
            return;

        groupHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleLeft,
            normal    = { textColor = new Color(0.75f, 0.75f, 0.75f) }
        };

        Texture2D selectedBg = MakeSolidTexture(new Color(0.24f, 0.37f, 0.58f));
        selectedEntryStyle = new GUIStyle
        {
            normal   = { background = selectedBg },
            padding  = new RectOffset(4, 4, 4, 4)
        };

        Texture2D normalBg = MakeSolidTexture(new Color(0.20f, 0.20f, 0.20f, 0f));
        normalEntryStyle = new GUIStyle
        {
            normal   = { background = normalBg },
            hover    = { background = MakeSolidTexture(new Color(0.25f, 0.25f, 0.25f)) },
            padding  = new RectOffset(4, 4, 4, 4)
        };

        complexityLabelStyle = new GUIStyle(EditorStyles.boldLabel)
        {
            fontSize  = 11,
            alignment = TextAnchor.MiddleCenter,
            normal    = { textColor = Color.white }
        };

        areStylesReady = true;
    }

    /// <summary>Creates a 1×1 solid-colour Texture2D for use as a GUIStyle background.</summary>
    private static Texture2D MakeSolidTexture(Color color)
    {
        Texture2D texture = new Texture2D(1, 1);
        texture.SetPixel(0, 0, color);
        texture.Apply();
        return texture;
    }
}
