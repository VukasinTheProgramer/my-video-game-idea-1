using UnityEngine;

/// <summary>
/// Reads player input and translates it into one grid action per turn:
/// move into an empty walkable cell, or attack an enemy standing in the
/// target cell. Only responds to input while it's the player's turn.
/// </summary>
[RequireComponent(typeof(Entity))]
public class PlayerController : Entity
{
    private void Update()
    {
        if (TurnManager.Instance == null || TurnManager.Instance.State != TurnState.PlayerTurn) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsActive) return; // input goes to the battle screen instead

        Vector2Int? move = ReadMoveInput();
        if (move == null) return;

        TryAct(move.Value);
    }

    private Vector2Int? ReadMoveInput()
    {
        // WASD / arrow keys, one cell per key press (GetKeyDown, not held).
        if (Input.GetKeyDown(KeyCode.W) || Input.GetKeyDown(KeyCode.UpArrow)) return Vector2Int.up;
        if (Input.GetKeyDown(KeyCode.S) || Input.GetKeyDown(KeyCode.DownArrow)) return Vector2Int.down;
        if (Input.GetKeyDown(KeyCode.A) || Input.GetKeyDown(KeyCode.LeftArrow)) return Vector2Int.left;
        if (Input.GetKeyDown(KeyCode.D) || Input.GetKeyDown(KeyCode.RightArrow)) return Vector2Int.right;
        return null;
    }

    private void TryAct(Vector2Int direction)
    {
        Vector2Int targetCell = Cell + direction;

        if (!DungeonGrid.IsWalkable(targetCell))
        {
            return; // wall — not a valid action, don't spend the turn
        }

        Entity occupant = DungeonGrid.GetOccupant(targetCell);
        if (occupant == null)
        {
            MoveTo(targetCell);

            ItemPickup item = DungeonGrid.TryTakeItem(targetCell);
            if (item != null)
            {
                // Only consume it if the pickup actually accepted it; otherwise put
                // it back on the grid so it isn't destroyed for nothing.
                if (item.PickUp(this)) Destroy(item.gameObject);
                else DungeonGrid.PlaceItem(targetCell, item);
            }
        }

        // Battle screen takes over from here if anyone's now in range - it ends
        // the player's dungeon turn itself once the fight resolves (BattleManager.EndBattle).
        // Covers both "moved and an enemy happens to be nearby" and the old
        // direct-bump case (an adjacent occupant is always within range >= 1).
        if (TryEngageNearbyEnemy()) return;

        if (occupant is EnemyController enemyOccupant)
        {
            Attack(enemyOccupant); // fallback if no BattleManager is wired up in the scene yet
        }

        TurnManager.Instance.EndPlayerTurn();
    }

    /// <summary>Opens the battle screen if any registered enemy is within BattleManager.EngageRange (IMPLEMENTED.md -> "Battle screen (encounter flow)").</summary>
    private bool TryEngageNearbyEnemy()
    {
        if (BattleManager.Instance == null || TurnManager.Instance == null) return false;

        foreach (EnemyController enemy in TurnManager.Instance.Enemies)
        {
            if (enemy == null || enemy.IsDead) continue;
            if (BattleManager.Instance.TryEngageIfInRange(this, enemy)) return true;
        }

        return false;
    }
}
