using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Pure grid geometry: given a weapon type and a committed primary target cell,
/// returns every cell that attack's pattern hits (ROADMAP.md -> "Weapon-driven
/// attack patterns", pure-targeting pass). Doesn't know about occupancy or
/// walkability - same split DungeonGrid's stateful queries already keep from
/// GridUtils/Pathfinder's pure math. Callers map the returned cells to live
/// EnemyController occupants themselves.
///
/// Bow/Staff's range (finding a target up to 2-3 cells away) is target
/// ACQUISITION, not target EXPANSION - that's PlayerController.TryAct's job,
/// scanning for the primaryTargetCell to hand in here. This resolver only
/// answers "given that primary target, what else does the swing/shot hit."
/// </summary>
public static class AttackPatternResolver
{
    /// <summary>Damage multiplier for Axe cleave's secondary (non-primary) targets -
    /// hand-picked placeholder (ROADMAP.md's table says "reduced dmg" with no
    /// number), same "retune freely" spirit as EnemyController's
    /// goldRewardMin/Max or ItemPricing's base-value table.</summary>
    public const float AxeCleaveSecondaryMultiplier = 0.5f;

    public readonly struct Target
    {
        public readonly Vector2Int Cell;
        public readonly bool IsPrimary;

        public Target(Vector2Int cell, bool isPrimary)
        {
            Cell = cell;
            IsPrimary = isPrimary;
        }
    }

    public static IReadOnlyList<Target> GetTargetCells(WeaponType weaponType, Vector2Int attackerCell, Vector2Int primaryTargetCell)
    {
        switch (weaponType)
        {
            case WeaponType.Axe:
                var cleave = new List<Target> { new Target(primaryTargetCell, true) };
                foreach (Vector2Int dir in GridUtils.CardinalDirections)
                {
                    cleave.Add(new Target(primaryTargetCell + dir, false));
                }
                return cleave;

            case WeaponType.Spear:
                Vector2Int direction = primaryTargetCell - attackerCell;
                direction = new Vector2Int(System.Math.Sign(direction.x), System.Math.Sign(direction.y));
                return new List<Target>
                {
                    new Target(primaryTargetCell, true),
                    new Target(primaryTargetCell + direction, true)
                };

            default: // None/Sword/Mace/Hammer/Dagger/Bow/Staff - single target, ranged-ness is acquisition not expansion
                return new List<Target> { new Target(primaryTargetCell, true) };
        }
    }
}
