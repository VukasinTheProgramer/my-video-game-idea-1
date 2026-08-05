using UnityEngine;

/// <summary>
/// A piece of equipment: sliced LPC frames for one slot. Weapon-capable slots
/// (MainHand/OffHand) additionally carry slash + slash-behind frames; other
/// slots leave those empty since armor/helmets don't animate independently
/// of the walk cycle in this scope.
/// </summary>
[CreateAssetMenu(menuName = "Equipment/Equippable Item")]
public class EquippableItem : ScriptableObject
{
    public EquipmentSlot slot;
    public string displayName;

    [Header("Stats (IMPLEMENTED.md -> \"Stat system\", \"Equipment\")")]
    public Stats bonusStats = new Stats();
    [Tooltip("MainHand only - feeds CombatResolver's rawDamage. Ignored for other slots.")]
    public int weaponDamage;
    public Rarity rarity = Rarity.Uncommon; // matches the pre-2026-08-05 default (was named Common then)
    [Tooltip("Hand-authored for now (v1 scope) - used for bag sorting and display. The full floor-scaling system (ROADMAP.md -> \"Item generation: floor & rarity stat scaling\") would set this per-drop instead of per-template.")]
    public int itemLevel = 1;
    [Tooltip("MainHand only - drives CombatResolver's scaling/defense stat pick (IMPLEMENTED.md -> \"Stat system\") and, later, attack pattern (ROADMAP.md -> \"Weapon-driven attack patterns\"). Leave None for non-weapon slots.")]
    public WeaponType weaponType = WeaponType.None;

    [Header("Walk (9 frames each)")]
    public Sprite[] walkDown;
    public Sprite[] walkLeft;
    public Sprite[] walkRight;
    public Sprite[] walkUp;

    [Header("Walk-behind — used facing up instead of walkUp, if the item has this asset")]
    public Sprite[] walkBehind;

    [Header("Hurt (6 frames, south-only)")]
    public Sprite[] hurt;

    [Header("Hurt-behind — used facing up instead of hurt, if the item has this asset")]
    public Sprite[] hurtBehind;

    [Header("Slash — weapon slots only (6 frames each)")]
    public Sprite[] slashDown;
    public Sprite[] slashLeft;
    public Sprite[] slashRight;
    public Sprite[] slashUp;

    [Header("Slash behind body — weapon slots only, used facing up")]
    public Sprite[] slashBehind;

    public bool HasSlash => slashDown != null && slashDown.Length > 0;
}
