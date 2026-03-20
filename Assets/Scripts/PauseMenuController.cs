using System.Collections;
using UnityEngine;
using UnityEngine.UI;
using UnityEngine.SceneManagement;

/// <summary>
/// Controls the pause menu animation sequence using a coffee machine distributor UI.
/// Positions in the scene editor are considered the final (pause-active) positions.
/// Call OpenPauseMenu() from the pause button onClick event.
/// </summary>
public class PauseMenuController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform machine;
    [SerializeField] private RectTransform cup;
    [SerializeField] private RectTransform smoke;
    [SerializeField] private Image smokeImage;
    [SerializeField] private CanvasGroup canvasGroup;
    [SerializeField] private CanvasGroup buttonsCanvasGroup;
    [SerializeField] private Button resumeButton;
    [SerializeField] private Button optionsButton;
    [SerializeField] private Button quitButton;
    [SerializeField] private AudioSource audioSource;
    [SerializeField] private AudioClip impactClip;

    [Header("Fade Settings")]
    [SerializeField] private bool useFade = true;
    [SerializeField] private float fadeInDuration = 0.25f;
    [SerializeField] private float fadeOutDuration = 0.2f;

    [Header("Machine Settings")]
    [SerializeField] private float machineDropOffset = 2200f;
    [SerializeField] private float machineDropDuration = 0.6f;
    [SerializeField] private float machineRiseDuration = 0.5f;
    [SerializeField] private float machineBounceHeight = 40f;
    [SerializeField] private float machineBounceDuration = 0.12f;

    [Header("Cup Settings")]
    [SerializeField] private float cupDropDelay = 0.1f;
    [SerializeField] private float cupDropDuration = 0.45f;
    [SerializeField] private float cupBounceHeight = 60f;

    [Header("Smoke Settings")]
    [SerializeField] private float smokeFloatAmplitude = 18f;
    [SerializeField] private float smokeFloatSpeed = 1.4f;

    [Header("Scene Settings")]
    [SerializeField] private string mainMenuSceneName = "MainMenu";

    // Positions finales enregistrées depuis l'éditeur (état pause actif)
    private Vector2 _machineFinalPos;
    private Vector2 _cupFinalPos;
    private Vector2 _smokeFinalPos;

    // Positions hors-écran calculées au démarrage
    private Vector2 _machineOffscreenPos;
    private Vector2 _cupOffscreenPos;

    private Coroutine _smokeCoroutine;
    private bool _isPauseOpen;

    private void Start()
    {
        // Enregistrer les positions finales telles que placées dans la scène
        if (machine != null) _machineFinalPos = machine.anchoredPosition;
        if (cup != null)     _cupFinalPos     = cup.anchoredPosition;
        if (smoke != null)   _smokeFinalPos   = smoke.anchoredPosition;

        // Calculer les positions hors-écran
        _machineOffscreenPos = _machineFinalPos + Vector2.up * machineDropOffset;
        _cupOffscreenPos     = _cupFinalPos     + Vector2.up * machineDropOffset;

        // Placer les éléments hors-écran et désactiver la fumée
        if (machine != null) machine.anchoredPosition = _machineOffscreenPos;
        if (cup != null)     cup.anchoredPosition     = _cupOffscreenPos;
        if (smoke != null)   smoke.gameObject.SetActive(false);

        // Initialiser le CanvasGroup si le fade est activé
        if (canvasGroup != null)
        {
            canvasGroup.alpha          = useFade ? 0f : 1f;
            canvasGroup.interactable   = !useFade;
            canvasGroup.blocksRaycasts = !useFade;
        }

        // Les boutons ignorent le CanvasGroup parent à l'ouverture :
        // ils apparaissent toujours à pleine opacité dès que la machine arrive.
        if (buttonsCanvasGroup != null)
            buttonsCanvasGroup.ignoreParentGroups = true;

        // Brancher les boutons
        if (resumeButton != null)  resumeButton.onClick.AddListener(ClosePauseMenu);
        if (quitButton != null)    quitButton.onClick.AddListener(QuitToMainMenu);
    }

    /// <summary>Ouvre le menu pause et lance la séquence d'animation.</summary>
    public void OpenPauseMenu()
    {
        if (_isPauseOpen) return;

        _isPauseOpen = true;
        Time.timeScale = 0f;
        StartCoroutine(OpenSequence());
    }

    /// <summary>Ferme le menu pause et lance la séquence de fermeture.</summary>
    public void ClosePauseMenu()
    {
        if (!_isPauseOpen) return;

        _isPauseOpen = false;
        StartCoroutine(CloseSequence());
    }

    /// <summary>Charge la scène du menu principal.</summary>
    public void QuitToMainMenu()
    {
        Time.timeScale = 1f;
        SceneManager.LoadScene(mainMenuSceneName);
    }

    private IEnumerator OpenSequence()
    {
        // 1. Fade-in du Canvas (simultané avec la chute)
        Coroutine fadeIn = null;
        if (useFade && canvasGroup != null)
        {
            canvasGroup.interactable   = false;
            canvasGroup.blocksRaycasts = true;
            fadeIn = StartCoroutine(FadeCanvasRoutine(0f, 1f, fadeInDuration));
        }

        // 2. Machine descend hors-écran → position finale avec rebond de choc
        yield return StartCoroutine(MachineDropWithBounceRoutine());

        // Attendre la fin du fade-in si pas encore terminé
        if (fadeIn != null) yield return fadeIn;

        if (canvasGroup != null) canvasGroup.interactable = true;

        // 3. Son d'impact à l'atterrissage
        PlaySound(impactClip);

        // 4. Délai puis gobelet descend avec rebond
        yield return new WaitForSecondsRealtime(cupDropDelay);
        yield return StartCoroutine(CupDropWithBounceRoutine());

        // 5. Activer et animer la fumée
        if (smoke != null)
        {
            smoke.anchoredPosition = _smokeFinalPos;
            smoke.gameObject.SetActive(true);
        }
        if (smokeImage != null)
        {
            Color c = smokeImage.color;
            c.a = 1f;
            smokeImage.color = c;
        }
        _smokeCoroutine = StartCoroutine(SmokeFloatRoutine());
    }

    private IEnumerator CloseSequence()
    {
        // Désactiver l'interactivité immédiatement
        if (canvasGroup != null) canvasGroup.interactable = false;

        // Les boutons participent maintenant au fade-out parent
        if (buttonsCanvasGroup != null)
            buttonsCanvasGroup.ignoreParentGroups = false;

        // Arrêter l'animation fumée
        if (_smokeCoroutine != null)
        {
            StopCoroutine(_smokeCoroutine);
            _smokeCoroutine = null;
        }
        if (smoke != null) smoke.gameObject.SetActive(false);

        // Machine et gobelet remontent ensemble hors-écran
        Coroutine machineRise = StartCoroutine(MoveRectRoutine(
            machine,
            _machineFinalPos,
            _machineOffscreenPos,
            machineRiseDuration,
            useUnscaledTime: true,
            easing: Easing.EaseInCubic
        ));
        Coroutine cupRise = StartCoroutine(MoveRectRoutine(
            cup,
            _cupFinalPos,
            _cupOffscreenPos,
            machineRiseDuration,
            useUnscaledTime: true,
            easing: Easing.EaseInCubic
        ));

        // Fade-out simultané avec la montée
        Coroutine fadeOut = null;
        if (useFade && canvasGroup != null)
            fadeOut = StartCoroutine(FadeCanvasRoutine(1f, 0f, fadeOutDuration));

        yield return machineRise;
        yield return cupRise;
        if (fadeOut != null) yield return fadeOut;

        if (canvasGroup != null)
        {
            canvasGroup.blocksRaycasts = false;
            canvasGroup.alpha          = useFade ? 0f : 1f;
        }

        // Rétablir l'isolation des boutons pour la prochaine ouverture
        if (buttonsCanvasGroup != null)
            buttonsCanvasGroup.ignoreParentGroups = true;

        Time.timeScale = 1f;
    }

    /// <summary>
    /// Chute de la machine en 3 phases : descente rapide (EaseInQuart) →
    /// petit sursaut vers le haut (rebond sur surface dure) → retour position finale.
    /// </summary>
    private IEnumerator MachineDropWithBounceRoutine()
    {
        if (machine == null) yield break;

        // Phase 1 : chute rapide hors-écran → position finale (élan fort, pas de ralentissement)
        float elapsed = 0f;
        while (elapsed < machineDropDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / machineDropDuration);
            machine.anchoredPosition = Vector2.Lerp(_machineOffscreenPos, _machineFinalPos, EaseInQuart(t));
            yield return null;
        }
        machine.anchoredPosition = _machineFinalPos;

        // Phase 2 : sursaut vers le haut (rebond dur et court)
        Vector2 bouncePos = _machineFinalPos + Vector2.up * machineBounceHeight;
        elapsed = 0f;
        float halfBounce = machineBounceDuration * 0.5f;
        while (elapsed < halfBounce)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfBounce);
            machine.anchoredPosition = Vector2.Lerp(_machineFinalPos, bouncePos, EaseOutCubic(t));
            yield return null;
        }
        machine.anchoredPosition = bouncePos;

        // Phase 3 : retombée sèche sur la position finale
        elapsed = 0f;
        while (elapsed < halfBounce)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / halfBounce);
            machine.anchoredPosition = Vector2.Lerp(bouncePos, _machineFinalPos, EaseInCubic(t));
            yield return null;
        }
        machine.anchoredPosition = _machineFinalPos;
    }

    private IEnumerator CupDropWithBounceRoutine()
    {
        if (cup == null) yield break;

        float elapsed = 0f;

        // Phase 1 : descente vers position finale + overshoot
        float overshootTarget = _cupFinalPos.y - cupBounceHeight;
        Vector2 startPos = _cupOffscreenPos;
        Vector2 overshootPos = new Vector2(_cupFinalPos.x, overshootTarget);

        while (elapsed < cupDropDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / cupDropDuration);
            cup.anchoredPosition = Vector2.Lerp(startPos, overshootPos, EaseOutCubic(t));
            yield return null;
        }

        // Phase 2 : rebond vers position finale
        float bounceDuration = cupDropDuration * 0.4f;
        elapsed = 0f;
        while (elapsed < bounceDuration)
        {
            elapsed += Time.unscaledDeltaTime;
            float t = Mathf.Clamp01(elapsed / bounceDuration);
            cup.anchoredPosition = Vector2.Lerp(overshootPos, _cupFinalPos, EaseOutCubic(t));
            yield return null;
        }

        cup.anchoredPosition = _cupFinalPos;
    }

    private IEnumerator SmokeFloatRoutine()
    {
        if (smoke == null) yield break;

        float time = 0f;
        while (true)
        {
            time += Time.unscaledDeltaTime;

            // Oscillation verticale
            float offsetY = Mathf.Sin(time * smokeFloatSpeed * Mathf.PI * 2f) * smokeFloatAmplitude;
            smoke.anchoredPosition = _smokeFinalPos + new Vector2(0f, offsetY);

            // Variation d'opacité synchronisée
            if (smokeImage != null)
            {
                float alpha = Mathf.Lerp(0.5f, 1f, (Mathf.Sin(time * smokeFloatSpeed * Mathf.PI * 2f) + 1f) * 0.5f);
                Color c = smokeImage.color;
                c.a = alpha;
                smokeImage.color = c;
            }

            yield return null;
        }
    }

    /// <summary>Interpole l'alpha du CanvasGroup en temps réel (non affecté par timeScale).</summary>
    private IEnumerator FadeCanvasRoutine(float from, float to, float duration)
    {
        if (canvasGroup == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.unscaledDeltaTime;
            canvasGroup.alpha = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
            yield return null;
        }
        canvasGroup.alpha = to;
    }

    /// <summary>Déplace un RectTransform de from vers to sur duration secondes (temps réel).</summary>
    private IEnumerator MoveRectRoutine(
        RectTransform rect,
        Vector2 from,
        Vector2 to,
        float duration,
        bool useUnscaledTime,
        Easing easing)
    {
        if (rect == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += useUnscaledTime ? Time.unscaledDeltaTime : Time.deltaTime;
            float t = Mathf.Clamp01(elapsed / duration);
            float easedT = ApplyEasing(t, easing);
            rect.anchoredPosition = Vector2.Lerp(from, to, easedT);
            yield return null;
        }
        rect.anchoredPosition = to;
    }

    private void PlaySound(AudioClip clip)
    {
        if (audioSource != null && clip != null)
            audioSource.PlayOneShot(clip);
    }

    // ─── Easing ──────────────────────────────────────────────────────────────

    private enum Easing { EaseOutCubic, EaseInCubic }

    private float ApplyEasing(float t, Easing easing)
    {
        return easing == Easing.EaseOutCubic ? EaseOutCubic(t) : EaseInCubic(t);
    }

    private float EaseOutCubic(float t) => 1f - Mathf.Pow(1f - t, 3f);
    private float EaseInCubic(float t)  => t * t * t;
    private float EaseInQuart(float t)  => t * t * t * t;
}
