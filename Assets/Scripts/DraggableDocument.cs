using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour placed on each document GameObject in the scene.
/// Handles drag-and-drop interaction via the Unity EventSystem.
/// On drop, delegates bin detection to DropZoneDetector and validation to SortingBin.
/// Notifies DocumentStackManager at drag start, valid drop, and cancelled drop
/// so all visual stack state and document lifetime is managed in one place.
/// Does NOT generate document content, validate sorting rules, perform raycasting,
/// or manage the document stack position — those are DocumentStackManager's responsibility.
/// </summary>
public class DraggableDocument : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// Controls alpha and raycast blocking for this document's UI hierarchy.
    /// Must be assigned in the Inspector on the prefab; blocksRaycasts is managed
    /// by DocumentStackManager to prevent accidental drag of non-top documents.
    /// </summary>
    [SerializeField] private CanvasGroup canvasGroup;

    /// <summary>
    /// The root Canvas. Required by RectTransformUtility to correctly convert
    /// screen-space pointer positions into local RectTransform coordinates.
    /// Not a [SerializeField] because a prefab has no knowledge of the scene structure
    /// and cannot hold a reliable reference to a scene object. Injected at runtime by DocumentSpawner.
    /// </summary>
    private Canvas parentCanvas;

    /// <summary>
    /// Single scene instance responsible for detecting which bin is under the pointer.
    /// Not a [SerializeField] because DropZoneDetector lives on the Canvas in the scene
    /// and a prefab asset cannot hold a reliable reference to a scene object — the field
    /// would be null on every spawned instance. Injected at runtime by DocumentSpawner.
    /// </summary>
    private DropZoneDetector dropZoneDetector;

    /// <summary>
    /// The stack manager that owns visual positioning and document lifetime.
    /// Not a [SerializeField] because DocumentStackManager lives in the scene;
    /// a prefab cannot store scene object references. Injected at runtime by DocumentSpawner.
    /// </summary>
    private DocumentStackManager stackManager;

    // -------------------------------------------------------------------------
    // Duration bounds — serialized, never hardcoded inline
    // -------------------------------------------------------------------------

    [SerializeField] private float invalidFlashDuration = 0.2f;
    [SerializeField] private float invalidFlashAlpha    = 0.4f;

    // -------------------------------------------------------------------------
    // Runtime state — not serialized, reset each drag
    // -------------------------------------------------------------------------

    /// <summary>Runtime document data injected by DocumentSpawner after instantiation.</summary>
    private DocumentData documentData;

    /// <summary>
    /// Snapshot of all active rules across all bins for the current day.
    /// Required to pass to SortingBin.ValidateDocument for Fallback validation.
    /// Injected by DocumentSpawner alongside documentData.
    /// </summary>
    private List<RuleData> allActiveRules = new List<RuleData>();

    /// <summary>
    /// Offset between the document's anchored position and the pointer's canvas-local position
    /// at the moment the drag began. Applied every frame in OnDrag to keep the document
    /// anchored to the exact spot the player touched rather than snapping its pivot to the pointer.
    /// </summary>
    private Vector2 dragPointerOffset;

    /// <summary>
    /// The BinHoverDetector currently receiving hover events.
    /// Tracked across frames so OnDocumentExited is always called on the previous detector
    /// before OnDocumentEntered is called on the new one, preventing bins from remaining
    /// permanently expanded when the pointer slides from one bin to another or to empty space.
    /// </summary>
    private BinHoverDetector currentHoveredBinDetector;

    // -------------------------------------------------------------------------
    // Public injection API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Injects the document's data after the GameObject has been instantiated.
    /// Must be called by DocumentSpawner before the player can interact with the object.
    /// </summary>
    /// <param name="injectedDocumentData">The data describing this document's specificities.</param>
    public void SetDocumentData(DocumentData injectedDocumentData)
    {
        documentData = injectedDocumentData;
    }

    /// <summary>
    /// Injects the scene Canvas reference after the GameObject has been instantiated.
    /// Injection at spawn time is the correct pattern for prefabs that require scene object
    /// references — the prefab itself cannot store them because it has no scene context.
    /// Must be called by DocumentSpawner immediately after Instantiate().
    /// </summary>
    /// <param name="canvas">The root Canvas of the UI hierarchy this document lives in.</param>
    public void SetCanvasReference(Canvas canvas)
    {
        parentCanvas = canvas;
    }

    /// <summary>
    /// Injects the DropZoneDetector reference after the GameObject has been instantiated.
    /// Must be called by DocumentSpawner immediately after Instantiate(), for the same
    /// reason as SetCanvasReference: scene object references cannot be stored in a prefab asset.
    /// </summary>
    /// <param name="detector">The single DropZoneDetector instance active in the scene.</param>
    public void SetDropZoneDetector(DropZoneDetector detector)
    {
        dropZoneDetector = detector;
    }

    /// <summary>
    /// Injects the DocumentStackManager reference after the GameObject has been instantiated.
    /// The stack manager controls visual positioning, size, alpha, and document destruction.
    /// Not stored in the prefab for the same reason as parentCanvas and dropZoneDetector.
    /// Must be called by DocumentSpawner immediately after Instantiate().
    /// </summary>
    /// <param name="manager">The DocumentStackManager instance active in the scene.</param>
    public void SetStackManager(DocumentStackManager manager)
    {
        stackManager = manager;
    }

    /// <summary>
    /// Injects the full cross-bin rule list so it can be forwarded to SortingBin.ValidateDocument.
    /// Stored here rather than fetched at drop time to avoid any scene query — the list is a
    /// point-in-time snapshot of the day's rules, consistent with what the player sees on the bins.
    /// </summary>
    /// <param name="rules">All active RuleData objects across all bins for the current day.</param>
    public void SetAllActiveRules(List<RuleData> rules)
    {
        allActiveRules = new List<RuleData>(rules);
    }

    // -------------------------------------------------------------------------
    // Drag event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by the EventSystem when the player begins dragging this document.
    /// Disables raycast blocking so the pointer can reach bins beneath this document,
    /// then notifies the stack manager to apply the drag visual and reveal the next document.
    /// </summary>
    /// <param name="eventData">Pointer event data provided by the EventSystem.</param>
    public void OnBeginDrag(PointerEventData eventData)
    {
        // Disable raycast blocking so pointer events pass through this document
        // and can reach the SortingBin drop zones behind it in the hierarchy.
        // Without this, the GraphicRaycaster would always hit this document first,
        // making it impossible to detect any bin underneath.
        canvasGroup.blocksRaycasts = false;

        // Convert the initial pointer screen position to canvas local space so we can
        // compute the offset between the pointer and the document pivot at drag start.
        // Without this offset, OnDrag would snap the pivot directly under the pointer,
        // causing the document to jump instead of being dragged from the touched point.
        bool isInitialPositionConverted = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            eventData.position,
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera,
            out Vector2 localPointerPositionAtDragStart
        );

        if (isInitialPositionConverted)
        {
            RectTransform documentRectTransform = GetComponent<RectTransform>();

            // Store the delta so every OnDrag frame can restore the original grab offset.
            dragPointerOffset = documentRectTransform.anchoredPosition - localPointerPositionAtDragStart;
        }

        // Notify stack manager to shrink/fade this document and reveal the next one below.
        // Stack manager owns all visual state — DraggableDocument only reports the event.
        stackManager.OnDocumentDragStarted(gameObject);
    }

    /// <summary>
    /// Called by the EventSystem every frame while the player is dragging.
    /// Moves this document's RectTransform to follow the pointer.
    /// </summary>
    /// <param name="eventData">Pointer event data provided by the EventSystem.</param>
    public void OnDrag(PointerEventData eventData)
    {
        RectTransform documentRectTransform = GetComponent<RectTransform>();

        bool isPositionConverted = RectTransformUtility.ScreenPointToLocalPointInRectangle(
            parentCanvas.transform as RectTransform,
            eventData.position,
            parentCanvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : parentCanvas.worldCamera,
            out Vector2 localPointerPosition
        );

        // Only reposition if the conversion succeeded — an unconverted point would
        // snap the document to the canvas origin, producing jarring visual behaviour.
        if (!isPositionConverted)
            return;

        // Adding dragPointerOffset each frame keeps the document anchored to the point
        // where the player originally grabbed it, instead of centering its pivot under
        // the pointer and producing a perceived downward snap.
        documentRectTransform.anchoredPosition = localPointerPosition + dragPointerOffset;

        UpdateHoveredBinDetector(eventData.position);
    }

    /// <summary>
    /// Called by the EventSystem when the player releases the pointer.
    /// Re-enables raycast blocking, resets any bin hover expansion, then queries DropZoneDetector
    /// for a bin under the pointer.
    /// On a valid drop: notifies the stack manager to destroy the document.
    /// On a missed or invalid drop: notifies the stack manager to cancel and restore the document.
    /// DraggableDocument does NOT reposition the document on cancel — that is handled by the stack manager.
    /// </summary>
    /// <param name="eventData">Pointer event data provided by the EventSystem.</param>
    public void OnEndDrag(PointerEventData eventData)
    {
        // Restore blocking so this document participates in raycasts again if it stays in the scene.
        canvasGroup.blocksRaycasts = true;

        // Always reset bin expansion on drop, regardless of whether the drop was valid or not.
        // Without this reset, a bin would remain expanded if the document is destroyed on a valid
        // drop, since OnDrag will never fire again to call OnDocumentExited.
        if (currentHoveredBinDetector != null)
        {
            currentHoveredBinDetector.OnDocumentExited();
            currentHoveredBinDetector = null;
        }

        SortingBin binUnderPointer = dropZoneDetector.GetBinUnderPointer(eventData.position);

        if (binUnderPointer == null)
        {
            // No bin was found — the player dropped into empty space.
            // Stack manager returns the document to the front of the queue and shows it again.
            stackManager.OnDocumentDragCancelled(gameObject);
            return;
        }

        bool isDropValid = binUnderPointer.ValidateDocument(documentData, allActiveRules);

        if (isDropValid)
        {
            // Valid drop: delegate lifetime management to the stack manager.
            // Stack manager calls Destroy — DraggableDocument must not destroy itself
            // because the stack manager needs to track the removal centrally.
            stackManager.OnDocumentRemoved(gameObject);
            return;
        }

        // Invalid bin: brief flash feedback, then cancel and restore via stack manager.
        StartCoroutine(FlashInvalidThenCancel());
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Performs a UI raycast at the given screen position to find a BinHoverDetector.
    /// Calls OnDocumentEntered on newly entered detectors and OnDocumentExited on the
    /// previous one when the pointer moves between bins or into empty space.
    /// Tracking the previous detector guarantees OnDocumentExited is always paired with
    /// OnDocumentEntered — bins can never become stuck in the expanded state.
    /// </summary>
    /// <param name="screenPosition">Current pointer position in screen space.</param>
    private void UpdateHoveredBinDetector(Vector2 screenPosition)
    {
        BinHoverDetector detectedBinDetector = dropZoneDetector.GetBinHoverDetectorUnderPointer(screenPosition);

        // No change — avoid redundant Enter/Exit calls that would restart the animation.
        if (detectedBinDetector == currentHoveredBinDetector)
            return;

        // Exit the previously hovered bin before entering the new one.
        // Without this, rapid pointer movement between two bins would leave the first one expanded.
        if (currentHoveredBinDetector != null)
            currentHoveredBinDetector.OnDocumentExited();

        if (detectedBinDetector != null)
            detectedBinDetector.OnDocumentEntered();

        currentHoveredBinDetector = detectedBinDetector;
    }

    /// <summary>
    /// Briefly reduces alpha to signal an invalid drop, then notifies the stack manager
    /// to restore the document to the top of the stack.
    /// </summary>
    private IEnumerator FlashInvalidThenCancel()
    {
        canvasGroup.alpha = invalidFlashAlpha;
        yield return new WaitForSeconds(invalidFlashDuration);
        canvasGroup.alpha = 1f;

        // Stack manager handles repositioning; DraggableDocument no longer owns position state.
        stackManager.OnDocumentDragCancelled(gameObject);
    }
}
