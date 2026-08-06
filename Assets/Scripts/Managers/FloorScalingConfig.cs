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
    [Tooltip("Per-room chance of a LootChest spawning, every floor (not gated to Layer 0 like the tutorial sign) - deliberately rarer than health potions, a chest is a bonus find, not a common one.")]
    [Range(0f, 1f)] public float chestSpawnChance = 0.15f;
    [Tooltip("Given a chest spawns this room, the chance it's the large 2-tile variant instead of the small 1-tile one - a large chest is meant to feel like a rarer, bigger find.")]
    [Range(0f, 1f)] public float largeChestChance = 0.25f;

    [Header("Floor scaling (all values are per-floor growth; floor 1 = defaults below)")]
    [Tooltip("Layer 0 (.claude/LAYERS.md) is flat - \"no depth scaling of any kind\" across its first N floors, room/enemy count included. Every field below this one is pinned to its floor-1 value through here; scaling (the old additive formula, offset to start from floor Layer0FloorCount+1 instead of floor 2) only resumes past it, as a placeholder until Layer 1's real exponential curve is designed (LAYERS.md -> \"Open / undecided\" leaves hard-jump-vs-continuous at that boundary unresolved - this picks continuous, not a settled design).")]
    public int layer0FloorCount = 10;

    [Tooltip("Layer 0's tutorial layout (LAYERS.md -> \"Layer 0\"): a straight line of fixed-size rooms left to right instead of the scattered Layer 1+ chain, so every Layer 0 floor teaches the same predictable shape. Room count for these floors - floorsPerExtraRoom/maxRoomCount below don't apply here, Layer 0 is flat.")]
    public int layer0RoomCount = 3;
    public int layer0RoomSize = 8;
    public int layer0CorridorLength = 5;
    [Tooltip("Row (0-indexed from each room's bottom edge) where the 2-wide corridor band sits - fixed and identical for every room in the chain (a straight line), independent of layer0RoomSize so resizing rooms later doesn't silently move it. Occupies this row and the one above it.")]
    public int layer0CorridorRow = 3;

    [Tooltip("Extra rooms are added every N floors.")]
    public int floorsPerExtraRoom = 2;
    public int maxRoomCount = 14;

    [Tooltip("An extra enemy per room is added every N floors.")]
    public int floorsPerExtraEnemy = 4;
    public int maxEnemiesPerRoom = 3;

    [Tooltip("Bonus enemy max health per floor.")]
    public int enemyHealthPerFloor = 2;

    [Tooltip("Exponential enemy maxHp growth for floors 1-5 specifically (multiplier = rate^(floor-1)), stacked on top of the flat per-floor bonus above (which stays ~0 through floor layer0FloorCount by Layer 0's own \"no scaling\" design). Explicit felt-difficulty-ramp override for the tutorial floors, not a redesign of that flat rule (CLAUDE.md rule 5) - requested 2026-08-06. Rate solved so floor 5 lands at exactly 3x floor-1 health (rate^4 = 3, rate = 3^0.25 ≈ 1.316) per follow-up request the same day. 1 = no growth.")]
    public float earlyFloorHealthGrowthRate = 1.316074f;

    [Tooltip("Bonus enemy damage, applied every 2 floors to keep it gentler than health.")]
    public int enemyDamagePerFloor = 1;

    [Tooltip("Exponential enemy attack growth for floors 1-5 specifically (multiplier = rate^(floor-1)), same mechanism/reasoning as earlyFloorHealthGrowthRate above but a separate field per CLAUDE.md rule 4 (data designers tune stays independently tunable). Rate solved so floor 5 lands at exactly 3x floor-1 attack (rate^4 = 3, rate = 3^0.25 ≈ 1.316) - requested 2026-08-06. 1 = no growth.")]
    public float earlyFloorAttackGrowthRate = 1.316074f;

    [Tooltip("Bonus gold per enemy kill, added per floor past the first (ROADMAP.md -> \"Currency: gold & gems\").")]
    public int goldBonusPerFloor = 1;
}
