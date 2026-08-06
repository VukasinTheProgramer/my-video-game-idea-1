using UnityEngine;

/// <summary>
/// DungeonGenerator's Layer 1+ generation tuning, pulled out per CLAUDE.md's
/// rule 4 ("Data that designers tune = ScriptableObject. Logic = component or
/// static resolver. Never both in one class.") - same shape as GameManager's
/// FloorScalingConfig split: DungeonGenerator is a scene singleton with no
/// natural prefab to hold its own data (unlike e.g. EnemyController's per-type
/// tuning, which already lives on the Enemy prefab where it belongs).
///
/// Read-only by design: DungeonGenerator.SetRoomCount mutates its own working
/// roomCount/mapBounds every floor to grow the map with room count, so those
/// two stay private runtime fields on the component, seeded from this asset's
/// roomCount/mapBounds as the floor-1 baseline - if they lived here instead,
/// every floor's growth would permanently overwrite the shared asset on disk.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Layout Config")]
public class DungeonLayoutConfig : ScriptableObject
{
    public int roomCount = 6;
    public Vector2Int roomMinSize = new Vector2Int(4, 4);
    public Vector2Int roomMaxSize = new Vector2Int(8, 8);
    public Vector2Int mapBounds = new Vector2Int(40, 40);
    public Vector2Int corridorLengthRange = new Vector2Int(3, 6); // inclusive, cells
    public int seed = 0; // 0 = random each run
}
