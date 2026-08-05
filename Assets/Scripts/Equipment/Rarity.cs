/// <summary>
/// Item rarity tiers (IMPLEMENTED.md -> "Equipment"). Mythic and Runic sit
/// above Legendary in raw power; Set stays at Legendary parity and wins on
/// multi-piece set bonuses instead of bigger numbers. Common is a new bottom
/// tier below Uncommon (added 2026-08-05) - Uncommon is the original "Common"
/// renamed, not a new tier itself.
///
/// Explicit int values on purpose (CLAUDE.md §0): Unity serializes an enum
/// field as its raw int, not its name, so a plain reorder that put Common
/// first would have silently reassigned every existing `rarity: 0` asset
/// (the 4 original Common items) from Uncommon to the new Common tier with
/// no error and no warning. Uncommon keeps int 0 and everything above it
/// keeps its original value; Common takes -1, a value nothing was ever
/// serialized with, so no existing asset changes meaning.
///
/// v1 scope note: each EquippableItem template has one fixed rarity baked
/// in (hand-authored), not a procedurally rolled rarity per drop - the
/// floor/rarity stat-multiplier generation system described in ROADMAP.md
/// -> "Item generation: floor & rarity stat scaling"
/// (GenerateItem(template, floor, rarity)) is a follow-up layer once base
/// items are working, not implemented yet.
/// </summary>
public enum Rarity
{
    Common = -1,
    Uncommon = 0,
    Rare = 1,
    Epic = 2,
    Legendary = 3,
    Set = 4,
    Mythic = 5,
    Runic = 6
}
