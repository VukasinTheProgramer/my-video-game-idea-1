using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Reads player input and translates it into one grid action per turn:
/// move into an empty walkable cell, or attack per the equipped weapon's
/// pattern (ROADMAP.md -> "Weapon-driven attack patterns" - no new hotkeys,
/// the same directional input just means something different per weapon).
/// Only responds to input while it's the player's turn.
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
        WeaponType weapon = EquippedWeaponType;
        int range = weapon switch
        {
            WeaponType.Bow => 3,
            WeaponType.Staff => 2,
            _ => 1 // None/Sword/Axe/Mace/Hammer/Spear/Dagger - all adjacent-only; Axe/Spear's extra cells are pattern EXPANSION from an adjacent primary target, not acquisition range
        };

        if (range == 1)
        {
            TryActAdjacent(direction, weapon);
        }
        else
        {
            TryActRanged(direction, weapon, range);
        }
    }

    /// <summary>Sword/Axe/Mace/Hammer/Spear/Dagger/None - unchanged wall/empty/move/pickup
    /// shape from before weapon patterns existed; the only addition is expanding a directed
    /// hit through AttackPatternResolver before engaging.</summary>
    private void TryActAdjacent(Vector2Int direction, WeaponType weapon)
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
            TryPickUpAt(targetCell);

            // Moved into empty space and happened to end up near some OTHER,
            // undirected enemy (IMPLEMENTED.md -> "Battle screen (encounter flow)") -
            // proximity-based, not weapon-pattern-aware, so it stays single-target.
            if (TryEngageNearbyEnemy()) return;
            TurnManager.Instance.EndPlayerTurn();
            return;
        }

        if (occupant is EnemyController enemyOccupant)
        {
            if (!EngageDirectedAttack(weapon, targetCell, enemyOccupant))
            {
                Attack(enemyOccupant); // fallback if no BattleManager is wired up in the scene yet
            }
        }

        TurnManager.Instance.EndPlayerTurn();
    }

    /// <summary>Bow/Staff - scans up to `range` cells in the facing direction for a
    /// target, stopping at the first non-walkable cell (can't shoot through walls) or
    /// the first occupant found. No target in range falls through to the ordinary
    /// move-into-empty-cell behavior, so the same direction key still walks when
    /// there's nothing to shoot.</summary>
    private void TryActRanged(Vector2Int direction, WeaponType weapon, int range)
    {
        EnemyController rangedTarget = null;
        Vector2Int rangedTargetCell = default;

        for (int step = 1; step <= range; step++)
        {
            Vector2Int scanCell = Cell + direction * step;
            if (!DungeonGrid.IsWalkable(scanCell)) break;

            Entity occupant = DungeonGrid.GetOccupant(scanCell);
            if (occupant == null) continue;

            if (occupant is EnemyController enemyOccupant)
            {
                rangedTarget = enemyOccupant;
                rangedTargetCell = scanCell;
            }
            break; // the first occupant along the line blocks it either way
        }

        if (rangedTarget != null)
        {
            Face(direction); // no MoveTo - a ranged shot doesn't close the distance
            if (!EngageDirectedAttack(weapon, rangedTargetCell, rangedTarget))
            {
                Attack(rangedTarget); // fallback if no BattleManager is wired up in the scene yet
            }
            TurnManager.Instance.EndPlayerTurn();
            return;
        }

        // Nothing in range - same wall/empty/move/pickup shape as the adjacent case,
        // just for the one cell directly ahead.
        Vector2Int moveCell = Cell + direction;
        if (!DungeonGrid.IsWalkable(moveCell)) return; // wall — not a valid action, don't spend the turn
        if (DungeonGrid.GetOccupant(moveCell) != null) return; // occupied by something the scan above didn't treat as a target - stay defensive, don't spend the turn on a move that can't happen

        MoveTo(moveCell);
        TryPickUpAt(moveCell);

        if (TryEngageNearbyEnemy()) return;
        TurnManager.Instance.EndPlayerTurn();
    }

    private void TryPickUpAt(Vector2Int cell)
    {
        ItemPickup item = DungeonGrid.TryTakeItem(cell);
        if (item == null) return;

        // Only consume it if the pickup actually accepted it; otherwise put
        // it back on the grid so it isn't destroyed for nothing.
        if (item.PickUp(this)) Destroy(item.gameObject);
        else DungeonGrid.PlaceItem(cell, item);
    }

    /// <summary>Resolves the weapon's attack pattern from primaryTargetCell
    /// (AttackPatternResolver.GetTargetCells - single cell for most weapons, Axe
    /// cleave's extra cells, Spear's second cell) and hands the full target list to
    /// BattleManager.EngagePlayerAttack. Returns false only when there's no
    /// BattleManager in the scene, telling the caller to use the plain single-target
    /// inline fallback instead - that fallback deliberately stays simple
    /// (CLAUDE.md -> "Combat: ONE system, TWO places").</summary>
    private bool EngageDirectedAttack(WeaponType weapon, Vector2Int primaryTargetCell, EnemyController primaryEnemy)
    {
        if (BattleManager.Instance == null) return false;

        var targets = new List<(EnemyController Enemy, float DamageMultiplier)>();
        foreach (var hit in AttackPatternResolver.GetTargetCells(weapon, Cell, primaryTargetCell))
        {
            if (DungeonGrid.GetOccupant(hit.Cell) is EnemyController enemy && !enemy.IsDead)
            {
                float multiplier = hit.IsPrimary ? 1f : AttackPatternResolver.AxeCleaveSecondaryMultiplier;
                targets.Add((enemy, multiplier));
            }
        }

        return BattleManager.Instance.EngagePlayerAttack(this, targets);
    }

    /// <summary>Opens the battle screen if any registered enemy is within BattleManager.EngageRange
    /// (IMPLEMENTED.md -> "Battle screen (encounter flow)") - proximity-based, not
    /// weapon-pattern-aware, so it only ever starts a 1-enemy fight. Reached from the
    /// empty-cell-move branches only; a directed hit (occupant at the target cell, or a
    /// ranged target found along the facing line) resolves through EngageDirectedAttack
    /// instead and never reaches this sweep.</summary>
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
