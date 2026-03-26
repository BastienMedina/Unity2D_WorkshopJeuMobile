using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
    private const int   MaxConditions    = 6;

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

    /// <summary>Specificity names loaded from the SpecificityDatabase for dropdown widgets.</summary>
    private string[] cachedSpecificityNames = Array.Empty<string>();

    /// <summary>Cached reference to the SpecificityDatabase ScriptableObject found in the project.</summary>
    private SpecificityDatabase cachedDatabase;

    /// <summary>
    /// Number of bins available in the current level configuration.
    /// Used to build the random pool when assigning bins to slots.
    /// Configurable by the designer directly in the window toolbar.
    /// </summary>
    private int numberOfBins = 3;

    /// <summary>Labels for the two bin slots shown in the terminal-node dropdown.</summary>
    private static readonly string[] BinSlotLabels = { "Corbeille 1", "Corbeille 2" };

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

    /// <summary>Logical connector options shown between condition nodes.</summary>
    private static readonly string[] ConnectorOptions = { "Et", "Ou", "Sauf" };

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
        RefreshSpecificityCache();
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

        if (GUILayout.Button("↻ Base", EditorStyles.toolbarButton, GUILayout.Width(52f)))
            RefreshSpecificityCache();

        // Bin count field so the designer can match the current level configuration.
        EditorGUILayout.LabelField("Corbeilles :", GUILayout.Width(66f));
        numberOfBins = EditorGUILayout.IntField(numberOfBins, GUILayout.Width(28f));
        numberOfBins = Mathf.Clamp(numberOfBins, 2, 8);

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
            List<RuleLibraryEntry> group = allEntries
                .Where(e => e.ruleTypeString == ruleType.ToString())
                .ToList();

            if (group.Count == 0)
                continue;

            DrawGroupHeader(GetFriendlyTypeName(ruleType));

            foreach (RuleLibraryEntry entry in group)
                DrawEntryRow(entry);
        }

        // Entries with no type assigned yet fall into an "uncategorised" group.
        List<RuleLibraryEntry> untyped = allEntries
            .Where(e => string.IsNullOrEmpty(e.ruleTypeString))
            .ToList();

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
            DrawStructuredEditor();

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

    /// <summary>Draws the toggle that switches between manuscript and structured modes.</summary>
    private void DrawAuthoringModeToggle()
    {
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Mode d'édition", GUILayout.Width(EditorGUIUtility.labelWidth));

        bool newIsManuscript = GUILayout.Toggle(editingEntry.isManuscript,
                                                " ✎ Manuscrit",
                                                EditorStyles.miniButtonLeft,
                                                GUILayout.Height(22f));
        bool newIsStructured = GUILayout.Toggle(!editingEntry.isManuscript,
                                                " ⚙ Structuré",
                                                EditorStyles.miniButtonRight,
                                                GUILayout.Height(22f));

        bool toggledToManuscript = newIsManuscript && !editingEntry.isManuscript;
        bool toggledToStructured = newIsStructured  &&  editingEntry.isManuscript;

        if (toggledToManuscript)
        {
            editingEntry.isManuscript = true;
            MarkDirty();
        }
        else if (toggledToStructured)
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

    // ─── Structured editor ────────────────────────────────────────────────────

    /// <summary>
    /// Draws the structured condition-chain editor.
    /// The designer builds a sequence of (spécificité → connecteur → ...) nodes,
    /// ending with a corbeille cible. A "Sauf" connector opens a secondary bin field.
    /// </summary>
    private void DrawStructuredEditor()
    {
        EditorGUILayout.LabelField("Conditions structurées", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Construisez la règle condition par condition.\n" +
            "Chaque ligne : [Spécificité]  →  [Connecteur vers la suivante]\n" +
            "La dernière ligne définit la corbeille cible.\n" +
            "Un connecteur « Sauf » ouvre une corbeille alternative.",
            MessageType.None);

        EditorGUILayout.Space(4f);

        EnsureAtLeastOneCondition();

        for (int i = 0; i < editingEntry.conditions.Count; i++)
            DrawConditionNode(i);

        EditorGUILayout.Space(4f);
        DrawConditionListControls();
        EditorGUILayout.Space(6f);
        DrawSecondaryBinField();
        EditorGUILayout.Space(8f);
        DrawBinResolutionSection();
    }

    /// <summary>Guarantees the condition list is never empty when the structured editor is active.</summary>
    private void EnsureAtLeastOneCondition()
    {
        if (editingEntry.conditions.Count == 0)
        {
            editingEntry.conditions.Add(new ConditionNode());
            MarkDirty();
        }
    }

    /// <summary>
    /// Draws one condition node row:
    ///   [Index label] [Spécificité dropdown] [Connecteur dropdown (non-terminal)] [Corbeille (terminal)]
    /// </summary>
    private void DrawConditionNode(int index)
    {
        ConditionNode node = editingEntry.conditions[index];
        bool isLastNode    = index == editingEntry.conditions.Count - 1;

        EditorGUILayout.BeginHorizontal();

        // Index label
        EditorGUILayout.LabelField($"#{index + 1}", GUILayout.Width(26f));

        // Specificity dropdown
        DrawSpecificityDropdown(node);

        if (!isLastNode)
        {
            // Connector dropdown between this node and the next.
            DrawConnectorDropdown(node);
        }
        else
        {
            // Target bin on the last node.
            DrawTargetBinField(node);
        }

        EditorGUILayout.EndHorizontal();

        // Show secondary bin hint when this non-last node has a "Sauf" connector.
        bool hasUnlessConnector = !isLastNode && node.connector == "Sauf";
        if (hasUnlessConnector)
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(30f);
            EditorGUILayout.HelpBox(
                "Le connecteur « Sauf » indique qu'une corbeille alternative peut recevoir " +
                "les documents qui ne satisfont PAS cette condition.\n" +
                "Définissez la corbeille alternative en bas du formulaire.",
                MessageType.Info);
            EditorGUILayout.EndHorizontal();
        }
    }

    /// <summary>Draws the specificity popup for one condition node.</summary>
    private void DrawSpecificityDropdown(ConditionNode node)
    {
        if (cachedSpecificityNames.Length == 0)
        {
            // No database found — fallback to a plain text field.
            string newSpec = EditorGUILayout.TextField(node.specificity, GUILayout.MinWidth(100f));
            if (newSpec != node.specificity)
            {
                node.specificity = newSpec;
                MarkDirty();
            }
            return;
        }

        int currentIndex = Array.IndexOf(cachedSpecificityNames, node.specificity);
        if (currentIndex < 0)
            currentIndex = 0;

        int newIndex = EditorGUILayout.Popup(currentIndex, cachedSpecificityNames,
                                             GUILayout.MinWidth(120f));
        if (newIndex != currentIndex || string.IsNullOrEmpty(node.specificity))
        {
            node.specificity = cachedSpecificityNames[newIndex];
            MarkDirty();
        }
    }

    /// <summary>Draws the logical connector dropdown (Et / Ou / Sauf) between two condition nodes.</summary>
    private void DrawConnectorDropdown(ConditionNode node)
    {
        int currentConnectorIndex = Array.IndexOf(ConnectorOptions, node.connector);
        if (currentConnectorIndex < 0)
            currentConnectorIndex = 0;

        int newConnectorIndex = EditorGUILayout.Popup(currentConnectorIndex, ConnectorOptions,
                                                      GUILayout.Width(60f));
        if (newConnectorIndex != currentConnectorIndex)
        {
            node.connector = ConnectorOptions[newConnectorIndex];
            MarkDirty();
        }
    }

    /// <summary>Draws the target bin slot popup for the terminal condition node.</summary>
    private void DrawTargetBinField(ConditionNode node)
    {
        EditorGUILayout.LabelField("→", GUILayout.Width(16f));

        // Slot index: 0 = Corbeille 1, 1 = Corbeille 2.
        int currentSlotIndex = Mathf.Clamp(node.targetBinSlot - 1, 0, BinSlotLabels.Length - 1);
        int newSlotIndex     = EditorGUILayout.Popup(currentSlotIndex, BinSlotLabels,
                                                     GUILayout.Width(106f));
        if (newSlotIndex != currentSlotIndex)
        {
            node.targetBinSlot = newSlotIndex + 1;
            MarkDirty();
        }
    }

    /// <summary>Draws "Add condition" and "Remove last condition" buttons.</summary>
    private void DrawConditionListControls()
    {
        EditorGUILayout.BeginHorizontal();

        bool canAdd    = editingEntry.conditions.Count < MaxConditions;
        bool canRemove = editingEntry.conditions.Count > 1;

        GUI.enabled = canAdd;
        if (GUILayout.Button("+ Ajouter une condition", EditorStyles.miniButton))
        {
            editingEntry.conditions.Add(new ConditionNode());
            MarkDirty();
        }

        GUI.enabled = canRemove;
        if (GUILayout.Button("− Retirer la dernière", EditorStyles.miniButton))
        {
            editingEntry.conditions.RemoveAt(editingEntry.conditions.Count - 1);
            MarkDirty();
        }

        GUI.enabled = true;
        EditorGUILayout.EndHorizontal();
    }

    /// <summary>
    /// Draws the secondary bin slot popup used when a "Sauf" connector is present in the chain.
    /// Documents that do not satisfy the negated condition route to this slot.
    /// </summary>
    private void DrawSecondaryBinField()
    {
        bool hasSaufConnector = editingEntry.conditions
            .Take(editingEntry.conditions.Count - 1)
            .Any(n => n.connector == "Sauf");

        if (!hasSaufConnector)
            return;

        EditorGUILayout.LabelField("Corbeille alternative (Sauf)", EditorStyles.boldLabel);

        int currentSlotIndex = Mathf.Clamp(editingEntry.secondaryBinSlot - 1, 0, BinSlotLabels.Length - 1);
        int newSlotIndex     = EditorGUILayout.Popup("Corbeille si « Sauf »",
                                                     currentSlotIndex, BinSlotLabels);
        if (newSlotIndex != currentSlotIndex)
        {
            editingEntry.secondaryBinSlot = newSlotIndex + 1;
            MarkDirty();
        }
    }

    /// <summary>
    /// Draws the bin resolution section: a "🎲 Assigner" button that randomly maps
    /// Corbeille 1 and Corbeille 2 to actual bin IDs, plus a read-only display of the result.
    /// The pool is built from numberOfBins consecutive bin IDs (bin_A, bin_B, …).
    /// Re-clicking the button re-rolls both assignments independently.
    /// </summary>
    private void DrawBinResolutionSection()
    {
        EditorGUILayout.LabelField("Assignation des corbeilles", EditorStyles.boldLabel);

        EditorGUILayout.BeginHorizontal();

        bool hasResolution = !string.IsNullOrEmpty(editingEntry.resolvedBin1);

        if (hasResolution)
        {
            // Read-only display of current resolved bins.
            GUI.enabled = false;
            EditorGUILayout.TextField("Corbeille 1", editingEntry.resolvedBin1);
            EditorGUILayout.TextField("Corbeille 2", editingEntry.resolvedBin2);
            GUI.enabled = true;
        }
        else
        {
            EditorGUILayout.HelpBox(
                "Cliquez « 🎲 Assigner » pour attribuer des corbeilles aléatoires.",
                MessageType.None);
        }

        EditorGUILayout.EndHorizontal();

        EditorGUILayout.BeginHorizontal();

        if (GUILayout.Button("🎲 Assigner aléatoirement", EditorStyles.miniButton))
        {
            ResolveRandomBins(editingEntry);
            MarkDirty();
        }

        if (hasResolution && GUILayout.Button("✕ Effacer", EditorStyles.miniButton, GUILayout.Width(60f)))
        {
            editingEntry.resolvedBin1 = string.Empty;
            editingEntry.resolvedBin2 = string.Empty;
            MarkDirty();
        }

        EditorGUILayout.EndHorizontal();
    }

    // ─── Entry management ─────────────────────────────────────────────────────

    /// <summary>
    /// Randomly picks two different bin IDs from the available pool (bin_A … bin_N)
    /// and stores them in resolvedBin1 and resolvedBin2 of the given entry.
    /// Pool size is determined by numberOfBins set in the toolbar.
    /// When fewer than 2 bins are available, resolvedBin2 falls back to resolvedBin1.
    /// </summary>
    private void ResolveRandomBins(RuleLibraryEntry entry)
    {
        List<string> pool = new List<string>();
        for (int i = 0; i < numberOfBins; i++)
            pool.Add(FloorDesignerData.GetBinID(i));

        int index1 = UnityEngine.Random.Range(0, pool.Count);
        entry.resolvedBin1 = pool[index1];
        pool.RemoveAt(index1);

        if (pool.Count == 0)
        {
            entry.resolvedBin2 = entry.resolvedBin1;
            return;
        }

        int index2 = UnityEngine.Random.Range(0, pool.Count);
        entry.resolvedBin2 = pool[index2];
    }

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

        selectedEntryGuid = entry.guid;
        editingEntry      = DeepCopyEntry(entry);
        hasPendingChanges = false;
        rightScrollPos    = Vector2.zero;
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

    // ─── Specificity cache ────────────────────────────────────────────────────

    /// <summary>
    /// Finds the first SpecificityDatabase asset in the project and caches its specificity names
    /// so condition-node dropdowns are populated without repeated asset queries.
    /// </summary>
    private void RefreshSpecificityCache()
    {
        string[] guids = AssetDatabase.FindAssets("t:SpecificityDatabase");

        if (guids.Length == 0)
        {
            cachedDatabase        = null;
            cachedSpecificityNames = Array.Empty<string>();
            return;
        }

        string assetPath = AssetDatabase.GUIDToAssetPath(guids[0]);
        cachedDatabase   = AssetDatabase.LoadAssetAtPath<SpecificityDatabase>(assetPath);

        if (cachedDatabase == null || cachedDatabase.allSpecificities == null)
        {
            cachedSpecificityNames = Array.Empty<string>();
            return;
        }

        cachedSpecificityNames = cachedDatabase.allSpecificities.ToArray();
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
    /// In structured mode: "Spec1 [connecteur] Spec2… → BinID".
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

        // Build a short summary from the condition chain.
        System.Text.StringBuilder sb = new System.Text.StringBuilder();
        for (int i = 0; i < entry.conditions.Count; i++)
        {
            ConditionNode node = entry.conditions[i];
            sb.Append(node.specificity);

            bool isLast = i == entry.conditions.Count - 1;
            if (isLast)
            {
                sb.Append($" → Corbeille {node.targetBinSlot}");
            }
            else
            {
                sb.Append(' ').Append(node.connector).Append(' ');
            }
        }

        string generated = sb.ToString();
        entry.label = generated.Length <= 48 ? generated : generated.Substring(0, 48) + "…";
    }

    /// <summary>
    /// Creates a full deep copy of a RuleLibraryEntry so the editing panel never
    /// mutates the persisted list directly until CommitChanges() is called.
    /// </summary>
    private static RuleLibraryEntry DeepCopyEntry(RuleLibraryEntry source)
    {
        RuleLibraryEntry copy = new RuleLibraryEntry
        {
            guid               = source.guid,
            label              = source.label,
            isManuscript       = source.isManuscript,
            manuscriptText     = source.manuscriptText,
            ruleTypeString     = source.ruleTypeString,
            complexity         = source.complexity,
            secondaryBinSlot   = source.secondaryBinSlot,
            resolvedBin1       = source.resolvedBin1,
            resolvedBin2       = source.resolvedBin2,
            conditions         = new List<ConditionNode>()
        };

        foreach (ConditionNode node in source.conditions)
        {
            copy.conditions.Add(new ConditionNode
            {
                specificity    = node.specificity,
                connector      = node.connector,
                targetBinSlot  = node.targetBinSlot
            });
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
