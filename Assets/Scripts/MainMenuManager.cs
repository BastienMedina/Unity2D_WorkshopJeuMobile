using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu navigation: fades the main menu buttons out and fades
/// the tower floor-selection panel in when the player taps Play.
/// Both panels must have a CanvasGroup at their root for the fades to work.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector — scene names
    // -------------------------------------------------------------------------

    /// <summary>
    /// Scene loaded when the player taps Infinite mode.
    /// Must match the name registered in Build Settings (case-sensitive).
    /// </summary>
    [SerializeField] private string infiniteSceneName = "TowerScene";

    /// <summary>
    /// Scene loaded when the player taps History mode.
    /// Must match the name registered in Build Settings (case-sensitive).
    /// </summary>
    [SerializeField] private string historySceneName = "DesignerTowerScene";

    // -------------------------------------------------------------------------
    // Inspector — UI panels
    // -------------------------------------------------------------------------

    /// <summary>Root panel containing the main menu buttons (Play, Options, Quit).</summary>
    [SerializeField] private GameObject mainMenuPanel;

    /// <summary>
    /// Root panel containing the tower floor-selection ScrollRect.
    /// Hidden by default; revealed with a fade when the player taps Play.
    /// </summary>
    [SerializeField] private GameObject towerSelectionPanel;

    // -------------------------------------------------------------------------
    // Inspector — fade settings
    // -------------------------------------------------------------------------

    [Header("Fade Settings")]

    /// <summary>Duration of the cross-fade between panels, in seconds.</summary>
    [SerializeField] private float fadeDuration = 0.4f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private CanvasGroup _mainMenuGroup;
    private CanvasGroup _towerSelectionGroup;
    private Coroutine   _activeFade;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        _mainMenuGroup       = GetOrAddCanvasGroup(mainMenuPanel);
        _towerSelectionGroup = GetOrAddCanvasGroup(towerSelectionPanel);
    }

    private void Start()
    {
        // Show menu instantly on first frame — no fade needed.
        ApplyCanvasGroup(_mainMenuGroup,       alpha: 1f, interactable: true);
        ApplyCanvasGroup(_towerSelectionGroup, alpha: 0f, interactable: false);

        if (towerSelectionPanel != null)
            towerSelectionPanel.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Panel navigation — called by Button onClick events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by Play_BTN onClick.
    /// Fades the main menu buttons to invisible then fades in the tower floor-selection view.
    /// </summary>
    public void OnPlayButtonClicked()
    {
        if (_activeFade != null) StopCoroutine(_activeFade);
        _activeFade = StartCoroutine(CrossFadePanels(
            fadeOut: _mainMenuGroup,
            fadeIn:  _towerSelectionGroup,
            panelToActivate: towerSelectionPanel));
    }

    /// <summary>
    /// Called by the Back button inside the tower selection panel.
    /// Fades the tower view out and restores the main menu.
    /// </summary>
    public void OnBackButtonClicked()
    {
        if (_activeFade != null) StopCoroutine(_activeFade);
        _activeFade = StartCoroutine(CrossFadePanels(
            fadeOut: _towerSelectionGroup,
            fadeIn:  _mainMenuGroup,
            panelToActivate: mainMenuPanel));
    }

    // -------------------------------------------------------------------------
    // Legacy mode buttons
    // -------------------------------------------------------------------------

    /// <summary>Called by InfiniteButton onClick (if still present).</summary>
    public void OnInfiniteButtonClicked()
    {
        SceneManager.LoadScene(infiniteSceneName);
    }

    /// <summary>Called by HistoryButton onClick (if still present).</summary>
    public void OnHistoryButtonClicked()
    {
        SceneManager.LoadScene(historySceneName);
    }

    // -------------------------------------------------------------------------
    // Coroutine
    // -------------------------------------------------------------------------

    private IEnumerator CrossFadePanels(CanvasGroup fadeOut, CanvasGroup fadeIn, GameObject panelToActivate)
    {
        // Immediately block interaction on the fading-out panel.
        if (fadeOut != null)
        {
            fadeOut.interactable   = false;
            fadeOut.blocksRaycasts = false;
        }

        // Activate the incoming panel before fading so its layout is ready.
        if (panelToActivate != null)
            panelToActivate.SetActive(true);

        if (fadeIn != null)
        {
            fadeIn.alpha          = 0f;
            fadeIn.interactable   = false;
            fadeIn.blocksRaycasts = false;
        }

        float elapsed = 0f;
        float startOut = fadeOut != null ? fadeOut.alpha : 0f;

        while (elapsed < fadeDuration)
        {
            elapsed += Time.deltaTime;
            float t  = Mathf.Clamp01(elapsed / fadeDuration);

            if (fadeOut != null) fadeOut.alpha = Mathf.Lerp(startOut, 0f, t);
            if (fadeIn  != null) fadeIn.alpha  = Mathf.Lerp(0f, 1f, t);

            yield return null;
        }

        // Ensure final values are exact.
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
