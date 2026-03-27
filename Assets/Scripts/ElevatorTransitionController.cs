using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the elevator door animation and the interior reveal effect.
///
/// ELEVATOR_UI stays at (1,1,1) at all times — it is never scaled.
///
/// Opening sequence:
///   1. Doors slide open.
///   2. Pause (delayBeforeZoom).
///   3. Doors hide instantly, Insider_Elevator and MurVierge animate.
///
/// Closing sequence (reverse):
///   1. Insider_Elevator and MurVierge animate back.
///   2. Pause (delayBeforeZoom).
///   3. Doors reappear and slide closed.
/// </summary>
public class ElevatorTransitionController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Door References")]
    /// <summary>RectTransform of the left elevator door.</summary>
    [SerializeField] private RectTransform leftDoor;

    /// <summary>RectTransform of the right elevator door.</summary>
    [SerializeField] private RectTransform rightDoor;

    [Header("Door Animation")]
    /// <summary>How far each door slides outward from its closed position in pixels.</summary>
    [SerializeField] private float doorOpenOffset = 560f;

    /// <summary>Duration in seconds for the door slide animation.</summary>
    [SerializeField] private float doorDuration = 0.65f;

    /// <summary>
    /// Pause in seconds between the doors finishing their slide and the zoom starting.
    /// On close, this is the pause between zoom finishing and the doors reappearing.
    /// </summary>
    [SerializeField] private float delayBeforeZoom = 0.3f;

    [Header("Insider Elevator")]
    /// <summary>Interior panel. Animates from its small designer scale to (1,1,1).</summary>
    [SerializeField] private RectTransform insiderElevator;

    /// <summary>Duration in seconds for the Insider_Elevator scale animation.</summary>
    [SerializeField] private float insiderDuration = 0.7f;

    [Header("Mur Vierge")]
    /// <summary>
    /// Blank wall panel in front of the interior.
    /// Scales up to <see cref="murViergeTargetScale"/> so it exits the screen.
    /// </summary>
    [SerializeField] private RectTransform murVierge;

    /// <summary>Uniform local scale MurVierge reaches at the end of the zoom.</summary>
    [SerializeField] private float murViergeTargetScale = 3f;

    /// <summary>Duration in seconds for the MurVierge scale animation.</summary>
    [SerializeField] private float murViergeDuration = 0.7f;

    // -------------------------------------------------------------------------
    // Cached start state
    // -------------------------------------------------------------------------

    private float _leftClosedX;
    private float _rightClosedX;
    private Vector3 _insiderStartScale;
    private Vector3 _murViergeStartScale;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        if (leftDoor        != null) _leftClosedX         = leftDoor.anchoredPosition.x;
        if (rightDoor       != null) _rightClosedX        = rightDoor.anchoredPosition.x;
        if (insiderElevator != null) _insiderStartScale   = insiderElevator.localScale;
        if (murVierge       != null) _murViergeStartScale = murVierge.localScale;
    }

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// 1. Doors slide open.
    /// 2. Pause (delayBeforeZoom).
    /// 3. Doors hide, Insider + MurVierge animate.
    /// onComplete called when fully open.
    /// </summary>
    public Coroutine OpenWithZoom(Action onComplete = null)
        => StartCoroutine(OpenRoutine(onComplete));

    /// <summary>
    /// Reverse sequence:
    /// 1. Insider + MurVierge animate back.
    /// 2. Pause (delayBeforeZoom).
    /// 3. Doors reappear and slide closed.
    /// onComplete called when fully closed.
    /// </summary>
    public Coroutine CloseAndHide(Action onComplete = null)
        => StartCoroutine(CloseRoutine(onComplete));

    /// <summary>Snaps everything back to start state instantly.</summary>
    public void ResetDoors()
    {
        SetDoorX(leftDoor,  _leftClosedX);
        SetDoorX(rightDoor, _rightClosedX);
        SetDoorsVisible(true);

        if (insiderElevator != null) insiderElevator.localScale = _insiderStartScale;
        if (murVierge       != null) murVierge.localScale       = _murViergeStartScale;
    }

    // -------------------------------------------------------------------------
    // Coroutines
    // -------------------------------------------------------------------------

    private IEnumerator OpenRoutine(Action onComplete)
    {
        // Step 1 — slide doors open.
        SetDoorsVisible(true);
        yield return StartCoroutine(SlideDoors(
            _leftClosedX,  _leftClosedX  - doorOpenOffset,
            _rightClosedX, _rightClosedX + doorOpenOffset,
            doorDuration));

        // Step 2 — pause.
        yield return new WaitForSeconds(delayBeforeZoom);

        // Step 3 — hide doors, animate interior.
        SetDoorsVisible(false);

        yield return StartCoroutine(AnimateInterior(
            insiderElevator != null ? insiderElevator.localScale : Vector3.one, Vector3.one,
            murVierge       != null ? murVierge.localScale       : Vector3.one,
            new Vector3(murViergeTargetScale, murViergeTargetScale, 1f)));

        onComplete?.Invoke();
    }

    private IEnumerator CloseRoutine(Action onComplete)
    {
        // Step 1 — animate interior back.
        Vector3 insiderCurrent = insiderElevator != null ? insiderElevator.localScale : Vector3.one;
        Vector3 murCurrent     = murVierge       != null ? murVierge.localScale       : Vector3.one;

        yield return StartCoroutine(AnimateInterior(
            insiderCurrent, _insiderStartScale,
            murCurrent,     _murViergeStartScale));

        // Step 2 — pause.
        yield return new WaitForSeconds(delayBeforeZoom);

        // Step 3 — show doors at open position, slide closed.
        SetDoorX(leftDoor,  _leftClosedX  - doorOpenOffset);
        SetDoorX(rightDoor, _rightClosedX + doorOpenOffset);
        SetDoorsVisible(true);

        yield return StartCoroutine(SlideDoors(
            _leftClosedX  - doorOpenOffset, _leftClosedX,
            _rightClosedX + doorOpenOffset, _rightClosedX,
            doorDuration));

        onComplete?.Invoke();
    }

    // -------------------------------------------------------------------------
    // Animation helpers
    // -------------------------------------------------------------------------

    private IEnumerator SlideDoors(float leftStart, float leftEnd, float rightStart, float rightEnd, float duration)
    {
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / duration));
            SetDoorX(leftDoor,  Mathf.Lerp(leftStart,  leftEnd,  t));
            SetDoorX(rightDoor, Mathf.Lerp(rightStart, rightEnd, t));
            yield return null;
        }
        SetDoorX(leftDoor,  leftEnd);
        SetDoorX(rightDoor, rightEnd);
    }

    private IEnumerator AnimateInterior(Vector3 insiderStart, Vector3 insiderEnd, Vector3 murStart, Vector3 murEnd)
    {
        float totalTime = Mathf.Max(insiderDuration, murViergeDuration);
        float elapsed   = 0f;
        while (elapsed < totalTime)
        {
            elapsed += Time.deltaTime;
            float tI = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / insiderDuration));
            float tM = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / murViergeDuration));

            if (insiderElevator != null) insiderElevator.localScale = Vector3.Lerp(insiderStart, insiderEnd, tI);
            if (murVierge       != null) murVierge.localScale       = Vector3.Lerp(murStart,     murEnd,     tM);
            yield return null;
        }
        if (insiderElevator != null) insiderElevator.localScale = insiderEnd;
        if (murVierge       != null) murVierge.localScale       = murEnd;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void SetDoorsVisible(bool visible)
    {
        SetImageEnabled(leftDoor,  visible);
        SetImageEnabled(rightDoor, visible);
    }

    private static void SetImageEnabled(RectTransform rt, bool visible)
    {
        if (rt == null) return;
        Image img = rt.GetComponent<Image>();
        if (img != null) img.enabled = visible;
    }

    private static void SetDoorX(RectTransform rt, float x)
    {
        if (rt == null) return;
        Vector2 pos = rt.anchoredPosition;
        pos.x = x;
        rt.anchoredPosition = pos;
    }
}

