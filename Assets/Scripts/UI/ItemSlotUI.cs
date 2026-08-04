using System;
using System.Collections;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// One item square: icon with a rarity-colored outline frame around it
/// (COMBAT_DESIGN.md §6). Runic gets an animated outline instead of a
/// static one - implemented as a color pulse (rarity color <-> black)
/// rather than a literal "marching ants" border, since that's reliable
/// with plain UI Images and needs no custom shader (same reasoning as the
/// hit-flash color substitution in Entity.cs).
///
/// Right-click opens the item's tooltip (stats + an Equip/Unequip button) -
/// there's no left-click instant-action anymore, so a misclick can't
/// silently swap gear.
///
/// Builds its own child hierarchy in Awake, so it works whether it's
/// hand-placed in a scene or spawned at runtime (EquipmentPanelUI's bag
/// pool does the latter) - no prefab required.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ItemSlotUI : MonoBehaviour, IPointerClickHandler
{
    [SerializeField] private float outlineThickness = 4f;
    [SerializeField] private float motionPulseSeconds = 0.6f;

    private Image outlineImage;
    private Image iconImage;
    private Button button;
    private Coroutine motionRoutine;
    private Action onRightClick;

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

    /// <summary>Shows item in this slot with its rarity outline. onRightClick fires on right-click (null = not interactable) and opens the item's stat tooltip with an Equip/Unequip button.</summary>
    public void Bind(EquippableItem item, Action onRightClick = null)
    {
        BuildHierarchy(); // safety if Bind is somehow called before Awake

        Sprite icon = item != null && item.walkDown != null && item.walkDown.Length > 0 ? item.walkDown[0] : null;
        iconImage.sprite = icon;
        iconImage.enabled = icon != null;

        StopMotion();
        this.onRightClick = onRightClick;
        button.interactable = onRightClick != null;

        if (item == null)
        {
            outlineImage.enabled = false;
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
