using System.Collections.Generic;
using UnityEngine;
#if UNITY_EDITOR
using UnityEditor;
#endif

/// <summary>
/// Reads the specificities from a DocumentData and applies the matching sprite to
/// each visual layer of the document prefab (Feuille → Tampon → Texte → Signature → Pastille).
///
/// Layer evaluation order (back to front):
///   0 — Feuille    always shows; uses fallback when no feuille_ specificity is present.
///   1 — Tampon     shown only if a tampon_ specificity is found.
///   2 — Texte      shown only if a text_ specificity is found.
///   3 — Signature  shown only if a signature_ specificity is found.
///   4 — Pastille   shown only if a pastille_ specificity is found.
///
/// Each layer has its own DocumentSpawnZone (position offset, rotation, color tint).
/// Sprite variants (_1, _2, _3) are picked at random.
///
/// This component is purely visual — it never modifies DocumentData, DraggableDocument,
/// or any sorting/validation logic. It is called once per document, immediately after spawn.
/// </summary>
public class DocumentVisualizer : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Ordered list of visual layers, from back (index 0 = Feuille) to front (index 4 = Pastille).
    /// Wire each layer's targetImage to the matching child Image on the DocumentPrefab.
    /// </summary>
    [SerializeField] private List<DocumentLayer> layers = new List<DocumentLayer>();

    // -------------------------------------------------------------------------
    // Public API — called by DocumentSpawner after injection
    // -------------------------------------------------------------------------

    /// <summary>
    /// Iterates the document's specificities and applies the first matching sprite to each layer.
    /// Layers with no match are hidden unless they have a fallback sprite assigned.
    /// Safe to call multiple times — each call fully resets all layer visuals first.
    /// </summary>
    /// <param name="documentData">The data produced by DocumentSpawner for this document instance.</param>
    public void ApplyVisuals(DocumentData documentData)
    {
        if (documentData == null)
        {
            Debug.LogWarning("[DocumentVisualizer] ApplyVisuals called with null DocumentData.");
            return;
        }

        ResetAllLayers();
        AssignSpecificities(documentData.specificities);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hides every layer image before applying new visuals.
    /// This guarantees that layers from a previous call never bleed through,
    /// even if ApplyVisuals is called more than once on the same instance.
    /// </summary>
    private void ResetAllLayers()
    {
        foreach (DocumentLayer layer in layers)
            layer.HideLayer();
    }

    /// <summary>
    /// For each specificity in the document, finds the layer that owns a matching entry
    /// and applies the randomly selected sprite + zone transform to it.
    /// After all specificities are processed, layers that received no match show their
    /// fallback sprite (if one is assigned) or remain hidden.
    /// </summary>
    /// <param name="specificities">The specificity strings belonging to this document.</param>
    private void AssignSpecificities(List<string> specificities)
    {
        // Track which layers have been matched so we can apply fallbacks to the rest.
        bool[] layerMatched = new bool[layers.Count];

        foreach (string specificity in specificities)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                // A layer is only eligible for one match per document —
                // two tampon_ specificities should not overwrite each other.
                if (layerMatched[i])
                    continue;

                if (layers[i].TryApply(specificity))
                {
                    layerMatched[i] = true;
                    break; // Each specificity maps to exactly one layer.
                }
            }
        }

        // Show fallbacks for layers that were not matched by any specificity.
        for (int i = 0; i < layers.Count; i++)
        {
            if (!layerMatched[i])
                layers[i].ApplyFallback();
        }
    }

    // -------------------------------------------------------------------------
    // Gizmos — editor-only, drawn when the DocumentVisualizer is selected
    // -------------------------------------------------------------------------

#if UNITY_EDITOR
    /// <summary>
    /// Draws each layer's spawn zone as a coloured rectangle in the Scene view.
    /// Visible only when the DocumentPrefab or one of its children is selected.
    /// - Filled rectangle (low alpha) = randomisation area (positionOffsetMin → positionOffsetMax).
    /// - Wire border = same area, full opacity.
    /// - Cross = base anchor position of the layer Image.
    /// - Arc = rotation range (rotationMin / rotationMax).
    /// - Label = layer name, drawn at the top-left corner of the zone.
    /// </summary>
    private void OnDrawGizmosSelected()
    {
        if (layers == null) return;

        foreach (DocumentLayer layer in layers)
        {
            if (layer.targetImage == null) continue;

            RectTransform rt        = layer.targetImage.rectTransform;
            Vector3       baseWorld = rt.position;
            Vector3       lossyScale = rt.lossyScale;

            // Convert canvas-local pixel offsets to world-space units.
            Vector3 minOffset = new Vector3(
                layer.spawnZone.positionOffsetMin.x * lossyScale.x,
                layer.spawnZone.positionOffsetMin.y * lossyScale.y,
                0f
            );
            Vector3 maxOffset = new Vector3(
                layer.spawnZone.positionOffsetMax.x * lossyScale.x,
                layer.spawnZone.positionOffsetMax.y * lossyScale.y,
                0f
            );

            Vector3 zoneCenter = baseWorld + (minOffset + maxOffset) * 0.5f;
            float   zoneW      = Mathf.Abs(maxOffset.x - minOffset.x);
            float   zoneH      = Mathf.Abs(maxOffset.y - minOffset.y);

            Color solidColor = layer.gizmoColor;
            solidColor.a     = 0.12f;

            Vector3 halfW = new Vector3(zoneW * 0.5f, 0f, 0f);
            Vector3 halfH = new Vector3(0f, zoneH * 0.5f, 0f);
            Vector3 tl    = zoneCenter - halfW + halfH;
            Vector3 tr    = zoneCenter + halfW + halfH;
            Vector3 br    = zoneCenter + halfW - halfH;
            Vector3 bl    = zoneCenter - halfW - halfH;

            // ── Filled + outlined zone ────────────────────────────────────────
            UnityEditor.Handles.color = solidColor;
            UnityEditor.Handles.DrawSolidRectangleWithOutline(
                new Vector3[] { tl, tr, br, bl },
                solidColor,
                layer.gizmoColor
            );

            // ── Base position cross ───────────────────────────────────────────
            float crossSize           = Mathf.Max(8f * lossyScale.x, 0.004f);
            UnityEditor.Handles.color = layer.gizmoColor;
            UnityEditor.Handles.DrawLine(
                baseWorld - Vector3.right * crossSize,
                baseWorld + Vector3.right * crossSize);
            UnityEditor.Handles.DrawLine(
                baseWorld - Vector3.up * crossSize,
                baseWorld + Vector3.up * crossSize);

            // ── Rotation arc ──────────────────────────────────────────────────
            float arcRadius = Mathf.Max(zoneW, zoneH) * 0.3f;
            float angleDelta = layer.spawnZone.rotationMax - layer.spawnZone.rotationMin;
            if (arcRadius > 0.0001f && Mathf.Abs(angleDelta) > 0.1f)
            {
                float angleFrom = layer.spawnZone.rotationMin;
                UnityEditor.Handles.color = new Color(
                    layer.gizmoColor.r, layer.gizmoColor.g, layer.gizmoColor.b, 0.8f);

                UnityEditor.Handles.DrawWireArc(
                    baseWorld,
                    Vector3.forward,
                    Quaternion.Euler(0f, 0f, angleFrom) * Vector3.right,
                    angleDelta,
                    arcRadius
                );

                Vector3 minDir = Quaternion.Euler(0f, 0f, angleFrom)            * Vector3.right * arcRadius;
                Vector3 maxDir = Quaternion.Euler(0f, 0f, angleFrom + angleDelta) * Vector3.right * arcRadius;
                UnityEditor.Handles.DrawLine(baseWorld, baseWorld + minDir);
                UnityEditor.Handles.DrawLine(baseWorld, baseWorld + maxDir);
            }

            // ── Layer name label ──────────────────────────────────────────────
            UnityEditor.Handles.Label(
                tl + Vector3.up * crossSize * 1.5f,
                layer.layerName,
                new GUIStyle(UnityEditor.EditorStyles.boldLabel)
                {
                    normal   = { textColor = layer.gizmoColor },
                    fontSize = 11
                }
            );
        }
    }
#endif
}
