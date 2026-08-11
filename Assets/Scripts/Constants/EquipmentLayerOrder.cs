using System.Collections.Generic;

/// <summary>
/// Sprite sorting order for each equipment layer relative to the base body.
/// Slots not listed here (MainHand/OffHand/Back) are direction-aware instead -
/// see DirectionAwareFront/Behind, picked at runtime based on facing.
/// </summary>
public static class EquipmentLayerOrder
{
    // These are absolute sortingOrder values on the default sorting layer, so they
    // have to be read against what else is already on it:
    //   floor tilemap = 0, health potion = 1, character body = 2, damage numbers = 100.
    // Every worn layer therefore has to sit ABOVE 2 to be visible at all, and the
    // "behind" variant has to sit between the floor and the body (not below 0, or
    // the tilemap covers it).
    public const int BodyOrder = 2;

    public const int DirectionAwareFront = 12;
    public const int DirectionAwareBehind = 1;

    // Stat-only slots (Belt/Ring1/Ring2/Trinket1/Trinket2) never render anything -
    // PickSprite returns null for them since they carry no LPC frames - so the
    // exact sorting order doesn't matter. They still need a Fixed entry or
    // Equipment.ApplyFrame throws a KeyNotFoundException the moment one is equipped.
    public const int StatOnlyOrder = 9;

    public static readonly IReadOnlyDictionary<EquipmentSlot, int> Fixed = new Dictionary<EquipmentSlot, int>
    {
        { EquipmentSlot.Feet, 3 },
        { EquipmentSlot.Legs, 4 },
        { EquipmentSlot.Chest, 5 },
        { EquipmentSlot.Hands, 6 },
        { EquipmentSlot.Shoulders, 7 },
        { EquipmentSlot.Neck, 8 },
        { EquipmentSlot.Head, 11 },
        { EquipmentSlot.Belt, StatOnlyOrder },
        { EquipmentSlot.Ring1, StatOnlyOrder },
        { EquipmentSlot.Ring2, StatOnlyOrder },
        { EquipmentSlot.Trinket1, StatOnlyOrder },
        { EquipmentSlot.Trinket2, StatOnlyOrder },
    };
}
