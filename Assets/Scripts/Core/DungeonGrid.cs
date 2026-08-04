using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Central source of truth for the dungeon layout: which cells are walkable,
/// and which entity (if any) currently occupies each cell. The DungeonGenerator
/// populates the walkable set; Entity.MoveTo keeps occupancy up to date.
/// This is a plain static registry (not a MonoBehaviour) so any script can
/// query it without needing a scene reference.
/// </summary>
public static class DungeonGrid
{
    private static readonly HashSet<Vector2Int> walkableCells = new HashSet<Vector2Int>();
    private static readonly Dictionary<Vector2Int, Entity> occupants = new Dictionary<Vector2Int, Entity>();
    private static readonly Dictionary<Vector2Int, ItemPickup> items = new Dictionary<Vector2Int, ItemPickup>();

    public static void Reset()
    {
        walkableCells.Clear();
        occupants.Clear();
        items.Clear();
    }

    public static void AddWalkable(Vector2Int cell)
    {
        walkableCells.Add(cell);
    }

    public static bool IsWalkable(Vector2Int cell)
    {
        return walkableCells.Contains(cell);
    }

    public static bool IsOccupied(Vector2Int cell)
    {
        return occupants.ContainsKey(cell);
    }

    public static Entity GetOccupant(Vector2Int cell)
    {
        occupants.TryGetValue(cell, out Entity entity);
        return entity;
    }

    public static bool CanMoveTo(Vector2Int cell)
    {
        return IsWalkable(cell) && !IsOccupied(cell);
    }

    public static void SetOccupant(Vector2Int cell, Entity entity)
    {
        occupants[cell] = entity;
    }

    public static void ClearOccupant(Vector2Int cell)
    {
        occupants.Remove(cell);
    }

    public static IReadOnlyCollection<Vector2Int> WalkableCells => walkableCells;

    /// <summary>Whether a cell already holds an item (one item per cell).</summary>
    public static bool HasItem(Vector2Int cell)
    {
        return items.ContainsKey(cell);
    }

    /// <summary>
    /// Items are a separate registry from occupants: walking onto an item's cell
    /// should pick it up, not be blocked. Returns false if the cell already holds
    /// an item - overwriting would orphan the first one, leaving it visible on
    /// screen but impossible to pick up or clean up.
    /// </summary>
    public static bool PlaceItem(Vector2Int cell, ItemPickup item)
    {
        if (item == null || items.ContainsKey(cell)) return false;
        items[cell] = item;
        return true;
    }

    /// <summary>Removes and returns the item at cell, or null if there isn't one.</summary>
    public static ItemPickup TryTakeItem(Vector2Int cell)
    {
        if (!items.TryGetValue(cell, out ItemPickup item)) return null;
        items.Remove(cell);
        return item;
    }
}
