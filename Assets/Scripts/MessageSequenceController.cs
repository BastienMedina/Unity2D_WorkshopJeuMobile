using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using TMPro;

/// <summary>
/// Controls a messaging UI animation sequence on Canvas.
/// Spawns message bubbles dynamically into a Vertical Layout Group with fade/scale animations.
/// </summary>
public class MessageSequenceController : MonoBehaviour
{
    [Header("Message Prefabs")]
    [SerializeField] private GameObject bubbleBigPrefab;
    [SerializeField] private GameObject bubbleSmallPrefab;

    [Header("Scene References")]
    [SerializeField] private RectTransform messageContent;
    [SerializeField] private CanvasGroup fadeCanvasGroup;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip notificationClip;

    [Header("Message Content")]
    [SerializeField] private string message1Text = "Message 1...";
    [SerializeField] private string message2Text = "Message 2...";

    [Header("Timing Settings")]
    [SerializeField] private float fadeDuration = 1f;
    [SerializeField] private float delayBeforeFirstMessage = 1.5f;
    [SerializeField] private float delayBetweenMessages = 1.5f;
    [SerializeField] private float delayBeforeFadeOut = 2f;

    [Header("Bubble Animation Settings")]
    [SerializeField] private float bubbleAppearDuration = 0.35f;

    private void Start()
    {
        if (fadeCanvasGroup != null)
            fadeCanvasGroup.alpha = 1f;

        StartCoroutine(ExecuteSequence());
    }

    private IEnumerator ExecuteSequence()
    {
        // 1. Fade-in: black screen -> scene
        yield return StartCoroutine(FadeRoutine(fadeCanvasGroup, 1f, 0f, fadeDuration));

        // 2. Wait before first message
        yield return new WaitForSeconds(delayBeforeFirstMessage);

        // 3. Spawn first message (bulle_big) + play notification sound
        SpawnBubble(bubbleBigPrefab, message1Text);
        PlayNotification();

        // 4. Wait between messages
        yield return new WaitForSeconds(delayBetweenMessages);

        // 5. Spawn second message (bulle_small) + play notification sound
        SpawnBubble(bubbleSmallPrefab, message2Text);
        PlayNotification();

        // 6. Wait before fade-out
        yield return new WaitForSeconds(delayBeforeFadeOut);

        // 7. Fade-out: scene -> black screen
        yield return StartCoroutine(FadeRoutine(fadeCanvasGroup, 0f, 1f, fadeDuration));
    }

    /// <summary>
    /// Instantiates a bubble prefab as a child of the message content container and plays its appear animation.
    /// If the prefab root has children (e.g. a Canvas wrapper), the first child is used as the actual bubble.
    /// </summary>
    private void SpawnBubble(GameObject prefab, string text)
    {
        if (prefab == null || messageContent == null) return;

        // Instantiate the prefab and immediately reparent its first child if the root is a Canvas wrapper.
        GameObject instance = Instantiate(prefab);

        // If the root has a Canvas component, the actual bubble is its first child.
        GameObject bubble;
        if (instance.GetComponent<Canvas>() != null && instance.transform.childCount > 0)
        {
            Transform actualBubble = instance.transform.GetChild(0);
            actualBubble.SetParent(messageContent, false);
            Destroy(instance); // Destroy the wrapper
            bubble = actualBubble.gameObject;
        }
        else
        {
            instance.transform.SetParent(messageContent, false);
            bubble = instance;
        }

        bubble.transform.localScale = Vector3.zero;

        // Assign text to TextMeshProUGUI or legacy Text
        TextMeshProUGUI tmpText = bubble.GetComponentInChildren<TextMeshProUGUI>();
        if (tmpText != null)
        {
            tmpText.text = text;
        }
        else
        {
            Text legacyText = bubble.GetComponentInChildren<Text>();
            if (legacyText != null)
                legacyText.text = text;
        }

        StartCoroutine(ScaleInRoutine(bubble.transform, bubbleAppearDuration));
    }

    /// <summary>Plays the notification sound if both AudioSource and AudioClip are assigned.</summary>
    private void PlayNotification()
    {
        if (audioSource != null && notificationClip != null)
            audioSource.PlayOneShot(notificationClip);
    }

    /// <summary>Fades a CanvasGroup alpha from startAlpha to endAlpha over duration seconds.</summary>
    private IEnumerator FadeRoutine(CanvasGroup group, float startAlpha, float endAlpha, float duration)
    {
        if (group == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            group.alpha = Mathf.Lerp(startAlpha, endAlpha, elapsed / duration);
            yield return null;
        }
        group.alpha = endAlpha;
    }

    /// <summary>Scales a Transform from zero to its original local scale over duration seconds.</summary>
    private IEnumerator ScaleInRoutine(Transform target, float duration)
    {
        if (target == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            // Ease-out: smooth deceleration at the end
            float eased = 1f - Mathf.Pow(1f - t, 3f);
            target.localScale = Vector3.one * eased;
            yield return null;
        }
        target.localScale = Vector3.one;
    }
}
