using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour that drives the score progress bar and its text overlay.
/// Purely presentational: it reflects values pushed to it by GameManager and
/// never reads from ScoreManager or ObjectiveManager directly.
/// Does NOT modify the score, does NOT know about objectives or day transitions.
/// </summary>
public class ScoreUI : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned UI references
    // -------------------------------------------------------------------------

    /// <summary>
    /// Filled Image used as the progress bar fill.
    /// Image Type must be set to Filled in the Inspector.
    /// Fill Method and Fill Origin are enforced in Awake so they are never left
    /// in an incorrect state if the prefab or scene is reset.
    /// </summary>
    [SerializeField] private Image fillImage;

    /// <summary>
    /// Text overlay centred on the bar, formatted as "currentScore / objectiveMax".
    /// Positioned in the Inspector so it sits visually on top of the fill image.
    /// </summary>
    [SerializeField] private TextMeshProUGUI scoreText;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Enforce fill settings here so the bar behaves correctly even if the
        // Inspector values were accidentally changed — Filled + Horizontal + Left
        // is the only valid configuration for a left-to-right progress bar.
        fillImage.type       = Image.Type.Filled;
        fillImage.fillMethod = Image.FillMethod.Horizontal;
        fillImage.fillOrigin = (int)Image.OriginHorizontal.Left;
        fillImage.fillAmount = 0f;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Updates the fill amount and text to reflect the current score against the objective maximum.
    /// Called by GameManager whenever the score or the objective maximum changes.
    /// </summary>
    /// <param name="currentScore">The player's current score for this day.</param>
    /// <param name="objectiveMax">The current maximum the player must reach to complete the day.</param>
    public void UpdateDisplay(int currentScore, int objectiveMax)
    {
        // Guard: a null fillImage means the Inspector reference was never assigned.
        // Log a clear error and return early so the null does not propagate into a
        // silent NullReferenceException that would be hard to trace at runtime.
        if (fillImage == null)
        {
            Debug.LogError("[ScoreUI] fillImage is not assigned. Assign the ScoreBarFill Image in the Inspector.");
            return;
        }

        // Guard: objectiveMax <= 0 would produce a divide-by-zero, yielding NaN for fillAmount
        // which silently freezes the bar at whatever value it held before.
        if (objectiveMax <= 0)
            return;

        // Both operands cast to float before division — integer division would truncate
        // to 0 for any score below objectiveMax, making the bar appear permanently empty.
        fillImage.fillAmount = Mathf.Clamp01((float)currentScore / (float)objectiveMax);

        scoreText.text = $"{currentScore} / {objectiveMax}";
    }
}
