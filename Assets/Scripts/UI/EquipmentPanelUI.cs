using System.Collections.Generic;
using System.Linq; // Enumerable.Contains on Inventory.Items (IReadOnlyList) in EquipFromBag
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Shows the player's 15 equipment slots and unlimited bag as ItemSlotUI
/// squares (icon + rarity-colored outline). Two ways to equip/unequip:
/// right-click a slot for a tooltip with an Equip/Unequip button, or drag an
/// item from the bag onto its matching equip slot (or drag an equipped item
/// onto the bag to unequip it) - both end up calling EquipFromBag/UnequipSlot/
/// UnequipItem, so there's exactly one equip/unequip code path either way.
/// Dropping an item onto the wrong equip slot type is rejected (ItemSlotUI.
/// SetDropTarget), not silently equipped somewhere else.
///
/// slotUIs must be sized 15 and ordered to match SlotOrder (Head, Neck,
/// MainHand, OffHand, Shoulders, Chest, Hands, Back, Legs, Feet, Belt,
/// Ring1, Ring2, Trinket1, Trinket2 - the last 5 are stat-only, no LPC art,
/// so their slot icons never show a character-layer preview, just whatever
/// item icon is bound). Bag slots are spawned/pooled at runtime under
/// bagSlotContainer - no bag prefab needed, just an empty RectTransform
/// with a layout group on it.
/// </summary>
public class EquipmentPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private ItemSlotUI[] slotUIs;
    [SerializeField] private Text[] slotLabels;

    [Header("Stats readout - single multi-line block, IMPLEMENTED.md -> \"Stat system\"")]
    [SerializeField] private Text statsText;

    [Header("Leveling - IMPLEMENTED.md -> \"Leveling & stat points\". + buttons only spend points on Attack/Health/Agility (Magic/DEF/MDEF stay gear-only); no respec.")]
    [SerializeField] private Text progressionText;
    [SerializeField] private Text availablePointsText;
    [SerializeField] private Button attackPointButton;
    [SerializeField] private Button healthPointButton;
    [SerializeField] private Button agilityPointButton;

    [Header("Tooltip - right-click a slot to show item stats + Equip/Unequip button")]
    [SerializeField] private ItemTooltipUI tooltip;

    [Header("Bag - ItemSlotUI squares are spawned/pooled here at runtime")]
    [SerializeField] private RectTransform bagSlotContainer;
    [SerializeField] private Vector2 bagSlotSize = new Vector2(48f, 48f);

    private readonly List<ItemSlotUI> bagSlotPool = new List<ItemSlotUI>();

    private PlayerController subscribedPlayer;
    private Inventory subscribedInventory;
    private PlayerProgression subscribedProgression;

    private static readonly EquipmentSlot[] SlotOrder =
    {
        EquipmentSlot.Head, EquipmentSlot.Neck, EquipmentSlot.MainHand, EquipmentSlot.OffHand,
        EquipmentSlot.Shoulders, EquipmentSlot.Chest, EquipmentSlot.Hands, EquipmentSlot.Back,
        EquipmentSlot.Legs, EquipmentSlot.Feet,
        EquipmentSlot.Belt, EquipmentSlot.Ring1, EquipmentSlot.Ring2,
        EquipmentSlot.Trinket1, EquipmentSlot.Trinket2
    };

    private void Awake()
    {
        if (attackPointButton != null) attackPointButton.onClick.AddListener(() => SpendPoint(PlayerProgression.AllocatableStat.Attack));
        if (healthPointButton != null) healthPointButton.onClick.AddListener(() => SpendPoint(PlayerProgression.AllocatableStat.Health));
        if (agilityPointButton != null) agilityPointButton.onClick.AddListener(() => SpendPoint(PlayerProgression.AllocatableStat.Agility));

        // Without this, drag-to-unequip only works if the bag already has an active
        // item slot to land on - an empty (or nearly-full-elsewhere) bag has nowhere
        // to drop onto at all, since bagSlotContainer itself starts with no
        // raycastable graphic (just a layout group).
        BagDropZone.Attach(bagSlotContainer, UnequipItem);
    }

    private void Start()
    {
        panelRoot.SetActive(false);
    }

    private void SpendPoint(PlayerProgression.AllocatableStat stat)
    {
        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        PlayerProgression progression = player.GetComponent<PlayerProgression>();
        if (progression != null && progression.TrySpendPoint(stat)) Refresh();
    }

    public void TogglePanel()
    {
        if (panelRoot == null) return;

        bool showing = !panelRoot.activeSelf;
        panelRoot.SetActive(showing);

        if (showing)
        {
            Subscribe();
            Refresh();
        }
        else
        {
            Unsubscribe();
            tooltip?.Hide();
        }
    }

    // Movement input stays live while the panel is open, so the player can walk
    // onto a drop or a potion with it showing. Without these the bag and the HP
    // line silently go stale until the panel is closed and reopened.
    private void Subscribe()
    {
        Unsubscribe(); // never stack handlers across repeated opens

        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        subscribedPlayer = player;
        subscribedPlayer.OnHealthChanged += HandleHealthChanged;

        subscribedInventory = player.GetComponent<Inventory>();
        if (subscribedInventory != null)
        {
            subscribedInventory.OnItemAdded += HandleBagChanged;
            subscribedInventory.OnItemRemoved += HandleBagChanged;
        }

        subscribedProgression = player.GetComponent<PlayerProgression>();
        if (subscribedProgression != null)
        {
            subscribedProgression.OnXPChanged += HandleProgressionXPChanged;
            subscribedProgression.OnStatPointsChanged += HandleProgressionPointsChanged;
        }
    }

    private void Unsubscribe()
    {
        if (subscribedPlayer != null) subscribedPlayer.OnHealthChanged -= HandleHealthChanged;
        if (subscribedInventory != null)
        {
            subscribedInventory.OnItemAdded -= HandleBagChanged;
            subscribedInventory.OnItemRemoved -= HandleBagChanged;
        }
        if (subscribedProgression != null)
        {
            subscribedProgression.OnXPChanged -= HandleProgressionXPChanged;
            subscribedProgression.OnStatPointsChanged -= HandleProgressionPointsChanged;
        }
        subscribedPlayer = null;
        subscribedInventory = null;
        subscribedProgression = null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void HandleHealthChanged(Entity entity, int current, int max) => Refresh();
    private void HandleBagChanged(EquippableItem item) => Refresh();
    private void HandleProgressionXPChanged(int currentXP, int xpToNextLevel) => Refresh();
    private void HandleProgressionPointsChanged(int availablePoints) => Refresh();

    /// <summary>
    /// Equips a specific bag item into its own slot. Whatever was previously in
    /// that slot is automatically sent back to the bag by Equipment.Equip -
    /// nothing is ever lost.
    ///
    /// Takes the item rather than a bag index on purpose: the tooltip's Equip
    /// button fires long after the slot was clicked, and sorting the bag in
    /// between would leave an index pointing at a different item.
    /// </summary>
    public void EquipFromBag(EquippableItem item)
    {
        if (item == null) return;

        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        Inventory inventory = player.GetComponent<Inventory>();
        Equipment equipment = player.GetComponent<Equipment>();
        if (inventory == null || equipment == null) return;
        if (!inventory.Items.Contains(item)) return; // already equipped or gone

        equipment.Equip(item.slot, item);
        Refresh();
    }

    /// <summary>Sells a bag item for gold and removes it from the bag permanently -
    /// this is the one deliberate exception to "never destroyed"
    /// (ROADMAP.md -> "Currency: gold & gems", sell-from-bag sink). Only reachable
    /// from the bag's tooltip, never the equip slots', so worn gear can't be sold
    /// by accident - it has to be unequipped first. Removes the item from the
    /// player's Inventory only; the EquippableItem asset itself is a shared
    /// template other drops still reference, never destroyed.</summary>
    public void SellFromBag(EquippableItem item)
    {
        if (item == null) return;

        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        Inventory inventory = player.GetComponent<Inventory>();
        Wallet wallet = player.GetComponent<Wallet>();
        if (inventory == null || wallet == null) return;
        if (!inventory.Items.Contains(item)) return; // already sold/equipped/gone

        int value = ItemPricing.SellValue(item);
        inventory.Remove(item);
        wallet.AddGold(value);
        Refresh();
    }

    /// <summary>Sends whatever's in this slot back to the bag.</summary>
    public void UnequipSlot(int slotOrderIndex)
    {
        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        Equipment equipment = player.GetComponent<Equipment>();
        if (equipment == null) return;
        if (slotOrderIndex < 0 || slotOrderIndex >= SlotOrder.Length) return;

        equipment.Unequip(SlotOrder[slotOrderIndex]);
        Refresh();
    }

    /// <summary>Unequips by item rather than slot index - used by drag-to-unequip
    /// (dropping an equipped item onto the bag), where the drop target only knows
    /// the dragged item, not which SlotOrder index it came from. No-op if the item
    /// isn't actually equipped (e.g. dropped a bag item onto the bag).</summary>
    private void UnequipItem(EquippableItem item)
    {
        if (item == null) return;

        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        Equipment equipment = player.GetComponent<Equipment>();
        if (equipment == null) return;
        if (equipment.GetEquipped(item.slot) != item) return; // not actually the equipped item in that slot

        equipment.Unequip(item.slot);
        Refresh();
    }

    /// <summary>Right-click on an equipped slot: shows the item's stats with an Unequip button.</summary>
    private void ShowUnequipTooltip(int slotOrderIndex, EquippableItem item)
    {
        if (tooltip == null) return;
        tooltip.Show(item, "Unequip", () => UnequipSlot(slotOrderIndex));
    }

    /// <summary>Right-click on a bag slot: shows the item's stats with Equip and Sell buttons.</summary>
    private void ShowEquipTooltip(EquippableItem item)
    {
        if (tooltip == null) return;
        int sellValue = ItemPricing.SellValue(item);
        tooltip.Show(item, "Equip", () => EquipFromBag(item), $"Sell ({sellValue}g)", () => SellFromBag(item));
    }

    /// <summary>Sorts the bag by rarity, highest first. Wire a "Sort by Rarity" Button to this.</summary>
    public void SortBagByRarity()
    {
        GetPlayerInventory()?.SortByRarity();
        Refresh();
    }

    /// <summary>Sorts the bag by item level, highest first. Wire a "Sort by Level" Button to this.</summary>
    public void SortBagByItemLevel()
    {
        GetPlayerInventory()?.SortByItemLevel();
        Refresh();
    }

    private Inventory GetPlayerInventory()
    {
        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        return player != null ? player.GetComponent<Inventory>() : null;
    }

    private void Refresh()
    {
        // Looked up fresh on open, not cached at Start() - the player is spawned
        // by GameManager.Start(), whose ordering relative to this Start() isn't
        // guaranteed within the same frame.
        PlayerController player = GameManager.Instance != null ? GameManager.Instance.Player : null;
        if (player == null) return;

        Equipment equipment = player.GetComponent<Equipment>();
        if (equipment == null) return;

        // Anything the tooltip was describing may have just moved slots.
        tooltip?.Hide();

        RefreshStats(player);
        RefreshProgression(player);

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquippableItem item = equipment.GetEquipped(SlotOrder[i]);
            int slotIndex = i; // capture for the closure below

            if (slotUIs != null && i < slotUIs.Length && slotUIs[i] != null)
            {
                slotUIs[i].Bind(item, item != null ? () => ShowUnequipTooltip(slotIndex, item) : (System.Action)null);
                slotUIs[i].SetDropTarget(SlotOrder[i], EquipFromBag);
            }

            if (slotLabels != null && i < slotLabels.Length && slotLabels[i] != null)
            {
                slotLabels[i].text = item != null ? $"{SlotOrder[i]}: {item.displayName}" : $"{SlotOrder[i]}: (empty)";
            }
        }

        RefreshBag(player);
    }

    /// <summary>
    /// Shows current HP plus every core/derived stat, using CombatResolver's own
    /// formulas (not a re-derived copy) so this always matches what combat
    /// actually rolls against - see CombatResolver's public Effective* methods.
    /// </summary>
    private void RefreshStats(PlayerController player)
    {
        if (statsText == null) return;

        Stats stats = player.Stats;
        bool canParry = CombatResolver.CanParry(player.EquippedWeaponType);

        statsText.text =
            $"HP: {player.CurrentHealth} / {stats.maxHp}\n" +
            "\n" +
            $"Attack: {stats.attack}\n" +
            $"Agility: {stats.agility}\n" +
            $"Magic: {stats.magic}\n" +
            $"Defense: {stats.defense}\n" +
            $"Magic Defense: {stats.magicDefense}\n" +
            "\n" +
            $"Crit Chance: {CombatResolver.EffectiveCritChance(stats):0.#}%\n" +
            $"Crit Damage: {CombatResolver.EffectiveCritDamageMultiplier(stats.critDamageBonus) * 100f:0}%\n" +
            $"Dodge: {CombatResolver.EffectiveDodgeChance(stats):0.#}%\n" +
            $"Parry: {(canParry ? $"{CombatResolver.EffectiveParryChance(stats, true):0.#}%" : "— (no melee weapon)")}\n" +
            $"Life Steal: {CombatResolver.EffectiveLifeStealPercent(stats):0.#}%";
    }

    /// <summary>Level/XP readout plus the Attack/Health/Agility point-spend buttons
    /// (IMPLEMENTED.md -> "Leveling & stat points") - buttons only enable while
    /// points are available; spending is permanent, there's no respec.</summary>
    private void RefreshProgression(PlayerController player)
    {
        PlayerProgression progression = player.GetComponent<PlayerProgression>();
        if (progression == null) return;

        if (progressionText != null)
        {
            progressionText.text = $"Level {progression.Level}   XP {progression.CurrentXP} / {progression.XPToNextLevel}";
        }

        if (availablePointsText != null)
        {
            availablePointsText.text = $"Points available: {progression.AvailableStatPoints}";
        }

        bool hasPoints = progression.AvailableStatPoints > 0;
        if (attackPointButton != null) attackPointButton.interactable = hasPoints;
        if (healthPointButton != null) healthPointButton.interactable = hasPoints;
        if (agilityPointButton != null) agilityPointButton.interactable = hasPoints;
    }

    private void RefreshBag(PlayerController player)
    {
        if (bagSlotContainer == null) return;

        Inventory inventory = player.GetComponent<Inventory>();
        IReadOnlyList<EquippableItem> items = inventory != null ? inventory.Items : System.Array.Empty<EquippableItem>();

        EnsureBagPoolSize(items.Count);

        for (int i = 0; i < bagSlotPool.Count; i++)
        {
            if (i < items.Count)
            {
                EquippableItem bagItem = items[i]; // capture the item, never the index
                bagSlotPool[i].gameObject.SetActive(true);
                bagSlotPool[i].Bind(bagItem, () => ShowEquipTooltip(bagItem));
                // null acceptSlot = a bag slot takes any dragged item - dropping an
                // equipped item here means "unequip".
                bagSlotPool[i].SetDropTarget(null, UnequipItem);
            }
            else
            {
                bagSlotPool[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Grows the pooled bag-slot list to at least count, reusing existing slots on later refreshes instead of recreating them.</summary>
    private void EnsureBagPoolSize(int count)
    {
        while (bagSlotPool.Count < count)
        {
            var go = new GameObject($"BagSlot_{bagSlotPool.Count}", typeof(RectTransform));
            go.transform.SetParent(bagSlotContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = bagSlotSize;

            bagSlotPool.Add(go.AddComponent<ItemSlotUI>());
        }
    }
}
