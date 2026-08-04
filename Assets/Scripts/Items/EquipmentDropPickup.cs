using UnityEngine;

/// <summary>
/// A dropped piece of equipment sitting on the floor (enemy loot, see
/// COMBAT_DESIGN.md §2 "Drops"). Picking it up adds it to the picker's bag
/// (Inventory) rather than auto-equipping - the player chooses when to
/// equip it. Unlimited bag capacity, no "drop" action to lose it again.
/// </summary>
public class EquipmentDropPickup : ItemPickup
{
    private EquippableItem item;

    public override EquippableItem PendingItem => item;

    /// <summary>Spawns a drop at a cell, purely from code (no prefab required) - see EnemyController.Die.</summary>
    public static EquipmentDropPickup SpawnAt(Vector2Int cell, EquippableItem item)
    {
        var go = new GameObject($"Drop_{item.displayName}");
        go.transform.position = GridUtils.CellToWorld(cell);

        // Without a renderer the drop is invisible and the player can only find it
        // by walking over every corpse tile. Uses the same frame ItemSlotUI uses as
        // an icon, tinted by rarity (COMBAT_DESIGN.md §6 drop colors).
        var renderer = go.AddComponent<SpriteRenderer>();
        renderer.sprite = item.walkDown != null && item.walkDown.Length > 0 ? item.walkDown[0] : null;
        renderer.color = RarityVisuals.OutlineColor(item.rarity);
        renderer.sortingOrder = EquipmentLayerOrder.DirectionAwareBehind; // above the floor, below characters

        EquipmentDropPickup pickup = go.AddComponent<EquipmentDropPickup>();
        pickup.item = item;
        return pickup;
    }

    public override bool PickUp(Entity picker)
    {
        if (item == null || picker == null) return false;

        Inventory inventory = picker.GetComponent<Inventory>();
        if (inventory == null)
        {
            Debug.LogWarning($"{picker.name} has no Inventory component; leaving {item.displayName} on the floor.");
            return false;
        }

        inventory.Add(item);
        return true;
    }
}
