using System;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Unlimited-capacity bag of EquippableItems not currently worn. Items enter
/// by being picked up (EquipmentDropPickup) or by being displaced when
/// something else is equipped into their slot; they only leave by being
/// equipped again. There's no "drop" action - once an item is picked up it
/// stays with the player permanently, living either on a body slot or in
/// the bag, never destroyed.
/// </summary>
public class Inventory : MonoBehaviour
{
    public event Action<EquippableItem> OnItemAdded;
    public event Action<EquippableItem> OnItemRemoved;

    private readonly List<EquippableItem> items = new List<EquippableItem>();

    public IReadOnlyList<EquippableItem> Items => items;

    public void Add(EquippableItem item)
    {
        if (item == null) return;
        items.Add(item);
        OnItemAdded?.Invoke(item);
    }

    /// <summary>Removes one instance of item from the bag - used when equipping something out of it. Returns false if it wasn't there.</summary>
    public bool Remove(EquippableItem item)
    {
        bool removed = items.Remove(item);
        if (removed) OnItemRemoved?.Invoke(item);
        return removed;
    }

    /// <summary>Sorts by rarity (IMPLEMENTED.md -> "Equipment" rarity ladder), highest first; item level breaks ties.</summary>
    public void SortByRarity()
    {
        items.Sort((a, b) =>
        {
            int rarityCompare = b.rarity.CompareTo(a.rarity);
            return rarityCompare != 0 ? rarityCompare : b.itemLevel.CompareTo(a.itemLevel);
        });
    }

    /// <summary>Sorts by item level, highest first; rarity breaks ties.</summary>
    public void SortByItemLevel()
    {
        items.Sort((a, b) =>
        {
            int levelCompare = b.itemLevel.CompareTo(a.itemLevel);
            return levelCompare != 0 ? levelCompare : b.rarity.CompareTo(a.rarity);
        });
    }
}
