using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Controls the sleep and wake transition sequence.
/// </summary>
public class SleepTransitionController : MonoBehaviour
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
    [SerializeField] private float hourHandRotationSpeed = 30f; // Degrees per second
    [SerializeField] private float minuteHandRotationSpeed = 360f; // Degrees per second
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

    private void Start()
    {
        // Ensure initial state
        if (fadeCanvasGroup != null) fadeCanvasGroup.alpha = 1f;
        if (zCanvasGroup != null) zCanvasGroup.alpha = 1f;
        if (nextDayTextCanvasGroup != null) nextDayTextCanvasGroup.alpha = 0f;
        
        if (nextDayTextMesh != null)
        {
            nextDayTextMesh.text = nextDayText;
        }
        
        StartCoroutine(ExecuteSequence());
    }

    private IEnumerator ExecuteSequence()
    {
        // 1. Start Fade-in from Black
        Coroutine fadeTask = StartCoroutine(FadeRoutine(fadeCanvasGroup, 1f, 0f, fadeDuration));

        // 2. Wait for a portion of the fade before starting animations
        float waitBeforeAnim = Mathf.Max(0, fadeDuration - animationStartOverlap);
        yield return new WaitForSeconds(waitBeforeAnim);

        // 3. Start Simultaneous Animations
        clockRunning = true;
        StartCoroutine(RotateClockRoutine());
        Coroutine zTask = StartCoroutine(ZAnimationRoutine());
        Coroutine windowTask = StartCoroutine(RotateWindowRoutine());

        // 4. Handle Clock Stop
        yield return new WaitForSeconds(clockStopDelay);
        clockRunning = false;

        // 5. Wait for Z and Window to finish
        yield return zTask; // Text appears at the end of Z routine
        yield return windowTask;

        // 6. Play Rooster Sound
        if (audioSource != null && roosterClip != null)
        {
            audioSource.PlayOneShot(roosterClip);
        }

        // 7. Delay
        yield return new WaitForSeconds(delayBeforeFadeOut);

        // 8. Fade-out EVERYTHING (Black screen AND Day Text)
        Coroutine finalFade = StartCoroutine(FadeRoutine(fadeCanvasGroup, 0f, 1f, fadeDuration));
        Coroutine textFadeOut = StartCoroutine(FadeRoutine(nextDayTextCanvasGroup, 1f, 0f, fadeDuration));
        
        yield return finalFade;
        yield return textFadeOut;
    }

    private IEnumerator FadeRoutine(CanvasGroup group, float start, float end, float duration)
    {
        if (group == null) yield break;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(start, end, elapsed / duration);
            yield return null;
        }
        group.alpha = end;
    }

    private IEnumerator RotateClockRoutine()
    {
        while (clockRunning)
        {
            if (hourHand != null) hourHand.Rotate(0, 0, -hourHandRotationSpeed * Time.deltaTime);
            if (minuteHand != null) minuteHand.Rotate(0, 0, -minuteHandRotationSpeed * Time.deltaTime);
            yield return null;
        }
    }

    private IEnumerator ZAnimationRoutine()
    {
        if (zObject == null) yield break;
        
        float elapsed = 0f;
        Vector2 startPos = zObject.anchoredPosition;

        while (elapsed < zLifetime)
        {
            elapsed += Time.deltaTime;
            float yOffset = Mathf.Sin(Time.time * zFloatSpeed) * zFloatAmplitude;
            zObject.anchoredPosition = startPos + new Vector2(0, yOffset);
            yield return null;
        }

        // Fade out ZZZ and simultaneously Fade in "LE JOUR SUIVANT"
        Coroutine zFadeOut = StartCoroutine(FadeRoutine(zCanvasGroup, 1f, 0f, textFadeDuration));
        Coroutine textFadeIn = StartCoroutine(FadeRoutine(nextDayTextCanvasGroup, 0f, 1f, textFadeDuration));
        
        yield return zFadeOut;
        yield return textFadeIn;
    }

    private IEnumerator RotateWindowRoutine()
    {
        if (windowImage == null) yield break;

        float elapsed = 0f;
        Quaternion startRot = windowImage.localRotation;
        Quaternion endRot = startRot * Quaternion.Euler(0, 0, windowTargetRotation);

        while (elapsed < windowRotationDuration)
        {
            elapsed += Time.deltaTime;
            windowImage.localRotation = Quaternion.Slerp(startRot, endRot, elapsed / windowRotationDuration);
            yield return null;
        }
        windowImage.localRotation = endRot;
    }
}
