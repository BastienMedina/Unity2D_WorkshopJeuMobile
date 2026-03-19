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
    /// Optional second TextMeshPro below the main day label, used exclusively for the
    /// difficulty change summary lines.
    /// When assigned, summary lines are written here and transitionText shows only the
    /// day/floor header — this allows separate font size and colour styling for each block.
    /// When null, all lines are appended to transitionText so the feature degrades gracefully
    /// without requiring a scene change.
    /// </summary>
    [SerializeField] private TextMeshProUGUI summaryText;

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
    /// Displays the transition screen with the day/floor header and an optional difficulty
    /// change summary, then starts the countdown before notifying GameManager.
    /// Pass nextDayIndex = -1 to signal a failure state — the overlay will read
    /// "FAILED — Back to Day 1" and no summary is shown regardless of the summary argument.
    /// If nextDayIndex == 0 the floor number is shown because day 1 of a new floor is
    /// a milestone the player needs to recognise as an advancement, not just another day.
    /// If a transition is already running it is stopped first so rapid calls do not stack.
    /// </summary>
    /// <param name="nextDayIndex">
    /// Zero-based index of the upcoming day, converted to 1-based for display.
    /// Pass -1 to trigger the failure message.
    /// </param>
    /// <param name="floorIndex">
    /// Zero-based index of the current floor. Displayed on day-1 transitions so
    /// the player understands they advanced to a new floor, not just a new day.
    /// </param>
    /// <param name="summary">
    /// Difficulty changes to display below the day header. Null is valid — when null
    /// the summary block is skipped entirely and only the header is shown.
    /// </param>
    public void PlayTransition(int nextDayIndex, int floorIndex = 0, DifficultyChangeSummary summary = null)
    {
        // -1 is the failure sentinel: always show the failure message so the player
        // cannot receive a misleading day label when a run is reset to the start.
        bool isFailureTransition = nextDayIndex < 0;

        string headerText;

        if (isFailureTransition)
        {
            headerText = "FAILED — Back to Day 1";
        }
        else if (nextDayIndex == 0)
        {
            // Showing the floor number on day 1 helps the player understand they advanced
            // to a new floor — without it, the "Day 1" label looks identical to a failure retry.
            headerText = "Floor " + (floorIndex + 1) + " — Day 1";
        }
        else
        {
            headerText = "Day " + (nextDayIndex + 1);
        }

        // Build the summary block from the change flags.
        // "+" prefix signals addition of something new; "~" signals modification of something
        // existing — the player can immediately distinguish additions from upgrades.
        string summaryBlock = BuildSummaryBlock(summary, isFailureTransition);

        bool hasDedicatedSummaryText = summaryText != null;

        if (hasDedicatedSummaryText)
        {
            // Separate text components allow independent font size and colour per block,
            // giving designers full styling control without requiring rich-text markup in code.
            transitionText.text = headerText;
            summaryText.text    = summaryBlock;
        }
        else
        {
            // Graceful degradation: append summary to the main label when no second component
            // is assigned — the feature works out of the box without requiring a scene change.
            transitionText.text = string.IsNullOrEmpty(summaryBlock)
                ? headerText
                : headerText + summaryBlock;
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
    /// Assembles the multi-line summary string from the change flags in the summary object.
    /// Returns an empty string when summary is null, when no flags are set, or when
    /// the transition is a failure (a failed day has no difficulty changes to report).
    /// </summary>
    /// <param name="summary">The difficulty change summary built during InitializeDay, or null.</param>
    /// <param name="isFailureTransition">True when the transition represents a failed day.</param>
    /// <returns>A newline-prefixed string of change descriptions, or empty string.</returns>
    private string BuildSummaryBlock(DifficultyChangeSummary summary, bool isFailureTransition)
    {
        // No summary to display on failure — the player just needs to know they failed.
        // Showing difficulty changes on a failure screen would be confusing context.
        if (summary == null || isFailureTransition)
            return string.Empty;

        string block = string.Empty;

        if (summary.floorBonusApplied)
            block += "\n" + summary.floorBonusDescription;

        // "+" prefix signals that something new was added — immediately distinguishable from "~".
        if (summary.newBinAdded)
            block += "\n+ " + summary.newBinDescription;

        if (summary.newRuleAdded)
            block += "\n+ " + summary.newRuleDescription;

        // "~" prefix signals modification of an existing element, not a pure addition.
        if (summary.ruleComplexified)
            block += "\n~ " + summary.complexifiedRuleDescription;

        return block;
    }

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
