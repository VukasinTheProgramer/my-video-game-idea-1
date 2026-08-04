using UnityEngine;

/// <summary>
/// Rarity outline colors for item-slot UI (COMBAT_DESIGN.md §6): Common
/// green, Rare blue, Epic purple, Legendary yellow, Set cyan, Mythic red,
/// Runic red pulsing to black ("motion outline" - the only rarity that
/// animates, everything else is a static colored frame).
/// </summary>
public static class RarityVisuals
{
    public static Color OutlineColor(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common: return new Color(0.25f, 0.85f, 0.25f);
            case Rarity.Rare: return new Color(0.25f, 0.55f, 1f);
            case Rarity.Epic: return new Color(0.65f, 0.25f, 0.95f);
            case Rarity.Legendary: return new Color(1f, 0.85f, 0.1f);
            case Rarity.Set: return new Color(0.2f, 0.9f, 0.9f);
            case Rarity.Mythic: return new Color(0.9f, 0.15f, 0.15f);
            case Rarity.Runic: return new Color(0.9f, 0.15f, 0.15f); // pulses toward RunicSecondaryColor - see HasMotionOutline
            default: return Color.white;
        }
    }

    /// <summary>Only Runic animates; every other rarity is a flat static outline.</summary>
    public static bool HasMotionOutline(Rarity rarity) => rarity == Rarity.Runic;

    public static Color RunicSecondaryColor => Color.black;
}
