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
    [SerializeField] private Vector2 bagSlotSize = new Vector2(64f, 64f);
    [SerializeField] private Text goldText;

    private const string GoldIconResourcePath = "Icons/Gold";
    private const float GoldIconSize = 24f;
    private const float GoldIconGap = 4f;

    private readonly List<ItemSlotUI> bagSlotPool = new List<ItemSlotUI>();

    // Bound once via GameManager.OnPlayerSpawned (CLAUDE.md -> "UI binds to
    // the player once, via an event") - the player is created once by
    // GameManager and only ever moved between floors, so this identity never
    // changes and never needs re-resolving per call.
    private PlayerController boundPlayer;
    private Equipment boundEquipment;
    private Inventory boundInventory;
    private PlayerProgression boundProgression;
    private Wallet boundWallet;
    private PotionBag boundPotionBag;
    private PotionBag subscribedPotionBag;

    private PlayerController subscribedPlayer;
    private Inventory subscribedInventory;
    private PlayerProgression subscribedProgression;
    private Wallet subscribedWallet;

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

        if (goldText != null) HudIcon.AddBeside(goldText.GetComponent<RectTransform>(), GoldIconResourcePath, GoldIconSize, GoldIconGap);
    }

    /// <summary>B ("Bag") and C ("Character") both open this same combined panel -
    /// there's only one view today (equip slots + bag + stats together, per the
    /// class doc comment), not separate bag-only/character-only screens, so both
    /// keys are wired to the identical TogglePanel() call rather than inventing a
    /// UI split nothing else asked for.</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.B) || Input.GetKeyDown(KeyCode.C))
        {
            TogglePanel();
        }
    }

    private void Start()
    {
        panelRoot.SetActive(false);

        // Bind through GameManager (its Awake always precedes any Start) rather
        // than FindObjectOfType here - the player may not exist yet at this point.
        if (GameManager.Instance == null) return;

        if (GameManager.Instance.Player != null) Bind(GameManager.Instance.Player);
        else GameManager.Instance.OnPlayerSpawned += Bind;
    }

    private void Bind(PlayerController player)
    {
        boundPlayer = player;
        boundEquipment = player.GetComponent<Equipment>();
        boundInventory = player.GetComponent<Inventory>();
        boundProgression = player.GetComponent<PlayerProgression>();
        boundWallet = player.GetComponent<Wallet>();
        boundPotionBag = player.GetComponent<PotionBag>();
    }

    private void SpendPoint(PlayerProgression.AllocatableStat stat)
    {
        if (boundProgression != null && boundProgression.TrySpendPoint(stat)) Refresh();
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

        if (boundPlayer == null) return;

        subscribedPlayer = boundPlayer;
        subscribedPlayer.OnHealthChanged += HandleHealthChanged;

        subscribedInventory = boundInventory;
        if (subscribedInventory != null)
        {
            subscribedInventory.OnItemAdded += HandleBagChanged;
            subscribedInventory.OnItemRemoved += HandleBagChanged;
        }

        subscribedProgression = boundProgression;
        if (subscribedProgression != null)
        {
            subscribedProgression.OnXPChanged += HandleProgressionXPChanged;
            subscribedProgression.OnStatPointsChanged += HandleProgressionPointsChanged;
        }

        subscribedPotionBag = boundPotionBag;
        if (subscribedPotionBag != null) subscribedPotionBag.OnChanged += Refresh;

        subscribedWallet = boundWallet;
        if (subscribedWallet != null) subscribedWallet.OnGoldChanged += HandleGoldChanged;
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
        if (subscribedPotionBag != null) subscribedPotionBag.OnChanged -= Refresh;
        if (subscribedWallet != null) subscribedWallet.OnGoldChanged -= HandleGoldChanged;
        subscribedPlayer = null;
        subscribedInventory = null;
        subscribedProgression = null;
        subscribedWallet = null;
    }

    private void OnDestroy()
    {
        Unsubscribe();
    }

    private void HandleHealthChanged(Entity entity, int current, int max) => Refresh();
    private void HandleBagChanged(EquippableItem item) => Refresh();
    private void HandleProgressionXPChanged(int currentXP, int xpToNextLevel) => Refresh();
    private void HandleProgressionPointsChanged(int availablePoints) => Refresh();
    private void HandleGoldChanged(int gold) { if (goldText != null) goldText.text = gold.ToString(); }

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
        if (item == null || boundInventory == null || boundEquipment == null) return;
        if (!boundInventory.Items.Contains(item)) return; // already equipped or gone

        boundEquipment.Equip(item.slot, item);
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
        if (item == null || boundInventory == null || boundWallet == null) return;
        if (!boundInventory.Items.Contains(item)) return; // already sold/equipped/gone

        int value = ItemPricing.SellValue(item);
        boundInventory.Remove(item);
        boundWallet.AddGold(value);
        Refresh();
    }

    /// <summary>Sends whatever's in this slot back to the bag.</summary>
    public void UnequipSlot(int slotOrderIndex)
    {
        if (boundEquipment == null) return;
        if (slotOrderIndex < 0 || slotOrderIndex >= SlotOrder.Length) return;

        boundEquipment.Unequip(SlotOrder[slotOrderIndex]);
        Refresh();
    }

    /// <summary>Unequips by item rather than slot index - used by drag-to-unequip
    /// (dropping an equipped item onto the bag), where the drop target only knows
    /// the dragged item, not which SlotOrder index it came from. No-op if the item
    /// isn't actually equipped (e.g. dropped a bag item onto the bag).</summary>
    private void UnequipItem(EquippableItem item)
    {
        if (item == null || boundEquipment == null) return;
        if (boundEquipment.GetEquipped(item.slot) != item) return; // not actually the equipped item in that slot

        boundEquipment.Unequip(item.slot);
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
        boundInventory?.SortByRarity();
        Refresh();
    }

    /// <summary>Sorts the bag by item level, highest first. Wire a "Sort by Level" Button to this.</summary>
    public void SortBagByItemLevel()
    {
        boundInventory?.SortByItemLevel();
        Refresh();
    }

    private void Refresh()
    {
        if (boundPlayer == null || boundEquipment == null) return;

        // Anything the tooltip was describing may have just moved slots.
        tooltip?.Hide();

        RefreshStats();
        RefreshProgression();
        if (goldText != null && boundWallet != null) goldText.text = boundWallet.Gold.ToString();

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquippableItem item = boundEquipment.GetEquipped(SlotOrder[i]);
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

        RefreshBag();
    }

    /// <summary>
    /// Shows current HP plus every core/derived stat, using CombatResolver's own
    /// formulas (not a re-derived copy) so this always matches what combat
    /// actually rolls against - see CombatResolver's public Effective* methods.
    /// </summary>
    private void RefreshStats()
    {
        if (statsText == null || boundPlayer == null) return;

        Stats stats = boundPlayer.Stats;
        bool canParry = CombatResolver.CanParry(boundPlayer.EquippedWeaponType);

        statsText.text =
            $"HP: {boundPlayer.CurrentHealth} / {stats.maxHp}\n" +
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
    private void RefreshProgression()
    {
        if (boundProgression == null) return;

        if (progressionText != null)
        {
            progressionText.text = $"Level {boundProgression.Level}   XP {boundProgression.CurrentXP} / {boundProgression.XPToNextLevel}";
        }

        if (availablePointsText != null)
        {
            availablePointsText.text = $"Points available: {boundProgression.AvailableStatPoints}";
        }

        bool hasPoints = boundProgression.AvailableStatPoints > 0;
        if (attackPointButton != null) attackPointButton.interactable = hasPoints;
        if (healthPointButton != null) healthPointButton.interactable = hasPoints;
        if (agilityPointButton != null) agilityPointButton.interactable = hasPoints;
    }

    private void RefreshBag()
    {
        if (bagSlotContainer == null) return;

        IReadOnlyList<EquippableItem> items = boundInventory != null ? boundInventory.Items : System.Array.Empty<EquippableItem>();

        // Potions share the bag grid but are stacked by kind, so 5 small potions are
        // one square showing "5" rather than 5 identical squares crowding out gear.
        List<KeyValuePair<ConsumableItem, int>> potionStacks = GroupPotions();

        EnsureBagPoolSize(items.Count + potionStacks.Count);

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
            else if (i < items.Count + potionStacks.Count)
            {
                KeyValuePair<ConsumableItem, int> stack = potionStacks[i - items.Count];
                ConsumableItem potion = stack.Key; // capture the object, never the index
                int count = stack.Value;
                bagSlotPool[i].gameObject.SetActive(true);
                bagSlotPool[i].BindPotion(potion, count, () => ShowPotionTooltip(potion, count));
                bagSlotPool[i].SetDropTarget(null, UnequipItem);
            }
            else
            {
                bagSlotPool[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Held potions collapsed to one entry per kind, weakest first.</summary>
    private List<KeyValuePair<ConsumableItem, int>> GroupPotions()
    {
        var stacks = new List<KeyValuePair<ConsumableItem, int>>();
        if (boundPotionBag == null) return stacks;

        var index = new Dictionary<ConsumableItem, int>();
        foreach (ConsumableItem potion in boundPotionBag.Potions)
        {
            if (potion == null) continue;
            if (index.TryGetValue(potion, out int at)) stacks[at] = new KeyValuePair<ConsumableItem, int>(potion, stacks[at].Value + 1);
            else
            {
                index[potion] = stacks.Count;
                stacks.Add(new KeyValuePair<ConsumableItem, int>(potion, 1));
            }
        }

        stacks.Sort((a, b) => a.Key.tier.CompareTo(b.Key.tier));
        return stacks;
    }

    private void ShowPotionTooltip(ConsumableItem potion, int count)
    {
        if (tooltip == null || potion == null) return;

        int floor = GameManager.Instance != null ? GameManager.Instance.CurrentFloor : 1;
        bool onCooldown = boundPotionBag != null && boundPotionBag.IsOnCooldown(floor);
        bool atFullHealth = boundPlayer != null && boundPlayer.CurrentHealth >= boundPlayer.Stats.maxHp;

        // The label carries the reason - a greyed "Use" with no explanation reads
        // as a broken button.
        string label = onCooldown
            ? $"On cooldown ({boundPotionBag.FloorsUntilReady(floor)} floors)"
            : atFullHealth ? "Already at full health" : "Use";

        tooltip.ShowPotion(potion, count, label, () => UsePotionFromBag(potion), !onCooldown && !atFullHealth);
    }

    private void UsePotionFromBag(ConsumableItem potion)
    {
        if (boundPotionBag == null || boundPlayer == null) return;

        int floor = GameManager.Instance != null ? GameManager.Instance.CurrentFloor : 1;
        boundPotionBag.TryUse(potion, boundPlayer, floor);
        Refresh();
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
