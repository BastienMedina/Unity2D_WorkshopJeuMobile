using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the Game Over screen: plays the mail-opening animation when the player fails a day,
/// and routes the two buttons to either restart the current floor or return to the main menu.
/// Attach this to the root GAME OVER ANIMATION GameObject.
/// Wire mailController, retryButton, returnButton, and gameManager in the Inspector.
/// </summary>
public class GameOverController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector references
    // -------------------------------------------------------------------------

    /// <summary>Plays the mail-opening animation sequence when the game over screen is shown.</summary>
    [SerializeField] private MailOpeningController mailController;

    /// <summary>Accept / Réessayer button — restarts the current floor from day 1.</summary>
    [SerializeField] private Button retryButton;

    /// <summary>Return / Retour au bureau button — loads the main menu scene.</summary>
    [SerializeField] private Button returnButton;

    /// <summary>Reference to GameManager so RestartCurrentFloor() can be called on retry.</summary>
    [SerializeField] private GameManager gameManager;

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string MenuSceneName = "Menu_Principal";

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Awake()
    {
        // Start hidden — GameManager.OnDayFailed() calls Show() when needed.
        // Note: listeners are registered in OnEnable() instead of here because
        // Awake() is never called on a GameObject that starts inactive.
        gameObject.SetActive(false);
    }

    private void OnEnable()
    {
        // Re-register each time the object becomes active to be safe.
        if (retryButton  != null) retryButton.onClick.AddListener(OnRetryPressed);
        if (returnButton != null) returnButton.onClick.AddListener(OnReturnPressed);
    }

    private void OnDisable()
    {
        // Always clean up listeners to avoid duplicate registrations.
        if (retryButton  != null) retryButton.onClick.RemoveListener(OnRetryPressed);
        if (returnButton != null) returnButton.onClick.RemoveListener(OnReturnPressed);
    }

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Makes the game over screen visible and plays the mail-opening animation.
    /// Safe to call multiple times: each call resets and replays the animation.
    /// </summary>
    public void Show()
    {
        gameObject.SetActive(true);

        if (mailController != null)
            mailController.PlayAnimation();
    }

    // -------------------------------------------------------------------------
    // Button callbacks
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hides the game over screen and asks GameManager to restart the current floor from day 1.
    /// The floor bonus already applied earlier in the session is intentionally preserved.
    /// </summary>
    private void OnRetryPressed()
    {
        gameObject.SetActive(false);

        if (gameManager != null)
            gameManager.RestartCurrentFloor();
    }

    /// <summary>Loads the main menu scene, abandoning the current session.</summary>
    private void OnReturnPressed()
    {
        SceneManager.LoadScene(MenuSceneName);
    }
}
