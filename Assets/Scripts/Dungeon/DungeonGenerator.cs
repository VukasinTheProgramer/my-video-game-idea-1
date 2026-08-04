using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Very simple procedural dungeon generator: places a handful of
/// non-overlapping rectangular rooms, connects them in sequence with
/// L-shaped corridors, and registers every carved cell as walkable in
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
    [SerializeField] private int seed = 0; // 0 = random each run

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
        ConnectRooms();
        PaintTiles();

        Random.state = previousState;
        return rooms;
    }

    private void PlaceRooms()
    {
        int attempts = 0;
        while (rooms.Count < roomCount && attempts < roomCount * 20)
        {
            attempts++;

            int w = Random.Range(roomMinSize.x, roomMaxSize.x + 1);
            int h = Random.Range(roomMinSize.y, roomMaxSize.y + 1);
            int x = Random.Range(-mapBounds.x / 2, mapBounds.x / 2 - w);
            int y = Random.Range(-mapBounds.y / 2, mapBounds.y / 2 - h);

            var candidate = new RectInt(x, y, w, h);
            if (OverlapsExistingRoom(candidate)) continue;

            rooms.Add(candidate);
            CarveRoom(candidate);
        }
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

    private void ConnectRooms()
    {
        for (int i = 1; i < rooms.Count; i++)
        {
            Vector2Int a = Vector2Int.RoundToInt(rooms[i - 1].center);
            Vector2Int b = Vector2Int.RoundToInt(rooms[i].center);
            CarveLCorridor(a, b);
        }
    }

    private void CarveLCorridor(Vector2Int a, Vector2Int b)
    {
        Vector2Int current = a;
        while (current.x != b.x)
        {
            DungeonGrid.AddWalkable(current);
            current.x += current.x < b.x ? 1 : -1;
        }
        while (current.y != b.y)
        {
            DungeonGrid.AddWalkable(current);
            current.y += current.y < b.y ? 1 : -1;
        }
        DungeonGrid.AddWalkable(current);
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
