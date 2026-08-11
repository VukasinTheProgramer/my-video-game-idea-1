using UnityEngine;

/// <summary>
/// A usable (non-worn) item - currently only healing potions. Separate type
/// from EquippableItem on purpose: it has no slot, no bonusStats and never
/// goes through ItemGenerator's stat-budget roll, so folding it into
/// EquippableItem would mean a slot enum entry that can't be worn plus a
/// generator branch that skips itself (CLAUDE.md rule 4 - data designers
/// tune = ScriptableObject, one job per class).
///
/// Heal is a PERCENT of the drinker's max HP, not a flat amount, so a potion
/// stays as useful on floor 40 as on floor 1 without a scaling table.
/// </summary>
[CreateAssetMenu(menuName = "Items/Consumable Item")]
public class ConsumableItem : ScriptableObject
{
    public string displayName = "Potion";

    [Tooltip("Fraction of the drinker's MAX HP restored - 0.1 = 10%. Percent, not flat, so potions don't fall off as maxHp grows.")]
    [Range(0f, 1f)]
    public float healPercent = 0.1f;

    [Tooltip("Shown in the quick-use HUD and the potion chooser. 16x16, same convention as every other inventory icon (.claude/ART_STYLE.md).")]
    public Sprite icon;

    [Tooltip("Sort order for the chooser list and for picking which icon the collapsed HUD button shows - low tier first.")]
    public int tier = 1;

    /// <summary>Actual HP this restores for a given entity. Always at least 1 so a
    /// low-percent potion on a low-maxHp character still does something.</summary>
    public int HealAmountFor(Entity entity)
    {
        if (entity == null) return 0;
        return Mathf.Max(1, Mathf.RoundToInt(entity.Stats.maxHp * healPercent));
    }
}
