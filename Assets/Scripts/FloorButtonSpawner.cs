using System.Collections;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dynamically instantiates floor-selection buttons inside a ScrollRect content container.
/// Only buttons for floors that have a saved floor_N.json file are shown as interactive;
/// floors without a save are displayed as locked via the FloorButton.lockedOverlay.
/// Buttons are generated from a single prefab, so styling all entries only requires
/// editing that one asset.
/// </summary>
public class FloorButtonSpawner : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-configurable fields
    // -------------------------------------------------------------------------

    /// <summary>Prefab that contains the FloorButton component + visual layout.</summary>
    [SerializeField] private FloorButton floorButtonPrefab;

    /// <summary>The ScrollRect content RectTransform that will hold all buttons.</summary>
    [SerializeField] private RectTransform contentContainer;

    /// <summary>
    /// Total number of floor slots to generate (buttons 1 … floorCount).
    /// Buttons for floors without a matching floor_N.json appear locked.
    /// </summary>
    [SerializeField] private int floorCount = 100;

    /// <summary>Vertical spacing between buttons in pixels.</summary>
    [SerializeField] private float spacing = 20f;

    /// <summary>
    /// Reference to the FloorSaveSystem that checks which floor files exist on disk.
    /// Assign the FloorSaveSystem GameObject present in the Menu_Principal scene.
    /// </summary>
    [SerializeField] private FloorSaveSystem floorSaveSystem;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private FloorButton[] _spawnedButtons;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        if (floorSaveSystem == null)
        {
            Debug.LogError("[FloorButtonSpawner] floorSaveSystem is not assigned in the Inspector. " +
                           "Buttons will all appear locked.");
        }

        // StreamingAssetsInstaller must finish copying floors from the APK to persistentDataPath
        // before FloorSaveSystem can find any file. Wait for the signal before spawning buttons.
        StartCoroutine(WaitForInstallerThenSpawn());
    }

    /// <summary>
    /// Polls StreamingAssetsInstaller.IsReady every frame until the copy coroutine finishes,
    /// then spawns the floor buttons so they reflect the correct locked/unlocked state.
    /// </summary>
    private IEnumerator WaitForInstallerThenSpawn()
    {
        while (!StreamingAssetsInstaller.IsReady)
            yield return null;

        SpawnButtons();
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears any existing children, then instantiates one FloorButton per floor slot.
    /// Each button receives the FloorSaveSystem reference so it can check its own save state.
    /// A VerticalLayoutGroup on contentContainer handles positioning automatically.
    /// </summary>
    public void SpawnButtons()
    {
        ClearButtons();

        _spawnedButtons = new FloorButton[floorCount];

        EnsureLayoutGroup();
        EnsureContentSizeFitter();

        // Floors are displayed from top (highest number) to bottom (floor 1)
        // so the player scrolls downward to reach lower floors.
        for (int i = floorCount; i >= 1; i--)
        {
            FloorButton instance = Instantiate(floorButtonPrefab, contentContainer);

            // Pass the save system so each button independently resolves its locked state.
            instance.Initialise(i, floorSaveSystem);

            _spawnedButtons[floorCount - i] = instance;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ClearButtons()
    {
        foreach (Transform child in contentContainer)
            Destroy(child.gameObject);
    }

    /// <summary>
    /// Adds or updates a VerticalLayoutGroup on the content container.
    /// </summary>
    private void EnsureLayoutGroup()
    {
        VerticalLayoutGroup vlg = contentContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
            vlg = contentContainer.gameObject.AddComponent<VerticalLayoutGroup>();

        vlg.spacing               = spacing;
        vlg.childAlignment        = TextAnchor.UpperCenter;
        vlg.childControlWidth     = true;
        vlg.childControlHeight    = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding               = new RectOffset(20, 20, 30, 30);
    }

    /// <summary>
    /// Adds a ContentSizeFitter so the content container grows with its children.
    /// </summary>
    private void EnsureContentSizeFitter()
    {
        ContentSizeFitter csf = contentContainer.GetComponent<ContentSizeFitter>();
        if (csf == null)
            csf = contentContainer.gameObject.AddComponent<ContentSizeFitter>();

        csf.verticalFit   = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }
}
