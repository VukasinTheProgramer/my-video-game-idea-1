using System;

/// <summary>
/// Full combat stat block for an Entity. See COMBAT_DESIGN.md §1/§1a for the
/// design rationale and the formulas that consume these values
/// (CombatResolver resolves one attack from a pair of Stats).
///
/// Percent-based fields (critChanceBonus, critDamageBonus, dodgeBonus,
/// parryBonus, lifeSteal) are stored as percentage points (5f == 5%), not
/// fractions, to match the design doc's formulas directly. They represent
/// the *bonus on top of* CombatResolver's built-in formula base (e.g. crit
/// chance is always "5 + AGI*1 + critChanceBonus", never critChanceBonus
/// alone) — gear/points never need to know the base numbers exist.
/// </summary>
[Serializable]
public class Stats
{
    public int attack;
    public int agility;
    public int magic;
    public int defense;      // physical
    public int magicDefense; // magic

    public int maxHp;

    public float critChanceBonus;
    public float critDamageBonus;
    public float dodgeBonus;
    public float parryBonus;
    public float lifeSteal;

    /// <summary>Level 1 starting baseline before any gear — COMBAT_DESIGN.md §1a.</summary>
    public static Stats Level1Default()
    {
        return new Stats
        {
            attack = 1,
            agility = 1,
            magic = 0,
            defense = 5,
            magicDefense = 5,
            maxHp = 101, // 100 flat + 1 baseline Health point
        };
    }

    public static Stats operator +(Stats a, Stats b)
    {
        return new Stats
        {
            attack = a.attack + b.attack,
            agility = a.agility + b.agility,
            magic = a.magic + b.magic,
            defense = a.defense + b.defense,
            magicDefense = a.magicDefense + b.magicDefense,
            maxHp = a.maxHp + b.maxHp,
            critChanceBonus = a.critChanceBonus + b.critChanceBonus,
            critDamageBonus = a.critDamageBonus + b.critDamageBonus,
            dodgeBonus = a.dodgeBonus + b.dodgeBonus,
            parryBonus = a.parryBonus + b.parryBonus,
            lifeSteal = a.lifeSteal + b.lifeSteal,
        };
    }
}
