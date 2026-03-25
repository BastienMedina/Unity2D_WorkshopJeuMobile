using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that activates and positions SortingBin slots based on the number of
/// bins required by the current day's difficulty settings.
/// Does NOT generate rules, validate documents, compute difficulty, or spawn documents.
/// Bin visibility is the single responsibility of this class.
/// </summary>
public class BinLayoutManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// The five available bin slot GameObjects, ordered by slot position:
    /// [0] LEFT_TOP, [1] LEFT_BOTTOM, [2] RIGHT_TOP, [3] RIGHT_BOTTOM, [4] BOTTOM.
    /// Each entry must carry a SortingBin component.
    /// </summary>
    [SerializeField] private List<RectTransform> binSlots;

    /// <summary>
    /// The order in which bins are activated as difficulty increases.
    /// Default: {0, 2, 4, 1, 3} — left/right top first, then bottom, then left/right bottom.
    /// This order ensures visual balance: the two most prominent slots appear first,
    /// the thumb-rest bottom slot appears third, and the lower side slots last.
    /// </summary>
    [SerializeField] private int[] activationOrder = { 0, 2, 4, 1, 3 };

    // -------------------------------------------------------------------------
    // Bounds — serialized, never hardcoded
    // -------------------------------------------------------------------------

    /// <summary>Minimum number of bins that can ever be active. Prevents a zero-bin state.</summary>
    [SerializeField] private int minimumBinCount = 1;

    /// <summary>Maximum number of bins that can ever be active. Matches the slot list size.</summary>
    [SerializeField] private int maximumBinCount = 5;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// How many bins were active after the most recent SetActiveBinCount call.
    /// Cached so GameManager can read the previous count before calling SetActiveBinCount
    /// again, enabling it to detect whether a new bin was added this day.
    /// </summary>
    private int currentActiveBinCount = 0;

    /// <summary>
    /// The bin ID of the most recently activated slot, or null if no new bin was activated
    /// during the last SetActiveBinCount call.
    /// Reset to null at the start of every SetActiveBinCount call and set only when a slot
    /// transitions from inactive to active — not when the count stays the same.
    /// </summary>
    private string lastActivatedBinID = null;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Activates the first <paramref name="count"/> bins in activationOrder and deactivates the rest.
    /// Also configures the BinHoverDetector on each slot so normalSize and expandedSize always
    /// reflect the current layout — computed at activation time, not baked as prefab defaults.
    /// Deactivating unused slots ensures the player is never confused by empty visible bin zones
    /// that could receive dropped documents without giving any visual feedback.
    /// Called by GameManager at the start of every day after computing DifficultySettings.
    /// </summary>
    /// <param name="count">How many bins should be visible and interactive this day.</param>
    public void SetActiveBinCount(int count)
    {
        // Clamp prevents out-of-range activation attempts from both directions:
        // zero bins would break GameManager (divide-by-zero in rules-per-bin),
        // and more than five would overrun the activationOrder array.
        int clampedCount = Mathf.Clamp(count, minimumBinCount, maximumBinCount);

        // Reset every call so GetLastActivatedBinID only returns a value when this
        // specific SetActiveBinCount call actually activated a new slot.
        lastActivatedBinID = null;

        for (int orderIndex = 0; orderIndex < activationOrder.Length; orderIndex++)
        {
            int slotIndex = activationOrder[orderIndex];

            // Guard: skip silently if the slot list is shorter than activationOrder.
            // This prevents an IndexOutOfRangeException when the Inspector list is incomplete.
            if (slotIndex >= binSlots.Count)
                continue;

            RectTransform slotRectTransform = binSlots[slotIndex];
            bool shouldBeActive             = orderIndex < clampedCount;
            bool wasAlreadyActive           = slotRectTransform.gameObject.activeSelf;

            slotRectTransform.gameObject.SetActive(shouldBeActive);

            // Record the first slot that just transitioned from inactive to active —
            // GameManager uses this to build the new-bin description in the summary.
            // Only the first newly activated slot is recorded; subsequent ones would mean
            // the count jumped by more than one, which the current difficulty system never does.
            bool isNewlyActivated = shouldBeActive && !wasAlreadyActive;

            if (isNewlyActivated && lastActivatedBinID == null)
            {
                SortingBin newBin = slotRectTransform.GetComponent<SortingBin>();

                if (newBin != null)
                    lastActivatedBinID = newBin.GetBinID();
            }

            BinHoverDetector hoverDetector = slotRectTransform.GetComponent<BinHoverDetector>();

            if (shouldBeActive)
            {
                // Compute and inject sizes when activating so BinHoverDetector always reflects
                // the current sizeDelta rather than a stale prefab default that may differ after
                // layout changes are applied at runtime.
                if (hoverDetector != null)
                {
                    Vector2 currentNormalSize = slotRectTransform.sizeDelta;

                    // expandedSize is derived from normalSize at activation time so it scales
                    // proportionally with whatever layout BinLayoutManager just applied.
                    // The offset (+60 width, +80 height) gives a noticeable but controlled expansion
                    // that does not risk overlapping the document zone.
                    Vector2 computedExpandedSize = currentNormalSize + new Vector2(60f, 80f);

                    hoverDetector.SetNormalSize(currentNormalSize);
                    hoverDetector.SetExpandedSize(computedExpandedSize);
                    hoverDetector.enabled = true;
                }
            }
            else
            {
                // Disable the detector on inactive slots so stale coroutines cannot
                // run on hidden GameObjects and produce unnecessary per-frame overhead.
                if (hoverDetector != null)
                    hoverDetector.enabled = false;
            }
        }

        currentActiveBinCount = clampedCount;
    }

    /// <summary>
    /// Returns the number of bins that were active after the most recent SetActiveBinCount call.
    /// GameManager reads this BEFORE calling SetActiveBinCount so it can compare the old and new
    /// counts to detect whether a new bin was activated this day.
    /// </summary>
    /// <returns>Current active bin count (already clamped to [minimumBinCount, maximumBinCount]).</returns>
    public int GetActiveBinCount()
    {
        return currentActiveBinCount;
    }

    /// <summary>
    /// Returns the bin ID of the slot that was most recently activated by SetActiveBinCount,
    /// or null if the last call did not activate any new slot (count was unchanged or decreased).
    /// GameManager uses this to build the "New bin: …" entry in DifficultyChangeSummary
    /// without having to compare two full bin lists itself.
    /// </summary>
    /// <returns>The bin ID of the newly activated slot, or null.</returns>
    public string GetLastActivatedBinID()
    {
        return lastActivatedBinID;
    }

    /// <summary>
    /// Returns the SortingBin components from every bin slot regardless of active state.
    /// Called by GameManager.RefreshBinSubscriptions to unsubscribe stale event delegates
    /// from ALL bins — including inactive ones — before re-subscribing only to active ones.
    /// Iterating only active bins would leave stale subscriptions on bins that were active
    /// in a previous day but deactivated for the current one, causing duplicate events.
    /// </summary>
    /// <returns>
    /// A list of every SortingBin in binSlots, active and inactive, in slot-list order.
    /// </returns>
    public List<SortingBin> GetAllBins()
    {
        List<SortingBin> allBins = new List<SortingBin>();

        foreach (RectTransform slot in binSlots)
        {
            // Guard: the slot RectTransform may have been destroyed during scene teardown
            // (OnDestroy order is not guaranteed in Unity). Skip destroyed references
            // silently to avoid MissingReferenceException on Play Mode exit.
            if (slot == null)
                continue;

            SortingBin sortingBin = slot.GetComponent<SortingBin>();

            if (sortingBin == null)
            {
                Debug.LogWarning($"[BinLayoutManager] Slot '{slot.name}' has no SortingBin component. " +
                                  "Assign a SortingBin to every entry in binSlots.");
                continue;
            }

            allBins.Add(sortingBin);
        }

        return allBins;
    }

    /// <summary>
    /// Returns the SortingBin components from every currently active bin slot.
    /// Called by GameManager to build the rule distribution target list after SetActiveBinCount.
    /// Only active GameObjects are included — inactive slots are invisible to the player
    /// and must not receive rules or participate in document validation.
    /// </summary>
    /// <returns>
    /// A list of SortingBin instances from all active slots, in slot-list order.
    /// </returns>
    public List<SortingBin> GetActiveBins()
    {
        List<SortingBin> activeBins = new List<SortingBin>();

        foreach (RectTransform slot in binSlots)
        {
            // Skip inactive slots: they have been hidden by SetActiveBinCount and must
            // not receive rules or be queried during validation this day.
            if (!slot.gameObject.activeSelf)
                continue;

            SortingBin sortingBin = slot.GetComponent<SortingBin>();

            // Guard: a slot without a SortingBin is a misconfigured prefab — warn clearly
            // rather than silently omitting it and producing a hard-to-trace rules mismatch.
            if (sortingBin == null)
            {
                Debug.LogWarning($"[BinLayoutManager] Slot '{slot.name}' has no SortingBin component. " +
                                  "Assign a SortingBin to every entry in binSlots.");
                continue;
            }

            activeBins.Add(sortingBin);
        }

        return activeBins;
    }
}
