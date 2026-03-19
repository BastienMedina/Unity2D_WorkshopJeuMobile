using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that manages the visual document stack in the centre of the screen.
/// Controls which document is on top and owns document lifetime (creation tracking and destruction).
/// Fires onStackBelowTarget when the total document count drops below targetStackSize
/// so DocumentSpawner can refill immediately without any coroutine or timing dependency.
/// Does NOT spawn documents, does NOT validate drops, does NOT generate content,
/// does NOT apply drag shrink or fade — DraggableDocument handles its own drag visuals.
/// </summary>
public class DocumentStackManager : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// The RectTransform of the centre stack zone. All documents are parented here
    /// and centred on it when they become the top of the stack.
    /// </summary>
    [SerializeField] private RectTransform stackZone;

    /// <summary>
    /// Unused at runtime: documents are sized by stretch anchors inside DocumentStackZone.
    /// Kept for backward-compatibility with existing Inspector serialisation; value is ignored
    /// by ShowNextDocument which now calls RestoreStretchAnchors instead.
    /// </summary>
    [SerializeField] private Vector2 stackDocumentSize = new Vector2(0f, 0f);

    /// <summary>
    /// Number of documents the stack should always contain (queue + top).
    /// [SerializeField] so designers can tune stack depth without touching code —
    /// different difficulty levels or game modes may want different stack depths.
    /// </summary>
    [SerializeField] public int targetStackSize = 3;

    // -------------------------------------------------------------------------
    // Events
    // -------------------------------------------------------------------------

    /// <summary>
    /// Fired once per missing document whenever the total document count drops below targetStackSize.
    /// Event-based refill keeps DocumentStackManager decoupled from DocumentSpawner:
    /// DocumentStackManager signals the need, DocumentSpawner decides how to satisfy it.
    /// </summary>
    public event Action onStackBelowTarget;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Documents waiting to be shown, in arrival order.
    /// LinkedList is used instead of Queue because it supports O(1) front insertion,
    /// which is required when a cancelled drag returns a document to the top of the stack.
    /// Queue&lt;T&gt; does not support front insertion natively.
    /// New documents are added at the back (AddLast); the front node is always shown next.
    /// </summary>
    private LinkedList<GameObject> documentQueue = new LinkedList<GameObject>();

    /// <summary>
    /// The document currently rendered on top of the stack and available for drag.
    /// Null when the stack is empty.
    /// </summary>
    private GameObject currentTopDocument;

    // -------------------------------------------------------------------------
    // Public API — called by DocumentSpawner and DraggableDocument
    // -------------------------------------------------------------------------

    /// <summary>
    /// Returns the total number of documents currently managed by this stack:
    /// the visible top document (if any) plus all queued documents.
    /// Public so DocumentSpawner can read it during FillStackToTarget on day start.
    /// </summary>
    /// <returns>
    /// Count of all live documents — includes currentTopDocument and every queued document.
    /// </returns>
    public int GetTotalDocumentCount()
    {
        // Total count includes both the visible top document and all queued documents —
        // counting only the queue would miss the top document and cause one extra spawn.
        return documentQueue.Count + (currentTopDocument != null ? 1 : 0);
    }

    /// <summary>
    /// Adds a document to the back of the queue.
    /// If no document is currently displayed, immediately promotes the new arrival to the top.
    /// This avoids a one-frame gap where the stack appears empty between a spawn and its display.
    /// Calls CheckAndRequestRefill after enqueueing so any remaining gap triggers more spawns.
    /// </summary>
    /// <param name="documentObject">The document GameObject just instantiated by DocumentSpawner.</param>
    public void EnqueueDocument(GameObject documentObject)
    {
        // Sibling index 0 = rendered first = behind all other siblings in the Canvas.
        // New documents must always appear behind the current top document and any queued
        // documents so the stack looks natural (the top card is always in front).
        documentObject.transform.SetSiblingIndex(0);

        // Queued documents that are not the current top must be invisible —
        // only the top document is shown; all others must not be seen or interacted with.
        CanvasGroup enqueueCanvasGroup = documentObject.GetComponent<CanvasGroup>();
        if (enqueueCanvasGroup != null)
        {
            enqueueCanvasGroup.alpha = 0f;

            // Invisible queued documents must not intercept touch or drag events —
            // a hidden document that blocks raycasts would swallow input silently.
            enqueueCanvasGroup.blocksRaycasts = false;
        }

        documentQueue.AddLast(documentObject);

        // If there is no current top document, the queue was empty before this enqueue —
        // promote immediately so the player always sees the next document without delay.
        bool hasNoCurrentDocument = currentTopDocument == null;
        if (hasNoCurrentDocument)
            ShowNextDocument();

        // Any enqueue may satisfy part of a deficit — check again in case the stack
        // still needs more documents after this one was added.
        CheckAndRequestRefill();
    }

    /// <summary>
    /// Dequeues the front document and presents it as the new top of the stack.
    /// Resets the document's anchors and alpha to the correct stack state.
    /// If the queue is empty, sets currentTopDocument to null and returns.
    /// Calls CheckAndRequestRefill after promoting so a depleted queue triggers new spawns.
    /// </summary>
    public void ShowNextDocument()
    {
        if (documentQueue.Count == 0)
        {
            currentTopDocument = null;

            // Queue is empty after dequeue — request a refill immediately so the stack
            // never stays empty for more than a single frame.
            CheckAndRequestRefill();
            return;
        }

        GameObject nextDocument = documentQueue.First.Value;
        documentQueue.RemoveFirst();

        RectTransform documentRectTransform = nextDocument.GetComponent<RectTransform>();

        // Document stretches to fill the entire DocumentStackZone using stretch anchors,
        // adapting to any screen size automatically without a fixed pixel size.
        RestoreStretchAnchors(documentRectTransform);

        // Top document must render in front of all queued documents —
        // last sibling = rendered last = drawn on top of everything behind it.
        nextDocument.transform.SetAsLastSibling();

        // Only the top document should be visible and interactive.
        // Queued documents behind it have alpha 0 and blocksRaycasts false (set in EnqueueDocument).
        CanvasGroup topCanvasGroup = nextDocument.GetComponent<CanvasGroup>();
        if (topCanvasGroup != null)
        {
            topCanvasGroup.alpha          = 1f;
            topCanvasGroup.blocksRaycasts = true;
        }

        // Only the top document should receive pointer events — all others must be inert
        // so that fingers touching the stack area only ever interact with the front document.
        SetDocumentRaycastBlocking(nextDocument, true);

        currentTopDocument = nextDocument;

        // Dequeue reduced the queue — check if a refill is now needed.
        CheckAndRequestRefill();
    }

    /// <summary>
    /// Responds to the player beginning to drag <paramref name="documentObject"/>.
    /// Removes the document from the stack immediately so the next document becomes visible,
    /// giving the player a clear view of what comes next while the current one is being dragged.
    /// All drag visuals (size, alpha) are applied by DraggableDocument — this method only
    /// manages stack ownership and promotes the next document.
    /// </summary>
    /// <param name="documentObject">The document whose drag just started.</param>
    public void OnDocumentDragStarted(GameObject documentObject)
    {
        // Only the top document is interactive — DraggableDocument's blocksRaycasts=false
        // guard prevents queued documents from receiving drag events, but this check is a
        // defensive backstop against any edge case where that guard is bypassed.
        bool isNotTopDocument = documentObject != currentTopDocument;
        if (isNotTopDocument)
            return;

        // Document leaves the stack entirely on drag — it is no longer the tracked top.
        // Nulling currentTopDocument before calling ShowNextDocument allows the next
        // document to be correctly promoted without being blocked by the dragging one.
        currentTopDocument = null;

        // Dragged document must render above everything, including the next document that
        // just became visible in the stack zone — SetAsLastSibling ensures it stays on top
        // of all siblings regardless of which document ShowNextDocument promotes next.
        documentObject.transform.SetAsLastSibling();

        // Reveal the next document immediately so the stack zone is never empty during a drag.
        // The player can see what comes next before completing the current drop.
        ShowNextDocument();

        // Do NOT modify the dragged document's size or alpha here.
        // DraggableDocument owns its own drag visuals — DocumentStackManager must not
        // fight against the drag size set by DraggableDocument.OnBeginDrag.
    }

    /// <summary>
    /// Responds to a drag being cancelled (document dropped outside any bin).
    /// Re-inserts the document at the front of the queue so the player sees it again immediately,
    /// then promotes it to the top of the stack.
    /// Does NOT call Destroy — document is returned to the stack, not removed.
    /// </summary>
    /// <param name="documentObject">The document whose drag was cancelled.</param>
    public void OnDocumentDragCancelled(GameObject documentObject)
    {
        // ShowNextDocument was already called when the drag started — calling it again here
        // would skip whichever document is currently showing and promote the one behind it.
        // Instead, if a document became the top during the drag, push it back into the queue
        // so the returned document can take precedence at the front.
        bool hasCurrentTopDocument = currentTopDocument != null;
        if (hasCurrentTopDocument)
        {
            // The document that appeared during the drag must go behind the returned one —
            // re-inserting it at the back preserves its original queue position.
            documentQueue.AddFirst(currentTopDocument);
            currentTopDocument = null;
        }

        // Restore stretch anchors so the document fills DocumentStackZone correctly
        // when it becomes the top document again.
        RectTransform documentRectTransform = documentObject.GetComponent<RectTransform>();
        RestoreStretchAnchors(documentRectTransform);

        // Cancelled document returns to the TOP of the stack — the player should not
        // lose their place. InsertAtFront ensures it is shown before all other queued documents.
        InsertAtFront(documentObject);

        // ShowNextDocument handles sibling index and CanvasGroup state correctly for the
        // returned document — no extra alpha or SetSiblingIndex management needed here.
        // It will call SetAsLastSibling and restore alpha = 1 / blocksRaycasts = true.
        ShowNextDocument();

        // Cancelled drag may have temporarily reduced the count below targetStackSize —
        // request a refill for any remaining gap.
        CheckAndRequestRefill();
    }

    /// <summary>
    /// Destroys <paramref name="documentObject"/> and removes it from the scene.
    /// Called after a valid or invalid drop has been fully resolved — the stack manager
    /// owns document lifetime and is the correct place to issue the Destroy call.
    /// Calls CheckAndRequestRefill so the stack is immediately topped back up to targetStackSize.
    /// </summary>
    /// <param name="documentObject">The document to remove from the scene.</param>
    public void OnDocumentRemoved(GameObject documentObject)
    {
        Destroy(documentObject);

        // A document was permanently removed — the total count just dropped by one.
        // Request an immediate refill so the stack never sits below targetStackSize.
        CheckAndRequestRefill();
    }

    /// <summary>
    /// Destroys every document in the queue and the current top document, then resets state.
    /// Called by GameManager at the end of each day before starting the transition overlay.
    /// Null-checks prevent exceptions when currentTopDocument was already destroyed by a valid drop.
    /// Clears onStackBelowTarget subscribers to prevent ghost spawns after the day ends —
    /// DocumentSpawner unsubscribes via StopDay(), but ClearStack provides a defensive backstop.
    /// </summary>
    public void ClearStack()
    {
        // Suppress refill events during cleanup — the day is over and no new documents
        // should spawn while the stack is being torn down.
        onStackBelowTarget = null;

        foreach (GameObject queuedDocument in documentQueue)
        {
            // Guard: a queued document could have been destroyed externally in an edge case.
            if (queuedDocument == null)
                continue;

            Destroy(queuedDocument);
        }

        documentQueue.Clear();

        // Null-check is mandatory: currentTopDocument is null when the stack is empty
        // (all documents were sorted), and Destroy(null) produces a warning in Unity.
        if (currentTopDocument != null)
        {
            Destroy(currentTopDocument);
            currentTopDocument = null;
        }
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Computes how many documents are missing from the stack and fires onStackBelowTarget
    /// once per missing document so DocumentSpawner spawns exactly the right number.
    /// Firing once per missing document (not once total) ensures the stack reaches
    /// targetStackSize in a single call rather than refilling one document per event cycle.
    /// </summary>
    private void CheckAndRequestRefill()
    {
        int missing = targetStackSize - GetTotalDocumentCount();

        // No deficit — nothing to do.
        if (missing <= 0)
            return;

        for (int i = 0; i < missing; i++)
            onStackBelowTarget?.Invoke();
    }

    /// <summary>
    /// Inserts <paramref name="documentObject"/> at the front of documentQueue.
    /// LinkedList supports O(1) front insertion via AddFirst — Queue&lt;T&gt; does not.
    /// This is why the queue was migrated from Queue to LinkedList: to allow a cancelled
    /// document to return to the top of the stack without rebuilding the entire collection.
    /// </summary>
    /// <param name="documentObject">The document to place at the head of the queue.</param>
    private void InsertAtFront(GameObject documentObject)
    {
        documentQueue.AddFirst(documentObject);
    }

    /// <summary>
    /// Sets <paramref name="rt"/> to full-stretch anchors so it fills its parent completely.
    /// Called in ShowNextDocument and OnDocumentDragCancelled to ensure the document
    /// always adapts to the DocumentStackZone size on any screen resolution.
    /// </summary>
    /// <param name="rt">The RectTransform to reset to stretch layout.</param>
    private void RestoreStretchAnchors(RectTransform rt)
    {
        rt.anchorMin        = Vector2.zero;
        rt.anchorMax        = Vector2.one;
        rt.sizeDelta        = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.pivot            = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// Sets the blocksRaycasts flag on the document's CanvasGroup.
    /// When true, the document is interactive (pointer events land on it).
    /// When false, pointer events pass through, preventing accidental drag of queued documents.
    /// </summary>
    /// <param name="documentObject">The document whose raycast blocking should be updated.</param>
    /// <param name="shouldBlockRaycasts">True to make the document interactive, false to make it inert.</param>
    private void SetDocumentRaycastBlocking(GameObject documentObject, bool shouldBlockRaycasts)
    {
        CanvasGroup documentCanvasGroup = documentObject.GetComponent<CanvasGroup>();

        if (documentCanvasGroup == null)
            return;

        documentCanvasGroup.blocksRaycasts = shouldBlockRaycasts;
    }
}
