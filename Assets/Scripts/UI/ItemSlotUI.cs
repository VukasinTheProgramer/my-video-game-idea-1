using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One item square: icon with a rarity-colored outline frame around it
/// (IMPLEMENTED.md -> "Equipment", rarity colors). Runic gets an animated outline instead of a
/// static one - implemented as a color pulse (rarity color <-> black)
/// rather than a literal "marching ants" border, since that's reliable
/// with plain UI Images and needs no custom shader (same reasoning as the
/// hit-flash color substitution in Entity.cs).
///
/// Right-click opens the item's tooltip (stats + an Equip/Unequip button) -
/// there's no left-click instant-*click*-action, so a stray click can't
/// silently swap gear. Left-click drag IS supported (SetDropTarget) - a
/// deliberate drag is a different gesture than a misclick, so it doesn't
/// reopen the bug the tooltip-only design was fixing.
///
/// Builds its own child hierarchy in Awake, so it works whether it's
/// hand-placed in a scene or spawned at runtime (EquipmentPanelUI's bag
/// pool does the latter) - no prefab required.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ItemSlotUI : MonoBehaviour, IPointerClickHandler, IBeginDragHandler, IDragHandler, IEndDragHandler, IDropHandler
{
    [SerializeField] private float outlineThickness = 4f;
    [SerializeField] private float motionPulseSeconds = 0.6f;

    private Image outlineImage;
    private Image iconImage;
    private Button button;
    private Coroutine motionRoutine;
    private Action onRightClick;
    private EquippableItem boundItem;
    public EquippableItem BoundItem => boundItem;

    // Drop-target config, set by EquipmentPanelUI once per Refresh via SetDropTarget:
    // null = a bag slot, accepts any dragged item (dropping an equipped item here
    // means "unequip"). Non-null = an equip slot, only accepts a dragged item whose
    // OWN .slot matches - dropping a helmet on the Boots square is rejected rather
    // than silently equipping it into Head anyway.
    private EquipmentSlot? acceptSlot;
    private Action<EquippableItem> onDropped;
    private bool acceptsDrops;

    private RectTransform dragGhost;

    private void Awake()
    {
        BuildHierarchy();
    }

    private void BuildHierarchy()
    {
        if (outlineImage != null) return; // already built

        // Outline: fills the whole slot as a colored background square.
        var outlineGO = new GameObject("Outline", typeof(RectTransform), typeof(Image));
        outlineGO.transform.SetParent(transform, false);
        outlineImage = outlineGO.GetComponent<Image>();
        SetStretch(outlineGO.GetComponent<RectTransform>(), 0f);

        // Icon: inset by outlineThickness so the outline shows as a border ring.
        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(transform, false);
        iconImage = iconGO.GetComponent<Image>();
        iconImage.preserveAspect = true;
        SetStretch(iconGO.GetComponent<RectTransform>(), outlineThickness);

        button = gameObject.GetComponent<Button>();
        if (button == null) button = gameObject.AddComponent<Button>();
        button.targetGraphic = outlineImage;

        outlineImage.enabled = false;
        iconImage.enabled = false;
    }

    private static void SetStretch(RectTransform rect, float inset)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(inset, inset);
        rect.offsetMax = new Vector2(-inset, -inset);
    }

    /// <summary>Configures this slot as a drop target. acceptSlot null = bag slot (accepts
    /// any dragged item - a drop here means "unequip"); non-null = equip slot (only
    /// accepts a dragged item whose own .slot matches this one). Call once per Refresh,
    /// same as Bind - EquipmentPanelUI owns the wiring, this component just reports drops.</summary>
    public void SetDropTarget(EquipmentSlot? acceptSlot, Action<EquippableItem> onDropped)
    {
        this.acceptSlot = acceptSlot;
        this.onDropped = onDropped;
        acceptsDrops = true;
    }

    /// <summary>Shows item in this slot with its rarity outline. onRightClick fires on right-click (null = not interactable) and opens the item's stat tooltip with an Equip/Unequip button.</summary>
    public void Bind(EquippableItem item, Action onRightClick = null)
    {
        BuildHierarchy(); // safety if Bind is somehow called before Awake

        boundItem = item;

        Sprite icon = item != null && item.walkDown != null && item.walkDown.Length > 0 ? item.walkDown[0] : null;
        iconImage.sprite = icon;
        iconImage.enabled = icon != null;

        StopMotion();
        this.onRightClick = onRightClick;
        button.interactable = onRightClick != null;

        if (item == null)
        {
            // Stays enabled (raycastable) but invisible, rather than disabled outright -
            // a disabled Graphic doesn't raycast at all, and an empty EQUIP slot must
            // still be a valid drop target (that's the main point of dropping an item:
            // filling an empty slot). Zero visible difference from fully disabled.
            outlineImage.color = new Color(0f, 0f, 0f, 0f);
            outlineImage.enabled = true;
            return;
        }

        outlineImage.enabled = true;

        if (RarityVisuals.HasMotionOutline(item.rarity))
        {
            motionRoutine = StartCoroutine(PulseOutline(RarityVisuals.OutlineColor(item.rarity), RarityVisuals.RunicSecondaryColor));
        }
        else
        {
            outlineImage.color = RarityVisuals.OutlineColor(item.rarity);
        }
    }

    public void OnPointerClick(PointerEventData eventData)
    {
        if (eventData.button != PointerEventData.InputButton.Right) return;
        onRightClick?.Invoke();
    }

    /// <summary>Empties this slot: no icon, no outline, not clickable.</summary>
    public void Clear()
    {
        Bind(null);
    }

    public void OnBeginDrag(PointerEventData eventData)
    {
        if (boundItem == null || iconImage.sprite == null) return;

        Canvas canvas = GetComponentInParent<Canvas>();
        if (canvas == null) return;

        var ghostGO = new GameObject("DragGhost", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        dragGhost = (RectTransform)ghostGO.transform;
        dragGhost.SetParent(canvas.transform, false);
        dragGhost.SetAsLastSibling();
        dragGhost.sizeDelta = iconImage.rectTransform.rect.size;

        Image ghostImage = ghostGO.GetComponent<Image>();
        ghostImage.sprite = iconImage.sprite;
        ghostImage.preserveAspect = true;
        // Must not intercept the raycast meant for whatever's underneath it - that
        // raycast is exactly how the EventSystem finds the drop target.
        ghostImage.raycastTarget = false;

        dragGhost.position = eventData.position;
    }

    public void OnDrag(PointerEventData eventData)
    {
        if (dragGhost != null) dragGhost.position = eventData.position;
    }

    public void OnEndDrag(PointerEventData eventData)
    {
        DestroyGhost();
    }

    /// <summary>Called by the EventSystem on whatever slot is under the pointer when a drag
    /// ends - fires before OnEndDrag on the slot that started the drag.</summary>
    public void OnDrop(PointerEventData eventData)
    {
        if (!acceptsDrops || eventData.pointerDrag == null) return;

        ItemSlotUI source = eventData.pointerDrag.GetComponent<ItemSlotUI>();
        if (source == null || source == this || source.boundItem == null) return;

        EquippableItem item = source.boundItem;
        if (acceptSlot.HasValue && item.slot != acceptSlot.Value) return; // wrong slot type - reject, don't equip elsewhere

        // Destroy the source's ghost BEFORE invoking the drop - onDropped triggers
        // EquipmentPanelUI.Refresh(), which can SetActive(false) the source slot
        // (e.g. a bag slot dropping below the shrunk item count). A disabled
        // GameObject never receives its own OnEndDrag, so waiting for that would
        // leave the ghost icon stuck on screen forever - this was a real bug, not
        // hypothetical (equip-from-bag reliably shrinks the bag by exactly one).
        source.DestroyGhost();

        onDropped?.Invoke(item);
    }

    /// <summary>Destroys this slot's in-flight drag ghost, if any. Public so a drop
    /// target (ItemSlotUI.OnDrop, BagDropZone.OnDrop) can clean up the SOURCE
    /// slot's ghost proactively before triggering a Refresh that might deactivate it.</summary>
    public void DestroyGhost()
    {
        if (dragGhost != null)
        {
            Destroy(dragGhost.gameObject);
            dragGhost = null;
        }
    }

    private void StopMotion()
    {
        if (motionRoutine != null)
        {
            StopCoroutine(motionRoutine);
            motionRoutine = null;
        }
    }

    private IEnumerator PulseOutline(Color colorA, Color colorB)
    {
        float t = 0f;
        while (true)
        {
            t += Time.deltaTime / Mathf.Max(0.01f, motionPulseSeconds);
            // (sin+1)/2 gives a smooth 0->1->0 back-and-forth instead of a hard snap-back.
            float lerp = (Mathf.Sin(t * Mathf.PI * 2f) + 1f) * 0.5f;
            outlineImage.color = Color.Lerp(colorA, colorB, lerp);
            yield return null;
        }
    }

    private void OnDestroy()
    {
        StopMotion();
    }
}
