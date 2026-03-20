using UnityEngine;
using UnityEngine.SceneManagement;

/// <summary>
/// Handles main menu button clicks and loads the correct game scene.
/// Acts as the single entry-point controller for the game — it owns no game logic,
/// no floor data, and no persistent state. Its only responsibility is navigation.
/// </summary>
public class MainMenuManager : MonoBehaviour
{
    // Scene names are SerializeFields so they can be updated in the Inspector
    // when scenes are renamed in Build Settings without requiring a code change.

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
    // Button callbacks — wired via Inspector onClick events, not FindObjectOfType
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by InfiniteButton onClick.
    /// Infinite mode uses procedural floor generation in TowerScene — no JSON files involved.
    /// </summary>
    public void OnInfiniteButtonClicked()
    {
        Debug.Log("[MainMenu] Loading Infinite mode: " + infiniteSceneName);

        // Infinite mode loads TowerScene which generates floors procedurally at runtime
        // using FloorDifficultyProgression — it never reads designer floor_N.json files.
        SceneManager.LoadScene(infiniteSceneName);
    }

    /// <summary>
    /// Called by HistoryButton onClick.
    /// History mode loads manually designed floors from floor_N.json files saved by the
    /// Floor Designer tool — completely separate from TowerScene's procedural system.
    /// </summary>
    public void OnHistoryButtonClicked()
    {
        Debug.Log("[MainMenu] Loading History mode: " + historySceneName);

        // History mode loads DesignerTowerScene which reads floor_N.json files and presents
        // the player with a tower of handcrafted story levels to choose from.
        SceneManager.LoadScene(historySceneName);
    }
}
