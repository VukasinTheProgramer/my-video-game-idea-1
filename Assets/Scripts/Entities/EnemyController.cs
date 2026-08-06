using System;
using UnityEngine;

/// <summary>
/// Enemy AI: if the player is within BattleManager's engage range, open the
/// battle screen. Otherwise the enemy is leashed to its spawn point rather
/// than chasing across the whole floor - it wiggles occasionally within
/// leashRadius when the player's far away, and closes in via the BFS-shortest
/// path (Pathfinder) only once the player is within aggroRange, but never
/// takes a step that would leave the leash box.
///
/// The leash is a flat square around the spawn cell (Chebyshev distance),
/// not the enemy's actual room polygon - DungeonGenerator doesn't hand
/// EnemyController a room rect today, and spawn cells already land well
/// inside a room via RandomCellInRoom, so a small flat radius rarely pokes
/// through a wall. Upgrade path if that ever matters: pass the spawning
/// room's RectInt into SpawnAt and clamp to that instead.
/// </summary>
[RequireComponent(typeof(Entity))]
public class EnemyController : Entity
{
    [SerializeField] private LootTable lootTable;

    [Header("Leash & wander")]
    [Tooltip("How far from its spawn cell (Chebyshev distance - a flat square, not a walkable-distance radius) this enemy will ever move.")]
    [SerializeField] private int leashRadius = 2;
    [Tooltip("Player must be within this many cells (Manhattan distance) before the enemy starts closing in. Beyond that, it only wiggles.")]
    [SerializeField] private int aggroRange = 5;
    [Tooltip("Chance per turn to take one random step while idle (player out of aggroRange). 0 = never wanders, just stands still.")]
    [Range(0f, 1f)] [SerializeField] private float wiggleChance = 0.3f;

    private Vector2Int spawnCell;

    [Tooltip("Flat XP granted to the player on a kill (IMPLEMENTED.md -> \"Leveling & stat points\") - tune per enemy type/floor tier directly, no formula.")]
    [SerializeField] private int xpReward = 10;

    [Tooltip("Gold rolled on a kill (ROADMAP.md -> \"Currency: gold & gems\") - min/max are the floor-1 baseline; GameManager adds a flat per-floor bonus at spawn time, same mechanism as enemy stat scaling.")]
    [SerializeField] private int goldRewardMin = 2;
    [SerializeField] private int goldRewardMax = 5;

    private int goldFloorBonus;

    /// <summary>Set by GameManager at spawn, alongside ApplyStatBonus - the same
    /// per-floor scaling hook, applied to gold instead of Stats.</summary>
    public void SetGoldFloorBonus(int bonus) => goldFloorBonus = bonus;

    /// <summary>Fires once, right after a loot drop is spawned on death - GameManager
    /// subscribes to track it for floor-cleanup. Only fires once a LootTable
    /// asset exists to roll against - see IN_PROGRESS.md -> "1b. Enemy loot".</summary>
    public event Action<ItemPickup> OnLootDropped;

    /// <summary>Fires once on a kill with the exact amount granted - lets a victory
    /// screen show precise numbers instead of diffing Wallet/PlayerProgression
    /// state around the kill (which breaks across a level-up's XP rollover).</summary>
    public event Action<int> OnXPGranted;
    public event Action<int> OnGoldGranted;

    private PlayerController player;

    public override void SpawnAt(Vector2Int cell)
    {
        base.SpawnAt(cell);
        spawnCell = cell;
    }

    private void Start()
    {
        // GameManager spawns us, so Instance is normally set; the lookup is only
        // a fallback for an Enemy prefab dropped straight into a scene for testing.
        player = GameManager.Instance != null
            ? GameManager.Instance.Player
            : FindFirstObjectByType<PlayerController>();

        if (TurnManager.Instance != null) TurnManager.Instance.RegisterEnemy(this);
    }

    protected override void OnDestroy()
    {
        base.OnDestroy();
        if (TurnManager.Instance != null)
        {
            TurnManager.Instance.UnregisterEnemy(this);
        }
    }

    /// <summary>Called once by TurnManager during the enemy phase.</summary>
    public void TakeTurn()
    {
        if (player == null || IsDead) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsActive) return; // frozen while a battle screen is up

        // Already close enough (e.g. the player stopped just outside range last
        // turn and this enemy didn't need to move) - open the battle screen
        // without spending a move.
        if (TryEngageOrFallbackAttack()) return;

        bool playerNearby = GridUtils.WithinRange(Cell, player.Cell, aggroRange);
        Vector2Int? nextCell = playerNearby ? GetLeashedStepToward(player.Cell) : GetWiggleStep();
        if (nextCell == null) return; // no move this turn - wait, either by design (idle) or leash-blocked

        if (DungeonGrid.CanMoveTo(nextCell.Value))
        {
            MoveTo(nextCell.Value);

            // Closing the distance may have brought it into range this same turn.
            // Engaging is fine (the battle screen is its own sequence), but the
            // inline fallback attack is NOT: moving and swinging in one turn gives
            // the enemy two actions where the player only ever gets one.
            TryEngage();
        }
    }

    private bool IsWithinLeash(Vector2Int cell)
    {
        return Mathf.Abs(cell.x - spawnCell.x) <= leashRadius && Mathf.Abs(cell.y - spawnCell.y) <= leashRadius;
    }

    /// <summary>One step along the shortest path toward goal, or null if that step
    /// would leave the leash box - the enemy holds its ground at the boundary
    /// rather than stepping out, even mid-chase.</summary>
    private Vector2Int? GetLeashedStepToward(Vector2Int goal)
    {
        Vector2Int? step = Pathfinder.FindNextStep(Cell, goal);
        if (step == null || !IsWithinLeash(step.Value)) return null;
        return step;
    }

    /// <summary>Rolls wiggleChance, then tries the 4 cardinal directions starting
    /// from a random one, taking the first that's walkable, unoccupied, and still
    /// inside the leash box. Null most turns by design - this is idle flavor, not
    /// a search for somewhere to go.</summary>
    private Vector2Int? GetWiggleStep()
    {
        if (UnityEngine.Random.value > wiggleChance) return null;

        int start = UnityEngine.Random.Range(0, GridUtils.CardinalDirections.Length);
        for (int i = 0; i < GridUtils.CardinalDirections.Length; i++)
        {
            Vector2Int candidate = Cell + GridUtils.CardinalDirections[(start + i) % GridUtils.CardinalDirections.Length];
            if (IsWithinLeash(candidate) && DungeonGrid.CanMoveTo(candidate)) return candidate;
        }
        return null;
    }

    /// <summary>Opens the battle screen if the player's within range (IMPLEMENTED.md -> "Battle screen (encounter flow)"),
    /// or falls back to the old direct adjacent-attack if no BattleManager is wired up yet.</summary>
    private bool TryEngageOrFallbackAttack()
    {
        if (BattleManager.Instance != null) return TryEngage();

        if (GridUtils.IsAdjacent(Cell, player.Cell))
        {
            Attack(player);
            return true;
        }
        return false;
    }

    /// <summary>Battle-screen engage only, with no inline-attack fallback.</summary>
    private bool TryEngage()
    {
        return BattleManager.Instance != null && BattleManager.Instance.TryEngageIfInRange(player, this);
    }

    protected override void Die()
    {
        if (lootTable != null)
        {
            // Every drop spawns at the same death cell - ItemPickup.Start()'s own
            // spiral scatter search (widened for this, ROADMAP.md -> "Multi-drop
            // loot rolls") handles fanning simultaneous drops out to nearby free
            // cells, so this loop doesn't need its own placement logic.
            foreach (EquippableItem drop in lootTable.RollDrops())
            {
                // floorStep 0 - items never had per-floor scaling before this, so
                // there's nothing to gate; stays off until Layer 1 exists
                // (IMPLEMENTED.md -> "Item generation").
                EquippableItem generated = ItemGenerator.Generate(drop, 0, drop.rarity);
                ItemPickup pickup = EquipmentDropPickup.SpawnAt(Cell, generated);
                OnLootDropped?.Invoke(pickup);
            }
        }

        if (player != null)
        {
            PlayerProgression progression = player.GetComponent<PlayerProgression>();
            progression?.AddXP(xpReward);
            OnXPGranted?.Invoke(xpReward);

            Wallet wallet = player.GetComponent<Wallet>();
            int gold = UnityEngine.Random.Range(goldRewardMin, goldRewardMax + 1) + goldFloorBonus;
            wallet?.AddGold(gold);
            OnGoldGranted?.Invoke(gold);
        }

        base.Die();
    }
}
