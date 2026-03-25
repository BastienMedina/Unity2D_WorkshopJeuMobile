using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Drives the main menu:
/// - Play   → loads GameScene immediately.
/// - Quit   → exits the application.
/// - Options → slides OPT_UI in from below (y = offscreenY → restY) on first press,
///             slides it back out on second press.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector
    // -------------------------------------------------------------------------

    [Header("Scene Names")]
    /// <summary>Scene loaded when the player taps Play.</summary>
    [SerializeField] private string gameSceneName = "GameScene";

    [Header("Panels")]
    /// <summary>Root panel containing the main menu buttons (Play, Options, Quit).</summary>
    [SerializeField] private GameObject mainMenuPanel;

    /// <summary>Tower floor-selection ScrollRect, revealed by OpenTowerSelection().</summary>
    [SerializeField] private GameObject towerSelectionPanel;

    /// <summary>Options panel that slides up from below the screen.</summary>
    [SerializeField] private RectTransform optUI;

    [Header("Fade Settings")]
    /// <summary>Duration of the main-menu cross-fade in seconds.</summary>
    [SerializeField] private float fadeDuration = 0.4f;

    [Header("OPT_UI Slide Settings")]
    /// <summary>Duration of the OPT_UI slide animation in seconds.</summary>
    [SerializeField] private float slideDuration = 0.45f;

    /// <summary>
    /// Y anchoredPosition OPT_UI starts from when sliding in (below the screen).
    /// </summary>
    [SerializeField] private float optUIOffscreenY = -2000f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private CanvasGroup _mainMenuGroup;
    private CanvasGroup _towerSelectionGroup;
    private Coroutine   _activeFade;
    private Coroutine   _activeSlide;

    private float _optUIRestY;
    private bool  _optUIOpen;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _mainMenuGroup       = GetOrAddCanvasGroup(mainMenuPanel);
        _towerSelectionGroup = GetOrAddCanvasGroup(towerSelectionPanel);

        if (optUI != null)
        {
            // Cache the designer-set resting position before moving it off-screen.
            _optUIRestY = optUI.anchoredPosition.y;
            SetOptUIY(optUIOffscreenY);
        }
    }

    private void Start()
    {
        ApplyCanvasGroup(_mainMenuGroup,       alpha: 1f, interactable: true);
        ApplyCanvasGroup(_towerSelectionGroup, alpha: 0f, interactable: false);

        if (towerSelectionPanel != null)
            towerSelectionPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Button callbacks
    // -------------------------------------------------------------------------

    /// <summary>Called by Play_BTN onClick — loads GameScene.</summary>
    public void OnPlayButtonClicked()
    {
        SceneManager.LoadScene(gameSceneName);
    }

    /// <summary>Called by Quit_BTN onClick — exits the application.</summary>
    public void OnQuitButtonClicked()
    {
#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    /// <summary>
    /// Called by Options_BTN onClick.
    /// Slides OPT_UI in from below on first press; slides it back out on second press.
    /// </summary>
    public void OnOptionsButtonClicked()
    {
        if (_optUIOpen)
            SlideOptUI(to: optUIOffscreenY, onComplete: () => _optUIOpen = false);
        else
            SlideOptUI(to: _optUIRestY,     onComplete: () => _optUIOpen = true);
    }

    /// <summary>Explicit close — wire to a Close/Back button inside OPT_UI.</summary>
    public void OnOptionsCloseClicked()
    {
        if (_optUIOpen)
            SlideOptUI(to: optUIOffscreenY, onComplete: () => _optUIOpen = false);
    }

    // -------------------------------------------------------------------------
    // Tower selection helpers (kept for TowerScene flow)
    // -------------------------------------------------------------------------

    /// <summary>Fades BTN_Holder out and reveals TowerScrollView.</summary>
    public void OpenTowerSelection()
    {
        if (_activeFade != null) StopCoroutine(_activeFade);
        _activeFade = StartCoroutine(CrossFadePanels(
            fadeOut:         _mainMenuGroup,
            fadeIn:          _towerSelectionGroup,
            panelToActivate: towerSelectionPanel));
    }

    /// <summary>Fades TowerScrollView out and restores BTN_Holder.</summary>
    public void OnBackButtonClicked()
    {
        if (_activeFade != null) StopCoroutine(_activeFade);
        _activeFade = StartCoroutine(CrossFadePanels(
            fadeOut:         _towerSelectionGroup,
            fadeIn:          _mainMenuGroup,
            panelToActivate: mainMenuPanel));
    }

    // -------------------------------------------------------------------------
    // Slide coroutine
    // -------------------------------------------------------------------------

    private void SlideOptUI(float to, System.Action onComplete = null)
    {
        if (optUI == null) return;
        if (_activeSlide != null) StopCoroutine(_activeSlide);
        _activeSlide = StartCoroutine(SlideRoutine(to, onComplete));
    }

    private IEnumerator SlideRoutine(float targetY, System.Action onComplete)
    {
        float startY  = optUI.anchoredPosition.y;
        float elapsed = 0f;

        while (elapsed < slideDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / slideDuration));
            SetOptUIY(Mathf.LerpUnclamped(startY, targetY, t));
            yield return null;
        }

        SetOptUIY(targetY);
        _activeSlide = null;
        onComplete?.Invoke();
    }

    private void SetOptUIY(float y)
    {
        Vector2 pos = optUI.anchoredPosition;
        pos.y = y;
        optUI.anchoredPosition = pos;
    }

    // -------------------------------------------------------------------------
    // Cross-fade coroutine
    // -------------------------------------------------------------------------

    private IEnumerator CrossFadePanels(CanvasGroup fadeOut, CanvasGroup fadeIn, GameObject panelToActivate)
    {
        if (fadeOut != null)
        {
            fadeOut.interactable   = false;
            fadeOut.blocksRaycasts = false;
        }

        if (panelToActivate != null)
            panelToActivate.SetActive(true);

        if (fadeIn != null)
        {
            fadeIn.alpha          = 0f;
            fadeIn.interactable   = false;
            fadeIn.blocksRaycasts = false;
        }

        float elapsed  = 0f;
        float startOut = fadeOut != null ? fadeOut.alpha : 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / fadeDuration);
            if (fadeOut != null) fadeOut.alpha = Mathf.Lerp(startOut, 0f, t);
            if (fadeIn  != null) fadeIn.alpha  = Mathf.Lerp(0f, 1f, t);
            yield return null;
        }

        if (fadeOut != null) fadeOut.alpha = 0f;
        if (fadeIn  != null)
        {
            fadeIn.alpha          = 1f;
            fadeIn.interactable   = true;
            fadeIn.blocksRaycasts = true;
        }

        _activeFade = null;
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static CanvasGroup GetOrAddCanvasGroup(GameObject go)
    {
        if (go == null) return null;
        CanvasGroup cg = go.GetComponent<CanvasGroup>();
        if (cg == null) cg = go.AddComponent<CanvasGroup>();
        return cg;
    }

    private static void ApplyCanvasGroup(CanvasGroup cg, float alpha, bool interactable)
    {
        if (cg == null) return;
        cg.alpha          = alpha;
        cg.interactable   = interactable;
        cg.blocksRaycasts = interactable;
    }
}
