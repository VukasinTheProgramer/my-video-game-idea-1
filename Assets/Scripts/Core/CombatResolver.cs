using UnityEngine;

/// <summary>
/// Outcome of a single Entity.Attack call. Entity.Attack applies Damage/
/// LifeStolen; UI (COMBAT_DESIGN.md §6 damage numbers, not implemented yet)
/// will read WasCrit/WasDodged/WasParried to color/label the popup.
/// </summary>
public readonly struct CombatResult
{
    public readonly int Damage;
    public readonly bool WasCrit;
    public readonly bool WasDodged;
    public readonly bool WasParried;
    public readonly int LifeStolen;

    public CombatResult(int damage, bool wasCrit, bool wasDodged, bool wasParried, int lifeStolen)
    {
        Damage = damage;
        WasCrit = wasCrit;
        WasDodged = wasDodged;
        WasParried = wasParried;
        LifeStolen = lifeStolen;
    }
}

/// <summary>
/// Resolves one attack per COMBAT_DESIGN.md §1: picks the scaling stat
/// (ATK/AGI/MAG) and defense stat (DEF/MDEF) from the attacker's weapon
/// type, rolls Dodge -> Parry -> Crit in that order, then applies whichever
/// damage formula won. Stateless/static — it only reads the two Entities
/// involved, no per-resolver state to manage.
/// </summary>
public static class CombatResolver
{
    public static CombatResult Resolve(Entity attacker, Entity defender)
    {
        Stats atk = attacker.Stats;
        Stats def = defender.Stats;
        WeaponType weapon = attacker.EquippedWeaponType;

        int scalingStat = ScalingStatFor(weapon, atk);
        int defenseStat = DefenseStatFor(weapon, def);

        int variance = Random.Range(-1, 2); // -1, 0, or 1
        int rawDamage = scalingStat + attacker.WeaponDamage + variance;

        float dodgeChance = EffectiveDodgeChance(def);
        bool dodged = Roll(dodgeChance);

        bool defenderCanParry = CanParry(defender.EquippedWeaponType);
        float parryChance = EffectiveParryChance(def, defenderCanParry);
        bool parried = !dodged && Roll(parryChance);

        float critChance = EffectiveCritChance(atk);
        bool crit = !dodged && !parried && Roll(critChance);

        int damage = ResolveDamage(rawDamage, defenseStat, dodged, parried, crit, atk.critDamageBonus);

        // §1 defines life steal as a % of damage *dealt*, so overkill doesn't pay
        // out: a 50-damage hit on a 1 HP target only removed 1 HP. The rate is
        // capped here too - it's the one modifier whose cap the doc lists but
        // nothing else in the codebase enforced.
        int damageDealt = Mathf.Min(damage, defender.CurrentHealth);
        int lifeStolen = dodged ? 0 : Mathf.RoundToInt(damageDealt * EffectiveLifeStealPercent(atk) / 100f);

        return new CombatResult(damage, crit, dodged, parried, lifeStolen);
    }

    // --- Formulas below are exposed publicly so stat-display UI shows the exact
    // numbers combat actually rolls against, instead of a second copy that can
    // drift out of sync (COMBAT_DESIGN.md §1). ---

    /// <summary>
    /// Melee-capable weapons may Parry (§1). Unarmed (None) may NOT: the doc ties
    /// Parry to having a melee weapon out to block with, so an empty MainHand rolls
    /// 0% rather than inheriting the "unarmed defaults to ATK/DEF" rule that
    /// ScalingStatFor/DefenseStatFor use for damage.
    /// </summary>
    public static bool CanParry(WeaponType weapon) =>
        weapon != WeaponType.None && weapon != WeaponType.Bow && weapon != WeaponType.Staff;

    /// <summary>Life steal rate after the documented 50% cap (§1 modifier table).</summary>
    public static float EffectiveLifeStealPercent(Stats attacker) => Mathf.Min(50f, attacker.lifeSteal);

    public static float EffectiveDodgeChance(Stats defender) =>
        Mathf.Min(25f, 2.5f + defender.agility * 0.5f + defender.dodgeBonus);

    /// <summary>0 if the defender's weapon can't Parry (see CanParry).</summary>
    public static float EffectiveParryChance(Stats defender, bool canParry) =>
        canParry ? Mathf.Min(20f, defender.agility * 0.25f + defender.parryBonus) : 0f;

    public static float EffectiveCritChance(Stats attacker) =>
        Mathf.Min(50f, 5f + attacker.agility * 1f + attacker.critChanceBonus);

    /// <summary>Multiplier applied to raw damage on a crit, e.g. 2.0 = 200%.</summary>
    public static float EffectiveCritDamageMultiplier(float critDamageBonus) =>
        2f + critDamageBonus / 100f;

    private static int ResolveDamage(int rawDamage, int defenseStat, bool dodged, bool parried, bool crit, float critDamageBonus)
    {
        if (dodged) return 0;
        if (parried) return Mathf.Max(1, Mathf.RoundToInt(rawDamage * 0.25f) - defenseStat);

        if (crit)
        {
            float critMultiplier = EffectiveCritDamageMultiplier(critDamageBonus);
            return Mathf.Max(1, Mathf.RoundToInt(rawDamage * critMultiplier) - defenseStat);
        }

        return Mathf.Max(1, rawDamage - defenseStat);
    }

    private static int ScalingStatFor(WeaponType weapon, Stats stats)
    {
        switch (weapon)
        {
            case WeaponType.Bow:
            case WeaponType.Dagger:
                return stats.agility;
            case WeaponType.Staff:
                return stats.magic;
            default:
                return stats.attack; // Sword/Axe/Mace/Hammer/Spear/None (unarmed)
        }
    }

    private static int DefenseStatFor(WeaponType weapon, Stats stats)
    {
        return weapon == WeaponType.Staff ? stats.magicDefense : stats.defense;
    }

    private static bool Roll(float percentChance)
    {
        if (percentChance <= 0f) return false;
        return Random.Range(0f, 100f) < percentChance;
    }
}
