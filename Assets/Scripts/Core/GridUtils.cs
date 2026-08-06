using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Helper methods for converting between grid cell coordinates (Vector2Int)
/// and world space positions. Keep CellSize in sync with your pixel art
/// tile size (e.g. a 16x16 sprite at 16 pixels-per-unit = 1 world unit per cell).
/// </summary>
public static class GridUtils
{
    public const float CellSize = 1f;

    public static readonly Vector2Int[] CardinalDirections =
    {
        Vector2Int.up, Vector2Int.down, Vector2Int.left, Vector2Int.right
    };

    /// <summary>A cell's world-space CORNER (minimum x/y), not its center - cell (3,4)
    /// spans world [3,4) x [4,5). Matches how the floor tilemap paints (tile sprite
    /// pivot 0,0 + Tilemap m_TileAnchor 0,0,0), which is what makes Entity.VisualPosition's
    /// +0.5 X land a bottom-center-pivoted body on the tile's horizontal center.</summary>
    public static Vector3 CellToWorld(Vector2Int cell)
    {
        return new Vector3(cell.x * CellSize, cell.y * CellSize, 0f);
    }

    /// <summary>
    /// Inverse of CellToWorld: which cell's [x, x+1) x [y, y+1) box contains this point.
    /// Floor, not round - rounding treats a cell as spanning [x-0.5, x+0.5), i.e. half a
    /// cell off from where tiles are actually drawn, so clicking the upper/right half of a
    /// tile resolved to its neighbour (and RoundToInt is wrong in the other direction for
    /// negative coordinates, which is half this map). Entities/items sit at exact integer
    /// world coords, which floor and round agree on, so only click input ever saw this.
    /// </summary>
    public static Vector2Int WorldToCell(Vector3 worldPos)
    {
        return new Vector2Int(
            Mathf.FloorToInt(worldPos.x / CellSize),
            Mathf.FloorToInt(worldPos.y / CellSize)
        );
    }

    public static int ManhattanDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Abs(a.x - b.x) + Mathf.Abs(a.y - b.y);
    }

    public static bool IsAdjacent(Vector2Int a, Vector2Int b)
    {
        return ManhattanDistance(a, b) == 1;
    }

    /// <summary>True if a and b are within range cells of each other (Manhattan distance, range 1 == adjacent).</summary>
    public static bool WithinRange(Vector2Int a, Vector2Int b, int range)
    {
        return ManhattanDistance(a, b) <= range;
    }

    /// <summary>Chebyshev (8-directional) distance - unlike ManhattanDistance, a diagonal
    /// neighbor is distance 1, not 2. Used where "1 cell away" should include diagonals
    /// (e.g. BattleManager's engage range), not just the 4 cardinal neighbors.</summary>
    public static int ChebyshevDistance(Vector2Int a, Vector2Int b)
    {
        return Mathf.Max(Mathf.Abs(a.x - b.x), Mathf.Abs(a.y - b.y));
    }

    /// <summary>True if a and b are within range cells of each other, counting diagonals as 1 step (Chebyshev distance).</summary>
    public static bool WithinChebyshevRange(Vector2Int a, Vector2Int b, int range)
    {
        return ChebyshevDistance(a, b) <= range;
    }

    /// <summary>Every cell at exactly Chebyshev distance `radius` from center - the
    /// perimeter of a (2*radius+1) square, corners included (diagonals are valid
    /// "nearby free floor" even though movement itself is cardinal-only). Used by
    /// ItemPickup's spiral scatter search when a drop's own cell is already taken
    /// (ROADMAP.md -> "Multi-drop loot rolls"). radius must be >= 1.</summary>
    public static IEnumerable<Vector2Int> RingCells(Vector2Int center, int radius)
    {
        for (int x = -radius; x <= radius; x++)
        {
            yield return center + new Vector2Int(x, radius);
            yield return center + new Vector2Int(x, -radius);
        }
        for (int y = -radius + 1; y <= radius - 1; y++)
        {
            yield return center + new Vector2Int(radius, y);
            yield return center + new Vector2Int(-radius, y);
        }
    }
}
