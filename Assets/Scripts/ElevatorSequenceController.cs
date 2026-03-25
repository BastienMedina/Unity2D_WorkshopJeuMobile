using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the elevator animation sequence on Canvas.
/// Implements IAnimationSequence so AnimationSequenceManager can drive it
/// at the correct moment in the game loop instead of auto-starting on Start().
/// </summary>
public class ElevatorSequenceController : MonoBehaviour, IAnimationSequence
{
    [Header("References")]
    [SerializeField] private RectTransform leftDoor;
    [SerializeField] private RectTransform rightDoor;
    [SerializeField] private RectTransform needle;
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private CanvasGroup floorTextCanvasGroup;
    [SerializeField] private TMPro.TextMeshProUGUI floorTextMesh;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip dingClip;

    [Header("Sequence Settings")]
    [SerializeField] private float initialWaitDelay = 5f;
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private float needleRotationAngle = -180f;
    [SerializeField] private float needleDuration = 1f;
    [SerializeField] private float delayAfterDing = 0.5f;
    [SerializeField] private float doorSlideDistance = 350f;
    [SerializeField] private float doorOpenDuration = 1.5f;
    [SerializeField] private float waitBeforeFadeOut = 2f;

    /// <summary>
    /// Floor number displayed on the "ETAGE N°X" label during the sequence.
    /// Set this from GameManager before calling PlaySequence so the correct floor is shown.
    /// </summary>
    public int FloorNumber { get; set; } = 1;

    // -------------------------------------------------------------------------
    // IAnimationSequence
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the elevator sequence. Called by AnimationSequenceManager.
    /// onComplete is invoked at the very end of the sequence.
    /// </summary>
    public void Play(Action onComplete)
    {
        ResetState();
        StartCoroutine(ExecuteSequence(onComplete));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    private void ResetState()
    {
        if (fadeCanvasGroup      != null) fadeCanvasGroup.alpha      = 1f;
        if (floorTextCanvasGroup != null) floorTextCanvasGroup.alpha = 0f;
        if (floorTextMesh        != null) floorTextMesh.text         = $"ETAGE N°{FloorNumber}";

        // Reset door positions to their original Inspector-set values.
        // Doors are stored relative to their parent so no absolute position is needed.
    }

    private IEnumerator ExecuteSequence(Action onComplete)
    {
        // 1. Initial wait (elevator travelling sound, tension)
        yield return new WaitForSeconds(initialWaitDelay);

        // 2. Fade-in (black → transparent)
        yield return StartCoroutine(FadeRoutine(fadeCanvasGroup, 1f, 0f, fadeDuration));

        // 3. Needle rotation
        yield return StartCoroutine(RotateNeedleRoutine());

        // 4. Ding sound
        if (audioSource != null && dingClip != null)
            audioSource.PlayOneShot(dingClip);

        // 5. Brief pause between ding and door open
        yield return new WaitForSeconds(delayAfterDing);

        // 6. Open doors AND fade-in floor text simultaneously
        Coroutine doors    = StartCoroutine(OpenDoorsRoutine());
        Coroutine textFade = StartCoroutine(FadeRoutine(floorTextCanvasGroup, 0f, 1f, doorOpenDuration));

        yield return doors;
        yield return textFade;

        // 7. Hold
        yield return new WaitForSeconds(waitBeforeFadeOut);

        // 8. Fade-out everything
        Coroutine finalFade  = StartCoroutine(FadeRoutine(fadeCanvasGroup,      0f, 1f, fadeDuration));
        Coroutine textFadeOut = StartCoroutine(FadeRoutine(floorTextCanvasGroup, 1f, 0f, fadeDuration));

        yield return finalFade;
        yield return textFadeOut;

        onComplete?.Invoke();
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float startAlpha, float endAlpha, float duration)
    {
        if (group == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed    += Time.deltaTime;
            group.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            yield return null;
        }
        group.alpha = endAlpha;
    }

    private IEnumerator RotateNeedleRoutine()
    {
        if (needle == null) yield break;

        float      elapsed       = 0f;
        Quaternion startRotation = needle.localRotation;
        Quaternion endRotation   = Quaternion.Euler(0, 0, needleRotationAngle);

        while (elapsed < needleDuration)
        {
            elapsed              += Time.deltaTime;
            needle.localRotation  = Quaternion.Lerp(startRotation, endRotation, elapsed / needleDuration);
            yield return null;
        }
        needle.localRotation = endRotation;
    }

    private IEnumerator OpenDoorsRoutine()
    {
        if (leftDoor == null || rightDoor == null) yield break;

        float   elapsed       = 0f;
        Vector2 leftStartPos  = leftDoor.anchoredPosition;
        Vector2 rightStartPos = rightDoor.anchoredPosition;
        Vector2 leftEndPos    = leftStartPos  + Vector2.left  * doorSlideDistance;
        Vector2 rightEndPos   = rightStartPos + Vector2.right * doorSlideDistance;

        while (elapsed < doorOpenDuration)
        {
            elapsed += Time.deltaTime;
            float t  = elapsed / doorOpenDuration;
            leftDoor.anchoredPosition  = Vector2.Lerp(leftStartPos,  leftEndPos,  t);
            rightDoor.anchoredPosition = Vector2.Lerp(rightStartPos, rightEndPos, t);
            yield return null;
        }
        leftDoor.anchoredPosition  = leftEndPos;
        rightDoor.anchoredPosition = rightEndPos;
    }
}
