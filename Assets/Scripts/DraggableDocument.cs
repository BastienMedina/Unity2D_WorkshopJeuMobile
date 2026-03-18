using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour placed on each document GameObject in the scene.
/// Handles drag-and-drop interaction via the Unity EventSystem.
/// On drop, delegates bin detection to DropZoneDetector and validation to SortingBin.
/// Does NOT generate document content, validate sorting rules, or perform raycasting.
/// </summary>
public class DraggableDocument : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
{
    // -------------------------------------------------------------------------
    // Inspector-assigned fields
    // -------------------------------------------------------------------------

    /// <summary>
    /// Controls alpha and raycast blocking for this document's UI hierarchy.
    /// Must be assigned in the Inspector; blocksRaycasts is toggled during drag.
    /// </summary>
    [SerializeField] private CanvasGroup canvasGroup;

    /// <summary>
    /// The root Canvas. Required by RectTransformUtility to correctly convert
    /// screen-space pointer positions into local RectTransform coordinates.
    /// Not a [SerializeField] because a prefab has no knowledge of the scene structure
    /// and cannot hold a reliable reference to a scene object. This field must always
    /// be injected at runtime by the spawner immediately after instantiation.
    /// </summary>
    private Canvas parentCanvas;

    /// <summary>
    /// Single scene instance responsible for detecting which bin is under the pointer.
    /// Not a [SerializeField] because DropZoneDetector lives on the Canvas in the scene
    /// and a prefab asset cannot hold a reliable reference to a scene object — the field
    /// would be null on every spawned instance. Must be injected at runtime by DocumentSpawner.
    /// </summary>
    private DropZoneDetector dropZoneDetector;

    // -------------------------------------------------------------------------
    // Duration bounds — never hardcoded inline
    // -------------------------------------------------------------------------

    [SerializeField] private float invalidFlashDuration = 0.2f;
    [SerializeField] private float invalidFlashAlpha = 0.4f;

    // -------------------------------------------------------------------------
    // Runtime state — not serialized, reset each drag
    // -------------------------------------------------------------------------

    /// <summary>Runtime document data injected by the spawner after instantiation.</summary>
    private DocumentData documentData;

    /// <summary>Anchored position captured at the start of the drag, used for snap-back.</summary>
    private Vector2 originalAnchoredPosition;

    /// <summary>
    /// Offset between the document's anchored position and the pointer's canvas-local position
    /// at the moment the drag began. Applied every frame in OnDrag to keep the document
    /// anchored to the exact spot the player touched rather than snapping its pivot to the pointer.
    /// </summary>
    private Vector2 dragPointerOffset;

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

    // -------------------------------------------------------------------------
    // Drag event handlers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Called by the EventSystem when the player begins dragging this document.
    /// Captures the starting position and disables raycast blocking so the pointer
    /// can reach bin objects beneath this document during the drag.
    /// </summary>
    /// <param name="eventData">Pointer event data provided by the EventSystem.</param>
    public void OnBeginDrag(PointerEventData eventData)
    {
        RectTransform documentRectTransform = GetComponent<RectTransform>();
        originalAnchoredPosition = documentRectTransform.anchoredPosition;

        // Disable raycast blocking so pointer events pass through this document
        // and can reach the SortingBin drop zones sitting behind it in the hierarchy.
        // Without this, the GraphicRaycaster would hit this document first on every frame,
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

        if (!isInitialPositionConverted)
            return;

        // Store the delta so every OnDrag frame can restore the original grab offset.
        dragPointerOffset = originalAnchoredPosition - localPointerPositionAtDragStart;
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
        // the pointer and producing the perceived ~200 px downward snap.
        documentRectTransform.anchoredPosition = localPointerPosition + dragPointerOffset;
    }

    /// <summary>
    /// Called by the EventSystem when the player releases the pointer.
    /// Re-enables raycast blocking, then queries DropZoneDetector for a bin under the pointer.
    /// Destroys this document on a valid drop; snaps back (with flash) on invalid or missed drop.
    /// </summary>
    /// <param name="eventData">Pointer event data provided by the EventSystem.</param>
    public void OnEndDrag(PointerEventData eventData)
    {
        // Restore blocking so this document participates in raycasts again after being placed.
        canvasGroup.blocksRaycasts = true;

        SortingBin binUnderPointer = dropZoneDetector.GetBinUnderPointer(eventData.position);

        if (binUnderPointer == null)
        {
            SnapBackToOriginalPosition();
            return;
        }

        bool isDropValid = binUnderPointer.ValidateDocument(documentData);

        if (isDropValid)
        {
            Destroy(gameObject);
            return;
        }

        StartCoroutine(FlashInvalidAndSnapBack());
    }

    // -------------------------------------------------------------------------
    // Private helpers
    // -------------------------------------------------------------------------

    /// <summary>
    /// Immediately returns the document to its pre-drag anchored position.
    /// </summary>
    private void SnapBackToOriginalPosition()
    {
        GetComponent<RectTransform>().anchoredPosition = originalAnchoredPosition;
    }

    /// <summary>
    /// Briefly reduces alpha to signal an invalid drop, then snaps the document back.
    /// </summary>
    private IEnumerator FlashInvalidAndSnapBack()
    {
        canvasGroup.alpha = invalidFlashAlpha;
        yield return new WaitForSeconds(invalidFlashDuration);
        canvasGroup.alpha = 1f;

        SnapBackToOriginalPosition();
    }
}
