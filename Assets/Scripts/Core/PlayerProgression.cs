using System;
using UnityEngine;

/// <summary>
/// Player XP, leveling, and stat-point spending (COMBAT_DESIGN.md §1a).
/// Attach to the same GameObject as PlayerController/Entity.
///
/// Points are permanent once spent (no respec, matching Bit Heroes) - so
/// unlike gear this never needs to be "un-applied," it just keeps adding to
/// Entity.baseStats via the existing ApplyStatBonus(Stats) hook (§2's
/// floor-scaling buffs use the same method, so this needed zero Entity
/// changes).
/// </summary>
[RequireComponent(typeof(Entity))]
public class PlayerProgression : MonoBehaviour
{
    /// <summary>The three stats a player can spend points on (§1a) - Magic/DEF/MDEF
    /// stay gear-only, same as the original 10-starting-point design.</summary>
    public enum AllocatableStat { Attack, Health, Agility }

    [Header("Starting pool - COMBAT_DESIGN.md §1a's original 10 free points, now spent through this same system")]
    [SerializeField] private int startingStatPoints = 10;

    [Header("Stat points granted each time you level up")]
    [SerializeField] private int pointsPerLevelUp = 1;

    [Header("XP curve - flat XP per enemy (EnemyController.xpReward), escalating cost per level: xpToNextLevel = baseXPToLevel2 + (level-1) * xpGrowthPerLevel")]
    [SerializeField] private int baseXPToLevel2 = 100;
    [SerializeField] private int xpGrowthPerLevel = 50;

    public int Level { get; private set; } = 1;
    public int CurrentXP { get; private set; }
    public int AvailableStatPoints { get; private set; }

    /// <summary>XP required to go from the current level to the next one.</summary>
    public int XPToNextLevel => baseXPToLevel2 + (Level - 1) * xpGrowthPerLevel;

    /// <summary>Fires whenever XP changes (including from a level-up's carryover/reset), with (currentXP, xpToNextLevel).</summary>
    public event Action<int, int> OnXPChanged;

    /// <summary>Fires once per level gained (AddXP can fire this multiple times off one big XP reward).</summary>
    public event Action<int> OnLevelUp;

    /// <summary>Fires whenever AvailableStatPoints changes (level-up grant or a point being spent).</summary>
    public event Action<int> OnStatPointsChanged;

    private Entity entity;

    private void Awake()
    {
        entity = GetComponent<Entity>();
        AvailableStatPoints = startingStatPoints;
    }

    private void Start()
    {
        // Let UI that binds in Start (e.g. panels subscribing on open) read the
        // initial state without needing a separate "GetInitial" API.
        OnXPChanged?.Invoke(CurrentXP, XPToNextLevel);
        OnStatPointsChanged?.Invoke(AvailableStatPoints);
    }

    /// <summary>Grants XP (e.g. EnemyController.Die on a kill) and rolls any level-ups it crosses.</summary>
    public void AddXP(int amount)
    {
        if (amount <= 0 || entity == null || entity.IsDead) return;

        CurrentXP += amount;

        while (CurrentXP >= XPToNextLevel)
        {
            CurrentXP -= XPToNextLevel;
            Level++;
            AvailableStatPoints += pointsPerLevelUp;
            OnLevelUp?.Invoke(Level);
            OnStatPointsChanged?.Invoke(AvailableStatPoints);
        }

        OnXPChanged?.Invoke(CurrentXP, XPToNextLevel);
    }

    /// <summary>Spends one available point on the given stat (flat 1:1, §1a). Returns false if no points are left.</summary>
    public bool TrySpendPoint(AllocatableStat stat)
    {
        if (AvailableStatPoints <= 0 || entity == null) return false;

        switch (stat)
        {
            case AllocatableStat.Attack:
                entity.ApplyStatBonus(new Stats { attack = 1 });
                break;
            case AllocatableStat.Health:
                entity.ApplyStatBonus(new Stats { maxHp = 1 });
                break;
            case AllocatableStat.Agility:
                entity.ApplyStatBonus(new Stats { agility = 1 });
                break;
        }

        AvailableStatPoints--;
        OnStatPointsChanged?.Invoke(AvailableStatPoints);
        return true;
    }
}
