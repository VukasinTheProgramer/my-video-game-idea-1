using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Very simple procedural dungeon generator: chains rooms together one at a
/// time, each pair joined by a straight, fixed-width corridor shot off in a
/// random cardinal direction, and registers every carved cell as walkable in
/// DungeonGrid. Tilemap painting is optional — assign floorTilemap and
/// floorTile in the inspector to see it visually; leave them empty and
/// the grid logic still works (useful before you have art in).
/// </summary>
public class DungeonGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private int roomCount = 6;
    [SerializeField] private Vector2Int roomMinSize = new Vector2Int(4, 4);
    [SerializeField] private Vector2Int roomMaxSize = new Vector2Int(8, 8);
    [SerializeField] private Vector2Int mapBounds = new Vector2Int(40, 40);
    [SerializeField] private Vector2Int corridorLengthRange = new Vector2Int(3, 6); // inclusive, cells
    [SerializeField] private int seed = 0; // 0 = random each run

    // Not exposed as a range — only one width was ever requested. Widen to a
    // SerializeField if a second value is ever needed.
    private const int CorridorWidth = 2;

    [Header("Rendering (optional)")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private TileBase floorTile;

    private readonly List<RectInt> rooms = new List<RectInt>();

    // The inspector values are treated as the floor-1 baseline; SetRoomCount scales from these.
    private int baseRoomCount;
    private Vector2Int baseMapBounds;

    private void Awake()
    {
        baseRoomCount = Mathf.Max(1, roomCount);
        baseMapBounds = mapBounds;
    }

    /// <summary>The floor-1 room count, i.e. whatever was configured in the inspector.</summary>
    public int BaseRoomCount => baseRoomCount;

    /// <summary>
    /// Sets how many rooms the next Generate() should place, growing the map to match.
    /// Bounds scale by sqrt of the room ratio because room count is an area, not a length -
    /// without this, extra rooms simply fail to find non-overlapping space.
    /// </summary>
    public void SetRoomCount(int count)
    {
        roomCount = Mathf.Max(1, count);

        // Guard the divide: Awake doesn't run on an inactive GameObject, but
        // GameManager.Start calls straight into this component either way, and a
        // zero baseRoomCount would make growth Infinity and mapBounds NaN.
        baseRoomCount = Mathf.Max(1, baseRoomCount);
        if (baseMapBounds == Vector2Int.zero) baseMapBounds = mapBounds;

        float growth = Mathf.Sqrt((float)roomCount / baseRoomCount);
        mapBounds = Vector2Int.RoundToInt((Vector2)baseMapBounds * growth);
    }

    /// <summary>Generates the dungeon and returns the room list (room 0 = player spawn).</summary>
    public List<RectInt> Generate()
    {
        DungeonGrid.Reset();
        rooms.Clear();
        if (floorTilemap != null) floorTilemap.ClearAllTiles();

        Random.State previousState = Random.state;
        // Offset by floor, or every floor re-seeds to the same value and generates a
        // byte-identical layout. Restoring the previous state afterwards keeps the
        // seed scoped to layout only - enemy/item/loot rolls stay unseeded.
        if (seed != 0)
        {
            int floor = GameManager.Instance != null ? GameManager.Instance.CurrentFloor : 1;
            Random.InitState(seed + floor);
        }

        PlaceRooms();
        PaintTiles();

        Random.state = previousState;
        return rooms;
    }

    private void PlaceRooms()
    {
        RectInt first = RandomRoom();
        rooms.Add(first);
        CarveRoom(first);

        int attempts = 0;
        while (rooms.Count < roomCount && attempts < roomCount * 30)
        {
            attempts++;
            TryAddChainedRoom(rooms[rooms.Count - 1]);
        }
    }

    private RectInt RandomRoom()
    {
        int w = Random.Range(roomMinSize.x, roomMaxSize.x + 1);
        int h = Random.Range(roomMinSize.y, roomMaxSize.y + 1);
        int x = Random.Range(-mapBounds.x / 2, mapBounds.x / 2 - w);
        int y = Random.Range(-mapBounds.y / 2, mapBounds.y / 2 - h);
        return new RectInt(x, y, w, h);
    }

    // Shoots a straight corridor off 'previous' in a random cardinal direction
    // and places the next room at its far end, so length/width are exact by
    // construction rather than whatever gap random placement happened to leave.
    private bool TryAddChainedRoom(RectInt previous)
    {
        Vector2Int dir = GridUtils.CardinalDirections[Random.Range(0, GridUtils.CardinalDirections.Length)];
        int length = Random.Range(corridorLengthRange.x, corridorLengthRange.y + 1);
        int w = Random.Range(roomMinSize.x, roomMaxSize.x + 1);
        int h = Random.Range(roomMinSize.y, roomMaxSize.y + 1);

        Vector2Int corridorStart = CorridorStart(previous, dir);
        Vector2Int lastCorridorCell = corridorStart + dir * (length - 1);
        RectInt candidate = RoomAfterCorridor(lastCorridorCell, dir, w, h);

        if (!InBounds(candidate) || OverlapsExistingRoom(candidate)) return false;

        rooms.Add(candidate);
        CarveRoom(candidate);
        CarveCorridor(corridorStart, dir, length);
        return true;
    }

    // Offset of a CorridorWidth-wide band along a wall of the given span, kept
    // off the first/last cells so corridors only ever exit/enter the middle of
    // a wall, never a corner. Margin scales down (instead of an all-or-nothing
    // fallback to the full span) on rooms too small for the full 2-cell margin,
    // so even a roomMinSize-sized room still keeps at least 1 cell clear on
    // each end - only a span this thin (<= CorridorWidth) has zero margin left.
    private int RandomBandOffset(int span)
    {
        int margin = Mathf.Clamp((span - CorridorWidth) / 2, 0, 2);
        int lo = margin;
        int hi = span - CorridorWidth - margin;
        return Random.Range(lo, hi + 1);
    }

    // First cell outside 'room's wall in direction dir, offset along the wall so
    // the CorridorWidth-wide band it anchors lands in the middle of the room's span.
    private Vector2Int CorridorStart(RectInt room, Vector2Int dir)
    {
        if (dir.x != 0)
        {
            int x = dir.x > 0 ? room.xMax : room.xMin - 1;
            int y = room.yMin + RandomBandOffset(room.yMax - room.yMin);
            return new Vector2Int(x, y);
        }
        int yEdge = dir.y > 0 ? room.yMax : room.yMin - 1;
        int xAnchor = room.xMin + RandomBandOffset(room.xMax - room.xMin);
        return new Vector2Int(xAnchor, yEdge);
    }

    // Places the next room immediately past the corridor's last cell, offset
    // along the wall so the corridor's band lands in the middle of the new room's span too.
    private RectInt RoomAfterCorridor(Vector2Int lastCorridorCell, Vector2Int dir, int w, int h)
    {
        Vector2Int edge = lastCorridorCell + dir;
        if (dir.x != 0)
        {
            int x = dir.x > 0 ? edge.x : edge.x + 1 - w;
            int yMin = lastCorridorCell.y - RandomBandOffset(h);
            return new RectInt(x, yMin, w, h);
        }
        int y = dir.y > 0 ? edge.y : edge.y + 1 - h;
        int xMin = lastCorridorCell.x - RandomBandOffset(w);
        return new RectInt(xMin, y, w, h);
    }

    private void CarveCorridor(Vector2Int start, Vector2Int dir, int length)
    {
        Vector2Int perp = dir.x != 0 ? Vector2Int.up : Vector2Int.right;
        for (int step = 0; step < length; step++)
        {
            Vector2Int cell = start + dir * step;
            DungeonGrid.AddWalkable(cell);
            DungeonGrid.AddWalkable(cell + perp);
        }
    }

    private bool InBounds(RectInt room)
    {
        int halfW = mapBounds.x / 2;
        int halfH = mapBounds.y / 2;
        return room.xMin >= -halfW && room.xMax <= halfW && room.yMin >= -halfH && room.yMax <= halfH;
    }

    private bool OverlapsExistingRoom(RectInt candidate)
    {
        // Pad by 1 cell so rooms always have a wall between them.
        var padded = new RectInt(candidate.x - 1, candidate.y - 1, candidate.width + 2, candidate.height + 2);
        foreach (var room in rooms)
        {
            if (padded.Overlaps(room)) return true;
        }
        return false;
    }

    private void CarveRoom(RectInt room)
    {
        for (int x = room.xMin; x < room.xMax; x++)
        {
            for (int y = room.yMin; y < room.yMax; y++)
            {
                DungeonGrid.AddWalkable(new Vector2Int(x, y));
            }
        }
    }

    private void PaintTiles()
    {
        if (floorTilemap == null || floorTile == null) return;

        // One batched call instead of ~1600 individual SetTile calls per floor.
        var cells = DungeonGrid.WalkableCells;
        var positions = new Vector3Int[cells.Count];
        var tiles = new TileBase[cells.Count];

        int i = 0;
        foreach (var cell in cells)
        {
            positions[i] = new Vector3Int(cell.x, cell.y, 0);
            tiles[i] = floorTile;
            i++;
        }

        floorTilemap.SetTiles(positions, tiles);
    }

    public Vector2Int RoomCenter(int roomIndex)
    {
        return Vector2Int.RoundToInt(rooms[roomIndex].center);
    }

    public Vector2Int RandomCellInRoom(int roomIndex)
    {
        RectInt room = rooms[roomIndex];
        int x = Random.Range(room.xMin, room.xMax);
        int y = Random.Range(room.yMin, room.yMax);
        return new Vector2Int(x, y);
    }

    public int RoomCount => rooms.Count;
}
