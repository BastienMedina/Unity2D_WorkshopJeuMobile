using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

// -------------------------------------------------------------------------
// SpecificityVisualEntry — maps one specificity ID to its sprite variants
// -------------------------------------------------------------------------

/// <summary>
/// Associates a specificity string (e.g. "tampon_rond") with its list of sprite variants.
/// The DocumentVisualizer picks one variant at random each time the document is spawned.
/// </summary>
[System.Serializable]
public class SpecificityVisualEntry
{
    /// <summary>
    /// Must match exactly one string in SpecificityDatabase.allSpecificities.
    /// E.g. "tampon_rond", "signature_main", "pastille_vert".
    /// </summary>
    public string specificityID;

    /// <summary>
    /// All sprite variants for this specificity (_1, _2, _3 …).
    /// One is chosen at random during ApplyVisuals.
    /// </summary>
    public Sprite[] variants;

    /// <summary>Returns a random variant, or null when the array is empty.</summary>
    public Sprite PickRandomVariant()
    {
        if (variants == null || variants.Length == 0)
            return null;

        return variants[Random.Range(0, variants.Length)];
    }
}

// -------------------------------------------------------------------------
// DocumentLayer — one visual layer on the document prefab
// -------------------------------------------------------------------------

/// <summary>
/// Represents a single visual layer on a document (Feuille, Tampon, Texte, Signature, Pastille).
/// Holds the target Image component, its randomisation zone, and the specificity-to-sprite entries.
/// DocumentVisualizer holds a serialised list of these.
/// </summary>
[System.Serializable]
public class DocumentLayer
{
    /// <summary>Human-readable label used in the Inspector (e.g. "Tampon", "Pastille").</summary>
    public string layerName;

    /// <summary>Color used to draw this layer's gizmo in the Scene view.</summary>
    public Color gizmoColor = Color.white;

    /// <summary>
    /// The Image component that will display the selected sprite.
    /// Wire this to the corresponding child of the DocumentPrefab in the Inspector.
    /// </summary>
    public Image targetImage;

    /// <summary>
    /// Optional fallback sprite shown when no specificity in the current document matches this layer.
    /// Useful for the Feuille layer, which should always show at least a blank sheet.
    /// Leave null to hide the layer when no match is found.
    /// </summary>
    public Sprite fallbackSprite;

    /// <summary>
    /// When true, the fallback sprite is placed through the spawn zone (offset, rotation, color)
    /// exactly like a matched specificity. When false, the fallback is shown at its prefab
    /// position with no tint and no rotation — useful for a static background sheet.
    /// </summary>
    public bool applyZoneOnFallback = false;

    /// <summary>Randomisation parameters (position, rotation, color) applied at spawn time.</summary>
    public DocumentSpawnZone spawnZone;

    /// <summary>All known specificity-to-sprite mappings for this layer.</summary>
    public List<SpecificityVisualEntry> entries = new List<SpecificityVisualEntry>();

    // -------------------------------------------------------------------------
    // Internal state
    // -------------------------------------------------------------------------

    // Cached anchoredPosition as it was set in the prefab, captured on first apply.
    // This ensures re-applying visuals always starts from the designer's base position,
    // not from a previously randomised one.
    private Vector2? _originalAnchoredPosition;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Searches entries for a matching specificityID and applies the result to targetImage.
    /// Samples position offset, rotation and color from spawnZone.
    /// </summary>
    /// <param name="specificityID">The specificity string to look up.</param>
    /// <returns>True if a matching entry was found and applied.</returns>
    public bool TryApply(string specificityID)
    {
        if (targetImage == null)
            return false;

        foreach (SpecificityVisualEntry entry in entries)
        {
            if (entry.specificityID != specificityID)
                continue;

            Sprite chosen = entry.PickRandomVariant();
            if (chosen == null)
                return false;

            ApplySpriteAndZone(chosen);
            return true;
        }

        return false;
    }

    /// <summary>
    /// Shows the fallback sprite when no specificity matches this layer.
    /// If applyZoneOnFallback is true, the spawn zone (offset, rotation, color) is sampled
    /// just like a normal match. Otherwise the layer is shown at its prefab position with no tint.
    /// </summary>
    public void ApplyFallback()
    {
        if (targetImage == null || fallbackSprite == null)
        {
            HideLayer();
            return;
        }

        if (applyZoneOnFallback)
        {
            ApplySpriteAndZone(fallbackSprite);
        }
        else
        {
            targetImage.gameObject.SetActive(true);
            targetImage.sprite = fallbackSprite;
            targetImage.color  = Color.white;
        }
    }

    /// <summary>Hides the layer's GameObject when there is nothing to display.</summary>
    public void HideLayer()
    {
        if (targetImage != null)
            targetImage.gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ApplySpriteAndZone(Sprite sprite)
    {
        targetImage.gameObject.SetActive(true);
        targetImage.sprite = sprite;
        targetImage.color  = spawnZone.SampleColor();

        RectTransform rt = targetImage.rectTransform;

        // Capture the prefab's base position once, so that repeated calls
        // to ApplyVisuals always offset from the same origin.
        if (!_originalAnchoredPosition.HasValue)
            _originalAnchoredPosition = rt.anchoredPosition;

        // Apply: base position (from prefab) + independent random X/Y offset.
        rt.anchoredPosition = _originalAnchoredPosition.Value + spawnZone.SampleOffset();
        rt.localRotation    = Quaternion.Euler(0f, 0f, spawnZone.SampleRotation());
    }
}
