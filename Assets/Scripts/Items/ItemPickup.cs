using UnityEngine;

/// <summary>
/// Base for anything that sits on a walkable dungeon cell and is picked up by
/// walking onto it (see PlayerController.TryAct). Not an Entity - no combat
/// or health of its own, just registers into DungeonGrid's item slot.
/// </summary>
public abstract class ItemPickup : MonoBehaviour
{
    protected virtual void Start()
    {
        Vector2Int cell = GridUtils.WorldToCell(transform.position);
        if (DungeonGrid.PlaceItem(cell, this)) return;

        // Cell already holds an item (an enemy died on top of a potion, say).
        // Overwriting would leave the first item visible but unreachable, so step
        // to an adjacent free cell instead.
        foreach (Vector2Int direction in GridUtils.CardinalDirections)
        {
            Vector2Int candidate = cell + direction;
            if (!DungeonGrid.IsWalkable(candidate) || DungeonGrid.HasItem(candidate)) continue;

            DungeonGrid.PlaceItem(candidate, this);
            transform.position = GridUtils.CellToWorld(candidate);
            return;
        }

        Debug.LogWarning($"{name}: no free cell near {cell} to place this pickup; destroying it.");
        Destroy(gameObject);
    }

    /// <summary>
    /// Called when an entity walks onto this item's cell. Returns true if the item
    /// was actually consumed - the caller destroys it only then, so a pickup that
    /// declines (no bag to put it in, already at full HP) stays on the floor
    /// instead of being silently destroyed (COMBAT_DESIGN.md §2½).
    /// </summary>
    public abstract bool PickUp(Entity picker);

    /// <summary>The equipment this pickup would grant, or null if it isn't an equipment drop. Lets GameManager rescue uncollected loot before a floor is torn down.</summary>
    public virtual EquippableItem PendingItem => null;
}
