using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// The player's held potions, plus the shared use-cooldown. Same shape as
/// Inventory/Wallet - a small component on the player, event-driven so UI
/// never polls (CLAUDE.md rule 1).
///
/// Kept separate from Inventory rather than widening it: Inventory is typed
/// to EquippableItem and every one of its callers (equip, sort by rarity/
/// item level, the bag panel's drag-to-equip) assumes a wearable. Mixing an
/// unwearable type in would mean a null-slot guard at each of those sites.
///
/// COOLDOWN: one potion per PotionCooldownFloors floors, tracked by floor
/// number rather than a timer - this is a turn-based game with no wall clock
/// during a fight, and floors are the only monotonic progress counter that
/// survives the player standing still.
/// </summary>
public class PotionBag : MonoBehaviour
{
    /// <summary>Floors that must pass between two potion uses. 1 use per 10 floors.</summary>
    public const int PotionCooldownFloors = 10;

    /// <summary>Sentinel meaning "never used one" - any real floor is >= 1, and
    /// this is far enough below zero that the first CanUse check clears the
    /// cooldown window regardless of which floor the run starts on.</summary>
    private const int NeverUsedFloor = int.MinValue / 2;

    private readonly List<ConsumableItem> potions = new List<ConsumableItem>();

    private int lastUsedFloor = NeverUsedFloor;

    public IReadOnlyList<ConsumableItem> Potions => potions;

    /// <summary>Fires whenever the held set changes OR a potion is used (the
    /// cooldown state changed), so one subscription keeps the HUD correct.</summary>
    public event Action OnChanged;

    public void Add(ConsumableItem potion)
    {
        if (potion == null) return;
        potions.Add(potion);
        OnChanged?.Invoke();
    }

    /// <summary>Floors still to go before another potion may be drunk, or 0 if ready.</summary>
    public int FloorsUntilReady(int currentFloor)
    {
        if (lastUsedFloor == NeverUsedFloor) return 0;
        return Mathf.Max(0, PotionCooldownFloors - (currentFloor - lastUsedFloor));
    }

    public bool IsOnCooldown(int currentFloor) => FloorsUntilReady(currentFloor) > 0;

    /// <summary>
    /// Drinks one potion. Returns false - consuming nothing - when it can't or
    /// shouldn't be drunk, so the caller never destroys a potion it didn't
    /// actually spend (CLAUDE.md §4, same contract as ItemPickup.PickUp and
    /// Wallet.TrySpendGold): not held, still on cooldown, drinker dead, or
    /// already at full health.
    /// </summary>
    public bool TryUse(ConsumableItem potion, Entity drinker, int currentFloor)
    {
        if (potion == null || drinker == null) return false;
        if (drinker.IsDead) return false;
        if (IsOnCooldown(currentFloor)) return false;
        // Mirrors HealthPotionPickup's own refusal to be spent at full HP -
        // otherwise the 10-floor cooldown would start for zero healing.
        if (drinker.CurrentHealth >= drinker.Stats.maxHp) return false;
        if (!potions.Remove(potion)) return false;

        drinker.Heal(potion.HealAmountFor(drinker));
        lastUsedFloor = currentFloor;
        OnChanged?.Invoke();
        return true;
    }
}
