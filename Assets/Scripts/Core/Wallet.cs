using System;
using UnityEngine;

/// <summary>
/// Player gold. Mirrors PlayerProgression/Inventory's pattern - a component
/// on the player, event-driven so UI never needs to poll (ROADMAP.md ->
/// "Currency: gold & gems").
///
/// Gold only for now - gems are a separate currency the design deliberately
/// keeps non-overlapping (gold buys more attempts, gems buy better odds),
/// and have no source or sink yet (both depend on item generation, last in
/// the currency build order). Add Gems/OnGemsChanged/TrySpendGems here,
/// mirroring Gold exactly, when that work starts.
/// </summary>
public class Wallet : MonoBehaviour
{
    public int Gold { get; private set; }

    public event Action<int> OnGoldChanged;

    public void AddGold(int amount)
    {
        if (amount <= 0) return;
        Gold += amount;
        OnGoldChanged?.Invoke(Gold);
    }

    /// <summary>Spends gold only if there's enough - the caller commits the
    /// purchase only on true (CLAUDE.md §4, same reasoning as ItemPickup.PickUp).</summary>
    public bool TrySpendGold(int amount)
    {
        if (amount <= 0 || Gold < amount) return false;
        Gold -= amount;
        OnGoldChanged?.Invoke(Gold);
        return true;
    }
}
