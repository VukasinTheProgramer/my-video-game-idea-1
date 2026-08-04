/// <summary>
/// Weapon families that drive both attack pattern (COMBAT_DESIGN.md §4,
/// not implemented yet) and which stat scales damage / which defense stat
/// applies (§1, CombatResolver). None = unarmed, treated as the melee/
/// physical fallback everywhere a weapon type matters.
/// </summary>
public enum WeaponType
{
    None,
    Sword,
    Axe,
    Mace,
    Hammer,
    Spear,
    Dagger,
    Bow,
    Staff
}
