using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Tilemaps;

/// <summary>
/// Two generation modes, both registering every carved cell as walkable in
/// DungeonGrid: Generate() chains rooms together one at a time, each pair
/// joined by a straight, fixed-width corridor shot off in a random cardinal
/// direction (Layer 1+ - see LAYERS.md); GenerateLinear() is the same chain
/// idea with size/direction/count all fixed instead of random, used for
/// Layer 0's tutorial floors (GameManager picks which one runs per floor).
/// Tilemap painting is optional — assign floorTilemap and floorTile in the
/// inspector to see it visually; leave them empty and the grid logic still
/// works (useful before you have art in).
/// </summary>
public class DungeonGenerator : MonoBehaviour
{
    [Header("Generation")]
    [SerializeField] private DungeonLayoutConfig layout;

    // Not exposed as a range — only one width was ever requested. Widen to a
    // SerializeField if a second value is ever needed.
    private const int CorridorWidth = 2;

    [Header("Rendering (optional)")]
    [SerializeField] private Tilemap floorTilemap;
    [SerializeField] private TileBase floorTile;

    // The border lives on the wall side, not the floor: floorTile is a single
    // plain sprite and the ring of non-walkable cells around it is painted with
    // these, each rotated per-cell at paint time. Putting the border on the floor
    // instead needed 31 pre-rotated variants and still left seams wherever a
    // combination had no matching art.
    //
    // All four are authored facing north (and north-east for the two corner
    // shapes); rotation comes from SetTransformMatrix, so orientation is data.
    [SerializeField] private TileBase wallTile;       // no floor on any side
    [SerializeField] private TileBase wallEdgeTile;   // floor on one side
    [SerializeField] private TileBase wallCornerTile; // floor on two adjacent sides
    [SerializeField] private TileBase wallNubTile;    // floor touching only at a corner

    // Clockwise from north, so a shape's rotation step is just the index of the
    // direction it faces - see WallTileFor.
    private static readonly Vector2Int[] Clockwise =
    {
        new Vector2Int(0, 1), new Vector2Int(1, 0), new Vector2Int(0, -1), new Vector2Int(-1, 0),
    };

    private static readonly Vector2Int[] ClockwiseDiagonals =
    {
        new Vector2Int(1, 1), new Vector2Int(1, -1), new Vector2Int(-1, -1), new Vector2Int(-1, 1),
    };

    // k clockwise quarter turns about the cell's centre. The tilemap anchors at the
    // cell's bottom-left corner, so a bare rotation would swing the sprite out of
    // its cell - hence the translate/rotate/translate composite.
    private static readonly Matrix4x4[] Rotations = BuildRotations();

    private static Matrix4x4[] BuildRotations()
    {
        var result = new Matrix4x4[4];
        var centre = new Vector3(0.5f, 0.5f, 0f);
        for (int k = 0; k < 4; k++)
        {
            result[k] = Matrix4x4.TRS(centre, Quaternion.Euler(0f, 0f, -90f * k), Vector3.one)
                        * Matrix4x4.Translate(-centre);
        }
        return result;
    }

    private readonly List<RectInt> rooms = new List<RectInt>();

    // Reused across floors so the wall sweep doesn't allocate a set per generation.
    private readonly HashSet<Vector2Int> wallCells = new HashSet<Vector2Int>();

    // Working state for the current floor, seeded from layout's read-only
    // floor-1 baseline in Awake and grown per floor by SetRoomCount - never
    // written back to layout itself (see DungeonLayoutConfig's doc comment).
    private int roomCount;
    private Vector2Int mapBounds;

    private void Awake()
    {
        if (layout == null)
        {
            Debug.LogError("DungeonGenerator: assign a DungeonLayoutConfig in the inspector.");
            return;
        }

        roomCount = Mathf.Max(1, layout.roomCount);
        mapBounds = layout.mapBounds;
    }

    /// <summary>The floor-1 room count, i.e. whatever's configured on the layout asset.</summary>
    public int BaseRoomCount => layout.roomCount;

    /// <summary>
    /// Sets how many rooms the next Generate() should place, growing the map to match.
    /// Bounds scale by sqrt of the room ratio because room count is an area, not a length -
    /// without this, extra rooms simply fail to find non-overlapping space.
    /// </summary>
    public void SetRoomCount(int count)
    {
        roomCount = Mathf.Max(1, count);

        float growth = Mathf.Sqrt((float)roomCount / Mathf.Max(1, layout.roomCount));
        mapBounds = Vector2Int.RoundToInt((Vector2)layout.mapBounds * growth);
    }

    /// <summary>Generates the dungeon and returns the room list (room 0 = player spawn).</summary>
    public List<RectInt> Generate()
    {
        ResetForNewFloor();

        Random.State previousState = Random.state;
        // Offset by floor, or every floor re-seeds to the same value and generates a
        // byte-identical layout. Restoring the previous state afterwards keeps the
        // seed scoped to layout only - enemy/item/loot rolls stay unseeded.
        if (layout.seed != 0)
        {
            int floor = GameManager.Instance != null ? GameManager.Instance.CurrentFloor : 1;
            Random.InitState(layout.seed + floor);
        }

        PlaceRooms();
        PaintTiles();

        Random.state = previousState;
        return rooms;
    }

    /// <summary>
    /// Layer 0's tutorial layout (LAYERS.md -> "Layer 0"): a straight line of
    /// fixed-size rooms left to right, joined by fixed-length corridors - no
    /// randomness anywhere (size, count, direction, AND alignment), so every
    /// Layer 0 floor is the same predictable shape. Deliberately does NOT reuse
    /// CorridorStart/RoomAfterCorridor's RandomBandOffset wobble - every room
    /// shares the same yMin (a straight line), and the corridor sits at a fixed
    /// row offset from it, rather than a random band or the room's center -
    /// so resizing rooms later can't silently move the corridor's row. Kept
    /// entirely separate from Generate()'s scattered chain (Layer 1+) rather
    /// than threading a "linear mode" flag through it - the two share almost
    /// nothing once everything stops being randomized. Rooms grow monotonically
    /// along +x from a fixed origin, so unlike PlaceRooms this can't overlap or
    /// run out of space - no bounds/overlap checks needed.
    /// </summary>
    public List<RectInt> GenerateLinear(int linearRoomCount, int roomSize, int corridorLength, int corridorRow)
    {
        ResetForNewFloor();

        RectInt first = new RectInt(0, 0, roomSize, roomSize);
        rooms.Add(first);
        CarveRoom(first);

        int roomY = first.yMin; // every room in the chain shares this y
        int corridorGlobalY = roomY + corridorRow; // occupies this row and the one above it (CarveCorridor's perp offset)

        for (int i = 1; i < linearRoomCount; i++)
        {
            RectInt previous = rooms[rooms.Count - 1];
            Vector2Int corridorStart = new Vector2Int(previous.xMax, corridorGlobalY);
            Vector2Int lastCorridorCell = corridorStart + Vector2Int.right * (corridorLength - 1);
            RectInt next = new RectInt(lastCorridorCell.x + 1, roomY, roomSize, roomSize);

            rooms.Add(next);
            CarveRoom(next);
            CarveCorridor(corridorStart, Vector2Int.right, corridorLength);
        }

        PaintTiles();
        return rooms;
    }

    private void ResetForNewFloor()
    {
        DungeonGrid.Reset();
        rooms.Clear();
        if (floorTilemap != null) floorTilemap.ClearAllTiles();
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
        int w = Random.Range(layout.roomMinSize.x, layout.roomMaxSize.x + 1);
        int h = Random.Range(layout.roomMinSize.y, layout.roomMaxSize.y + 1);
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
        int length = Random.Range(layout.corridorLengthRange.x, layout.corridorLengthRange.y + 1);
        int w = Random.Range(layout.roomMinSize.x, layout.roomMaxSize.x + 1);
        int h = Random.Range(layout.roomMinSize.y, layout.roomMaxSize.y + 1);

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
        PaintWalls(cells);
    }

    // Paints the one-cell ring of non-walkable cells touching the floor. Not
    // batched like the floor above: each wall cell also needs its own rotation,
    // and SetTransformMatrix is per-cell only. It runs once per floor, not per
    // frame, so the extra calls don't matter.
    private void PaintWalls(IReadOnlyCollection<Vector2Int> floorCells)
    {
        if (wallEdgeTile == null) return;

        wallCells.Clear();
        foreach (var cell in floorCells)
        {
            for (int d = 0; d < 4; d++) CollectWall(cell + Clockwise[d]);
            for (int d = 0; d < 4; d++) CollectWall(cell + ClockwiseDiagonals[d]);
        }

        foreach (var cell in wallCells)
        {
            int rotation;
            TileBase tile = WallTileFor(cell, out rotation);
            if (tile == null) continue;

            var position = new Vector3Int(cell.x, cell.y, 0);
            floorTilemap.SetTile(position, tile);
            floorTilemap.SetTransformMatrix(position, Rotations[rotation]);
        }
    }

    private void CollectWall(Vector2Int cell)
    {
        if (!DungeonGrid.IsWalkable(cell)) wallCells.Add(cell);
    }

    // Picks the wall shape from which sides face floor, plus how many clockwise
    // quarter turns put the authored (north-facing) sprite in that orientation.
    // Because Clockwise is ordered north/east/south/west, the direction index IS
    // the rotation step - no lookup table needed.
    //
    // Two opposite sides, or three or more, fall through to the plain wall tile:
    // those only occur on a wall thin enough to have floor on both faces, which
    // this generator's 1-cell room padding doesn't produce.
    private TileBase WallTileFor(Vector2Int cell, out int rotation)
    {
        rotation = 0;

        int sides = 0;
        int sideCount = 0;
        for (int d = 0; d < 4; d++)
        {
            if (!DungeonGrid.IsWalkable(cell + Clockwise[d])) continue;
            sides |= 1 << d;
            sideCount++;
        }

        switch (sideCount)
        {
            case 1:
                rotation = BitIndex(sides);
                return wallEdgeTile;

            case 2:
                for (int d = 0; d < 4; d++)
                {
                    if (sides != ((1 << d) | (1 << ((d + 1) % 4)))) continue;
                    rotation = d;
                    return wallCornerTile;
                }
                return wallTile;

            case 0:
                int corners = 0;
                int cornerCount = 0;
                for (int d = 0; d < 4; d++)
                {
                    if (!DungeonGrid.IsWalkable(cell + ClockwiseDiagonals[d])) continue;
                    corners |= 1 << d;
                    cornerCount++;
                }

                if (cornerCount == 1)
                {
                    rotation = BitIndex(corners);
                    return wallNubTile;
                }
                break;
        }

        return wallTile;
    }

    private static int BitIndex(int singleBit)
    {
        for (int i = 0; i < 4; i++)
        {
            if ((singleBit & (1 << i)) != 0) return i;
        }
        return 0;
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
