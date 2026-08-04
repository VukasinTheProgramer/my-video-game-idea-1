/// <summary>
/// Weapon families that drive both attack pattern (ROADMAP.md ->
/// "Weapon-driven attack patterns", not implemented yet) and which stat
/// scales damage / which defense stat applies (IMPLEMENTED.md ->
/// "Stat system", CombatResolver). None = unarmed, treated as the melee/
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
