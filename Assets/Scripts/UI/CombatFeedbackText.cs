using UnityEngine;

/// <summary>
/// Text/color/size mapping for a CombatResult - the single source both
/// DamageNumberSpawner (world-space, dungeon fallback) and BattleScreenUI
/// (UI-space, battle screen) read from, so MISS/PARRY/CRIT display rules
/// can't drift between the two combat entry points (CLAUDE.md rule 5).
/// </summary>
public static class CombatFeedbackText
{
    public static readonly Color MissColor = new Color(0.75f, 0.75f, 0.75f);
    public static readonly Color ParryColor = new Color(0.35f, 0.55f, 1f);
    public static readonly Color CritColor = Color.yellow;
    public static readonly Color NormalColor = Color.white;

    public static (string Text, Color Color, bool Big) For(CombatResult result)
    {
        if (result.WasDodged) return ("MISS", MissColor, false);
        if (result.WasParried) return ("PARRY", ParryColor, false);
        return (result.Damage.ToString(), result.WasCrit ? CritColor : NormalColor, result.WasCrit);
    }
}
