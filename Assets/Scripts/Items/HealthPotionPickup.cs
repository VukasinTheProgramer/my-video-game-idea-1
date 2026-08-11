using UnityEngine;

/// <summary>
/// Health pickup: sits on a walkable cell and is collected by moving onto its
/// cell (see PlayerController.TryAct). Registration into DungeonGrid's item
/// slot is handled by the ItemPickup base.
///
/// Collecting BANKS the potion into the picker's PotionBag rather than
/// drinking it on the spot - drinking is a deliberate act on the quick-use
/// HUD, because potions now share a 10-floor cooldown and auto-drinking one
/// while at 90% HP would burn that window for almost nothing.
/// </summary>
public class HealthPotionPickup : ItemPickup
{
    [Tooltip("Which potion this grants. Required - without it there is nothing to bank, and the pickup stays on the floor rather than vanishing.")]
    [SerializeField] private ConsumableItem potion;

    public ConsumableItem Potion => potion;

    /// <summary>Takes the on-floor sprite from the ConsumableItem itself, so the
    /// thing lying in the dungeon can never disagree with the icon shown in the
    /// quick-use HUD - one source of truth instead of two hand-synced fields.</summary>
    private void Awake()
    {
        if (potion == null || potion.icon == null) return;
        if (TryGetComponent(out SpriteRenderer renderer)) renderer.sprite = potion.icon;
    }

    public override bool PickUp(Entity picker)
    {
        if (picker == null) return false;

        if (potion == null)
        {
            // Loud, because a pickup with no potion assigned is an authoring
            // mistake that would otherwise look like a pickup that silently
            // does nothing (CLAUDE.md §4 - fail loudly at the boundary).
            Debug.LogError($"{name}: no ConsumableItem assigned; nothing to grant.", this);
            return false;
        }

        // Optional component, so degrade instead of throwing - and return false
        // so the caller leaves the potion on the floor rather than destroying
        // something the player never received (IMPLEMENTED.md -> "Bag / Inventory").
        if (!picker.TryGetComponent(out PotionBag bag))
        {
            Debug.LogWarning($"{name}: {picker.name} has no PotionBag; leaving this potion on the floor.", this);
            return false;
        }

        bag.Add(potion);
        DamageNumberSpawner.Instance.ShowPickup(picker, $"+1 {potion.displayName}");
        return true;
    }
}
