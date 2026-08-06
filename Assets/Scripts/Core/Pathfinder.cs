using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// BFS over DungeonGrid's walkable cells. Grid is uniform-cost (every walkable
/// cell costs the same to enter), so plain BFS finds the same shortest path
/// A* would, without the overhead of a priority queue and heuristic.
/// </summary>
public static class Pathfinder
{
    // Reused across calls so a full-grid search doesn't allocate a fresh
    // dictionary + queue for every enemy, every turn. Gameplay is single-threaded,
    // and each search completes before the next begins, so sharing is safe.
    private static readonly Dictionary<Vector2Int, Vector2Int> cameFrom = new Dictionary<Vector2Int, Vector2Int>();
    private static readonly Queue<Vector2Int> queue = new Queue<Vector2Int>();

    /// <summary>Fills cameFrom with the BFS tree from start; returns whether goal was
    /// reached. The goal cell is enterable even when occupied (so you can path *at* an
    /// enemy to engage it), but no other occupied cell is.</summary>
    private static bool Search(Vector2Int start, Vector2Int goal)
    {
        cameFrom.Clear();
        queue.Clear();
        cameFrom[start] = start;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == goal) return true;

            foreach (Vector2Int direction in GridUtils.CardinalDirections)
            {
                Vector2Int next = current + direction;
                if (cameFrom.ContainsKey(next)) continue;
                if (!DungeonGrid.IsWalkable(next)) continue;
                if (next != goal && DungeonGrid.IsOccupied(next)) continue;

                cameFrom[next] = current;
                queue.Enqueue(next);
            }
        }

        return cameFrom.ContainsKey(goal);
    }

    /// <summary>Returns the cell adjacent to start to move into next along the shortest walkable path toward goal, or null if unreachable. This is an absolute cell, not a direction offset.</summary>
    public static Vector2Int? FindNextStep(Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return null;
        if (!Search(start, goal)) return null;

        Vector2Int step = goal;
        while (cameFrom[step] != start)
        {
            step = cameFrom[step];
        }
        return step;
    }

    /// <summary>
    /// The whole route from start to goal as absolute cells, start excluded and goal
    /// included, or null if unreachable. Callers that follow a route over several turns
    /// should plan once with this rather than re-running FindNextStep each turn:
    /// re-planning every turn against a *moving* obstacle can cycle forever (the player
    /// circling an enemy in a two-wide corridor), because each new plan is only optimal
    /// for a layout that changes again before the next step.
    /// </summary>
    public static List<Vector2Int> FindPath(Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return null;
        if (!Search(start, goal)) return null;

        var path = new List<Vector2Int>();
        for (Vector2Int cell = goal; cell != start; cell = cameFrom[cell])
        {
            path.Add(cell);
        }
        path.Reverse();
        return path;
    }
}
