using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour attached to each BinSlot GameObject.
/// Reads the rules assigned to the co-located SortingBin and renders the corresponding
/// specificity icons inside a child container.
/// For Branch rules, conditionB is negated: its icon is shown with the negation sprite overlaid.
/// All icon GameObjects are created and destroyed dynamically — no manual prefab wiring required.
/// Does NOT validate documents, does NOT modify rules, and does NOT reference SortingBin directly
/// for any logic: it only reacts to the RefreshIcons call made by SortingBin after AssignRules.
/// </summary>
public class BinIconDisplay : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// Database that maps specificity keys to their icon sprites and holds the negation overlay.
    /// Assign the SpecificityIconDatabase asset in the Inspector.
    /// </summary>
    [SerializeField] private SpecificityIconDatabase iconDatabase;

    /// <summary>
    /// RectTransform acting as the parent for all generated icon GameObjects.
    /// Should be a child of the BinSlot with a HorizontalLayoutGroup or similar layout component.
    /// If left null, icons are parented directly to this GameObject's transform.
    /// </summary>
    [SerializeField] private RectTransform iconsContainer;

    /// <summary>
    /// Size in pixels applied to each icon Image RectTransform.
    /// Uniform for all icons; tune in the Inspector to match your visual design.
    /// </summary>
    [SerializeField] private Vector2 iconSize = new Vector2(48f, 48f);

    // -------------------------------------------------------------------------
    // Constants
    // -------------------------------------------------------------------------

    private const string IconWrapperName = "IconWrapper";
    private const string BaseIconName    = "SpecificityIcon";
    private const string NegationIconName = "NegationOverlay";

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Destroys all previously generated icons and rebuilds the icon set from the provided rules.
    /// Called by SortingBin immediately after AssignRules so icons always mirror current rules.
    /// Forces a layout rebuild after populating so ContentSizeFitter recalculates in the same frame.
    /// </summary>
    /// <param name="rules">Rules currently assigned to the bin this frame.</param>
    public void RefreshIcons(List<RuleData> rules)
    {
        ClearIcons();

        if (iconDatabase == null)
        {
            Debug.LogWarning($"[BinIconDisplay] No SpecificityIconDatabase assigned on {gameObject.name}. Icons will not be displayed.");
            return;
        }

        RectTransform parent = iconsContainer != null ? iconsContainer : (RectTransform)transform;

        foreach (RuleData rule in rules)
            BuildIconsForRule(rule, parent);

        // Force immediate layout recalculation so ContentSizeFitter resizes the container
        // to fit the newly added icons in the same frame they are created.
        LayoutRebuilder.ForceRebuildLayoutImmediate(parent);
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Immediately destroys all child GameObjects of the icons container using DestroyImmediate.
    /// Destroy() is deferred to end-of-frame, which means newly added icons coexist with stale
    /// ones during the same frame — this breaks ContentSizeFitter recalculation.
    /// DestroyImmediate guarantees the hierarchy is clean before new icons are added.
    /// </summary>
    private void ClearIcons()
    {
        RectTransform parent = iconsContainer != null ? iconsContainer : (RectTransform)transform;

        for (int i = parent.childCount - 1; i >= 0; i--)
            DestroyImmediate(parent.GetChild(i).gameObject);
    }

    /// <summary>
    /// Generates icon GameObjects for a single rule.
    /// Simple    → one affirmative icon for conditionA.
    /// Multiple  → one affirmative icon for conditionA, one for conditionB.
    /// Branch    → one affirmative icon for conditionA, one negated icon for conditionB.
    /// </summary>
    private void BuildIconsForRule(RuleData rule, RectTransform parent)
    {
        switch (rule.ruleType)
        {
            case RuleType.Simple:
                BuildAffirmativeIcon(rule.conditionA, parent);
                break;

            case RuleType.Multiple:
                BuildAffirmativeIcon(rule.conditionA, parent);
                BuildAffirmativeIcon(rule.conditionB, parent);
                break;

            case RuleType.Branch:
                BuildAffirmativeIcon(rule.conditionA, parent);
                BuildNegatedIcon(rule.conditionB, parent);
                break;
        }
    }

    /// <summary>
    /// Creates a single Image GameObject for an affirmative specificity condition.
    /// Skipped silently if the specificity key has no matching sprite in the database.
    /// </summary>
    private void BuildAffirmativeIcon(string specificityKey, RectTransform parent)
    {
        if (string.IsNullOrEmpty(specificityKey))
            return;

        Sprite icon = iconDatabase.GetIcon(specificityKey);

        if (icon == null)
        {
            Debug.LogWarning($"[BinIconDisplay] No icon found for specificity '{specificityKey}' in {iconDatabase.name}.");
            return;
        }

        GameObject wrapper = CreateWrapper(parent);
        CreateImageChild(wrapper.transform, BaseIconName, icon);
    }

    /// <summary>
    /// Creates a wrapper GameObject containing the specificity icon with the negation overlay on top.
    /// The overlay Image is rendered last (highest sibling index) so it always appears above the base icon.
    /// Skipped silently if either sprite is missing.
    /// </summary>
    private void BuildNegatedIcon(string specificityKey, RectTransform parent)
    {
        if (string.IsNullOrEmpty(specificityKey))
            return;

        Sprite icon = iconDatabase.GetIcon(specificityKey);

        if (icon == null)
        {
            Debug.LogWarning($"[BinIconDisplay] No icon found for negated specificity '{specificityKey}' in {iconDatabase.name}.");
            return;
        }

        if (iconDatabase.negationIcon == null)
        {
            Debug.LogWarning($"[BinIconDisplay] NegationIcon is not assigned in {iconDatabase.name}. Negated icon will display without overlay.");
        }

        GameObject wrapper = CreateWrapper(parent);
        CreateImageChild(wrapper.transform, BaseIconName, icon);

        // Overlay is added after the base icon so it is rendered on top (higher sibling index = rendered last in UI).
        if (iconDatabase.negationIcon != null)
            CreateImageChild(wrapper.transform, NegationIconName, iconDatabase.negationIcon);
    }

    /// <summary>
    /// Creates a wrapper RectTransform parented to the icons container.
    /// A LayoutElement with explicit preferred dimensions is added so the VerticalLayoutGroup
    /// (running in childControl mode) correctly measures the wrapper size when computing
    /// the ContentSizeFitter preferred height for the container.
    /// </summary>
    private GameObject CreateWrapper(RectTransform parent)
    {
        GameObject wrapper = new GameObject(IconWrapperName, typeof(RectTransform), typeof(LayoutElement));
        RectTransform wrapperRect = wrapper.GetComponent<RectTransform>();
        wrapperRect.SetParent(parent, false);
        wrapperRect.sizeDelta = iconSize;

        // LayoutElement tells the VerticalLayoutGroup exactly how much space this wrapper
        // needs — required when childControlWidth/Height are true on the layout group.
        LayoutElement layoutElement = wrapper.GetComponent<LayoutElement>();
        layoutElement.preferredWidth  = iconSize.x;
        layoutElement.preferredHeight = iconSize.y;
        layoutElement.minWidth        = iconSize.x;
        layoutElement.minHeight       = iconSize.y;

        return wrapper;
    }

    /// <summary>
    /// Creates a child GameObject with an Image component stretched to fill its parent wrapper.
    /// </summary>
    private void CreateImageChild(Transform wrapperTransform, string objectName, Sprite sprite)
    {
        GameObject iconGO = new GameObject(objectName, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        RectTransform iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.SetParent(wrapperTransform, false);

        // Stretch to fill the wrapper so the overlay exactly covers the base icon.
        iconRect.anchorMin        = Vector2.zero;
        iconRect.anchorMax        = Vector2.one;
        iconRect.offsetMin        = Vector2.zero;
        iconRect.offsetMax        = Vector2.zero;

        Image image = iconGO.GetComponent<Image>();
        image.sprite              = sprite;
        image.preserveAspect      = true;
        image.raycastTarget       = false;
    }
}
