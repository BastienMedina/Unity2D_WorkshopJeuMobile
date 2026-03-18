using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// MonoBehaviour placed on the root Canvas object.
/// Performs UI raycasting to identify which SortingBin sits under a given screen position.
/// Intended as a single shared instance accessed by DraggableDocument at drag end.
/// Does NOT validate documents, store rules, or manage drag state.
/// </summary>
public class DropZoneDetector : MonoBehaviour
{
    // -------------------------------------------------------------------------
    // Inspector-assigned dependencies
    // -------------------------------------------------------------------------

    /// <summary>
    /// Raycaster attached to the Canvas. Responsible for hitting UI elements.
    /// A GraphicRaycaster is used instead of Physics2D because all interactive
    /// elements live on a UI Canvas (Screen Space Overlay), which is not part of
    /// the physics world — Physics2D.Raycast would never intersect UI geometry.
    /// </summary>
    [SerializeField] private GraphicRaycaster graphicRaycaster;

    /// <summary>EventSystem required by GraphicRaycaster to build pointer event data.</summary>
    [SerializeField] private EventSystem eventSystem;

    // -------------------------------------------------------------------------
    // Public API
    // -------------------------------------------------------------------------

    /// <summary>
    /// Casts a UI ray at the given screen position and returns the first SortingBin hit.
    /// </summary>
    /// <param name="screenPosition">
    /// The screen-space position to test, typically sourced from PointerEventData.position.
    /// </param>
    /// <returns>
    /// The first SortingBin component found under the pointer, or null if none was hit.
    /// </returns>
    public SortingBin GetBinUnderPointer(Vector2 screenPosition)
    {
        PointerEventData pointerEventData = new PointerEventData(eventSystem)
        {
            position = screenPosition
        };

        List<RaycastResult> raycastResults = new List<RaycastResult>();
        graphicRaycaster.Raycast(pointerEventData, raycastResults);

        foreach (RaycastResult raycastResult in raycastResults)
        {
            SortingBin sortingBin = raycastResult.gameObject.GetComponent<SortingBin>();

            // Return immediately on the first hit that carries a SortingBin component.
            // Results are ordered front-to-back by the GraphicRaycaster, so the first
            // match is always the topmost visible bin — no further traversal needed.
            if (sortingBin != null)
                return sortingBin;
        }

        return null;
    }
}
