using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Controls the floor-win promotion screen (REJOUER ANIMATION canvas).
///
/// Flow:
///   1. GameManager calls Show() when the last night of a floor is completed.
///   2. MailOpeningController plays the promotion-letter animation.
///   3a. Player presses "Continuer" → OnContinuePressed hides this screen and asks
///       GameManager to play the elevator animation, which then loads the next floor.
///   3b. Player presses "Retour" → OnReturnPressed loads Menu_Principal immediately.
///
/// Attach this to the root REJOUER ANIMATION GameObject.
/// Wire mailController, continueButton, returnButton, and gameManager in the Inspector.
/// </summary>
public class WinPromotionController : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector references
    // -------------------------------------------------------------------------

    /// <summary>Plays the mail-opening (promotion letter) animation when shown.</summary>
    [SerializeField] private MailOpeningController mailController;

    /// <summary>
    /// "Continuer" button — hides this screen then triggers the elevator sequence
    /// followed by loading the next floor.
    /// </summary>
    [SerializeField] private Button continueButton;

    /// <summary>"Retour" button — loads the main menu immediately.</summary>
    [SerializeField] private Button returnButton;

    /// <summary>
    /// Reference to GameManager so it can be asked to run the elevator → next-floor sequence.
    /// </summary>
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
        // Branche les listeners par code — indépendant du prefab et de l'Inspector.
        if (continueButton != null)
        {
            continueButton.onClick.RemoveAllListeners();
            continueButton.onClick.AddListener(OnContinuePressed);
            Debug.Log("[WinPromotion] Awake() — listener OnContinuePressed branché sur Accept_BTN.");
        }
        else
        {
            Debug.LogError("[WinPromotion] Awake() — continueButton est NULL ! Assigne Accept_BTN dans l'Inspector.");
        }

        if (returnButton != null)
        {
            returnButton.onClick.RemoveAllListeners();
            returnButton.onClick.AddListener(OnReturnPressed);
            Debug.Log("[WinPromotion] Awake() — listener OnReturnPressed branché sur Return_BTN.");
        }
        else
        {
            Debug.LogError("[WinPromotion] Awake() — returnButton est NULL ! Assigne Return_BTN dans l'Inspector.");
        }

        // Start hidden — GameManager calls Show() when the floor is won.
        gameObject.SetActive(false);
    }

    // -------------------------------------------------------------------------
    // Public API — called by GameManager
    // -------------------------------------------------------------------------

    /// <summary>
    /// Makes the promotion screen visible and plays the mail-opening animation.
    /// Safe to call multiple times: each call resets and replays the animation.
    /// </summary>
    public void Show()
    {
        Debug.Log("[WinPromotion] Show() appelé — activation du canvas.");
        gameObject.SetActive(true);

        if (mailController != null)
        {
            Debug.Log("[WinPromotion] Show() — PlayAnimation() déclenché sur MailOpeningController.");
            mailController.PlayAnimation();
        }
        else
        {
            Debug.LogWarning("[WinPromotion] Show() — mailController est NULL, animation non jouée.");
        }
    }

    // -------------------------------------------------------------------------
    // Button callbacks — public pour le branchement en persistent onClick
    // -------------------------------------------------------------------------

    /// <summary>
    /// Hides this screen and tells GameManager to play the elevator animation followed
    /// by loading the next floor scene.
    /// Wire this to Accept_BTN.onClick in the Inspector.
    /// </summary>
    public void OnContinuePressed()
    {
        Debug.Log("[WinPromotion] OnContinuePressed() — bouton Continuer cliqué.");

        Debug.Log("[WinPromotion] OnContinuePressed() — désactivation du canvas.");
        gameObject.SetActive(false);

        if (gameManager != null)
        {
            Debug.Log("[WinPromotion] OnContinuePressed() — appel GameManager.PlayElevatorAndLoadNextFloor().");
            gameManager.PlayElevatorAndLoadNextFloor();
        }
        else
        {
            Debug.LogError("[WinPromotion] OnContinuePressed() — gameManager est NULL ! Assigne-le dans l'Inspector.");
        }
    }

    /// <summary>
    /// Loads the main menu scene immediately, abandoning the current session.
    /// Wire this to Return_BTN.onClick in the Inspector.
    /// </summary>
    public void OnReturnPressed()
    {
        Debug.Log("[WinPromotion] OnReturnPressed() — bouton Retour cliqué.");
        Debug.Log($"[WinPromotion] OnReturnPressed() — chargement de la scène '{MenuSceneName}'.");
        SceneManager.LoadScene(MenuSceneName);
    }
}
