using UnityEditor;
using UnityEngine;

/// <summary>
/// Custom Inspector for DocumentVisualizer.
/// - Displays each layer with its gizmo color swatch.
/// - Adds a "Focus" button per layer to frame the Scene view on that layer's Image.
/// - Adds a "Ping" button to highlight the Image asset in the Project window.
/// - Shows a live summary of how many sprite variants are wired per specificity.
/// </summary>
[CustomEditor(typeof(DocumentVisualizer))]
public class DocumentVisualizerEditor : Editor
{
    // ── Foldout state (per-layer) ─────────────────────────────────────────────
    private bool[] _foldouts;

    private SerializedProperty _layersProp;

    // Default gizmo colors assigned automatically when a layer is added.
    private static readonly Color[] DefaultLayerColors =
    {
        new Color(0.95f, 0.95f, 0.85f), // Feuille  — warm white
        new Color(0.90f, 0.20f, 0.20f), // Tampon   — red
        new Color(0.25f, 0.55f, 0.95f), // Texte    — blue
        new Color(0.25f, 0.85f, 0.45f), // Signature — green
        new Color(0.95f, 0.85f, 0.15f), // Pastille  — yellow
    };

    private void OnEnable()
    {
        _layersProp = serializedObject.FindProperty("layers");
        SyncFoldouts();
    }

    public override void OnInspectorGUI()
    {
        serializedObject.Update();
        SyncFoldouts();

        EditorGUILayout.Space(4);
        EditorGUILayout.LabelField("Document Visualizer", EditorStyles.boldLabel);
        EditorGUILayout.HelpBox(
            "Select the DocumentPrefab in the Scene or Prefab Stage to see spawn zone gizmos.",
            MessageType.Info
        );
        EditorGUILayout.Space(6);

        for (int i = 0; i < _layersProp.arraySize; i++)
        {
            DrawLayerElement(i);
            EditorGUILayout.Space(4);
        }

        serializedObject.ApplyModifiedProperties();
    }

    // ── Per-layer drawing ─────────────────────────────────────────────────────

    private void DrawLayerElement(int index)
    {
        SerializedProperty layerProp        = _layersProp.GetArrayElementAtIndex(index);
        SerializedProperty nameProp         = layerProp.FindPropertyRelative("layerName");
        SerializedProperty colorProp        = layerProp.FindPropertyRelative("gizmoColor");
        SerializedProperty imageProp        = layerProp.FindPropertyRelative("targetImage");
        SerializedProperty fallbackProp     = layerProp.FindPropertyRelative("fallbackSprite");
        SerializedProperty fallbackZoneProp = layerProp.FindPropertyRelative("applyZoneOnFallback");
        SerializedProperty zoneProp         = layerProp.FindPropertyRelative("spawnZone");
        SerializedProperty entriesProp      = layerProp.FindPropertyRelative("entries");

        // Assign a default gizmo color when the layer is first created (color is black by default).
        if (colorProp.colorValue == Color.black && index < DefaultLayerColors.Length)
            colorProp.colorValue = DefaultLayerColors[index];

        // ── Header row ────────────────────────────────────────────────────────
        string layerLabel = string.IsNullOrEmpty(nameProp.stringValue) ? $"Layer {index}" : nameProp.stringValue;

        EditorGUILayout.BeginVertical(GUI.skin.box);

        EditorGUILayout.BeginHorizontal();

        // Color swatch (click to open color picker).
        colorProp.colorValue = EditorGUILayout.ColorField(
            GUIContent.none, colorProp.colorValue, false, false, false,
            GUILayout.Width(22), GUILayout.Height(18)
        );

        _foldouts[index] = EditorGUILayout.Foldout(_foldouts[index], layerLabel, true, EditorStyles.foldoutHeader);

        // Focus button — frames the Scene view on this layer's Image.
        if (GUILayout.Button("Focus", GUILayout.Width(52)))
            FocusLayer(imageProp);

        EditorGUILayout.EndHorizontal();

        // ── Body (collapsible) ────────────────────────────────────────────────
        if (_foldouts[index])
        {
            EditorGUI.indentLevel++;

            EditorGUILayout.PropertyField(nameProp,  new GUIContent("Layer Name"));
            EditorGUILayout.PropertyField(imageProp, new GUIContent("Target Image"));

            EditorGUILayout.Space(4);

            // ── Fallback ──────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Fallback", EditorStyles.miniBoldLabel);

            EditorGUILayout.PropertyField(fallbackProp, new GUIContent("Fallback Sprite",
                "Sprite shown when no specificity in the document matches this layer.\n" +
                "Leave empty to hide the layer when unmatched."));

            if (fallbackProp.objectReferenceValue != null)
            {
                EditorGUILayout.PropertyField(fallbackZoneProp, new GUIContent("Apply Zone on Fallback",
                    "If enabled, the fallback sprite is also randomised through the spawn zone " +
                    "(offset, rotation, color). Disable for a static background sheet."));
            }

            EditorGUILayout.Space(4);

            // ── Spawn zone ────────────────────────────────────────────────────
            EditorGUILayout.LabelField("Spawn Zone", EditorStyles.miniBoldLabel);
            DrawSpawnZone(zoneProp);

            EditorGUILayout.Space(4);

            // ── Specificity entries ───────────────────────────────────────────
            EditorGUILayout.LabelField("Specificity Entries", EditorStyles.miniBoldLabel);
            DrawEntriesSummary(entriesProp);

            EditorGUI.indentLevel--;
        }

        EditorGUILayout.EndVertical();
    }

    // ── Spawn zone fields ─────────────────────────────────────────────────────

    private void DrawSpawnZone(SerializedProperty zoneProp)
    {
        SerializedProperty offsetXMinProp = zoneProp.FindPropertyRelative("offsetXMin");
        SerializedProperty offsetXMaxProp = zoneProp.FindPropertyRelative("offsetXMax");
        SerializedProperty offsetYMinProp = zoneProp.FindPropertyRelative("offsetYMin");
        SerializedProperty offsetYMaxProp = zoneProp.FindPropertyRelative("offsetYMax");
        SerializedProperty rotMinProp     = zoneProp.FindPropertyRelative("rotationMin");
        SerializedProperty rotMaxProp     = zoneProp.FindPropertyRelative("rotationMax");
        SerializedProperty colorsProp     = zoneProp.FindPropertyRelative("possibleColors");

        // Offset X range.
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Offset X  Min / Max", GUILayout.Width(140));
        offsetXMinProp.floatValue = EditorGUILayout.FloatField(offsetXMinProp.floatValue);
        offsetXMaxProp.floatValue = EditorGUILayout.FloatField(offsetXMaxProp.floatValue);
        EditorGUILayout.EndHorizontal();

        // Offset Y range.
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Offset Y  Min / Max", GUILayout.Width(140));
        offsetYMinProp.floatValue = EditorGUILayout.FloatField(offsetYMinProp.floatValue);
        offsetYMaxProp.floatValue = EditorGUILayout.FloatField(offsetYMaxProp.floatValue);
        EditorGUILayout.EndHorizontal();

        // Rotation range.
        EditorGUILayout.BeginHorizontal();
        EditorGUILayout.LabelField("Rotation  Min / Max", GUILayout.Width(140));
        rotMinProp.floatValue = EditorGUILayout.FloatField(rotMinProp.floatValue);
        rotMaxProp.floatValue = EditorGUILayout.FloatField(rotMaxProp.floatValue);
        EditorGUILayout.EndHorizontal();

        // Color palette — pick at random from this list at spawn.
        EditorGUILayout.PropertyField(colorsProp, new GUIContent("Possible Colors"), true);
    }

    // ── Entries summary ───────────────────────────────────────────────────────

    private void DrawEntriesSummary(SerializedProperty entriesProp)
    {
        for (int e = 0; e < entriesProp.arraySize; e++)
        {
            SerializedProperty entry      = entriesProp.GetArrayElementAtIndex(e);
            SerializedProperty idProp     = entry.FindPropertyRelative("specificityID");
            SerializedProperty varProp    = entry.FindPropertyRelative("variants");

            int count     = varProp.arraySize;
            string status = count > 0
                ? $"✓ {count} variant{(count > 1 ? "s" : "")}"
                : "⚠ no sprites";

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(
                idProp.stringValue,
                GUILayout.Width(EditorGUIUtility.currentViewWidth * 0.45f)
            );
            GUIStyle statusStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = count > 0 ? new Color(0.4f, 0.9f, 0.4f) : new Color(0.9f, 0.6f, 0.2f) }
            };
            EditorGUILayout.LabelField(status, statusStyle);
            EditorGUILayout.EndHorizontal();
        }

        // Edit all entries button.
        EditorGUILayout.Space(2);
        if (GUILayout.Button("Edit All Entries", GUILayout.Height(20)))
            EditorGUIUtility.ExitGUI(); // Let Unity default draw handle it after repaint.

        // Full serialised list (shown when user clicks).
        EditorGUILayout.PropertyField(entriesProp, GUIContent.none, true);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────

    private void FocusLayer(SerializedProperty imageProp)
    {
        var image = imageProp.objectReferenceValue as UnityEngine.UI.Image;
        if (image == null) return;

        Selection.activeGameObject = image.gameObject;
        SceneView.lastActiveSceneView?.FrameSelected();
    }

    private void SyncFoldouts()
    {
        int count = _layersProp?.arraySize ?? 0;
        if (_foldouts == null || _foldouts.Length != count)
        {
            bool[] previous = _foldouts ?? new bool[0];
            _foldouts = new bool[count];
            for (int i = 0; i < Mathf.Min(count, previous.Length); i++)
                _foldouts[i] = previous[i];
        }
    }
}
