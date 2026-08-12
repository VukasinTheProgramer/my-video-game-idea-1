using UnityEngine;

/// <summary>
/// Rarity outline colors for item-slot UI (IMPLEMENTED.md -> "Equipment"): Common
/// gray, Uncommon green, Rare blue, Epic purple, Legendary gold, Set cyan,
/// Mythic red, Runic red pulsing to black ("motion outline" - the only
/// rarity that animates, everything else is a static colored frame).
///
/// Colors pulled from the "Adventurer Loadout" pixel-art mockup's dark warm
/// palette (2026-08-12 equipment panel reskin) - muted/desaturated versions
/// read cleanly against the panel's dark backgrounds instead of clashing
/// with it the way the old fully-saturated colors did.
/// </summary>
public static class RarityVisuals
{
    public static Color OutlineColor(Rarity rarity)
    {
        switch (rarity)
        {
            case Rarity.Common: return HexColor("#7C7A75");
            case Rarity.Uncommon: return HexColor("#47A34E");
            case Rarity.Rare: return HexColor("#2F8ADC");
            case Rarity.Epic: return HexColor("#9A5DD5");
            case Rarity.Legendary: return HexColor("#DCA331");
            case Rarity.Set: return HexColor("#00B0B1");
            case Rarity.Mythic: return HexColor("#D73337");
            case Rarity.Runic: return HexColor("#CC272E"); // pulses toward RunicSecondaryColor - see HasMotionOutline
            default: return Color.white;
        }
    }

    /// <summary>Only Runic animates; every other rarity is a flat static outline.</summary>
    public static bool HasMotionOutline(Rarity rarity) => rarity == Rarity.Runic;

    public static Color RunicSecondaryColor => Color.black;

    private static Color HexColor(string hex)
    {
        ColorUtility.TryParseHtmlString(hex, out Color c);
        return c;
    }
}
