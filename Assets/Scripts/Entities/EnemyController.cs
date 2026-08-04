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
    [Tooltip("Flat XP granted to the player on a kill (COMBAT_DESIGN.md §1a) - tune per enemy type/floor tier directly, no formula.")]
    [SerializeField] private int xpReward = 10;

    /// <summary>Fires once, right after a loot drop is spawned on death - GameManager
    /// subscribes to track it for floor-cleanup (COMBAT_DESIGN.md §2 "Drops").</summary>
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

    /// <summary>Opens the battle screen if the player's within range (COMBAT_DESIGN.md §0),
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
        }

        base.Die();
    }
}
