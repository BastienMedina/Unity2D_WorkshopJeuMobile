using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour responsible for displaying a full-screen transition overlay between days.
/// Shows the upcoming day number (or a failure message) for a fixed duration, then notifies
/// GameManager that it is safe to start the next day.
/// Does NOT modify game state, does NOT read score or objective values,
/// and does NOT know about level structure beyond the day number passed to PlayTransition.
/// </summary>
public class DayTransitionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>
    /// CanvasGroup on the full-screen black panel.
    /// Alpha is set to 1 to show the overlay and 0 to hide it — no lerp is needed
    /// because an instant cut is more readable on mobile than a slow fade mid-flow.
    /// </summary>
    [SerializeField] private CanvasGroup transitionCanvasGroup;

    /// <summary>White TextMeshPro component centred on the black panel, used for the day label.</summary>
    [SerializeField] private TextMeshProUGUI transitionText;

    /// <summary>
    /// The black Image component on the TransitionScreen panel.
    /// Its raycastTarget is toggled alongside alpha so that the invisible panel
    /// never silently swallows pointer events from DraggableDocument.
    /// </summary>
    [SerializeField] private Image transitionImage;

    // -------------------------------------------------------------------------
    // Timing — never hardcoded inline
    // -------------------------------------------------------------------------

    /// <summary>How long the black overlay remains fully visible before GameManager is notified.</summary>
    [SerializeField] private float transitionDuration = 2f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Reference to the running transition coroutine; stored so it can be cancelled safely if needed.</summary>
    private Coroutine transitionCoroutine;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired when transitionDuration seconds have elapsed and the overlay has been hidden.
    /// GameManager subscribes to this to call InitializeDay() at the correct moment,
    /// ensuring no game logic runs while the player is looking at the transition screen.
    /// </summary>
    public event Action onTransitionComplete;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Displays the transition screen with the appropriate label and starts the countdown.
    /// Pass nextDayIndex = -1 to signal a failure state — the overlay will read
    /// "FAILED — Back to Day 1" regardless of any overrideText argument.
    /// If a transition is already running it is stopped first so rapid calls do not stack.
    /// </summary>
    /// <param name="nextDayIndex">
    /// Zero-based index of the upcoming day, converted to 1-based for display.
    /// Pass -1 to trigger the failure message.
    /// </param>
    /// <param name="overrideText">
    /// Optional explicit label. Ignored when nextDayIndex is -1 (failure always uses a fixed message).
    /// </param>
    public void PlayTransition(int nextDayIndex, string overrideText = null)
    {
        // -1 is the failure sentinel: always show the failure message so the player
        // cannot receive a misleading "Day 0" label when a run is reset to the start.
        bool isFailureTransition = nextDayIndex < 0;

        if (isFailureTransition)
        {
            transitionText.text = "FAILED — Back to Day 1";
        }
        else
        {
            // Use the caller-supplied override text when provided; otherwise build the default label.
            bool hasOverrideText = !string.IsNullOrEmpty(overrideText);
            transitionText.text  = hasOverrideText ? overrideText : $"Day {nextDayIndex + 1}";
        }

        // Show the panel and enable raycasting together so the two states are never out of sync.
        SetTransitionActive(true);

        StopTransitionCoroutineSafely();
        transitionCoroutine = StartCoroutine(TransitionRoutine());
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    /// <summary>
    /// Waits transitionDuration seconds, hides the overlay, then fires onTransitionComplete.
    /// </summary>
    private IEnumerator TransitionRoutine()
    {
        yield return new WaitForSeconds(transitionDuration);

        // Hide the panel and disable raycasting before notifying GameManager,
        // so the next day's drag-and-drop input is unblocked from the very first frame.
        SetTransitionActive(false);

        onTransitionComplete?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Toggles both the visual alpha and the Image raycastTarget in a single call.
    /// raycastTarget must be disabled when the panel is invisible — otherwise it silently
    /// blocks all drag-and-drop pointer events even though nothing is visible on screen.
    /// A CanvasGroup with alpha = 0 does NOT automatically stop raycasting on child Image
    /// components; the flag must be toggled explicitly.
    /// </summary>
    /// <param name="isActive">True to show the panel and block input; false to hide it and allow input.</param>
    private void SetTransitionActive(bool isActive)
    {
        transitionCanvasGroup.alpha   = isActive ? 1f : 0f;
        transitionImage.raycastTarget = isActive;
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops the currently running transition coroutine by stored reference if one exists.
    /// Avoids StopAllCoroutines to prevent inadvertently cancelling future coroutines
    /// added to this MonoBehaviour.
    /// </summary>
    private void StopTransitionCoroutineSafely()
    {
        if (transitionCoroutine == null)
            return;

        StopCoroutine(transitionCoroutine);
        transitionCoroutine = null;
    }
}
