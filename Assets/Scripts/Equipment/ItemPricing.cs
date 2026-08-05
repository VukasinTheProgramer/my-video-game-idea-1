using UnityEngine;

/// <summary>
/// Gold value for selling a bag item (ROADMAP.md -> "Currency: gold & gems",
/// sell-from-bag sink). Base value scales with Rarity, itemLevel multiplies
/// it - both hand-picked v1 numbers, same spirit as EnemyController's
/// hand-tuned goldRewardMin/Max, not derived from anything else. Retune
/// freely; nothing else in the project depends on these exact values.
///
/// Switch, not an int[] indexed by (int)rarity - Rarity.Common is -1
/// (Rarity.cs's §0 note explains why), so a plain array lookup would throw.
/// </summary>
public static class ItemPricing
{
    public static int SellValue(EquippableItem item)
    {
        return BaseValue(item.rarity) * Mathf.Max(1, item.itemLevel);
    }

    private static int BaseValue(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common: return 2;
            case Rarity.Uncommon: return 5;
            case Rarity.Rare: return 15;
            case Rarity.Epic: return 40;
            case Rarity.Legendary: return 100;
            case Rarity.Set: return 100; // power-parity with Legendary, same as RarityVisuals
            case Rarity.Mythic: return 250;
            case Rarity.Runic: return 250;
            default: return 5;
        }
    }
}
