using System;
using UnityEngine;

/// <summary>
/// Enemy AI: if the player is within BattleManager's engage range, open the
/// battle screen; otherwise take one step along the BFS-shortest path toward
/// the player (see Pathfinder).
/// </summary>
[RequireComponent(typeof(Entity))]
public class EnemyController : Entity
{
    [SerializeField] private LootTable lootTable;
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

    private PlayerController player;

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

        Vector2Int? nextCell = Pathfinder.FindNextStep(Cell, player.Cell);
        if (nextCell == null) return; // no walkable path this turn, just wait

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
            EquippableItem drop = lootTable.RollDrop();
            if (drop != null)
            {
                ItemPickup pickup = EquipmentDropPickup.SpawnAt(Cell, drop);
                OnLootDropped?.Invoke(pickup);
            }
        }

        if (player != null)
        {
            PlayerProgression progression = player.GetComponent<PlayerProgression>();
            progression?.AddXP(xpReward);

            Wallet wallet = player.GetComponent<Wallet>();
            int gold = UnityEngine.Random.Range(goldRewardMin, goldRewardMax + 1) + goldFloorBonus;
            wallet?.AddGold(gold);
        }

        base.Die();
    }
}
