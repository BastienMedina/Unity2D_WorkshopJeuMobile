using System.Collections;
using TMPro;
using UnityEngine;

/// <summary>
/// Centralised tooltip panel displayed at the top of the screen when the player
/// hovers a dragged document over a sorting bin.
///
/// Appearance and disappearance are animated via a CanvasGroup alpha fade.
/// BinHoverDetector calls the static Show/Hide methods — no bin holds a direct
/// reference to this class, keeping bins and UI fully decoupled.
/// </summary>
[RequireComponent(typeof(CanvasGroup))]
public class BinTooltipDisplay : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    /// <summary>TextMeshPro label inside the panel displaying the bin's rules.</summary>
    [SerializeField] private TextMeshProUGUI tooltipLabel;

    /// <summary>Duration of the fade-in and fade-out animation in seconds.</summary>
    [SerializeField] private float fadeDuration = 0.2f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private static BinTooltipDisplay _instance;

    private CanvasGroup _canvasGroup;
    private Coroutine   _fadeCoroutine;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _instance    = this;
        _canvasGroup = GetComponent<CanvasGroup>();

        // Start invisible and non-interactive.
        _canvasGroup.alpha          = 0f;
        _canvasGroup.blocksRaycasts = false;
        _canvasGroup.interactable   = false;
    }

    private void OnDestroy()
    {
        if (_instance == this)
            _instance = null;
    }

    // -------------------------------------------------------------------------
    // Static public API — called by BinHoverDetector
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fades in the tooltip panel and updates the displayed text.
    /// Safe to call while a fade is already in progress — interrupts and reverses.
    /// </summary>
    public static void Show(string text)
    {
        if (_instance == null) return;

        _instance.tooltipLabel.text = text;
        _instance.StartFade(targetAlpha: 1f);
    }

    /// <summary>
    /// Fades out the tooltip panel.
    /// Safe to call while a fade-in is in progress — interrupts and reverses.
    /// </summary>
    public static void Hide()
    {
        if (_instance == null) return;

        _instance.StartFade(targetAlpha: 0f);
    }

    // -------------------------------------------------------------------------
    // Fade logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops any in-progress fade and starts a new one toward targetAlpha.
    /// Raycasts are enabled only while the panel is fully visible.
    /// </summary>
    private void StartFade(float targetAlpha)
    {
        if (_fadeCoroutine != null)
            StopCoroutine(_fadeCoroutine);

        _fadeCoroutine = StartCoroutine(FadeRoutine(targetAlpha));
    }

    private IEnumerator FadeRoutine(float targetAlpha)
    {
        float startAlpha = _canvasGroup.alpha;
        float elapsed    = 0f;

        // Disable raycasts immediately when fading out so the panel never intercepts input.
        if (targetAlpha < startAlpha)
        {
            _canvasGroup.blocksRaycasts = false;
            _canvasGroup.interactable   = false;
        }

        while (elapsed < fadeDuration)
        {
            elapsed            += Time.unscaledDeltaTime; // unscaled: works correctly while paused
            _canvasGroup.alpha  = Mathf.Lerp(startAlpha, targetAlpha, elapsed / fadeDuration);
            yield return null;
        }

        _canvasGroup.alpha = targetAlpha;

        // Restore raycast state once the animation is complete.
        bool fullyVisible           = targetAlpha >= 1f;
        _canvasGroup.blocksRaycasts = fullyVisible;
        _canvasGroup.interactable   = fullyVisible;

        _fadeCoroutine = null;
    }
}
