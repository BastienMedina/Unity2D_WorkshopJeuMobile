using System;
using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the sleep and wake transition sequence.
/// Implements IAnimationSequence so AnimationSequenceManager can drive it
/// at the correct moment in the game loop instead of auto-starting on Start().
/// </summary>
public class SleepTransitionController : MonoBehaviour, IAnimationSequence
{
    [Header("References")]
    [SerializeField] private RectTransform hourHand;
    [SerializeField] private RectTransform minuteHand;
    [SerializeField] private RectTransform zObject;
    [SerializeField] private CanvasGroup zCanvasGroup;
    [SerializeField] private RectTransform windowImage;
    [SerializeField] private CanvasGroup nextDayTextCanvasGroup;
    [SerializeField] private TMPro.TextMeshProUGUI nextDayTextMesh;
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip roosterClip;

    [Header("Sequence Settings")]
    [SerializeField] private float fadeDuration = 1.5f;
    [SerializeField] private float animationStartOverlap = 0.5f;
    [SerializeField] private float hourHandRotationSpeed = 30f;
    [SerializeField] private float minuteHandRotationSpeed = 360f;
    [SerializeField] private float clockStopDelay = 4f;
    [SerializeField] private float zFloatAmplitude = 20f;
    [SerializeField] private float zFloatSpeed = 2f;
    [SerializeField] private float zLifetime = 3f;
    [SerializeField] private float windowTargetRotation = 180f;
    [SerializeField] private float windowRotationDuration = 4f;
    [SerializeField] private string nextDayText = "LE JOUR SUIVANT";
    [SerializeField] private float textFadeDuration = 1f;
    [SerializeField] private float delayBeforeFadeOut = 2f;

    private bool clockRunning = false;

    // -------------------------------------------------------------------------
    // IAnimationSequence
    // -------------------------------------------------------------------------

    /// <summary>
    /// Starts the sleep sequence. Called by AnimationSequenceManager.
    /// onComplete is invoked at the end of the last fade-out.
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
        clockRunning = false;
        if (fadeCanvasGroup != null)        fadeCanvasGroup.alpha        = 1f;
        if (zCanvasGroup != null)           zCanvasGroup.alpha           = 1f;
        if (nextDayTextCanvasGroup != null) nextDayTextCanvasGroup.alpha = 0f;
        if (nextDayTextMesh != null)        nextDayTextMesh.text         = nextDayText;
    }

    private IEnumerator ExecuteSequence(Action onComplete)
    {
        // 1. Fade-in from black
        Coroutine fadeTask = StartCoroutine(FadeRoutine(fadeCanvasGroup, 1f, 0f, fadeDuration));

        // 2. Wait for most of the fade before starting animations
        float waitBeforeAnim = Mathf.Max(0, fadeDuration - animationStartOverlap);
        yield return new WaitForSeconds(waitBeforeAnim);

        // 3. Simultaneous animations
        clockRunning = true;
        StartCoroutine(RotateClockRoutine());
        Coroutine zTask      = StartCoroutine(ZAnimationRoutine());
        Coroutine windowTask = StartCoroutine(RotateWindowRoutine());

        // 4. Stop clock
        yield return new WaitForSeconds(clockStopDelay);
        clockRunning = false;

        // 5. Wait for Z and Window
        yield return zTask;
        yield return windowTask;

        // 6. Rooster sound
        if (audioSource != null && roosterClip != null)
            audioSource.PlayOneShot(roosterClip);

        // 7. Hold
        yield return new WaitForSeconds(delayBeforeFadeOut);

        // 8. Final fade to black
        Coroutine finalFade  = StartCoroutine(FadeRoutine(fadeCanvasGroup,        0f, 1f, fadeDuration));
        Coroutine textFadeOut = StartCoroutine(FadeRoutine(nextDayTextCanvasGroup, 1f, 0f, fadeDuration));

        yield return finalFade;
        yield return textFadeOut;

        onComplete?.Invoke();
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float start, float end, float duration)
    {
        if (group == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed    += Time.deltaTime;
            group.alpha = Mathf.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        group.alpha = end;
    }

    private IEnumerator RotateClockRoutine()
    {
        while (clockRunning)
        {
            if (hourHand   != null) hourHand.Rotate(0,   0, -hourHandRotationSpeed   * Time.deltaTime);
            if (minuteHand != null) minuteHand.Rotate(0, 0, -minuteHandRotationSpeed * Time.deltaTime);
            yield return null;
        }
    }

    private IEnumerator ZAnimationRoutine()
    {
        if (zObject == null) yield break;

        float   elapsed  = 0f;
        Vector2 startPos = zObject.anchoredPosition;

        while (elapsed < zLifetime)
        {
            elapsed += Time.deltaTime;
            float yOffset = Mathf.Sin(Time.time * zFloatSpeed) * zFloatAmplitude;
            zObject.anchoredPosition = startPos + new Vector2(0, yOffset);
            yield return null;
        }

        Coroutine zFadeOut   = StartCoroutine(FadeRoutine(zCanvasGroup,           1f, 0f, textFadeDuration));
        Coroutine textFadeIn = StartCoroutine(FadeRoutine(nextDayTextCanvasGroup, 0f, 1f, textFadeDuration));

        yield return zFadeOut;
        yield return textFadeIn;
    }

    private IEnumerator RotateWindowRoutine()
    {
        if (windowImage == null) yield break;

        float      elapsed  = 0f;
        Quaternion startRot = windowImage.localRotation;
        Quaternion endRot   = startRot * Quaternion.Euler(0, 0, windowTargetRotation);

        while (elapsed < windowRotationDuration)
        {
            elapsed += Time.deltaTime;
            windowImage.localRotation = Quaternion.Slerp(startRot, endRot, elapsed / windowRotationDuration);
            yield return null;
        }
        windowImage.localRotation = endRot;
    }
}
