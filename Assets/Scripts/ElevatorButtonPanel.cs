using System;
using System.Collections;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

/// <summary>
/// Builds the elevator floor-selection panel directly inside Insider_Elevator.
///
/// Layout (mirrors a real elevator button panel):
///   ┌─────────────┐
///   │     [13]    │  ← top, centred
///   │  [11]  [12] │
///   │   [9]  [10] │
///   │   [7]   [8] │
///   │   [5]   [6] │
///   │   [3]   [4] │
///   │   [1]   [2] │  ← bottom-left is floor 1
///   │    [RDC]    │  ← bottom centre, returns to main menu
///   └─────────────┘
///
/// Buttons are rebuilt in edit mode automatically (via [ExecuteAlways]) so they
/// are visible in the Hierarchy and Inspector without entering Play mode.
/// Each floor has a <see cref="FloorButtonConfig"/> editable from the Inspector.
/// </summary>
[ExecuteAlways]
public class ElevatorButtonPanel : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Nested serialisable config — one entry per floor, visible in Inspector
    // -------------------------------------------------------------------------

    [Serializable]
    public class FloorButtonConfig
    {
        /// <summary>Display label shown on the button. Leave empty to use the floor number.</summary>
        public string labelOverride = "";

        /// <summary>Custom sprite for this button. Overrides the panel default when set.</summary>
        public Sprite spriteOverride;

        /// <summary>
        /// Custom color for this button's background.
        /// Only applied when <see cref="useCustomColor"/> is true.
        /// </summary>
        public Color customColor = Color.white;

        /// <summary>When true, <see cref="customColor"/> replaces the panel's default unlocked color.</summary>
        public bool useCustomColor;

        /// <summary>
        /// Force this button to appear locked regardless of save data.
        /// Useful for designer-controlled level gating during production.
        /// </summary>
        public bool forceLocked;
    }

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const int TotalFloors  = 13;
    private const int GridFloors   = 12;
    private const int GridColumns  = 2;
    private const string GameScene = "GameScene";

    // -------------------------------------------------------------------------
    // Inspector — References
    // -------------------------------------------------------------------------

    [Header("─── Références ───────────────────────────────")]
    [SerializeField] private FloorSaveSystem   floorSaveSystem;
    [SerializeField] private TransitionManager transitionManager;

    // -------------------------------------------------------------------------
    // Inspector — Apparence commune
    // -------------------------------------------------------------------------

    [Header("─── Apparence des boutons ──────────────────────")]
    [Tooltip("Sprite des boutons déverrouillés")]
    [SerializeField] private Sprite defaultUnlockedSprite;

    [Tooltip("Sprite des boutons verrouillés (facultatif)")]
    [SerializeField] private Sprite defaultLockedSprite;

    [Tooltip("Couleur de fond — bouton déverrouillé")]
    [SerializeField] private Color defaultUnlockedColor = new Color(0.85f, 0.78f, 0.65f, 1f);

    [Tooltip("Couleur de fond — bouton verrouillé")]
    [SerializeField] private Color defaultLockedColor   = new Color(0.35f, 0.32f, 0.28f, 1f);

    [Tooltip("Couleur du texte sur les boutons")]
    [SerializeField] private Color labelColor           = new Color(0.15f, 0.10f, 0.05f, 1f);

    // -------------------------------------------------------------------------
    // Inspector — Grille 2×6 (étages 1–12)
    // -------------------------------------------------------------------------

    [Header("─── Grille étages 1 à 12 ────────────────────────")]
    [Tooltip("Largeur de chaque bouton de la grille (px)")]
    [SerializeField] private float buttonWidth  = 120f;

    [Tooltip("Hauteur de chaque bouton de la grille (px)")]
    [SerializeField] private float buttonHeight = 90f;

    [Tooltip("Espace horizontal entre les 2 colonnes (px)")]
    [SerializeField] private float columnGap    = 20f;

    [Tooltip("Espace vertical entre les rangées (px)")]
    [SerializeField] private float rowGap       = 14f;

    [Tooltip("Position du centre de la grille par rapport au centre du panel (px)\nX = gauche/droite   Y = haut/bas")]
    [SerializeField] private Vector2 gridOffset = new Vector2(0f, -40f);

    [Tooltip("Scale uniforme appliqué à chaque bouton de la grille")]
    [SerializeField] private float gridButtonScale = 1f;

    // -------------------------------------------------------------------------
    // Inspector — Bouton étage 13
    // -------------------------------------------------------------------------

    [Header("─── Bouton étage 13 ──────────────────────────────")]
    [Tooltip("Largeur du bouton 13 (px)")]
    [SerializeField] private float topButtonWidth  = 120f;

    [Tooltip("Hauteur du bouton 13 (px)")]
    [SerializeField] private float topButtonHeight = 80f;

    [Tooltip("Position du centre du bouton 13 par rapport au centre du panel (px)")]
    [SerializeField] private Vector2 topButtonOffset = new Vector2(0f, 160f);

    [Tooltip("Scale uniforme appliqué au bouton 13")]
    [SerializeField] private float topButtonScale = 1f;

    // -------------------------------------------------------------------------
    // Inspector — Bouton RDC
    // -------------------------------------------------------------------------

    [Header("─── Bouton RDC (retour menu) ─────────────────────")]
    [Tooltip("Texte affiché sur le bouton RDC")]
    [SerializeField] private string rdcLabel = "RDC";

    [Tooltip("Sprite du bouton RDC (facultatif)")]
    [SerializeField] private Sprite rdcSprite;

    [Tooltip("Couleur de fond du bouton RDC")]
    [SerializeField] private Color  rdcColor = new Color(0.55f, 0.35f, 0.20f, 1f);

    [Tooltip("Largeur du bouton RDC (px)")]
    [SerializeField] private float rdcButtonWidth  = 120f;

    [Tooltip("Hauteur du bouton RDC (px)")]
    [SerializeField] private float rdcButtonHeight = 80f;

    [Tooltip("Position du centre du bouton RDC par rapport au centre du panel (px)")]
    [SerializeField] private Vector2 rdcButtonOffset = new Vector2(0f, -200f);

    [Tooltip("Scale uniforme appliqué au bouton RDC")]
    [SerializeField] private float rdcButtonScale = 1f;

    // -------------------------------------------------------------------------
    // Inspector — Config par étage
    // -------------------------------------------------------------------------

    [Header("─── Config par étage (0 = étage 1, 12 = étage 13) ─")]
    [SerializeField] private FloorButtonConfig[] buttonConfigs = new FloorButtonConfig[TotalFloors];

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private RectTransform _panelRect;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void OnValidate()
    {
        // Keep the array exactly TotalFloors long when the user resizes it by mistake.
        if (buttonConfigs == null || buttonConfigs.Length != TotalFloors)
        {
            FloorButtonConfig[] resized = new FloorButtonConfig[TotalFloors];
            for (int i = 0; i < TotalFloors; i++)
            {
                if (buttonConfigs != null && i < buttonConfigs.Length && buttonConfigs[i] != null)
                    resized[i] = buttonConfigs[i];
                else
                    resized[i] = new FloorButtonConfig();
            }
            buttonConfigs = resized;
        }

        for (int i = 0; i < buttonConfigs.Length; i++)
        {
            if (buttonConfigs[i] == null)
                buttonConfigs[i] = new FloorButtonConfig();
        }

#if UNITY_EDITOR
        // Rebuild in edit mode so buttons are visible in the Hierarchy without Play.
        if (!Application.isPlaying)
            UnityEditor.EditorApplication.delayCall += RebuildInEditMode;
#endif
    }

#if UNITY_EDITOR
    private void RebuildInEditMode()
    {
        // Guard against calls after the object was destroyed.
        if (this == null) return;
        _panelRect = GetComponent<RectTransform>();
        if (_panelRect == null) return;

        foreach (Transform child in transform)
            DestroyImmediate(child.gameObject);

        BuildPanel(editMode: true);
    }
#endif

    private void Start()
    {
        _panelRect = GetComponent<RectTransform>();

#if UNITY_EDITOR
        if (!Application.isPlaying) return;
#endif
        StartCoroutine(WaitForInstallerThenBuild());
    }

    // -------------------------------------------------------------------------
    // Build logic
    // -------------------------------------------------------------------------

    private IEnumerator WaitForInstallerThenBuild()
    {
        while (!StreamingAssetsInstaller.IsReady)
            yield return null;

        BuildPanel(editMode: false);
    }

    /// <summary>
    /// Creates all button GameObjects procedurally.
    /// In edit mode, buttons are non-interactive placeholders (no Button component wired).
    /// </summary>
    private void BuildPanel(bool editMode)
    {
        foreach (Transform child in transform)
        {
            if (editMode) DestroyImmediate(child.gameObject);
            else          Destroy(child.gameObject);
        }

        if (_panelRect == null) return;

        // -- Bouton étage 13 : position directe depuis l'offset Inspector --
        CreateButton(13,
            anchoredPos : topButtonOffset,
            size        : new Vector2(topButtonWidth, topButtonHeight),
            scale       : topButtonScale,
            editMode    : editMode);

        // -- Grille 2×6 (étages 1–12) --
        // Le coin supérieur-gauche de la grille est calculé depuis gridOffset (centre de la grille).
        float gridTotalHeight = 6 * buttonHeight + 5 * rowGap;
        float gridTotalWidth  = 2 * buttonWidth  + columnGap;

        // Centre de la colonne gauche et droite.
        float colLeftX  = gridOffset.x - buttonWidth  * 0.5f - columnGap * 0.5f;
        float colRightX = gridOffset.x + buttonWidth  * 0.5f + columnGap * 0.5f;

        // Rangée 0 = haut (étages 11/12), rangée 5 = bas (étages 1/2).
        float topRowY = gridOffset.y + gridTotalHeight * 0.5f - buttonHeight * 0.5f;

        for (int row = 0; row < 6; row++)
        {
            int floorRight = GridFloors - (row * 2);       // 12, 10, 8, 6, 4, 2
            int floorLeft  = GridFloors - (row * 2) - 1;   // 11,  9, 7, 5, 3, 1

            float rowY = topRowY - row * (buttonHeight + rowGap);

            CreateButton(floorLeft,  new Vector2(colLeftX,  rowY), new Vector2(buttonWidth, buttonHeight), gridButtonScale, editMode);
            CreateButton(floorRight, new Vector2(colRightX, rowY), new Vector2(buttonWidth, buttonHeight), gridButtonScale, editMode);
        }

        // -- Bouton RDC : position directe depuis l'offset Inspector --
        CreateRdcButton(rdcButtonOffset, new Vector2(rdcButtonWidth, rdcButtonHeight), rdcButtonScale, editMode);
    }

    // -------------------------------------------------------------------------
    // Button factory
    // -------------------------------------------------------------------------

    /// <summary>
    /// Creates a single floor button GameObject parented to this panel.
    /// Reads per-floor config from <see cref="buttonConfigs"/> and falls back to panel defaults.
    /// </summary>
    private void CreateButton(int floorNumber, Vector2 anchoredPos, Vector2 size, float scale, bool editMode)
    {
        FloorButtonConfig cfg = buttonConfigs[floorNumber - 1];

        bool hasSave = !editMode && floorSaveSystem != null && floorSaveSystem.FloorExists(floorNumber - 1);
        bool locked  = editMode ? false : (cfg.forceLocked || !hasSave);

        Sprite resolvedSprite = cfg.spriteOverride != null
            ? cfg.spriteOverride
            : (locked ? (defaultLockedSprite != null ? defaultLockedSprite : defaultUnlockedSprite) : defaultUnlockedSprite);

        Color resolvedBgColor = locked
            ? defaultLockedColor
            : (cfg.useCustomColor ? cfg.customColor : defaultUnlockedColor);

        string resolvedLabel = string.IsNullOrEmpty(cfg.labelOverride)
            ? floorNumber.ToString()
            : cfg.labelOverride;

        GameObject root  = new GameObject($"FloorBtn_{floorNumber}", typeof(RectTransform));
        root.transform.SetParent(transform, false);

        RectTransform rt    = root.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = anchoredPos;
        rt.localScale       = new Vector3(scale, scale, 1f);

        Image bg         = root.AddComponent<Image>();
        bg.sprite        = resolvedSprite;
        bg.color         = resolvedBgColor;
        bg.raycastTarget = !editMode;

        if (!editMode)
        {
            Button btn        = root.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.interactable  = !locked;

            ColorBlock cb    = btn.colors;
            cb.normalColor   = resolvedBgColor;
            cb.pressedColor  = new Color(
                defaultUnlockedColor.r * 0.7f,
                defaultUnlockedColor.g * 0.7f,
                defaultUnlockedColor.b * 0.7f, 1f);
            cb.disabledColor = defaultLockedColor;
            btn.colors       = cb;

            if (!locked)
            {
                int captured = floorNumber;
                btn.onClick.AddListener(() => OnFloorButtonClicked(captured));
            }
        }

        Color textColor = locked
            ? new Color(labelColor.r, labelColor.g, labelColor.b, 0.4f)
            : labelColor;

        AddLabel(root, resolvedLabel, textColor);

        if (!editMode && locked)
            AddLockOverlay(root);
    }

    private void CreateRdcButton(Vector2 anchoredPos, Vector2 size, float scale, bool editMode)
    {
        GameObject root  = new GameObject("RDC_BTN", typeof(RectTransform));
        root.transform.SetParent(transform, false);

        RectTransform rt    = root.GetComponent<RectTransform>();
        rt.anchorMin        = new Vector2(0.5f, 0.5f);
        rt.anchorMax        = new Vector2(0.5f, 0.5f);
        rt.pivot            = new Vector2(0.5f, 0.5f);
        rt.sizeDelta        = size;
        rt.anchoredPosition = anchoredPos;
        rt.localScale       = new Vector3(scale, scale, 1f);

        Image bg         = root.AddComponent<Image>();
        bg.sprite        = rdcSprite != null ? rdcSprite : defaultUnlockedSprite;
        bg.color         = rdcColor;
        bg.raycastTarget = !editMode;

        if (!editMode)
        {
            Button btn        = root.AddComponent<Button>();
            btn.targetGraphic = bg;
            btn.interactable  = true;

            ColorBlock cb    = btn.colors;
            cb.normalColor   = rdcColor;
            cb.pressedColor  = new Color(rdcColor.r * 0.7f, rdcColor.g * 0.7f, rdcColor.b * 0.7f, 1f);
            btn.colors       = cb;

            btn.onClick.AddListener(OnRdcButtonClicked);
        }

        AddLabel(root, rdcLabel, labelColor);
    }

    private void AddLabel(GameObject parent, string text, Color color)
    {
        GameObject labelGO      = new GameObject("Label", typeof(RectTransform));
        labelGO.transform.SetParent(parent.transform, false);

        RectTransform labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin     = Vector2.zero;
        labelRect.anchorMax     = Vector2.one;
        labelRect.offsetMin     = Vector2.zero;
        labelRect.offsetMax     = Vector2.zero;

        TextMeshProUGUI tmp   = labelGO.AddComponent<TextMeshProUGUI>();
        tmp.text              = text;
        tmp.alignment         = TextAlignmentOptions.Center;
        tmp.color             = color;
        tmp.enableAutoSizing  = true;
        tmp.fontSizeMin       = 8f;
        tmp.fontSizeMax       = 60f;
    }

    private void AddLockOverlay(GameObject parent)
    {
        GameObject overlay       = new GameObject("LockOverlay", typeof(RectTransform));
        overlay.transform.SetParent(parent.transform, false);

        RectTransform overlayRect = overlay.GetComponent<RectTransform>();
        overlayRect.anchorMin    = Vector2.zero;
        overlayRect.anchorMax    = Vector2.one;
        overlayRect.offsetMin    = Vector2.zero;
        overlayRect.offsetMax    = Vector2.zero;

        Image overlayImg         = overlay.AddComponent<Image>();
        overlayImg.color         = new Color(0f, 0f, 0f, 0.45f);
        overlayImg.raycastTarget = false;
    }

    // -------------------------------------------------------------------------
    // Button callbacks
    // -------------------------------------------------------------------------

    /// <summary>Stores the selected floor in FloorSessionData and loads GameScene.</summary>
    private void OnFloorButtonClicked(int floorNumber)
    {
        if (floorSaveSystem == null)
        {
            Debug.LogError("[ElevatorButtonPanel] floorSaveSystem is not assigned.");
            return;
        }

        FloorSaveData data = floorSaveSystem.LoadFloor(floorNumber - 1);

        if (data == null)
        {
            Debug.LogError($"[ElevatorButtonPanel] No save data found for floor {floorNumber}.");
            return;
        }

        FloorSessionData.SelectedFloor    = data;
        FloorSessionData.IsReplayingFloor = false;

        SceneManager.LoadScene(GameScene);
    }

    /// <summary>Plays the closing animation then returns to the main menu.</summary>
    private void OnRdcButtonClicked()
    {
        TransitionManager tm = transitionManager != null
            ? transitionManager
            : FindFirstObjectByType<TransitionManager>();

        if (tm == null)
        {
            Debug.LogError("[ElevatorButtonPanel] No TransitionManager found.");
            return;
        }

        tm.PlayTransitionToMainMenu();
    }
}
