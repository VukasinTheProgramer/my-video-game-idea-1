using UnityEngine;

/// <summary>
/// Turns an authored EquippableItem template into an actual dropped instance
/// with rolled stats (.claude/ITEM_SCALING.md, base-roll model - Layer 0 only,
/// scoped 2026-08-06). A template's own bonusStats/itemLevel become dead data
/// for anything routed through Generate (EnemyController.Die()'s loot drops,
/// GameManager.SpawnOrMovePlayer's starting weapon) - the one exception is
/// Trinket1/Trinket2, which have no budget spec yet (ITEM_SCALING.md defers
/// them) and fall back to copying the template's bonusStats unchanged.
///
/// weaponType/slot/rarity/displayName/sprite frames are copied verbatim from
/// the template; only bonusStats/itemLevel are generated fresh per instance.
/// Percent stats (crit/dodge/parry/life steal) stay 0 - the rarity table's
/// "bonus stat rolls" count column has no specified mechanics anywhere yet,
/// deferred along with SetId/MythicSkill/RuneSocketCount (ITEM_SCALING.md §3
/// "Still needed for the upper tiers").
/// </summary>
public static class ItemGenerator
{
    private const float MaxHpBudgetCost = 4f; // 4 budget = 1 maxHp point, everything else costs 1 per point

    private static readonly (EquipmentSlot Slot, int Budget)[] SlotBudgets =
    {
        (EquipmentSlot.Head, 10), (EquipmentSlot.Neck, 8), (EquipmentSlot.OffHand, 15),
        (EquipmentSlot.Shoulders, 12), (EquipmentSlot.Chest, 17), (EquipmentSlot.Hands, 11),
        (EquipmentSlot.Back, 10), (EquipmentSlot.Legs, 13), (EquipmentSlot.Feet, 10),
        (EquipmentSlot.Belt, 10), (EquipmentSlot.Ring1, 6), (EquipmentSlot.Ring2, 6),
    };

    // Sword/Axe/Mace always roll their 1H row - Handedness doesn't exist as a field
    // yet (ROADMAP.md -> "Weapon handedness & shields", separate unbuilt item), and
    // none of the 3 existing weapon assets are flagged 2H. The 2H rows from
    // ITEM_SCALING.md's table are unreachable until that ships - not a bug, a
    // scoped-out gap.
    private static readonly (WeaponType Weapon, int Min, int Max)[] WeaponRanges =
    {
        (WeaponType.Sword, 7, 12), (WeaponType.Axe, 8, 13), (WeaponType.Mace, 7, 12),
        (WeaponType.Hammer, 13, 18), (WeaponType.Spear, 9, 14), (WeaponType.Dagger, 7, 12),
        (WeaponType.Bow, 5, 9), (WeaponType.Staff, 5, 9),
    };

    public static EquippableItem Generate(EquippableItem template, int floorStep, Rarity rarity)
    {
        var item = ScriptableObject.CreateInstance<EquippableItem>();
        item.slot = template.slot;
        item.displayName = template.displayName;
        item.weaponType = template.weaponType;
        item.rarity = rarity;
        item.itemLevel = 1 + floorStep;
        CopySpriteFrames(template, item);

        if (template.slot == EquipmentSlot.Trinket1 || template.slot == EquipmentSlot.Trinket2)
        {
            item.bonusStats = template.bonusStats; // no budget spec yet - ITEM_SCALING.md defers these two slots
            return item;
        }

        Stats stats = template.slot == EquipmentSlot.MainHand
            ? RollWeaponStat(template.weaponType)
            : RollArmorBudget(template.slot);

        ApplyFloorGrowth(stats, floorStep);
        item.bonusStats = Multiply(stats, RarityMultiplier(rarity));
        return item;
    }

    private static Stats RollArmorBudget(EquipmentSlot slot)
    {
        int budget = SlotBudget(slot);
        var stats = new Stats();
        if (budget <= 0) return stats;

        // 5 normalized random weights -> points (maxHp at 4:1), floored, then any
        // leftover from flooring spent one point at a time on whichever stats can
        // still afford it - a leftover of 1-3 can't buy another maxHp point.
        float[] weights = { Random.value, Random.value, Random.value, Random.value, Random.value };
        float weightSum = weights[0] + weights[1] + weights[2] + weights[3] + weights[4];

        int[] points = new int[5]; // attack, agility, defense, magicDefense, maxHp
        float[] costs = { 1f, 1f, 1f, 1f, MaxHpBudgetCost };
        float spent = 0f;

        for (int i = 0; i < 5; i++)
        {
            float share = budget * (weights[i] / weightSum);
            points[i] = Mathf.FloorToInt(share / costs[i]);
            spent += points[i] * costs[i];
        }

        float remaining = budget - spent;
        int guardIterations = 0;
        while (remaining > 0f && guardIterations < 100)
        {
            guardIterations++;
            int[] affordable = AffordableIndices(costs, remaining);
            if (affordable.Length == 0) break;

            int pick = affordable[Random.Range(0, affordable.Length)];
            points[pick]++;
            remaining -= costs[pick];
        }

        stats.attack = points[0];
        stats.agility = points[1];
        stats.defense = points[2];
        stats.magicDefense = points[3];
        stats.maxHp = points[4];
        return stats;
    }

    private static int[] AffordableIndices(float[] costs, float remaining)
    {
        var list = new System.Collections.Generic.List<int>();
        for (int i = 0; i < costs.Length; i++)
        {
            if (costs[i] <= remaining) list.Add(i);
        }
        return list.ToArray();
    }

    private static Stats RollWeaponStat(WeaponType weaponType)
    {
        var stats = new Stats();
        int roll = 0;
        foreach (var (weapon, min, max) in WeaponRanges)
        {
            if (weapon == weaponType) { roll = Random.Range(min, max + 1); break; }
        }

        // Matches CombatResolver.ScalingStatFor's own weapon->stat mapping.
        switch (weaponType)
        {
            case WeaponType.Bow:
            case WeaponType.Dagger:
                stats.agility = roll;
                break;
            case WeaponType.Staff:
                stats.magic = roll;
                break;
            default: // Sword/Axe/Mace/Hammer/Spear
                stats.attack = roll;
                break;
        }
        return stats;
    }

    private static int SlotBudget(EquipmentSlot slot)
    {
        foreach (var (s, budget) in SlotBudgets)
        {
            if (s == slot) return budget;
        }
        return 0;
    }

    /// <summary>+1 every N floors per stat (.claude/ITEM_SCALING.md §2) - always a
    /// no-op this pass, floorStep is always 0 until Layer 1 exists, but wired in so
    /// this function doesn't need a second pass later.</summary>
    private static void ApplyFloorGrowth(Stats stats, int floorStep)
    {
        if (floorStep <= 0) return;

        stats.maxHp += floorStep;
        stats.defense += floorStep / 2;
        stats.magicDefense += floorStep / 2;
        stats.attack += floorStep / 3;
        stats.agility += floorStep / 3;
        stats.magic += floorStep / 3;
        stats.critChanceBonus += floorStep / 4;
        stats.critDamageBonus += floorStep / 4;
        stats.dodgeBonus += floorStep / 4;
        stats.parryBonus += floorStep / 4;
        stats.lifeSteal += floorStep / 4;
    }

    private static Stats Multiply(Stats stats, float multiplier)
    {
        return new Stats
        {
            attack = Mathf.RoundToInt(stats.attack * multiplier),
            agility = Mathf.RoundToInt(stats.agility * multiplier),
            magic = Mathf.RoundToInt(stats.magic * multiplier),
            defense = Mathf.RoundToInt(stats.defense * multiplier),
            magicDefense = Mathf.RoundToInt(stats.magicDefense * multiplier),
            maxHp = Mathf.RoundToInt(stats.maxHp * multiplier),
            critChanceBonus = stats.critChanceBonus * multiplier,
            critDamageBonus = stats.critDamageBonus * multiplier,
            dodgeBonus = stats.dodgeBonus * multiplier,
            parryBonus = stats.parryBonus * multiplier,
            lifeSteal = stats.lifeSteal * multiplier,
        };
    }

    /// <summary>Switch, not an array indexed by (int)rarity - Rarity.Common = -1
    /// (Rarity.cs's own rename-safety int values) would break array indexing.
    /// Mirrors ItemPricing.BaseValue/RarityVisuals.OutlineColor's existing pattern.</summary>
    private static float RarityMultiplier(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common: return 1.0f;
            case Rarity.Uncommon: return 1.07f;
            case Rarity.Rare: return 1.15f;
            case Rarity.Epic: return 1.3f;
            case Rarity.Legendary: return 1.5f;
            case Rarity.Set: return 1.5f;
            case Rarity.Mythic: return 1.7f;
            case Rarity.Runic: return 1.9f;
            default: return 1.0f;
        }
    }

    private static void CopySpriteFrames(EquippableItem from, EquippableItem to)
    {
        to.walkDown = from.walkDown;
        to.walkLeft = from.walkLeft;
        to.walkRight = from.walkRight;
        to.walkUp = from.walkUp;
        to.walkBehind = from.walkBehind;
        to.hurt = from.hurt;
        to.hurtBehind = from.hurtBehind;
        to.slashDown = from.slashDown;
        to.slashLeft = from.slashLeft;
        to.slashRight = from.slashRight;
        to.slashUp = from.slashUp;
        to.slashBehind = from.slashBehind;
    }
}
