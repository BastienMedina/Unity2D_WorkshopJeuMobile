using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Dynamically instantiates floor-selection buttons inside a ScrollRect content container.
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

    /// <summary>Total number of floors to generate (buttons 1 … floorCount).</summary>
    [SerializeField] private int floorCount = 100;

    /// <summary>Vertical spacing between buttons in pixels.</summary>
    [SerializeField] private float spacing = 20f;

    // -------------------------------------------------------------------------
    // Private state
    // -------------------------------------------------------------------------

    private FloorButton[] _spawnedButtons;

    // -------------------------------------------------------------------------
    // Unity lifecycle
    // -------------------------------------------------------------------------

    private void Start()
    {
        SpawnButtons();
    }

    // -------------------------------------------------------------------------
    // Core logic
    // -------------------------------------------------------------------------

    /// <summary>
    /// Clears any existing children, then instantiates one FloorButton per floor.
    /// A VerticalLayoutGroup on contentContainer handles positioning automatically.
    /// </summary>
    public void SpawnButtons()
    {
        ClearButtons();

        _spawnedButtons = new FloorButton[floorCount];

        // Ensure the content container has the required layout components.
        EnsureLayoutGroup();
        EnsureContentSizeFitter();

        // Floors are displayed from top (highest number) to bottom (floor 1)
        // so the player scrolls downward to reach lower floors — matching the
        // metaphor of descending through the tower list.
        // Change the loop direction here if you prefer ascending order.
        for (int i = floorCount; i >= 1; i--)
        {
            FloorButton instance = Instantiate(floorButtonPrefab, contentContainer);
            instance.Initialise(i);
            _spawnedButtons[floorCount - i] = instance;
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private void ClearButtons()
    {
        foreach (Transform child in contentContainer)
        {
            Destroy(child.gameObject);
        }
    }

    /// <summary>
    /// Adds or updates a VerticalLayoutGroup on the content container.
    /// This drives uniform sizing, centred alignment, and configurable spacing.
    /// </summary>
    private void EnsureLayoutGroup()
    {
        VerticalLayoutGroup vlg = contentContainer.GetComponent<VerticalLayoutGroup>();
        if (vlg == null)
        {
            vlg = contentContainer.gameObject.AddComponent<VerticalLayoutGroup>();
        }

        vlg.spacing = spacing;
        vlg.childAlignment = TextAnchor.UpperCenter;
        vlg.childControlWidth = true;
        vlg.childControlHeight = false;
        vlg.childForceExpandWidth = true;
        vlg.childForceExpandHeight = false;
        vlg.padding = new RectOffset(20, 20, 30, 30);
    }

    /// <summary>
    /// Adds a ContentSizeFitter so the content container grows with its children.
    /// </summary>
    private void EnsureContentSizeFitter()
    {
        ContentSizeFitter csf = contentContainer.GetComponent<ContentSizeFitter>();
        if (csf == null)
        {
            csf = contentContainer.gameObject.AddComponent<ContentSizeFitter>();
        }

        csf.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        csf.horizontalFit = ContentSizeFitter.FitMode.Unconstrained;
    }
}
