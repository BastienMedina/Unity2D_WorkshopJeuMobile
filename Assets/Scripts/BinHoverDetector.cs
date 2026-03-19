using System.Collections;
using UnityEngine;

/// <summary>
/// MonoBehaviour attached to each bin slot.
/// Detects when a dragged document enters or exits the bin zone and triggers a smooth
/// visual expansion animation on the bin's RectTransform.
/// Does NOT validate documents, does NOT modify sorting rules, and does NOT handle drop logic.
/// </summary>
public class BinHoverDetector : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>The SortingBin this detector is co-located with. Assigned in the Inspector.</summary>
    [SerializeField] private SortingBin targetBin;

    /// <summary>The RectTransform of the bin that will be resized during hover. Assigned in the Inspector.</summary>
    [SerializeField] private RectTransform binRectTransform;

    /// <summary>
    /// Default size of the bin at rest, set by BinLayoutManager at activation time.
    /// [SerializeField] so a designer can preview or override the value without touching code.
    /// </summary>
    [SerializeField] private Vector2 normalSize;

    /// <summary>
    /// Size the bin expands to when a document hovers over it.
    /// [SerializeField] so the expansion amount can be tuned in the Inspector without code changes.
    /// </summary>
    [SerializeField] private Vector2 expandedSize;

    /// <summary>
    /// Speed multiplier for the Lerp-based size animation.
    /// Higher values produce snappier, lower values produce softer transitions.
    /// </summary>
    [SerializeField] private float expandSpeed = 8f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>Tracks whether the bin is currently in its expanded hover state.</summary>
    private bool isExpanded;

    /// <summary>Reference to the running animation coroutine so it can be stopped before starting a new one.</summary>
    private Coroutine expansionCoroutine;

    // -------------------------------------------------------------------------
    // Public API — called by DraggableDocument during OnDrag / OnEndDrag
    // -------------------------------------------------------------------------

    /// <summary>
    /// Sets the normalSize used as the animation origin, called by BinLayoutManager at activation.
    /// </summary>
    /// <param name="size">The current sizeDelta of the bin at rest.</param>
    public void SetNormalSize(Vector2 size)
    {
        normalSize = size;
    }

    /// <summary>
    /// Sets the expandedSize the bin will animate toward on hover, called by BinLayoutManager at activation.
    /// </summary>
    /// <param name="size">The target sizeDelta when a document hovers over the bin.</param>
    public void SetExpandedSize(Vector2 size)
    {
        expandedSize = size;
    }

    /// <summary>
    /// Called by DraggableDocument when a dragged document enters this bin's area.
    /// Stops any in-progress animation and starts expanding toward expandedSize.
    /// </summary>
    public void OnDocumentEntered()
    {
        // Stop the previous animation safely before starting a new one.
        // Without this, two coroutines would fight over sizeDelta and produce visual jitter.
        StopExpansionCoroutineSafely();

        isExpanded = true;
        expansionCoroutine = StartCoroutine(AnimateSizeTo(expandedSize));
    }

    /// <summary>
    /// Called by DraggableDocument when a dragged document leaves this bin's area or is dropped.
    /// Stops any in-progress animation and starts shrinking back toward normalSize.
    /// </summary>
    public void OnDocumentExited()
    {
        // Stop the previous animation safely before starting a new one.
        // Without this, two coroutines would fight over sizeDelta and produce visual jitter.
        StopExpansionCoroutineSafely();

        isExpanded = false;
        expansionCoroutine = StartCoroutine(AnimateSizeTo(normalSize));
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Stops the running expansionCoroutine if one is active.
    /// Centralised to avoid null checks at every call site.
    /// </summary>
    private void StopExpansionCoroutineSafely()
    {
        if (expansionCoroutine != null)
            StopCoroutine(expansionCoroutine);
    }

    /// <summary>
    /// Smoothly animates the bin's sizeDelta toward targetSize using Lerp each frame.
    /// Snaps to the exact target value at the end to prevent an infinite Lerp that
    /// asymptotically approaches but never fully reaches the destination.
    /// </summary>
    /// <param name="targetSize">The final sizeDelta value to animate toward.</param>
    private IEnumerator AnimateSizeTo(Vector2 targetSize)
    {
        // Continue animating while the current size is meaningfully different from the target.
        // sqrMagnitude avoids the Sqrt in Vector2.Distance — cheaper for per-frame comparison.
        while ((binRectTransform.sizeDelta - targetSize).sqrMagnitude > 0.01f)
        {
            binRectTransform.sizeDelta = Vector2.Lerp(
                binRectTransform.sizeDelta,
                targetSize,
                expandSpeed * Time.deltaTime
            );

            yield return null;
        }

        // Snap to exact target at animation end — Lerp never reaches its destination exactly,
        // so without this final assignment the bin would drift by a sub-pixel indefinitely.
        binRectTransform.sizeDelta = targetSize;
        expansionCoroutine = null;
    }
}
