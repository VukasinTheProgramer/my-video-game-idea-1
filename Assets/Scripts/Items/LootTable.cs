using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Drop table an enemy rolls against on death (ROADMAP.md -> "Multi-drop loot
/// rolls"). Quantity (how many things drop) and quality (how rare each one is)
/// are two independent axes, rolled separately - genre convention (Diablo/PoE
/// both keep Magic Find quality-only, never quantity), not this project's own
/// invention. 10 independent slots, each with its own fixed chance, roll every
/// kill; a slot that succeeds then rolls a rarity from a flat per-item
/// percentage table, and only then picks an item of that rarity from `entries`
/// - each entry's own EquippableItem.rarity groups it, no separate per-entry
/// weight needed anymore.
///
/// Runic is deliberately excluded from the rarity table - not obtainable
/// through random drops at all (ROADMAP.md -> "Open / undecided", "How Runic
/// items are actually obtained").
/// </summary>
[CreateAssetMenu(menuName = "Items/Loot Table")]
public class LootTable : ScriptableObject
{
    [System.Serializable]
    public class Entry
    {
        public EquippableItem item;
    }

    [SerializeField] private Entry[] entries;

    // Rolled independently, all at once, not gated on each other - "double
    // Common" or "Common + Rare together" both fall out naturally from two
    // slots landing differently, no per-combination special-casing. Halved
    // from the original 95/56/33/19.5/11.5/6.8/4/2.4/1.4/0.8 (~230.4%, ~2.3
    // items/kill - too frequent for an unlimited bag with no pickup friction)
    // down to ~115.2%, i.e. ~1.15 expected items per kill on average, same
    // decay shape just scaled down.
    private static readonly float[] SlotChances =
    {
        47.5f, 28f, 16.5f, 9.75f, 5.75f, 3.4f, 2f, 1.2f, 0.7f, 0.4f
    };

    // Flat per-item % a succeeding slot lands on this rarity - solved backward
    // from the "resulting chance of at least one per kill" target (ROADMAP.md's
    // own table), not guessed forward. Sums to 100%.
    private static readonly (Rarity Rarity, float Chance)[] RarityChances =
    {
        (Rarity.Common, 53.10f),
        (Rarity.Uncommon, 35.17f),
        (Rarity.Rare, 9.41f),
        (Rarity.Epic, 2.21f),
        (Rarity.Set, 0.043f),
        (Rarity.Legendary, 0.043f),
        (Rarity.Mythic, 0.022f),
    };

    /// <summary>Rolls all 10 slots and returns every item that dropped - zero to
    /// ten entries, most kills yield 0-2. A slot landing on a rarity with no
    /// authored item in this table (Legendary/Set/Mythic as of 2026-08-06 - no
    /// items exist at those tiers yet, a data gap not a bug, CLAUDE.md §5) simply
    /// drops nothing for that slot rather than falling back to a lower tier or
    /// rerolling - upgrade path once those tiers have items: none needed, this
    /// starts working the moment an Entry with that rarity exists.</summary>
    public IReadOnlyList<EquippableItem> RollDrops()
    {
        var drops = new List<EquippableItem>();
        if (entries == null || entries.Length == 0) return drops;

        foreach (float slotChance in SlotChances)
        {
            if (Random.Range(0f, 100f) >= slotChance) continue;

            Rarity? rarity = RollRarity();
            if (rarity == null) continue;

            EquippableItem item = PickItemOfRarity(rarity.Value);
            if (item != null) drops.Add(item);
        }

        return drops;
    }

    private static Rarity? RollRarity()
    {
        float roll = Random.Range(0f, 100f);
        float cumulative = 0f;
        foreach (var (rarity, chance) in RarityChances)
        {
            cumulative += chance;
            // Strict < so an exact roll of 0 can't select the first tier before any
            // chance has accumulated (LootTable's old weighted-pick used the same guard).
            if (roll < cumulative) return rarity;
        }
        return null; // table sums to 100 so this shouldn't happen, but stay defensive rather than throw
    }

    private EquippableItem PickItemOfRarity(Rarity rarity)
    {
        var candidates = new List<EquippableItem>();
        foreach (Entry entry in entries)
        {
            if (entry?.item != null && entry.item.rarity == rarity) candidates.Add(entry.item);
        }
        if (candidates.Count == 0) return null; // no item authored at this tier yet
        return candidates[Random.Range(0, candidates.Count)];
    }
}
