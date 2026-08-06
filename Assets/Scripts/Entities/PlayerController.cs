using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;

/// <summary>
/// Click-to-move only - no WASD/arrow keys. A click plans a route once
/// (Pathfinder.FindPath) and the player walks it one cell per turn. Clicking
/// directly on an enemy or an IInteractable (SignPost, LootChest) is a valid
/// target - DungeonGrid.IsWalkable is terrain-only and doesn't care about
/// occupancy, and neither ever needs the player to actually step onto its cell:
/// coming within BattleManager.EngageRange opens the battle screen automatically,
/// while an interactable only ever shows a "{Verb} (E)" prompt (InteractionPromptUI)
/// when within its InteractRange - ambient, not tied to having clicked it - and
/// Interact() itself only fires on an explicit E press while that prompt is up.
/// Two-step by design: unlike combat, merely walking near/through an interactable
/// on the way somewhere else should never fire its action, but the prompt itself
/// is harmless to show ambiently. Each step still resolves through the same
/// TryAct as before: move into an empty walkable cell, or attack per the equipped
/// weapon's pattern (ROADMAP.md -> "Weapon-driven attack patterns").
/// </summary>
[RequireComponent(typeof(Entity))]
public class PlayerController : Entity
{
    // Planned once per click and then followed, rather than re-derived each turn.
    // See Pathfinder.FindPath for why re-planning against a moving obstacle cycles.
    private readonly List<Vector2Int> autoWalkPath = new List<Vector2Int>();
    private Vector2Int? pendingClickCell;

    private void Update()
    {
        // Polled every frame, deliberately outside the turn-state gate below:
        // GetMouseButtonDown is true for exactly one frame, while the enemy phase
        // spans several (TurnManager's stagger + animationTailSeconds), so gating
        // this behind PlayerTurn silently dropped any click made while the enemies
        // were still acting - which is most of the time during a walk.
        if (Input.GetMouseButtonDown(0)) BufferClick();

        // Ambient, not gated on whose turn it is or on having clicked the
        // interactable - the prompt is a passive hint (harmless to show any time)
        // and interacting spends no turn, same spirit as signs' original "reading
        // costs nothing" design, now shared by every IInteractable. Suppressed only
        // during an active battle, so E doesn't do anything unexpected while the
        // battle screen owns input.
        bool inBattle = BattleManager.Instance != null && BattleManager.Instance.IsActive;
        IInteractable nearbyInteractable = inBattle ? null : FindNearbyInteractable();
        if (nearbyInteractable != null) InteractionPromptUI.Instance.Show(nearbyInteractable.PromptLabel);
        else InteractionPromptUI.Instance.Hide();
        if (nearbyInteractable != null && Input.GetKeyDown(KeyCode.E))
        {
            nearbyInteractable.Interact(this);
        }

        if (TurnManager.Instance == null || TurnManager.Instance.State != TurnState.PlayerTurn) return;
        if (inBattle)
        {
            // An enemy can also open the battle screen on its own turn (EnemyController.TakeTurn)
            // mid-route - that path never touches PlayerController, so without this the stale
            // route would survive the fight and resume once it ends. Whichever side started
            // the battle, stay put once it's over.
            StopAutoWalk();
            return; // input goes to the battle screen instead
        }

        if (pendingClickCell != null)
        {
            StartAutoWalk(pendingClickCell.Value);
            pendingClickCell = null;
        }

        if (autoWalkPath.Count > 0) ContinueAutoWalk();
    }

    /// <summary>The nearest registered interactable within its own InteractRange of
    /// the player's current cell, or null. Ambient - checked every frame regardless of
    /// movement or clicks, since showing the prompt has no side effect; only pressing
    /// E does.</summary>
    private IInteractable FindNearbyInteractable()
    {
        // Checks every cell a multi-tile fixture (e.g. a 2-wide LootChest) occupies,
        // not just one anchor point, so the prompt appears at InteractRange from
        // whichever side the player actually approaches from.
        foreach (IInteractable interactable in DungeonGrid.Interactables)
        {
            if (interactable == null) continue;
            foreach (Vector2Int cell in interactable.Cells)
            {
                if (GridUtils.WithinRange(Cell, cell, interactable.InteractRange)) return interactable;
            }
        }
        return null;
    }

    private void StopAutoWalk()
    {
        autoWalkPath.Clear();
        pendingClickCell = null;
    }

    /// <summary>Resolves the click to a cell immediately, even if it can't be acted on
    /// until the enemy phase ends. Converting now (not when the turn comes around) is
    /// deliberate: the camera follows the player, so a later conversion of the same
    /// screen point would name whatever cell had slid under the cursor by then.
    ///
    /// EventSystem.IsPointerOverGameObject() blocks on ANY raycastTarget=true hit, not
    /// just interactive controls - which is exactly what's needed here, since the
    /// battle screen's Backdrop and the equipment panel's background are plain, non-
    /// interactive Images deliberately left raycastTarget=true so they cover the map
    /// underneath. (An earlier version of this method narrowed the check to Selectable
    /// only, which fixed an invisible-label click-blocking bug but broke this - a click
    /// on empty space inside an open panel leaked through, queuing a map-walk that fired
    /// the instant the panel closed. Reverted.) The actual fix for that original bug is
    /// in the scene: every purely decorative HUD graphic (labels, fills, icons) has
    /// raycastTarget off, so only real controls and deliberate modal backdrops remain
    /// raycastable - keep new UI elements to that same rule instead of narrowing this
    /// check again.</summary>
    private void BufferClick()
    {
        if (EventSystem.current != null && EventSystem.current.IsPointerOverGameObject()) return; // clicked UI, not the map
        if (Camera.main == null) return;

        Vector2Int cell = GridUtils.WorldToCell(Camera.main.ScreenToWorldPoint(Input.mousePosition));

        // An interactable's own cell fails IsWalkable (it marks itself solid on Start),
        // but it's still a valid click target - same exception Pathfinder.Search itself
        // makes for the goal cell. Checked before the wall rejection below so clicking
        // directly on one isn't mistaken for clicking a wall.
        bool isInteractableCell = DungeonGrid.GetInteractable(cell) != null;
        if (!isInteractableCell && !DungeonGrid.IsWalkable(cell)) return; // clicked a wall — same as walking into one, not a valid target

        pendingClickCell = cell;
    }

    private void StartAutoWalk(Vector2Int goal)
    {
        autoWalkPath.Clear();

        List<Vector2Int> path = Pathfinder.FindPath(Cell, goal);
        if (path == null) return; // unreachable, or already standing there

        autoWalkPath.AddRange(path);
    }

    /// <summary>Takes the next cell off the planned route. Deliberately does NOT
    /// re-plan around anything that got in the way - stopping is the correct answer
    /// there, and re-planning every turn is what made the player circle a chasing
    /// enemy forever instead of ever arriving (see Pathfinder.FindPath).</summary>
    private void ContinueAutoWalk()
    {
        if (TryEngageNearbyEnemy())
        {
            // Walked into engage range of something - fight it, and drop the rest of
            // the route rather than resuming toward the click once combat ends.
            StopAutoWalk();
            return;
        }

        Vector2Int next = autoWalkPath[0];

        // Something occupies the next cell but wasn't close enough to engage above
        // (a dead enemy still awaiting cleanup, another entity). Don't burn turns
        // shuffling around it.
        if (DungeonGrid.IsOccupied(next))
        {
            StopAutoWalk();
            return;
        }

        autoWalkPath.RemoveAt(0);

        // TryAct declines some moves without spending the turn. Since the turn didn't
        // end, Update would run again next frame and retry forever - stop instead.
        Vector2Int before = Cell;
        TryAct(next - Cell);
        if (Cell == before) StopAutoWalk();
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
