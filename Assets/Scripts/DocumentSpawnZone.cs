using UnityEngine;

/// <summary>
/// Defines the randomisation space for a visual layer on the document prefab.
/// Each field is a configurable range — the DocumentVisualizer samples it once per spawn.
/// Attach one of these per layer inside DocumentVisualizer's inspector list.
/// </summary>
[System.Serializable]
public class DocumentSpawnZone
{
    /// <summary>Minimum local-space position offset applied to this layer's RectTransform.</summary>
    public Vector2 positionOffsetMin = Vector2.zero;

    /// <summary>Maximum local-space position offset applied to this layer's RectTransform.</summary>
    public Vector2 positionOffsetMax = Vector2.zero;

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

    /// <summary>Returns a random position offset within [positionOffsetMin, positionOffsetMax].</summary>
    public Vector2 SamplePosition()
    {
        return new Vector2(
            Random.Range(positionOffsetMin.x, positionOffsetMax.x),
            Random.Range(positionOffsetMin.y, positionOffsetMax.y)
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
