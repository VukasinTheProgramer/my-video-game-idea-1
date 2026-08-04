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

    /// <summary>Returns the cell adjacent to start to move into next along the shortest walkable path toward goal, or null if unreachable. This is an absolute cell, not a direction offset.</summary>
    public static Vector2Int? FindNextStep(Vector2Int start, Vector2Int goal)
    {
        if (start == goal) return null;

        cameFrom.Clear();
        queue.Clear();
        cameFrom[start] = start;
        queue.Enqueue(start);

        while (queue.Count > 0)
        {
            Vector2Int current = queue.Dequeue();
            if (current == goal) break;

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

        if (!cameFrom.ContainsKey(goal)) return null;

        Vector2Int step = goal;
        while (cameFrom[step] != start)
        {
            step = cameFrom[step];
        }
        return step;
    }
}
