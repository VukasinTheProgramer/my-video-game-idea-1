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
    /// starts working the moment an Entry with that rarity exists.
    ///
    /// slotChanceMultiplier scales every slot's roll chance (clamped to 100% each -
    /// a probability can't exceed that); topTierRarityMultiplier scales the combined
    /// Set/Legendary/Mythic rarity chance, pulling the exact increase out of Common
    /// so the table still sums to 100% - the same mechanism LAYERS.md's boss loot
    /// multipliers already document (mini/medium/big boss 2x/5x/20x), generalized
    /// here so LootChest can reuse it instead of a second copy of this math. Both
    /// default to 1 (no change), so every existing caller (EnemyController) behaves
    /// identically to before this generalization.</summary>
    public IReadOnlyList<EquippableItem> RollDrops(float slotChanceMultiplier = 1f, float topTierRarityMultiplier = 1f)
    {
        var drops = new List<EquippableItem>();
        if (entries == null || entries.Length == 0) return drops;

        foreach (float slotChance in SlotChances)
        {
            float boostedChance = Mathf.Min(100f, slotChance * slotChanceMultiplier);
            if (Random.Range(0f, 100f) >= boostedChance) continue;

            Rarity? rarity = RollRarity(topTierRarityMultiplier);
            if (rarity == null) continue;

            EquippableItem item = PickItemOfRarity(rarity.Value);
            if (item != null) drops.Add(item);
        }

        return drops;
    }

    private static bool IsTopTier(Rarity rarity) =>
        rarity == Rarity.Set || rarity == Rarity.Legendary || rarity == Rarity.Mythic;

    private static Rarity? RollRarity(float topTierRarityMultiplier)
    {
        // Increase is computed once as an absolute percentage-point amount, then
        // added to the top tiers and subtracted from Common - not a proportional
        // renormalization across every tier, matching LAYERS.md's own worked
        // example exactly (a 20x big-boss multiplier pulls Common from 53.10%
        // down to 51.05%, not scaled down uniformly).
        float topTierIncrease = 0f;
        if (topTierRarityMultiplier != 1f)
        {
            float baseTopTier = 0f;
            foreach (var (rarity, chance) in RarityChances)
            {
                if (IsTopTier(rarity)) baseTopTier += chance;
            }
            topTierIncrease = baseTopTier * (topTierRarityMultiplier - 1f);
        }

        float roll = Random.Range(0f, 100f);
        float cumulative = 0f;
        foreach (var (rarity, chance) in RarityChances)
        {
            float adjusted = chance;
            if (rarity == Rarity.Common) adjusted -= topTierIncrease;
            else if (IsTopTier(rarity)) adjusted = chance * topTierRarityMultiplier;

            cumulative += adjusted;
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

    /// <summary>Picks one item whose rarity is minRarity or better (Rarity's explicit
    /// int values make >= a real ordering, not just tag equality - CLAUDE.md §0/Rarity.cs),
    /// weighted by this table's own RarityChances among just the qualifying tiers,
    /// renormalized so those relative odds sum to 100 among themselves. Separate from
    /// RollDrops' normal per-slot roll - used for a "guaranteed floor" top-up (a
    /// special LootChest variant that always includes at least one Rare+), not part
    /// of the ordinary 10-slot pass. Returns null only if no tier at or above
    /// minRarity has an authored item in this table.</summary>
    public EquippableItem RollAtLeastRarity(Rarity minRarity)
    {
        var qualifying = new List<(Rarity Rarity, float Chance)>();
        float totalChance = 0f;
        foreach (var (rarity, chance) in RarityChances)
        {
            if (rarity < minRarity) continue;
            qualifying.Add((rarity, chance));
            totalChance += chance;
        }
        if (qualifying.Count == 0 || totalChance <= 0f) return null;

        float roll = Random.Range(0f, totalChance);
        float cumulative = 0f;
        foreach (var (rarity, chance) in qualifying)
        {
            cumulative += chance;
            if (roll < cumulative)
            {
                EquippableItem item = PickItemOfRarity(rarity);
                if (item != null) return item;
                break; // that specific tier has no authored item - fall through to the scan below instead of returning null outright
            }
        }

        // The weighted pick landed on (or fell through from) a tier with nothing
        // authored - try every qualifying tier before giving up, so "no Rare items
        // exist yet but Epic does" still honors the guarantee instead of silently
        // granting nothing.
        foreach (var (rarity, _) in qualifying)
        {
            EquippableItem item = PickItemOfRarity(rarity);
            if (item != null) return item;
        }
        return null;
    }

    /// <summary>RollDrops, then - only if guaranteesMinRarity is true and that roll
    /// didn't already produce a qualifying item - tops up with one more via
    /// RollAtLeastRarity. The exact combined "roll then check then top up" sequence
    /// both LootChest.Interact (a rare/special chest) and EnemyController.Die (a
    /// boss kill, LAYERS.md -> "Boss stat &amp; loot multipliers", "a guaranteed
    /// rarity floor on one item") need - kept here once instead of duplicated in
    /// both callers.</summary>
    public IReadOnlyList<EquippableItem> RollDropsWithGuarantee(
        float slotChanceMultiplier, float topTierRarityMultiplier,
        bool guaranteesMinRarity, Rarity guaranteedMinRarity)
    {
        var drops = new List<EquippableItem>(RollDrops(slotChanceMultiplier, topTierRarityMultiplier));

        if (guaranteesMinRarity && !drops.Exists(item => item.rarity >= guaranteedMinRarity))
        {
            EquippableItem guaranteed = RollAtLeastRarity(guaranteedMinRarity);
            if (guaranteed != null) drops.Add(guaranteed);
        }

        return drops;
    }
}
