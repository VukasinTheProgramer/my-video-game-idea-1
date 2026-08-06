using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Click-to-move only - no WASD/arrow keys. A click plans a route once
/// (Pathfinder.FindPath) and the player walks it one cell per turn. Clicking
/// directly on an enemy is a valid target - DungeonGrid.IsWalkable is terrain-only
/// and doesn't care about occupancy - and the walk never has to step onto its cell,
/// because coming within BattleManager.EngageRange opens the battle screen first.
/// Each step still resolves through the same TryAct as before: move into an empty
/// walkable cell, or attack per the equipped weapon's pattern (ROADMAP.md ->
/// "Weapon-driven attack patterns").
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

        if (TurnManager.Instance == null || TurnManager.Instance.State != TurnState.PlayerTurn) return;
        if (BattleManager.Instance != null && BattleManager.Instance.IsActive)
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

    private void StopAutoWalk()
    {
        autoWalkPath.Clear();
        pendingClickCell = null;
    }

    /// <summary>Resolves the click to a cell immediately, even if it can't be acted on
    /// until the enemy phase ends. Converting now (not when the turn comes around) is
    /// deliberate: the camera follows the player, so a later conversion of the same
    /// screen point would name whatever cell had slid under the cursor by then.</summary>
    private void BufferClick()
    {
        if (IsPointerOverBlockingUI()) return; // clicked UI, not the map
        if (Camera.main == null) return;

        Vector2Int cell = GridUtils.WorldToCell(Camera.main.ScreenToWorldPoint(Input.mousePosition));
        if (!DungeonGrid.IsWalkable(cell)) return; // clicked a wall — same as walking into one, not a valid target

        pendingClickCell = cell;
    }

    /// <summary>
    /// True only if the pointer is over UI the player could actually interact with.
    /// EventSystem.IsPointerOverGameObject() alone is NOT enough: Unity defaults every
    /// Text/Image to raycastTarget = true, and the raycaster hit-tests the whole
    /// RectTransform, not the visible glyphs - so a HUD label (even an empty one, like
    /// GameOverText sitting invisible in the center of the screen) silently swallowed
    /// every map click inside its 600x100 rect. That reads as "the game randomly won't
    /// move," and because the camera follows the player, the dead zone is fixed to the
    /// screen while sliding over the world, so it looks intermittent rather than
    /// positional. The scene's labels have raycastTarget off now; this filter is the
    /// guard that keeps the next added label from resurrecting the bug.
    /// </summary>
    private static bool IsPointerOverBlockingUI()
    {
        if (EventSystem.current == null) return false;

        var pointer = new PointerEventData(EventSystem.current) { position = Input.mousePosition };
        var hits = new List<RaycastResult>();
        EventSystem.current.RaycastAll(pointer, hits);

        foreach (RaycastResult hit in hits)
        {
            // A Selectable (Button/Slider/...) is a real control; anything else under
            // the cursor is decoration that shouldn't consume a click meant for the map.
            if (hit.gameObject.GetComponentInParent<Selectable>() != null) return true;
        }
        return false;
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
