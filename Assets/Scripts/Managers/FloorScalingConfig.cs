using UnityEngine;

/// <summary>
/// GameManager's per-floor tuning knobs, pulled out per CLAUDE.md's rule
/// ("Data that designers tune = ScriptableObject. Logic = component or
/// static resolver. Never both in one class.") - GameManager stayed the
/// orchestrator (GenerateFloor, SpawnEnemies, turn-flow triggers), this asset
/// holds only the numbers that shape a run.
/// </summary>
[CreateAssetMenu(menuName = "Dungeon/Floor Scaling Config")]
public class FloorScalingConfig : ScriptableObject
{
    [Range(0f, 1f)] public float itemSpawnChance = 0.5f;

    [Header("Floor scaling (all values are per-floor growth; floor 1 = defaults below)")]
    [Tooltip("Layer 0 (.claude/LAYERS.md) is flat - \"no depth scaling of any kind\" across its first N floors, room/enemy count included. Every field below this one is pinned to its floor-1 value through here; scaling (the old additive formula, offset to start from floor Layer0FloorCount+1 instead of floor 2) only resumes past it, as a placeholder until Layer 1's real exponential curve is designed (LAYERS.md -> \"Open / undecided\" leaves hard-jump-vs-continuous at that boundary unresolved - this picks continuous, not a settled design).")]
    public int layer0FloorCount = 10;

    [Tooltip("Layer 0's tutorial layout (LAYERS.md -> \"Layer 0\"): a straight line of fixed-size rooms left to right instead of the scattered Layer 1+ chain, so every Layer 0 floor teaches the same predictable shape. Room count for these floors - floorsPerExtraRoom/maxRoomCount below don't apply here, Layer 0 is flat.")]
    public int layer0RoomCount = 3;
    public int layer0RoomSize = 8;
    public int layer0CorridorLength = 5;

    [Tooltip("Extra rooms are added every N floors.")]
    public int floorsPerExtraRoom = 2;
    public int maxRoomCount = 14;

    [Tooltip("An extra enemy per room is added every N floors.")]
    public int floorsPerExtraEnemy = 4;
    public int maxEnemiesPerRoom = 3;

    [Tooltip("Bonus enemy max health per floor.")]
    public int enemyHealthPerFloor = 2;

    [Tooltip("Bonus enemy damage, applied every 2 floors to keep it gentler than health.")]
    public int enemyDamagePerFloor = 1;

    [Tooltip("Bonus gold per enemy kill, added per floor past the first (ROADMAP.md -> \"Currency: gold & gems\").")]
    public int goldBonusPerFloor = 1;
}
