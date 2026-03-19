using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// MonoBehaviour that manages the visual document stack in the centre of the screen.
/// Controls which document is on top, handles the drag visual feedback (shrink and fade),
/// and owns document lifetime (creation tracking and destruction).
/// Does NOT spawn documents, does NOT validate drops, does NOT generate content.
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
    /// Absolute sizeDelta applied to the document while it is being dragged.
    /// Reducing to thumb size gives the player clear visibility of the bin they are aiming at,
    /// which is especially important on small mobile screens in portrait orientation.
    /// Applied as an absolute pixel size when drag starts; stretch anchors are restored on cancel.
    /// </summary>
    [SerializeField] private Vector2 draggedDocumentSize = new Vector2(180f, 230f);

    /// <summary>CanvasGroup alpha applied to the document while it is being dragged.</summary>
    [SerializeField] private float draggedAlpha = 0.5f;

    // -------------------------------------------------------------------------
    // Runtime state
    // -------------------------------------------------------------------------

    /// <summary>
    /// Documents waiting to be shown, in arrival order.
    /// New documents are enqueued at the back; the front is always shown next.
    /// </summary>
    private Queue<GameObject> documentQueue = new Queue<GameObject>();

    /// <summary>
    /// The document currently rendered on top of the stack and available for drag.
    /// Null when the stack is empty.
    /// </summary>
    private GameObject currentTopDocument;

    // -------------------------------------------------------------------------
    // Public API — called by DocumentSpawner and DraggableDocument
    // -------------------------------------------------------------------------

    /// <summary>
    /// Adds a document to the end of the queue.
    /// If no document is currently displayed, immediately promotes the new arrival to the top.
    /// This avoids a one-frame gap where the stack appears empty between a spawn and its display.
    /// </summary>
    /// <param name="documentObject">The document GameObject just instantiated by DocumentSpawner.</param>
    public void EnqueueDocument(GameObject documentObject)
    {
        // All queued (non-top) documents must not receive pointer events so the player
        // cannot accidentally begin dragging a document that is not yet on top.
        SetDocumentRaycastBlocking(documentObject, false);

        documentQueue.Enqueue(documentObject);

        // If there is no current top document, the queue was empty before this enqueue —
        // promote immediately so the player always sees the next document without delay.
        bool hasNoCurrentDocument = currentTopDocument == null;
        if (hasNoCurrentDocument)
            ShowNextDocument();
    }

    /// <summary>
    /// Dequeues the next waiting document and presents it as the new top of the stack.
    /// Resets the document's size, alpha, and position to the correct stack state.
    /// If the queue is empty, sets currentTopDocument to null and returns.
    /// </summary>
    public void ShowNextDocument()
    {
        if (documentQueue.Count == 0)
        {
            currentTopDocument = null;
            return;
        }

        GameObject nextDocument = documentQueue.Dequeue();

        RectTransform documentRectTransform = nextDocument.GetComponent<RectTransform>();

        // Document stretches to fill the entire DocumentStackZone using stretch anchors,
        // adapting to any screen size automatically without a fixed pixel size.
        RestoreStretchAnchors(documentRectTransform);

        // Restore full opacity: a document may arrive shrunken or faded if it was
        // previously dragged and cancelled, then re-enqueued at the front.
        CanvasGroup documentCanvasGroup = nextDocument.GetComponent<CanvasGroup>();
        if (documentCanvasGroup != null)
            documentCanvasGroup.alpha = 1f;

        // Render on top of all siblings so no other document visually overlaps the active one.
        nextDocument.transform.SetAsLastSibling();

        // Only the top document should receive pointer events — all others must be inert
        // so that fingers touching the stack area only ever interact with the front document.
        SetDocumentRaycastBlocking(nextDocument, true);

        currentTopDocument = nextDocument;
    }

    /// <summary>
    /// Responds to the player beginning to drag <paramref name="documentObject"/>.
    /// Shrinks the document to thumb size and fades it so the player can see the bins below.
    /// Immediately calls ShowNextDocument so the next document appears beneath the dragged one —
    /// the player can preview what comes next before completing the current drop.
    /// Only the current top document can be dragged; drag events on queued documents are ignored
    /// to prevent interacting with documents the player cannot yet see or act on.
    /// </summary>
    /// <param name="documentObject">The document whose drag just started.</param>
    public void OnDocumentDragStarted(GameObject documentObject)
    {
        // Only the top document is interactive; ignore drags initiated on queued documents.
        // This guard is a safety net — DraggableDocument should already have blocksRaycasts=false
        // on non-top documents, but a defensive check here prevents any edge-case corruption.
        bool isNotTopDocument = documentObject != currentTopDocument;
        if (isNotTopDocument)
            return;

        RectTransform documentRectTransform = documentObject.GetComponent<RectTransform>();

        // Apply absolute sizeDelta as thumb size when dragging starts.
        // Anchors are set to centre-pivot first so sizeDelta is interpreted as an absolute size.
        documentRectTransform.anchorMin       = new Vector2(0.5f, 0.5f);
        documentRectTransform.anchorMax       = new Vector2(0.5f, 0.5f);
        documentRectTransform.pivot           = new Vector2(0.5f, 0.5f);
        documentRectTransform.anchoredPosition = Vector2.zero;
        documentRectTransform.sizeDelta       = draggedDocumentSize;

        CanvasGroup documentCanvasGroup = documentObject.GetComponent<CanvasGroup>();
        if (documentCanvasGroup != null)
            documentCanvasGroup.alpha = draggedAlpha;

        // Clear the top slot first so ShowNextDocument can assign a new top without
        // mistaking the currently-dragged document for the active top.
        currentTopDocument = null;

        // Reveal the next document immediately so it is visible beneath the dragged one —
        // the player sees what they will need to sort next before completing the current drop.
        ShowNextDocument();
    }

    /// <summary>
    /// Responds to a drag being cancelled (document dropped outside any bin).
    /// Restores the document's full size and opacity, re-inserts it at the front of the queue,
    /// and promotes it back to the top position.
    /// Re-inserting at the front (not the back) preserves intended document order — the player
    /// was not penalised for the miss and should see the same document again immediately.
    /// </summary>
    /// <param name="documentObject">The document whose drag was cancelled.</param>
    public void OnDocumentDragCancelled(GameObject documentObject)
    {
        RectTransform documentRectTransform = documentObject.GetComponent<RectTransform>();

        // Restore stretch anchors so the document fills DocumentStackZone again
        // after a cancelled drag, adapting to any screen size automatically.
        RestoreStretchAnchors(documentRectTransform);

        CanvasGroup documentCanvasGroup = documentObject.GetComponent<CanvasGroup>();
        if (documentCanvasGroup != null)
            documentCanvasGroup.alpha = 1f;

        // Re-insert at the front so this document becomes the next to be shown,
        // not pushed to the back of the queue behind documents that arrived later.
        ReinsertAtFrontOfQueue(documentObject);

        ShowNextDocument();
    }

    /// <summary>
    /// Destroys <paramref name="documentObject"/> and removes it from the scene.
    /// Called after a valid or invalid drop has been fully resolved — the stack manager
    /// owns document lifetime and is the correct place to issue the Destroy call.
    /// </summary>
    /// <param name="documentObject">The document to remove from the scene.</param>
    public void OnDocumentRemoved(GameObject documentObject)
    {
        Destroy(documentObject);
    }

    /// <summary>
    /// Destroys every document in the queue and the current top document, then resets state.
    /// Called by GameManager at the end of each day before starting the transition overlay.
    /// Null-checks prevent exceptions when currentTopDocument was already destroyed by a valid drop.
    /// </summary>
    public void ClearStack()
    {
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
    /// Sets <paramref name="rt"/> to full-stretch anchors so it fills its parent completely.
    /// Called in ShowNextDocument and OnDocumentDragCancelled to ensure the document
    /// always adapts to the DocumentStackZone size on any screen resolution.
    /// </summary>
    /// <param name="rt">The RectTransform to reset to stretch layout.</param>
    private void RestoreStretchAnchors(RectTransform rt)
    {
        rt.anchorMin       = Vector2.zero;
        rt.anchorMax       = Vector2.one;
        rt.sizeDelta       = Vector2.zero;
        rt.anchoredPosition = Vector2.zero;
        rt.pivot           = new Vector2(0.5f, 0.5f);
    }

    /// <summary>
    /// Rebuilds the queue by inserting <paramref name="documentObject"/> at the front,
    /// followed by all previously queued documents in their original order.
    /// Queue does not support front-insertion natively, so a temporary list is used.
    /// </summary>
    /// <param name="documentObject">The document to place at the head of the queue.</param>
    private void ReinsertAtFrontOfQueue(GameObject documentObject)
    {
        List<GameObject> remainingDocuments = new List<GameObject>(documentQueue);
        documentQueue.Clear();
        documentQueue.Enqueue(documentObject);

        foreach (GameObject remainingDocument in remainingDocuments)
            documentQueue.Enqueue(remainingDocument);
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
