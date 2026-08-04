/// <summary>
/// Item rarity tiers (COMBAT_DESIGN.md §2c). Mythic and Runic sit above
/// Legendary in raw power; Set stays at Legendary parity and wins on
/// multi-piece set bonuses instead of bigger numbers.
///
/// v1 scope note: each EquippableItem template has one fixed rarity baked
/// in (hand-authored), not a procedurally rolled rarity per drop - the
/// floor/rarity stat-multiplier generation system described in §2c
/// (GenerateItem(template, floor, rarity)) is a follow-up layer once base
/// items are working, not implemented yet.
/// </summary>
public enum Rarity
{
    Common,
    Rare,
    Epic,
    Legendary,
    Set,
    Mythic,
    Runic
}
