using UnityEngine;

/// <summary>One equipped item's visual layer: a child sprite renderer synced to the base body's animation frame.</summary>
[RequireComponent(typeof(SpriteRenderer))]
public class EquipmentLayer : MonoBehaviour
{
    private SpriteRenderer spriteRenderer;

    private void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
    }

    public void SetFrame(Sprite sprite)
    {
        spriteRenderer.enabled = sprite != null;
        if (sprite != null) spriteRenderer.sprite = sprite;
    }

    public void SetSortingOrder(int order)
    {
        spriteRenderer.sortingOrder = order;
    }
}
