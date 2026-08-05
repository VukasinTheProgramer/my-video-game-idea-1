using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Catches a drag dropped into empty space inside the bag area - not on top
/// of any specific ItemSlotUI (which already handle drops landing directly
/// on them, matching Chebyshev/wrong-slot rejection etc). Without this, drag-
/// to-unequip only worked if the bag already had at least one active item
/// slot to land on; an empty (or fully-populated-elsewhere) bag left nowhere
/// to drop onto at all.
///
/// Self-building: EquipmentPanelUI adds this (plus a fully transparent Image
/// so the container has a raycastable surface at all - a bare RectTransform
/// with only a layout group on it, which is what BagSlotContainer starts as,
/// never gets hit by a raycast) to bagSlotContainer's own GameObject once, in
/// Awake. Individual item slots are children rendered on top, so they still
/// win the raycast at their own position - this only catches the gaps.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BagDropZone : MonoBehaviour, IDropHandler
{
    private Action<EquippableItem> onDropped;

    /// <summary>Ensures the raycastable background + this component both exist on
    /// container, and (re)points the drop callback - call once, e.g. from Awake.</summary>
    public static BagDropZone Attach(RectTransform container, Action<EquippableItem> onDropped)
    {
        if (container == null) return null;

        Image bg = container.GetComponent<Image>();
        if (bg == null) bg = container.gameObject.AddComponent<Image>();
        bg.color = new Color(0f, 0f, 0f, 0f); // invisible but still raycastable (default alphaHitTestMinimumThreshold is 0)
        bg.raycastTarget = true;

        BagDropZone zone = container.GetComponent<BagDropZone>();
        if (zone == null) zone = container.gameObject.AddComponent<BagDropZone>();
        zone.onDropped = onDropped;
        return zone;
    }

    public void OnDrop(PointerEventData eventData)
    {
        if (eventData.pointerDrag == null) return;

        ItemSlotUI source = eventData.pointerDrag.GetComponent<ItemSlotUI>();
        if (source == null || source.BoundItem == null) return;

        source.DestroyGhost();
        onDropped?.Invoke(source.BoundItem);
    }
}
