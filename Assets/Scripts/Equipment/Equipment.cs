using System.Collections.Generic;
using UnityEngine;

public enum AnimAction { Walk, Attack, Hurt }

/// <summary>
/// Manages the player's 10 equipment slots as layered child sprite renderers,
/// kept in sync with the base body's current animation frame by
/// DirectionalSpriteAnimator.ApplyFrame calls.
/// </summary>
public class Equipment : MonoBehaviour
{
    private static readonly HashSet<EquipmentSlot> DirectionAwareSlots = new HashSet<EquipmentSlot>
    {
        EquipmentSlot.MainHand, EquipmentSlot.OffHand, EquipmentSlot.Back
    };

    private readonly Dictionary<EquipmentSlot, EquippableItem> equipped = new Dictionary<EquipmentSlot, EquippableItem>();
    private readonly Dictionary<EquipmentSlot, EquipmentLayer> layers = new Dictionary<EquipmentSlot, EquipmentLayer>();

    private bool lastFacingUp;
    private bool sortingInitialised;
    private Entity entity;
    private Inventory inventory;

    // Last frame DirectionalSpriteAnimator drove, replayed after Equip/Unequip so
    // newly equipped gear renders immediately instead of staying invisible until
    // the next facing change or attack. Defaults match the animator's own initial
    // pose (facing down, walk frame 0) for gear equipped before the first frame.
    private Vector2Int lastDirection = Vector2Int.down;
    private AnimAction lastAction = AnimAction.Walk;
    private int lastFrameIndex;

    private void Awake()
    {
        entity = GetComponent<Entity>();
        inventory = GetComponent<Inventory>();
    }

    /// <summary>
    /// Equips item into slot. Whatever was already there goes to the bag
    /// (never destroyed - there's no "drop" action). If item itself came
    /// from the bag, it's removed from there so it isn't listed as both
    /// equipped and in the bag at once.
    /// </summary>
    public void Equip(EquipmentSlot slot, EquippableItem item)
    {
        if (item == null) return;

        // An item only belongs in its own slot. Without this a caller passing the
        // wrong slot would put e.g. torso art into the direction-aware MainHand
        // layer and report the wrong EquippedWeaponType, and the item could never
        // be re-equipped afterwards (the bag path always uses item.slot).
        if (item.slot != slot) return;

        EquippableItem previous = GetEquipped(slot);

        // Already wearing this exact item: nothing to do. Returning early matters
        // because LootTable hands out the shared ScriptableObject template, so the
        // bag can legitimately hold a second reference to the same asset - falling
        // through would Remove that copy without Adding anything back, destroying
        // it (IMPLEMENTED.md -> "Bag / Inventory": an item is never lost).
        if (previous == item) return;

        if (previous != null) inventory?.Add(previous);
        inventory?.Remove(item);

        equipped[slot] = item;
        if (!layers.ContainsKey(slot))
        {
            var go = new GameObject($"Equip_{slot}", typeof(EquipmentLayer));
            go.transform.SetParent(transform, false);
            layers[slot] = go.GetComponent<EquipmentLayer>();
        }

        // Force the next ApplyFrame to write sorting orders, so this new layer gets one.
        sortingInitialised = false;
        entity?.RefreshEquipmentStats();
        ReapplyLastFrame();
    }

    /// <summary>Returns the item in this slot, or null if empty.</summary>
    public EquippableItem GetEquipped(EquipmentSlot slot)
    {
        return equipped.TryGetValue(slot, out var item) ? item : null;
    }

    /// <summary>Unequips slot, sending whatever was there back to the bag (not destroyed).</summary>
    public void Unequip(EquipmentSlot slot)
    {
        EquippableItem previous = GetEquipped(slot);
        if (previous != null) inventory?.Add(previous);

        equipped.Remove(slot);
        if (layers.TryGetValue(slot, out var layer))
        {
            Destroy(layer.gameObject);
            layers.Remove(slot);
        }
        entity?.RefreshEquipmentStats();
        ReapplyLastFrame();
    }

    /// <summary>Re-drives the most recent animation frame so an equip/unequip is visible at once.</summary>
    private void ReapplyLastFrame()
    {
        ApplyFrame(lastDirection, lastAction, lastFrameIndex);
    }

    /// <summary>Sum of bonusStats across every equipped item (IMPLEMENTED.md -> "Equipment"). Entity.Stats adds this to baseStats.</summary>
    public Stats TotalBonusStats
    {
        get
        {
            var total = new Stats();
            foreach (EquippableItem item in equipped.Values)
            {
                if (item != null && item.bonusStats != null) total += item.bonusStats;
            }
            return total;
        }
    }

    /// <summary>Weapon family of whatever's in MainHand, or None if empty/unarmed (CombatResolver, §1/§4).</summary>
    public WeaponType EquippedWeaponType => GetEquipped(EquipmentSlot.MainHand)?.weaponType ?? WeaponType.None;

    /// <summary>Flat weapon damage from MainHand (§2), 0 if empty.</summary>
    public int WeaponDamage => GetEquipped(EquipmentSlot.MainHand)?.weaponDamage ?? 0;

    /// <summary>Called by DirectionalSpriteAnimator every time the base body's own sprite changes.</summary>
    public void ApplyFrame(Vector2Int direction, AnimAction action, int frameIndex)
    {
        bool facingUp = direction == Vector2Int.up;
        // Sorting only depends on facing, so it only needs rewriting when facing changes -
        // not on every frame of every animation.
        bool sortingDirty = !sortingInitialised || facingUp != lastFacingUp;

        foreach (var pair in equipped)
        {
            EquipmentSlot slot = pair.Key;
            EquippableItem item = pair.Value;
            if (!layers.TryGetValue(slot, out EquipmentLayer layer)) continue;

            if (sortingDirty)
            {
                layer.SetSortingOrder(DirectionAwareSlots.Contains(slot)
                    ? (facingUp ? EquipmentLayerOrder.DirectionAwareBehind : EquipmentLayerOrder.DirectionAwareFront)
                    : EquipmentLayerOrder.Fixed[slot]);
            }

            layer.SetFrame(PickSprite(item, direction, action, frameIndex, facingUp));
        }

        lastFacingUp = facingUp;
        sortingInitialised = true;
        lastDirection = direction;
        lastAction = action;
        lastFrameIndex = frameIndex;
    }

    private static Sprite PickSprite(EquippableItem item, Vector2Int direction, AnimAction action, int frameIndex, bool facingUp)
    {
        if (action == AnimAction.Hurt)
        {
            if (facingUp && item.hurtBehind != null && item.hurtBehind.Length > 0)
            {
                return AtOrLast(item.hurtBehind, frameIndex);
            }
            return AtOrLast(item.hurt, frameIndex);
        }

        if (action == AnimAction.Attack && item.HasSlash)
        {
            if (facingUp && item.slashBehind != null && item.slashBehind.Length > 0)
            {
                return AtOrLast(item.slashBehind, frameIndex);
            }
            return AtOrLast(PickByDirection(direction, item.slashDown, item.slashLeft, item.slashRight, item.slashUp), frameIndex);
        }

        // Walk, or attacking/acting with no dedicated art for this slot: hold/step the walk pose.
        int walkFrame = action == AnimAction.Walk ? frameIndex : 0;
        if (facingUp && item.walkBehind != null && item.walkBehind.Length > 0)
        {
            return AtOrLast(item.walkBehind, walkFrame);
        }
        return AtOrLast(PickByDirection(direction, item.walkDown, item.walkLeft, item.walkRight, item.walkUp), walkFrame);
    }

    private static Sprite[] PickByDirection(Vector2Int direction, Sprite[] down, Sprite[] left, Sprite[] right, Sprite[] up)
    {
        if (Mathf.Abs(direction.x) >= Mathf.Abs(direction.y))
        {
            return direction.x < 0 ? left : right;
        }
        return direction.y < 0 ? down : up;
    }

    private static Sprite AtOrLast(Sprite[] frames, int index)
    {
        if (frames == null || frames.Length == 0) return null;
        return frames[Mathf.Clamp(index, 0, frames.Length - 1)];
    }
}
