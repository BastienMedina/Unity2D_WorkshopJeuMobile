using UnityEngine;

/// <summary>
/// Defines the randomisation space for a visual layer on the document prefab.
/// Each field is a configurable range — the DocumentVisualizer samples it once per spawn.
///
/// The base position is always the RectTransform's anchoredPosition as placed in the prefab.
/// Only the offsets and rotation ranges need to be configured here.
/// </summary>
[System.Serializable]
public class DocumentSpawnZone
{
    /// <summary>Minimum random X offset added to the layer's current anchoredPosition (canvas pixels).</summary>
    public float offsetXMin = 0f;

    /// <summary>Maximum random X offset added to the layer's current anchoredPosition (canvas pixels).</summary>
    public float offsetXMax = 0f;

    /// <summary>Minimum random Y offset added to the layer's current anchoredPosition (canvas pixels).</summary>
    public float offsetYMin = 0f;

    /// <summary>Maximum random Y offset added to the layer's current anchoredPosition (canvas pixels).</summary>
    public float offsetYMax = 0f;

    /// <summary>Minimum Z-rotation in degrees applied to this layer (negative = counter-clockwise).</summary>
    public float rotationMin = 0f;

    /// <summary>Maximum Z-rotation in degrees applied to this layer.</summary>
    public float rotationMax = 0f;

    /// <summary>
    /// Possible tint colors to randomly pick from.
    /// Leave empty (or with a single white entry) to apply no tint.
    /// </summary>
    public Color[] possibleColors = new Color[] { Color.white };

    // -------------------------------------------------------------------------
    // Sampling helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns a random (X, Y) offset sampled independently from
    /// [offsetXMin, offsetXMax] and [offsetYMin, offsetYMax].
    /// Add this to the RectTransform's original anchoredPosition.
    /// </summary>
    public Vector2 SampleOffset()
    {
        return new Vector2(
            Random.Range(offsetXMin, offsetXMax),
            Random.Range(offsetYMin, offsetYMax)
        );
    }

    /// <summary>Returns a random Z-rotation within [rotationMin, rotationMax].</summary>
    public float SampleRotation() => Random.Range(rotationMin, rotationMax);

    /// <summary>
    /// Returns a random color from possibleColors.
    /// Returns white when the array is empty.
    /// </summary>
    public Color SampleColor()
    {
        if (possibleColors == null || possibleColors.Length == 0)
            return Color.white;

        return possibleColors[Random.Range(0, possibleColors.Length)];
    }
}
