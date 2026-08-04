using UnityEngine;

/// <summary>
/// Health pickup: sits on a walkable cell, heals whoever moves onto its cell
/// (see PlayerController.TryAct). Registration into DungeonGrid's item slot
/// is handled by the ItemPickup base.
/// </summary>
public class HealthPotionPickup : ItemPickup
{
    [SerializeField] private int healAmount = 5;

    public int HealAmount => healAmount;

    public override bool PickUp(Entity picker)
    {
        if (picker == null) return false;

        // Don't burn the potion (and don't show a misleading "+N") when it would
        // heal nothing - leave it on the floor for when it's actually needed.
        if (picker.CurrentHealth >= picker.MaxHealth) return false;

        picker.Heal(healAmount);
        return true;
    }
}
