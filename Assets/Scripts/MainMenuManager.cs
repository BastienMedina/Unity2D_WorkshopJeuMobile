using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu navigation: shows/hides the main menu panel and the
/// floor-selection tower panel. Acts as the single entry-point controller —
/// it owns no game logic, no floor data, and no persistent state.
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
    /// Hidden by default; revealed when the player taps Play.
    /// </summary>
    [SerializeField] private GameObject towerSelectionPanel;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        ShowMainMenu();
    }

    // -------------------------------------------------------------------------
    // Panel navigation
    // -------------------------------------------------------------------------

    /// <summary>Shows the main menu and hides the tower selection panel.</summary>
    public void ShowMainMenu()
    {
        SetPanelActive(mainMenuPanel, true);
        SetPanelActive(towerSelectionPanel, false);
    }

    /// <summary>
    /// Called by Play_BTN onClick.
    /// Hides the main menu and reveals the immersive tower floor-selection view.
    /// </summary>
    public void OnPlayButtonClicked()
    {
        Debug.Log("[MainMenu] Opening tower floor selection.");
        SetPanelActive(mainMenuPanel, false);
        SetPanelActive(towerSelectionPanel, true);
    }

    /// <summary>
    /// Called by the Back button inside the tower selection panel.
    /// Returns the player to the main menu screen.
    /// </summary>
    public void OnBackButtonClicked()
    {
        Debug.Log("[MainMenu] Returning to main menu.");
        ShowMainMenu();
    }

    // -------------------------------------------------------------------------
    // Legacy / extra mode buttons (kept for backward compatibility)
    // -------------------------------------------------------------------------

    /// <summary>Called by InfiniteButton onClick (if still present).</summary>
    public void OnInfiniteButtonClicked()
    {
        Debug.Log("[MainMenu] Loading Infinite mode: " + infiniteSceneName);
        SceneManager.LoadScene(infiniteSceneName);
    }

    /// <summary>Called by HistoryButton onClick (if still present).</summary>
    public void OnHistoryButtonClicked()
    {
        Debug.Log("[MainMenu] Loading History mode: " + historySceneName);
        SceneManager.LoadScene(historySceneName);
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private static void SetPanelActive(GameObject panel, bool active)
    {
        if (panel != null) panel.SetActive(active);
    }
}
