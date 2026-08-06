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
    private static readonly Dictionary<Vector2Int, IInteractable> interactables = new Dictionary<Vector2Int, IInteractable>();

    public static void Reset()
    {
        walkableCells.Clear();
        occupants.Clear();
        items.Clear();
        interactables.Clear();
    }

    public static void AddWalkable(Vector2Int cell)
    {
        walkableCells.Add(cell);
    }

    /// <summary>Used by fixtures that occupy a cell without being an Entity (SignPost,
    /// LootChest) - a solid prop, not floor, so the player can never stand exactly on
    /// it (Pathfinder.Search still lets it be a valid click TARGET despite this, the
    /// same way an occupied enemy cell is - see Search's own doc comment).</summary>
    public static void RemoveWalkable(Vector2Int cell)
    {
        walkableCells.Remove(cell);
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

    /// <summary>
    /// Interactables (SignPost, LootChest) are permanent-until-Reset fixtures - most
    /// never get removed mid-floor (unlike items, no TryTake here), only Reset() (a new
    /// floor) clears the registry. LootChest is the one exception: it calls
    /// UnregisterInteractable on itself once opened, since a used-up chest shouldn't
    /// keep showing an "Open (E)" prompt. Overwrites silently rather than rejecting on
    /// a cell collision: two interactables would only ever land on the same cell
    /// through a scene-authoring/spawn-placement mistake, not a runtime race like
    /// multi-drop loot, so there's no orphaned-object concern to guard against here.
    /// </summary>
    public static void RegisterInteractable(Vector2Int cell, IInteractable interactable)
    {
        interactables[cell] = interactable;
    }

    /// <summary>Removes the interactable at cell, if any - see RegisterInteractable's
    /// note on LootChest being the one caller that needs this.</summary>
    public static void UnregisterInteractable(Vector2Int cell)
    {
        interactables.Remove(cell);
    }

    /// <summary>The interactable at cell, or null if there isn't one - used by
    /// PlayerController.BufferClick to let a click land directly on an interactable's
    /// own (solid) cell as a valid walk target.</summary>
    public static IInteractable GetInteractable(Vector2Int cell)
    {
        interactables.TryGetValue(cell, out IInteractable interactable);
        return interactable;
    }

    /// <summary>Every registered interactable - used for the ambient prompt proximity
    /// scan (PlayerController.FindNearbyInteractable), mirroring TurnManager.Enemies'
    /// role for enemy engagement.
    ///
    /// Deliberately typed as the concrete ValueCollection rather than IReadOnlyCollection,
    /// unlike WalkableCells above: FindNearbyInteractable runs this foreach EVERY frame,
    /// and enumerating through an interface boxes Dictionary's struct enumerator onto the
    /// heap (measured: 39 B per foreach, vs 0 B concrete - ~140 KB/min of pure GC churn at
    /// 60 FPS, even with zero interactables registered). ValueCollection is just as
    /// read-only as the interface was, so nothing is given up. Only exposed this way
    /// because the scan is per-frame - a per-turn or per-floor collection should keep
    /// the interface.</summary>
    public static Dictionary<Vector2Int, IInteractable>.ValueCollection Interactables => interactables.Values;
}
