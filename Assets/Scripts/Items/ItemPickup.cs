using UnityEngine;

/// <summary>
/// Base for anything that sits on a walkable dungeon cell and is picked up by
/// walking onto it (see PlayerController.TryAct). Not an Entity - no combat
/// or health of its own, just registers into DungeonGrid's item slot.
/// </summary>
public abstract class ItemPickup : MonoBehaviour
{
    // A kill can drop up to 10 items at once (ROADMAP.md -> "Multi-drop loot
    // rolls") landing on the same cell in the same frame - each one's Start()
    // runs in turn, so a single ring (radius 1, 4 cells) fills up fast, especially
    // in a tight corridor. 6 rings covers a 13x13 area, comfortably more cells
    // than one kill can ever fill.
    private const int MaxScatterRadius = 6;

    protected virtual void Start()
    {
        Vector2Int cell = GridUtils.WorldToCell(transform.position);
        if (DungeonGrid.PlaceItem(cell, this)) return;

        // Cell already holds an item (a multi-drop kill, or an enemy died on top of
        // a potion). Overwriting would leave the first item visible but
        // unreachable, so spiral outward ring by ring until a free walkable cell
        // turns up.
        for (int radius = 1; radius <= MaxScatterRadius; radius++)
        {
            foreach (Vector2Int candidate in GridUtils.RingCells(cell, radius))
            {
                if (!DungeonGrid.IsWalkable(candidate) || DungeonGrid.HasItem(candidate)) continue;

                DungeonGrid.PlaceItem(candidate, this);
                transform.position = GridUtils.CellToWorld(candidate);
                return;
            }
        }

        Debug.LogWarning($"{name}: no free cell within {MaxScatterRadius} of {cell} to place this pickup; destroying it.");
        Destroy(gameObject);
    }

    /// <summary>
    /// Called when an entity walks onto this item's cell. Returns true if the item
    /// was actually consumed - the caller destroys it only then, so a pickup that
    /// declines (no bag to put it in, already at full HP) stays on the floor
    /// instead of being silently destroyed (IMPLEMENTED.md -> "Bag / Inventory").
    /// </summary>
    public abstract bool PickUp(Entity picker);

    /// <summary>The equipment this pickup would grant, or null if it isn't an equipment drop. Lets GameManager rescue uncollected loot before a floor is torn down.</summary>
    public virtual EquippableItem PendingItem => null;
}
