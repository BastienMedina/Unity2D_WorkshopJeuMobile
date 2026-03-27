using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Orchestrates the full Play → TowerScrollView transition and the
/// RDC → Main Menu return transition.
///
/// Sequence — Play pressed:
///   1. Fade to black.
///   2. Show ELEVATOR_UI (closed doors, scale = 1).
///   3. Fade from black — player sees the closed elevator.
///   4. OpenWithZoom() — doors slide open while elevator zooms in simultaneously.
///   5. TowerScrollView activates and fades in on top of the zoomed elevator backdrop.
///   6. UI interactive — player sees floor buttons floating over the elevator interior.
///
/// Sequence — RDC pressed (return to main menu):
///   1. Fade to black.
///   2. Hide TowerScrollView, reset elevator, restore main menu.
///   3. Fade from black.
///   4. UI interactive.
/// </summary>
public class TransitionManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector — panels
    // -------------------------------------------------------------------------

    [Header("Panels")]
    /// <summary>Root CanvasGroup wrapping Play / Options / Credits buttons.</summary>
    [SerializeField] private CanvasGroup mainMenuGroup;

    /// <summary>TowerScrollView GameObject (already implemented — do not recreate).</summary>
    [SerializeField] private GameObject towerScrollView;

    /// <summary>CanvasGroup on TowerScrollView for fade control.</summary>
    [SerializeField] private CanvasGroup towerScrollViewGroup;

    /// <summary>Elevator backdrop panel sitting inside CANVAS_MENU.</summary>
    [SerializeField] private GameObject elevatorUI;

    /// <summary>Title / pancarte panel hidden when entering the tower.</summary>
    [SerializeField] private GameObject titlePanel;

    // -------------------------------------------------------------------------
    // Inspector — overlay
    // -------------------------------------------------------------------------

    [Header("Fade Overlay")]
    /// <summary>
    /// Full-screen black Image — must be the last child of CANVAS_MENU so it renders on top.
    /// </summary>
    [SerializeField] private CanvasGroup fadeOverlay;

    // -------------------------------------------------------------------------
    // Inspector — animation
    // -------------------------------------------------------------------------

    [Header("Animation")]
    /// <summary>Drives door slide + zoom animations.</summary>
    [SerializeField] private ElevatorTransitionController elevatorController;

    [Header("Timings (seconds)")]
    [SerializeField] private float fadeOutDuration   = 0.5f;
    [SerializeField] private float fadeInDuration    = 0.6f;
    [SerializeField] private float towerFadeInDuration = 0.4f;
    [SerializeField] private float holdBlackDuration = 0.1f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private bool _transitioning;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (fadeOverlay != null)
        {
            fadeOverlay.alpha          = 0f;
            fadeOverlay.blocksRaycasts = false;
            fadeOverlay.interactable   = false;
        }

        if (elevatorUI != null)
            elevatorUI.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by Play_BTN — runs the full main menu → elevator zoom → TowerScrollView sequence.
    /// </summary>
    public void PlayTransitionToTower()
    {
        if (_transitioning) return;
        StartCoroutine(SequenceToTower());
    }

    /// <summary>
    /// Called by the RDC button inside TowerScrollView — returns to the main menu.
    /// </summary>
    public void PlayTransitionToMainMenu()
    {
        if (_transitioning) return;
        StartCoroutine(SequenceToMainMenu());
    }

    // -------------------------------------------------------------------------
    // Sequences
    // -------------------------------------------------------------------------

    private IEnumerator SequenceToTower()
    {
        _transitioning = true;

        // 1. Lock main menu immediately.
        SetGroup(mainMenuGroup, interactable: false);

        // 2. Fade to black.
        yield return StartCoroutine(Fade(fadeOverlay, 0f, 1f, fadeOutDuration));

        // 3. Swap: hide menu, show elevator with doors reset.
        SetGroup(mainMenuGroup, alpha: 0f, interactable: false);
        if (titlePanel != null) titlePanel.SetActive(false);

        if (elevatorUI != null)         elevatorUI.SetActive(true);
        if (elevatorController != null) elevatorController.ResetDoors();

        yield return new WaitForSeconds(holdBlackDuration);

        // 4. Fade from black — player sees the closed elevator doors.
        yield return StartCoroutine(Fade(fadeOverlay, 1f, 0f, fadeInDuration));

        // 5. Doors open + elevator zooms in simultaneously.
        bool animDone = false;
        if (elevatorController != null)
            elevatorController.OpenWithZoom(() => animDone = true);
        else
            animDone = true;

        yield return new WaitUntil(() => animDone);

        // 6. Activate TowerScrollView (transparent) on top of the zoomed elevator.
        if (towerScrollView != null)
        {
            towerScrollView.SetActive(true);
            SetGroup(towerScrollViewGroup, alpha: 0f, interactable: false);
        }

        // 7. Fade TowerScrollView in — elevator stays visible beneath it as backdrop.
        yield return StartCoroutine(FadeGroup(towerScrollViewGroup, 0f, 1f, towerFadeInDuration));

        // 8. Unlock UI.
        SetGroup(towerScrollViewGroup, alpha: 1f, interactable: true);

        _transitioning = false;
    }

    private IEnumerator SequenceToMainMenu()
    {
        _transitioning = true;

        // 1. Lock tower UI.
        SetGroup(towerScrollViewGroup, interactable: false);

        // 2. Fade out TowerScrollView overlay.
        yield return StartCoroutine(FadeGroup(towerScrollViewGroup, 1f, 0f, towerFadeInDuration));
        if (towerScrollView != null) towerScrollView.SetActive(false);

        // 3. Play the closing animation in reverse — doors close, insider shrinks, mur shrinks.
        bool animDone = false;
        if (elevatorController != null)
            elevatorController.CloseAndHide(() => animDone = true);
        else
            animDone = true;

        yield return new WaitUntil(() => animDone);

        // 4. Fade to black once doors are closed.
        yield return StartCoroutine(Fade(fadeOverlay, 0f, 1f, fadeOutDuration));

        // 5. Hide elevator, restore main menu.
        if (elevatorUI != null)         elevatorUI.SetActive(false);
        if (elevatorController != null) elevatorController.ResetDoors();
        if (titlePanel != null)         titlePanel.SetActive(true);
        SetGroup(mainMenuGroup, alpha: 0f, interactable: false);

        yield return new WaitForSeconds(holdBlackDuration);

        // 6. Fade from black — player sees the main menu.
        yield return StartCoroutine(Fade(fadeOverlay, 1f, 0f, fadeInDuration));

        // 7. Unlock main menu.
        SetGroup(mainMenuGroup, alpha: 1f, interactable: true);

        _transitioning = false;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private IEnumerator Fade(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        cg.blocksRaycasts = true;
        cg.alpha           = from;
        float elapsed      = 0f;
        while (elapsed < duration)
        {
            elapsed  += Time.deltaTime;
            cg.alpha  = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        cg.alpha = to;
        if (to <= 0f) { cg.blocksRaycasts = false; cg.interactable = false; }
    }

    private IEnumerator FadeGroup(CanvasGroup cg, float from, float to, float duration)
    {
        if (cg == null) yield break;
        cg.alpha = from;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed  += Time.deltaTime;
            cg.alpha  = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        cg.alpha = to;
    }

    private static void SetGroup(CanvasGroup cg, float alpha = -1f, bool interactable = false)
    {
        if (cg == null) return;
        if (alpha >= 0f) cg.alpha = alpha;
        cg.interactable   = interactable;
        cg.blocksRaycasts = interactable;
    }
}