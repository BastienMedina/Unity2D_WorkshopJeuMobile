using System;
using System.Collections;
using UnityEngine;

/// <summary>
/// Centralises all full-screen animation sequences played during transitions.
/// Each type of transition (day end, day fail, floor complete) has its own Canvas slot
/// you can assign in the Inspector. The manager activates the correct Canvas, waits for
/// the animation to finish, then fires onSequenceComplete so GameManager can continue.
///
/// Wiring: assign this to GameManager's animationSequenceManager slot, then subscribe
/// GameManager to onSequenceComplete instead of — or in addition to — DayTransitionManager.
///
/// How to add a new animation:
///   1. Create your Canvas with a CanvasGroup at the root.
///   2. Attach a script that implements IAnimationSequence on the Canvas root.
///   3. Drag the Canvas into the matching slot in the Inspector.
/// </summary>
public class AnimationSequenceManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Transition types
    // -------------------------------------------------------------------------

    public enum TransitionType
    {
        /// <summary>Played between two days on the same floor (success path).</summary>
        DayEnd,

        /// <summary>Played when the player fails and is sent back to day 1.</summary>
        DayFail,

        /// <summary>Played when all days on a floor are completed (floor cleared).</summary>
        FloorComplete,

        /// <summary>
        /// Played immediately when the floor is won — shows the promotion letter
        /// before the elevator sequence starts.
        /// </summary>
        FloorWin,
    }

    // -------------------------------------------------------------------------
    // Inspector slots — drag your Canvas GameObjects here
    // -------------------------------------------------------------------------

    [Header("Day End Animation (between days, same floor)")]

    /// <summary>
    /// Canvas played at the end of each successful day.
    /// Assign your sleep / dodo animation Canvas here.
    /// The CanvasGroup on the root is used to hide/show the Canvas.
    /// Must implement IAnimationSequence if you want callback support,
    /// otherwise the manager falls back to a fixed fallback duration.
    /// </summary>
    [SerializeField] private GameObject dayEndCanvas;

    [Header("Day Fail Animation (failure → back to day 1)")]

    /// <summary>
    /// Canvas played when the player fails.
    /// Assign a specific failure animation Canvas, or leave empty
    /// to fall back to dayEndCanvas (same visual, different context).
    /// </summary>
    [SerializeField] private GameObject dayFailCanvas;

    [Header("Floor Complete Animation (all days cleared on a floor)")]

    /// <summary>
    /// Canvas played when all days on a floor are completed.
    /// Assign your elevator / floor-change animation Canvas here.
    /// </summary>
    [SerializeField] private GameObject floorCompleteCanvas;

    [Header("Floor Win Animation (promotion letter shown before elevator)")]

    /// <summary>
    /// Canvas played first when the floor is won.
    /// Assign the REJOUER ANIMATION root GameObject here.
    /// WinPromotionController drives the two buttons (Continue / Retour) from this Canvas.
    /// AnimationSequenceManager only activates / deactivates it; the button logic lives in
    /// WinPromotionController so responsibilities stay separated.
    /// </summary>
    [SerializeField] private GameObject winPromotionCanvas;

    [Header("Fallback Duration (seconds)")]

    /// <summary>
    /// Time waited before firing onSequenceComplete when the active Canvas does not
    /// implement IAnimationSequence. Acts as a safety net — set to match your
    /// longest animation duration so the game never advances too early.
    /// </summary>
    [SerializeField] private float fallbackDuration = 3f;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired when the active animation sequence has finished.
    /// GameManager subscribes to this to advance the game state
    /// (InitializeDay, SceneManager.LoadScene, etc.).
    /// </summary>
    public event Action onSequenceComplete;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    private Coroutine activeSequenceCoroutine;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // All canvases start hidden — they are shown on demand by PlaySequence.
        SetCanvasVisible(dayEndCanvas,       false);
        SetCanvasVisible(dayFailCanvas,      false);
        SetCanvasVisible(floorCompleteCanvas, false);
        SetCanvasVisible(winPromotionCanvas,  false);
    }

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Plays the animation sequence for the given transition type.
    /// Stops any currently running sequence first so rapid calls never stack.
    /// Fires onSequenceComplete when the sequence ends.
    /// </summary>
    /// <param name="type">Which transition to play.</param>
    public void PlaySequence(TransitionType type)
    {
        GameObject canvas = ResolveCanvas(type);

        if (activeSequenceCoroutine != null)
            StopCoroutine(activeSequenceCoroutine);

        activeSequenceCoroutine = StartCoroutine(RunSequence(canvas));
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    private IEnumerator RunSequence(GameObject canvas)
    {
        if (canvas == null)
        {
            // No canvas assigned — fire immediately so the game is never stuck.
            onSequenceComplete?.Invoke();
            yield break;
        }

        SetCanvasVisible(canvas, true);

        // IAnimationSequence may be on the root or on a child (e.g. SleepController,
        // ElevatorController) — use GetComponentInChildren so both cases are handled.
        IAnimationSequence sequence = canvas.GetComponentInChildren<IAnimationSequence>(true);

        if (sequence != null)
        {
            bool finished = false;
            sequence.Play(() => finished = true);
            yield return new WaitUntil(() => finished);
        }
        else
        {
            yield return new WaitForSeconds(fallbackDuration);
        }

        SetCanvasVisible(canvas, false);

        activeSequenceCoroutine = null;
        onSequenceComplete?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the canvas to play for the given transition type.
    /// DayFail falls back to dayEndCanvas when no specific failure canvas is assigned,
    /// so you are not forced to create a separate canvas for the fail case.
    /// </summary>
    private GameObject ResolveCanvas(TransitionType type)
    {
        switch (type)
        {
            case TransitionType.DayEnd:
                return dayEndCanvas;

            case TransitionType.DayFail:
                // Graceful fallback: use the day-end canvas if no fail-specific one is set.
                return dayFailCanvas != null ? dayFailCanvas : dayEndCanvas;

            case TransitionType.FloorComplete:
                return floorCompleteCanvas;

            case TransitionType.FloorWin:
                return winPromotionCanvas;

            default:
                return null;
        }
    }

    /// <summary>
    /// Hides the win-promotion canvas without firing onSequenceComplete.
    /// Called by WinPromotionController when the player presses a button so the canvas
    /// disappears before the next sequence (elevator or menu load) starts.
    /// </summary>
    public void HideWinPromotion()
    {
        SetCanvasVisible(winPromotionCanvas, false);
    }

    /// <summary>
    /// Shows or hides a Canvas by toggling its active state AND its CanvasGroup.
    /// SetActive is required because the controller scripts (SleepTransitionController,
    /// ElevatorSequenceController) run coroutines — a disabled GameObject cannot run any.
    /// The CanvasGroup is kept in sync so inner fades still work correctly.
    /// </summary>
    private void SetCanvasVisible(GameObject canvas, bool visible)
    {
        if (canvas == null) return;

        // Must activate before touching the CanvasGroup so Start/Awake fire if needed.
        canvas.SetActive(visible);

        CanvasGroup group = canvas.GetComponent<CanvasGroup>();
        if (group != null)
        {
            group.alpha          = visible ? 1f : 0f;
            group.blocksRaycasts = visible;
            group.interactable   = visible;
        }
    }
}
