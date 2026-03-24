using System.Collections;
using UnityEngine;

/// <summary>
/// Anime l'ouverture d'un courrier en 4 phases séquentielles :
/// 1. Lettre_ouverte part d'une petite échelle vers sa taille finale (enveloppe qui arrive).
/// 2. Top_Container s'écrase sur Y (1→0) pour simuler l'ouverture du couvercle
///    sans rotation 3D (évite les clignotements de backface culling sur Canvas UI).
/// 3. Lettre_ouverte descend hors-écran par le bas.
/// 4. Lettre_Promotion monte depuis sa position de départ vers sa position finale.
/// Appeler PlayAnimation() pour déclencher la séquence.
/// </summary>
public class MailOpeningController : MonoBehaviour
{
    [Header("References")]
    [SerializeField] private RectTransform canvasRoot;
    [SerializeField] private RectTransform lettreOuverte;
    [SerializeField] private RectTransform topContainer;
    [SerializeField] private RectTransform lettrePromotion;

    [Header("Playback")]
    [SerializeField] private bool playOnStart = true;

    [Header("Phase 1 — Apparition du canvas")]
    [SerializeField] private float enveloppeStartScale  = 0.05f;
    [SerializeField] private float enveloppeDuration    = 0.6f;

    [Header("Phase 2 — Ouverture du couvercle")]
    [SerializeField] private float lidOpenDelay         = 0.2f;
    [SerializeField] private float lidHalfDuration      = 0.25f;   // durée de chaque demi-phase (total × 2)
    [SerializeField] private float lidFlipRotationZ     = 180f;    // rotation Z appliquée au milieu du flip

    [Header("Phase 3 — Descente de la lettre vide")]
    [SerializeField] private float letterDropOverlap    = 0.15f;   // secondes avant la fin du lid où la descente commence
    [SerializeField] private float letterDropOffset     = 2200f;
    [SerializeField] private float letterDropDuration   = 0.45f;

    [Header("Phase 4 — Montée de la lettre promotion")]
    [SerializeField] private float promoRiseDelay       = 0.1f;
    [SerializeField] private float promoStartY          = -1850f;
    [SerializeField] private float promoFinalY          = 660f;
    [SerializeField] private float promoRiseDuration    = 0.55f;

    // Valeurs de référence enregistrées au démarrage
    private Vector3 _lettreOuverteFinalScale;
    private Vector3 _topContainerInitialRotation;
    private Vector2 _lettreOuverteFinalPos;

    private void Awake()
    {
        // Enregistrer les états finaux tels que placés dans la scène
        if (lettreOuverte != null) _lettreOuverteFinalScale       = lettreOuverte.localScale;
        if (topContainer  != null) _topContainerInitialRotation   = topContainer.localEulerAngles;
        if (lettreOuverte != null) _lettreOuverteFinalPos         = lettreOuverte.anchoredPosition;

        ResetToInitialState();
    }

    private void Start()
    {
        if (playOnStart)
            StartCoroutine(PlaySequence());
    }

    /// <summary>Déclenche la séquence d'animation depuis l'état initial.</summary>
    public void PlayAnimation()
    {
        StopAllCoroutines();
        ResetToInitialState();
        StartCoroutine(PlaySequence());
    }

    // ─── State reset ──────────────────────────────────────────────────────────

    private void ResetToInitialState()
    {
        // Lettre_ouverte minuscule (c'est elle qui "arrive")
        if (lettreOuverte != null)
        {
            lettreOuverte.localScale        = Vector3.one * enveloppeStartScale;
            lettreOuverte.anchoredPosition  = _lettreOuverteFinalPos;
        }

        // Couvercle : rotation et scale Y remis à l'état initial
        if (topContainer != null)
        {
            topContainer.localEulerAngles = _topContainerInitialRotation;
            Vector3 s = topContainer.localScale;
            topContainer.localScale = new Vector3(s.x, 1f, s.z);
        }

        // Lettre promotion à sa position de départ (cachée dans l'enveloppe)
        if (lettrePromotion != null)
            lettrePromotion.anchoredPosition = new Vector2(lettrePromotion.anchoredPosition.x, promoStartY);
    }

    // ─── Séquence principale ─────────────────────────────────────────────────

    private IEnumerator PlaySequence()
    {
        // Phase 1 : Lettre_ouverte scale 0 → taille finale (enveloppe qui arrive)
        yield return StartCoroutine(ScaleRoutine(
            lettreOuverte,
            Vector3.one * enveloppeStartScale,
            _lettreOuverteFinalScale,
            enveloppeDuration,
            Easing.EaseOutBack
        ));

        // Phase 2 + 3 : le flip démarre, la descente commence avant sa fin (overlap)
        yield return new WaitForSeconds(lidOpenDelay);
        Coroutine lidCoroutine = StartCoroutine(LidFlipRoutine(topContainer));

        float lidTotalDuration = lidHalfDuration * 2f;
        float waitBeforeDrop   = Mathf.Max(0f, lidTotalDuration - letterDropOverlap);
        yield return new WaitForSeconds(waitBeforeDrop);

        // Phase 3 : descente de la lettre vide (peut chevaucher la fin du lid)
        Coroutine dropCoroutine = StartCoroutine(MoveRectRoutine(
            lettreOuverte,
            _lettreOuverteFinalPos,
            _lettreOuverteFinalPos + Vector2.down * letterDropOffset,
            letterDropDuration,
            Easing.EaseInCubic
        ));

        // Attendre que le lid et la descente soient tous les deux terminés
        yield return lidCoroutine;
        yield return dropCoroutine;

        // Phase 4 : lettre promotion monte de promoStartY vers promoFinalY
        yield return new WaitForSeconds(promoRiseDelay);
        float promoX = lettrePromotion != null ? lettrePromotion.anchoredPosition.x : 0f;
        yield return StartCoroutine(MoveRectRoutine(
            lettrePromotion,
            new Vector2(promoX, promoStartY),
            new Vector2(promoX, promoFinalY),
            promoRiseDuration,
            Easing.EaseOutCubic
        ));
    }

    // ─── Routines d'animation ────────────────────────────────────────────────

    /// <summary>Interpole le localScale d'un RectTransform de from à to.</summary>
    private IEnumerator ScaleRoutine(RectTransform target, Vector3 from, Vector3 to, float duration, Easing easing)
    {
        if (target == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / duration), easing);
            target.localScale = Vector3.LerpUnclamped(from, to, t);
            yield return null;
        }
        target.localScale = to;
    }

    /// <summary>
    /// Simule l'ouverture d'un couvercle en 3 sous-phases sans rotation 3D :
    /// 1. scaleY 1 → 0  (le couvercle s'écrase vers son axe central)
    /// 2. rotation Z += lidFlipRotationZ  (pivot instantané au milieu, face cachée)
    /// 3. scaleY 0 → 1  (le couvercle s'ouvre de l'autre côté)
    /// </summary>
    private IEnumerator LidFlipRoutine(RectTransform target)
    {
        if (target == null) yield break;

        Vector3 baseScale    = target.localScale;
        Vector3 baseRotation = target.localEulerAngles;

        // Sous-phase 1 : scaleY 1 → 0
        float elapsed = 0f;
        while (elapsed < lidHalfDuration)
        {
            elapsed += Time.deltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / lidHalfDuration), Easing.EaseInCubic);
            target.localScale = new Vector3(baseScale.x, Mathf.LerpUnclamped(1f, 0f, t), baseScale.z);
            yield return null;
        }
        target.localScale = new Vector3(baseScale.x, 0f, baseScale.z);

        // Sous-phase 2 : rotation Z instantanée au milieu du flip
        target.localEulerAngles = new Vector3(
            baseRotation.x,
            baseRotation.y,
            baseRotation.z + lidFlipRotationZ
        );

        // Sous-phase 3 : scaleY 0 → 1
        elapsed = 0f;
        while (elapsed < lidHalfDuration)
        {
            elapsed += Time.deltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / lidHalfDuration), Easing.EaseOutCubic);
            target.localScale = new Vector3(baseScale.x, Mathf.LerpUnclamped(0f, 1f, t), baseScale.z);
            yield return null;
        }
        target.localScale = new Vector3(baseScale.x, 1f, baseScale.z);
    }

    /// <summary>Interpole uniquement localScale.Y sans toucher X et Z.</summary>
    private IEnumerator ScaleYRoutine(RectTransform target, float fromY, float toY, float duration, Easing easing)
    {
        if (target == null) yield break;

        Vector3 originalScale = target.localScale;
        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / duration), easing);
            target.localScale = new Vector3(originalScale.x, Mathf.LerpUnclamped(fromY, toY, t), originalScale.z);
            yield return null;
        }
        target.localScale = new Vector3(originalScale.x, toY, originalScale.z);
    }

    /// <summary>Interpole l'anchoredPosition d'un RectTransform de from à to.</summary>
    private IEnumerator MoveRectRoutine(RectTransform target, Vector2 from, Vector2 to, float duration, Easing easing)
    {
        if (target == null) yield break;

        float elapsed = 0f;
        while (elapsed < duration)
        {
            elapsed += Time.deltaTime;
            float t = ApplyEasing(Mathf.Clamp01(elapsed / duration), easing);
            target.anchoredPosition = Vector2.LerpUnclamped(from, to, t);
            yield return null;
        }
        target.anchoredPosition = to;
    }

    // ─── Easing ──────────────────────────────────────────────────────────────

    private enum Easing
    {
        EaseOutBack,
        EaseInCubic,
        EaseOutCubic,
        EaseInOutCubic,
    }

    private float ApplyEasing(float t, Easing easing)
    {
        switch (easing)
        {
            case Easing.EaseOutBack:
                const float c1 = 1.70158f;
                const float c3 = c1 + 1f;
                return 1f + c3 * Mathf.Pow(t - 1f, 3f) + c1 * Mathf.Pow(t - 1f, 2f);

            case Easing.EaseInCubic:
                return t * t * t;

            case Easing.EaseOutCubic:
                return 1f - Mathf.Pow(1f - t, 3f);

            case Easing.EaseInOutCubic:
                return t < 0.5f
                    ? 4f * t * t * t
                    : 1f - Mathf.Pow(-2f * t + 2f, 3f) / 2f;

            default:
                return t;
        }
    }
}
