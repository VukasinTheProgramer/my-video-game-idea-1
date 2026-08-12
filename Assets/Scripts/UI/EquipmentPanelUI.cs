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
/// 2026-08-12 reskin: the whole visual tree (header/stats/equip grid/bag/
/// footer) is now self-built in Awake, following the ItemSlotUI/
/// ItemTooltipUI/ModalScreenUI convention, instead of ~60 hand-placed scene
/// GameObjects - matches the "Adventurer Loadout" pixel-art mockup's layout
/// and dark warm palette. panelRoot itself is still the pre-existing scene
/// object (unchanged reference, just resized/recolored); ItemTooltip is the
/// one child left untouched, since ItemTooltipUI already self-builds its own
/// hierarchy and its serialized reference here still points at that instance.
/// </summary>
public class EquipmentPanelUI : MonoBehaviour
{
    [SerializeField] private GameObject panelRoot;
    [SerializeField] private ItemTooltipUI tooltip;

    private const float PanelWidth = 908f;
    private const float PanelHeight = 616f;
    private const float HeaderHeight = 44f;
    private const float FooterHeight = 64f;
    private const float BagSlotSize = 64f;

    // ---- palette: "Adventurer Loadout" mockup (oklch values converted to sRGB hex) ----
    private static readonly Color PanelBg = Hex("#1B110B");
    private static readonly Color PanelAlt = Hex("#271A11");
    private static readonly Color PanelInset = Hex("#0D0603");
    private static readonly Color BorderColor = Hex("#3F2F24");
    private static readonly Color BorderSoft = Hex("#2E2118");
    private static readonly Color TextColor = Hex("#E2D5CB");
    private static readonly Color TextDim = Hex("#93867E");
    private static readonly Color TextFaint = Hex("#61554E");
    private static readonly Color Accent = Hex("#927EEC");
    private static readonly Color AccentBright = Hex("#B6A8FF");
    private static readonly Color AccentDim = Hex("#42386E");
    private static readonly Color AccentInk = Hex("#090715");
    private static readonly Color GoldColor = Hex("#D7AA42");
    private static readonly Color HpColor = Hex("#CC3336");
    private static readonly Color HpTrack = Hex("#311211");
    private static readonly Color XpTrack = Hex("#20160F");
    private static readonly Color StatAtk = Hex("#E55745");
    private static readonly Color StatMag = Hex("#628BEA");
    private static readonly Color StatAgi = Hex("#54B66E");
    private static readonly Color StatHp = Hex("#D85164");
    private static readonly Color StatDef = Hex("#6693AA");
    private static readonly Color StatMdef = Hex("#807DC0");
    private static readonly Color StatCrit = Hex("#CD9C1F");
    private static readonly Color StatCritDmg = Hex("#E25C26");
    private static readonly Color StatDodge = Hex("#4BAE87");
    private static readonly Color StatParry = Hex("#5892A9");
    private static readonly Color StatLs = Hex("#BC4B87");

    private static Color Hex(string html)
    {
        ColorUtility.TryParseHtmlString(html, out Color c);
        return c;
    }

    // ---- built at runtime by BuildHierarchy(), mirrors the mockup 1:1 ----
    private ItemSlotUI[] slotUIs;
    private GameObject bagPanelGO;
    private RectTransform bagSlotContainer;
    private Text goldText;

    private Image hpFillImage;
    private Text hpBarText;
    private Text attackValueText, magicValueText, agilityValueText, healthValueText;
    private Text defenseValueText, magicDefValueText;
    private Text critChanceValueText, critDamageValueText, dodgeValueText, parryValueText, lifeStealValueText;

    private Text levelText, xpText, availablePointsText;
    private Image xpFillImage;
    private Button attackPointButton, healthPointButton, agilityPointButton;
    private Button sortByRarityButton, sortByLevelButton;

    private const string GoldIconResourcePath = "Icons/Gold";
    private const float GoldIconSize = 20f;
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

    private static readonly string[] SlotCaptions =
    {
        "Head", "Neck", "Main Hand", "Off Hand", "Shoulders", "Chest", "Hands", "Back", "Legs", "Feet",
        "Belt", "Ring 1", "Ring 2", "Trinket 1", "Trinket 2"
    };

    // Grid cell (column 1-5, row 1-6) for each slot, matching the mockup's
    // equipment silhouette layout exactly (col3/rows3-5 is the sprite
    // placeholder, not a slot).
    private static readonly Dictionary<EquipmentSlot, (int col, int row)> GridPositions = new Dictionary<EquipmentSlot, (int col, int row)>
    {
        { EquipmentSlot.Head, (3, 1) },
        { EquipmentSlot.Shoulders, (2, 2) },
        { EquipmentSlot.Neck, (4, 2) },
        { EquipmentSlot.Back, (2, 3) },
        { EquipmentSlot.Hands, (4, 3) },
        { EquipmentSlot.Ring1, (1, 4) },
        { EquipmentSlot.Chest, (2, 4) },
        { EquipmentSlot.Legs, (4, 4) },
        { EquipmentSlot.Trinket1, (5, 4) },
        { EquipmentSlot.Ring2, (1, 5) },
        { EquipmentSlot.Belt, (2, 5) },
        { EquipmentSlot.Feet, (4, 5) },
        { EquipmentSlot.Trinket2, (5, 5) },
        { EquipmentSlot.MainHand, (2, 6) },
        { EquipmentSlot.OffHand, (4, 6) },
    };

    private void Awake()
    {
        if (panelRoot == null)
        {
            Debug.LogError("EquipmentPanelUI: panelRoot not assigned - the panel can never show.");
            return;
        }

        BuildHierarchy();

        // Without this, drag-to-unequip only works if the bag already has an active
        // item slot to land on - an empty (or nearly-full-elsewhere) bag has nowhere
        // to drop onto at all, since bagSlotContainer itself starts with no
        // raycastable graphic (just a layout group).
        BagDropZone.Attach(bagSlotContainer, UnequipItem);
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
    private void HandleGoldChanged(int gold) { if (goldText != null) goldText.text = gold.ToString("N0"); }

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

    /// <summary>Sorts the bag by rarity, highest first. Wired to the header's "Rarity" button.</summary>
    public void SortBagByRarity()
    {
        boundInventory?.SortByRarity();
        SetSortButtonActive(sortByRarityButton, sortByLevelButton);
        Refresh();
    }

    /// <summary>Sorts the bag by item level, highest first. Wired to the header's "Level" button.</summary>
    public void SortBagByItemLevel()
    {
        boundInventory?.SortByItemLevel();
        SetSortButtonActive(sortByLevelButton, sortByRarityButton);
        Refresh();
    }

    private void Refresh()
    {
        if (boundPlayer == null || boundEquipment == null) return;

        // Anything the tooltip was describing may have just moved slots.
        tooltip?.Hide();

        RefreshStats();
        RefreshProgression();
        if (goldText != null && boundWallet != null) goldText.text = boundWallet.Gold.ToString("N0");

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquippableItem item = boundEquipment.GetEquipped(SlotOrder[i]);
            int slotIndex = i; // capture for the closure below

            if (slotUIs != null && i < slotUIs.Length && slotUIs[i] != null)
            {
                slotUIs[i].Bind(item, item != null ? () => ShowUnequipTooltip(slotIndex, item) : (System.Action)null);
                slotUIs[i].SetDropTarget(SlotOrder[i], EquipFromBag);
            }
        }

        RefreshBag();
    }

    /// <summary>
    /// Per-row stat readout (mockup: Primary/Defense/Combat columns) plus the HP
    /// bar, using CombatResolver's own formulas (not a re-derived copy) so this
    /// always matches what combat actually rolls against - see CombatResolver's
    /// public Effective* methods.
    /// </summary>
    private void RefreshStats()
    {
        if (boundPlayer == null) return;

        Stats stats = boundPlayer.Stats;
        bool canParry = CombatResolver.CanParry(boundPlayer.EquippedWeaponType);

        if (hpFillImage != null) hpFillImage.fillAmount = stats.maxHp > 0 ? Mathf.Clamp01((float)boundPlayer.CurrentHealth / stats.maxHp) : 0f;
        if (hpBarText != null) hpBarText.text = $"{boundPlayer.CurrentHealth} / {stats.maxHp}";

        SetStat(attackValueText, stats.attack.ToString());
        SetStat(magicValueText, stats.magic.ToString());
        SetStat(agilityValueText, stats.agility.ToString());
        SetStat(healthValueText, stats.maxHp.ToString());
        SetStat(defenseValueText, stats.defense.ToString());
        SetStat(magicDefValueText, stats.magicDefense.ToString());
        SetStat(critChanceValueText, $"{CombatResolver.EffectiveCritChance(stats):0.#}%");
        SetStat(critDamageValueText, $"{CombatResolver.EffectiveCritDamageMultiplier(stats.critDamageBonus) * 100f:0}%");
        SetStat(dodgeValueText, $"{CombatResolver.EffectiveDodgeChance(stats):0.#}%");
        // No melee weapon equipped -> parry can't roll at all; "-" fits the mockup's
        // compact one-line-per-stat column (the old verbose "(no melee weapon)"
        // parenthetical doesn't fit here - right-click a MainHand slot for detail).
        SetStat(parryValueText, canParry ? $"{CombatResolver.EffectiveParryChance(stats, true):0.#}%" : "—");
        SetStat(lifeStealValueText, $"{CombatResolver.EffectiveLifeStealPercent(stats):0.#}%");
    }

    private static void SetStat(Text target, string value)
    {
        if (target != null) target.text = value;
    }

    /// <summary>Level/XP bar plus the Attack/Health/Agility point-spend buttons
    /// (IMPLEMENTED.md -> "Leveling & stat points") - buttons only enable while
    /// points are available; spending is permanent, there's no respec. No Magic
    /// button: PlayerProgression.AllocatableStat only has Attack/Health/Agility -
    /// Magic stays gear-only, so there's nothing to wire a 4th button to.</summary>
    private void RefreshProgression()
    {
        if (boundProgression == null) return;

        if (levelText != null) levelText.text = $"Lv. {boundProgression.Level}";
        if (xpFillImage != null) xpFillImage.fillAmount = boundProgression.XPToNextLevel > 0 ? Mathf.Clamp01((float)boundProgression.CurrentXP / boundProgression.XPToNextLevel) : 0f;
        if (xpText != null) xpText.text = $"{boundProgression.CurrentXP:N0} / {boundProgression.XPToNextLevel:N0} XP";
        if (availablePointsText != null)
        {
            int points = boundProgression.AvailableStatPoints;
            availablePointsText.text = $"{points} point{(points == 1 ? string.Empty : "s")}";
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
            go.GetComponent<RectTransform>().sizeDelta = new Vector2(BagSlotSize, BagSlotSize);

            bagSlotPool.Add(go.AddComponent<ItemSlotUI>());
        }
    }

    // =====================================================================
    // Layout build - "Adventurer Loadout" mockup, converted from CSS flex/
    // grid to fixed anchored-top-left RectTransforms (same idiom the old
    // hand-placed scene hierarchy used, just generated instead of hand-
    // placed). Runtime-only, same as every other self-building UI piece in
    // this codebase (ModalScreenUI/ItemTooltipUI/DamageNumberSpawner) - not
    // meant to be saved back into Main.unity.
    // =====================================================================

    private void BuildHierarchy()
    {
        ClearOldChildren();

        RectTransform rootRt = panelRoot.GetComponent<RectTransform>();
        rootRt.sizeDelta = new Vector2(PanelWidth, PanelHeight);
        Image rootImg = panelRoot.GetComponent<Image>();
        if (rootImg == null) rootImg = panelRoot.AddComponent<Image>();
        rootImg.color = PanelBg;
        AddBorder(panelRoot.transform, 0f, 0f, PanelWidth, PanelHeight, BorderColor, 2f);

        BuildHeader();
        BuildStatsColumn();
        BuildEquipGrid();
        BuildBagPanel();
        BuildFooter();
    }

    /// <summary>Destroys every previous child except the hand-placed ItemTooltip
    /// instance (ItemTooltipUI self-builds its own hierarchy and this component's
    /// serialized `tooltip` field still points at it - rebuilding it here would
    /// both duplicate work and orphan that reference).</summary>
    private void ClearOldChildren()
    {
        var toDestroy = new List<GameObject>();
        foreach (Transform child in panelRoot.transform)
        {
            if (child.GetComponent<ItemTooltipUI>() != null) continue;
            toDestroy.Add(child.gameObject);
        }
        foreach (GameObject go in toDestroy) DestroyImmediate(go);
    }

    private void BuildHeader()
    {
        CreatePanel(panelRoot.transform, "HeaderBg", 0f, 0f, PanelWidth, HeaderHeight, PanelAlt);
        CreatePanel(panelRoot.transform, "HeaderBorder", 0f, HeaderHeight - 2f, PanelWidth, 2f, BorderColor);

        CreateLabel(panelRoot.transform, "Title", 16f, 10f, 300f, 24f, "CHARACTER", TextColor, 20, TextAnchor.MiddleLeft, FontStyle.Bold);

        CreateButton(panelRoot.transform, "CloseButton", PanelWidth - 16f - 20f, 12f, 20f, 20f, BorderSoft, "X", TextDim, 12, TogglePanel);

        var bagToggleRt = CreateButtonRect(panelRoot.transform, "BagToggleButton", PanelWidth - 16f - 20f - 8f - 60f, 10f, 60f, 24f, AccentDim, "Bag", AccentBright, 13);
        bagToggleRt.GetComponent<Button>().onClick.AddListener(() => bagPanelGO.SetActive(!bagPanelGO.activeSelf));

        float goldClusterRight = PanelWidth - 16f - 20f - 8f - 60f - 12f;
        goldText = CreateLabel(panelRoot.transform, "GoldText", goldClusterRight - 70f, 12f, 70f, 20f, "0", GoldColor, 15, TextAnchor.MiddleLeft);
        HudIcon.AddBeside(goldText.GetComponent<RectTransform>(), GoldIconResourcePath, GoldIconSize, GoldIconGap);
    }

    private void BuildStatsColumn()
    {
        const float x = 16f, w = 200f;
        float y = 60f;

        CreatePanel(panelRoot.transform, "HpBarTrack", x, y, w, 20f, HpTrack);
        AddBorder(panelRoot.transform, x, y, w, 20f, BorderColor, 2f);
        RectTransform hpFillRt = CreateRect(panelRoot.transform, "HpBarFill", x, y, w, 20f);
        hpFillImage = hpFillRt.gameObject.AddComponent<Image>();
        hpFillImage.color = HpColor;
        hpFillImage.type = Image.Type.Filled;
        hpFillImage.fillMethod = Image.FillMethod.Horizontal;
        hpFillImage.raycastTarget = false;
        hpBarText = CreateLabel(panelRoot.transform, "HpBarText", x, y, w, 20f, string.Empty, TextColor, 13, TextAnchor.MiddleCenter);
        y += 20f + 8f;

        y = BuildStatSection(x, y, w, "PRIMARY", new (string label, Color color, System.Action<Text> bind)[]
        {
            ("Attack", StatAtk, t => attackValueText = t),
            ("Magic", StatMag, t => magicValueText = t),
            ("Agility", StatAgi, t => agilityValueText = t),
            ("Health", StatHp, t => healthValueText = t),
        }, divider: true);

        y = BuildStatSection(x, y, w, "DEFENSE", new (string label, Color color, System.Action<Text> bind)[]
        {
            ("Defense", StatDef, t => defenseValueText = t),
            ("Magic Def.", StatMdef, t => magicDefValueText = t),
        }, divider: true);

        BuildStatSection(x, y, w, "COMBAT", new (string label, Color color, System.Action<Text> bind)[]
        {
            ("Crit Chance", StatCrit, t => critChanceValueText = t),
            ("Crit Damage", StatCritDmg, t => critDamageValueText = t),
            ("Dodge", StatDodge, t => dodgeValueText = t),
            ("Parry", StatParry, t => parryValueText = t),
            ("Life Steal", StatLs, t => lifeStealValueText = t),
        }, divider: false);
    }

    /// <summary>Builds one labeled group of stat rows (Primary/Defense/Combat) and
    /// returns the y cursor just past it - a tiny layout cursor beats hand-computing
    /// every row's absolute y by hand for a 11-row column.</summary>
    private float BuildStatSection(float x, float y, float w, string header, (string label, Color color, System.Action<Text> bind)[] rows, bool divider)
    {
        CreateLabel(panelRoot.transform, header + "Header", x, y, w, 14f, header, TextFaint, 10, TextAnchor.MiddleLeft);
        y += 14f;

        foreach (var row in rows)
        {
            CreateLabel(panelRoot.transform, row.label + "Label", x, y, w * 0.6f, 18f, row.label, row.color, 14, TextAnchor.MiddleLeft);
            Text value = CreateLabel(panelRoot.transform, row.label + "Value", x + w * 0.6f, y, w * 0.4f, 18f, string.Empty, TextColor, 14, TextAnchor.MiddleRight);
            row.bind(value);
            y += 18f;
        }

        if (divider)
        {
            CreatePanel(panelRoot.transform, header + "Divider", x, y + 4f, w, 2f, BorderSoft);
            y += 4f + 2f + 6f;
        }

        return y;
    }

    private void BuildEquipGrid()
    {
        const float gridX = 230f, gridY = 60f;
        float[] colX = { 0f, 78f, 156f, 250f, 328f };
        float[] colW = { 70f, 70f, 86f, 70f, 70f };
        float[] rowY = { 0f, 78f, 156f, 234f, 312f, 390f };
        const float rowH = 70f, slotSize = 54f, labelH = 14f, labelGap = 2f;

        slotUIs = new ItemSlotUI[SlotOrder.Length];

        for (int i = 0; i < SlotOrder.Length; i++)
        {
            EquipmentSlot slot = SlotOrder[i];
            (int col, int row) = GridPositions[slot];
            float cellX = gridX + colX[col - 1];
            float cellY = gridY + rowY[row - 1];
            float cellW = colW[col - 1];

            CreateLabel(panelRoot.transform, "Label_" + slot, cellX, cellY, cellW, labelH,
                SlotCaptions[i].ToUpperInvariant(), TextFaint, 10, TextAnchor.MiddleCenter);

            float slotX = cellX + (cellW - slotSize) / 2f;
            float slotY = cellY + labelH + labelGap;
            RectTransform slotRect = CreateRect(panelRoot.transform, "Slot_" + slot, slotX, slotY, slotSize, slotSize);
            slotUIs[i] = slotRect.gameObject.AddComponent<ItemSlotUI>();
        }

        // Sprite silhouette placeholder - centre column, rows 3-5. Purely decorative
        // chrome, matching the mockup 1:1 (its own "Sprite" box is a placeholder too -
        // there's no live character portrait render anywhere in this project).
        float spriteX = gridX + colX[2];
        float spriteY = gridY + rowY[2];
        float spriteW = colW[2];
        float spriteH = rowY[4] + rowH - rowY[2];
        Image spriteBg = CreatePanel(panelRoot.transform, "SpriteSilhouette", spriteX, spriteY, spriteW, spriteH, PanelInset);
        AddBorder(panelRoot.transform, spriteX, spriteY, spriteW, spriteH, BorderSoft, 2f);
        CreateLabel(spriteBg.transform, "Label", 4f, spriteH / 2f - 8f, spriteW - 8f, 16f, "SPRITE", TextFaint, 10, TextAnchor.MiddleCenter);
    }

    private void BuildBagPanel()
    {
        const float bagX = 642f, bagY = 60f, bagW = 250f, bagH = 476f, pad = 10f;

        Image bg = CreatePanel(panelRoot.transform, "BagPanel", bagX, bagY, bagW, bagH, PanelAlt);
        bagPanelGO = bg.gameObject;
        AddBorder(panelRoot.transform, bagX, bagY, bagW, bagH, BorderColor, 2f);

        float innerX = bagX + pad, innerY = bagY + pad, innerW = bagW - pad * 2f;

        CreateLabel(panelRoot.transform, "BagTitle", innerX, innerY, 70f, 20f, "BAG", TextColor, 15, TextAnchor.MiddleLeft);

        const float sortBtnW = 58f, sortBtnH = 22f, sortBtnGap = 6f;
        sortByLevelButton = CreateButton(panelRoot.transform, "SortByLevelButton",
            innerX + innerW - sortBtnW, innerY, sortBtnW, sortBtnH, PanelInset, "Level", TextDim, 11, SortBagByItemLevel);
        sortByRarityButton = CreateButton(panelRoot.transform, "SortByRarityButton",
            innerX + innerW - sortBtnW * 2f - sortBtnGap, innerY, sortBtnW, sortBtnH, PanelInset, "Rarity", AccentBright, 11, SortBagByRarity);
        SetSortButtonActive(sortByRarityButton, sortByLevelButton); // Rarity starts active, same default as the mockup

        float scrollY = innerY + 20f + 8f;
        float scrollH = bagH - pad * 2f - 20f - 8f;

        RectTransform scrollRt = CreateRect(panelRoot.transform, "BagScrollView", innerX, scrollY, innerW, scrollH);
        ScrollRect scroll = scrollRt.gameObject.AddComponent<ScrollRect>();
        scroll.horizontal = false;
        scroll.vertical = true;
        scroll.movementType = ScrollRect.MovementType.Clamped;
        scroll.scrollSensitivity = 24f;

        RectTransform viewport = CreateRect(scrollRt, "Viewport", 0f, 0f, innerW, scrollH);
        Image viewportImg = viewport.gameObject.AddComponent<Image>();
        viewportImg.color = new Color(0f, 0f, 0f, 0f); // Mask needs *a* Graphic; fully transparent still clips/raycasts fine
        viewport.gameObject.AddComponent<RectMask2D>();

        bagSlotContainer = CreateRect(viewport, "BagSlotContainer", 0f, 0f, innerW, 0f);
        GridLayoutGroup grid = bagSlotContainer.gameObject.AddComponent<GridLayoutGroup>();
        grid.cellSize = new Vector2(BagSlotSize, BagSlotSize);
        grid.spacing = new Vector2(8f, 8f);
        ContentSizeFitter fitter = bagSlotContainer.gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;
        // BagDropZone.Attach (called once from Awake, after BuildHierarchy) adds the
        // container's own raycastable background Image - not duplicated here.

        scroll.content = bagSlotContainer;
        scroll.viewport = viewport;
    }

    private void BuildFooter()
    {
        const float y = 552f;
        CreatePanel(panelRoot.transform, "FooterBg", 0f, y, PanelWidth, FooterHeight, PanelAlt);
        CreatePanel(panelRoot.transform, "FooterBorder", 0f, y, PanelWidth, 2f, BorderColor);

        float innerY = y + (FooterHeight - 20f) / 2f;
        levelText = CreateLabel(panelRoot.transform, "LevelText", 16f, innerY, 54f, 20f, "Lv. 1", TextColor, 15, TextAnchor.MiddleLeft, FontStyle.Bold);

        const float xpBarX = 78f, xpBarW = 280f, xpBarH = 16f;
        float xpBarY = y + (FooterHeight - xpBarH) / 2f;
        CreatePanel(panelRoot.transform, "XpBarTrack", xpBarX, xpBarY, xpBarW, xpBarH, XpTrack);
        AddBorder(panelRoot.transform, xpBarX, xpBarY, xpBarW, xpBarH, BorderColor, 2f);
        RectTransform xpFillRt = CreateRect(panelRoot.transform, "XpBarFill", xpBarX, xpBarY, xpBarW, xpBarH);
        xpFillImage = xpFillRt.gameObject.AddComponent<Image>();
        xpFillImage.color = Accent;
        xpFillImage.type = Image.Type.Filled;
        xpFillImage.fillMethod = Image.FillMethod.Horizontal;
        xpFillImage.raycastTarget = false;

        xpText = CreateLabel(panelRoot.transform, "XpText", xpBarX + xpBarW + 8f, innerY, 130f, 20f, string.Empty, TextDim, 13, TextAnchor.MiddleLeft);
        availablePointsText = CreateLabel(panelRoot.transform, "PointsText", xpBarX + xpBarW + 8f + 138f, innerY, 90f, 20f, string.Empty, GoldColor, 13, TextAnchor.MiddleLeft);

        const float btnW = 70f, btnH = 32f, btnGap = 8f;
        float buttonsX = PanelWidth - 16f - (btnW * 3f + btnGap * 2f);
        float buttonsY = y + (FooterHeight - btnH) / 2f;
        attackPointButton = CreateButton(panelRoot.transform, "AttackPointButton", buttonsX, buttonsY, btnW, btnH, Accent, "ATK+", AccentInk, 14, () => SpendPoint(PlayerProgression.AllocatableStat.Attack));
        agilityPointButton = CreateButton(panelRoot.transform, "AgilityPointButton", buttonsX + btnW + btnGap, buttonsY, btnW, btnH, Accent, "AGI+", AccentInk, 14, () => SpendPoint(PlayerProgression.AllocatableStat.Agility));
        healthPointButton = CreateButton(panelRoot.transform, "HealthPointButton", buttonsX + (btnW + btnGap) * 2f, buttonsY, btnW, btnH, Accent, "HP+", AccentInk, 14, () => SpendPoint(PlayerProgression.AllocatableStat.Health));
    }

    private void SetSortButtonActive(Button active, Button other)
    {
        ApplySortButtonStyle(active, true);
        ApplySortButtonStyle(other, false);
    }

    private static void ApplySortButtonStyle(Button button, bool active)
    {
        if (button == null) return;
        Image bg = button.GetComponent<Image>();
        if (bg != null) bg.color = active ? AccentDim : PanelInset;
        Text label = button.GetComponentInChildren<Text>();
        if (label != null) label.color = active ? AccentBright : TextDim;
    }

    // ---- small layout helpers: every rect is anchored top-left (pivot 0,1),
    // positioned by (x from parent's left, y from parent's top) - same idiom the
    // hand-placed scene hierarchy this replaces already used. ----

    private static RectTransform CreateRect(Transform parent, string name, float x, float y, float w, float h)
    {
        var go = new GameObject(name, typeof(RectTransform));
        var rt = (RectTransform)go.transform;
        rt.SetParent(parent, false);
        rt.anchorMin = new Vector2(0f, 1f);
        rt.anchorMax = new Vector2(0f, 1f);
        rt.pivot = new Vector2(0f, 1f);
        rt.anchoredPosition = new Vector2(x, -y);
        rt.sizeDelta = new Vector2(w, h);
        return rt;
    }

    private static Image CreatePanel(Transform parent, string name, float x, float y, float w, float h, Color color)
    {
        RectTransform rt = CreateRect(parent, name, x, y, w, h);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = color;
        img.raycastTarget = false;
        return img;
    }

    /// <summary>Four flat 1px-thick strips instead of a 9-sliced border sprite -
    /// no extra art asset needed for a sharp-cornered, single-color frame.
    /// Strips are created as siblings at the SAME (x,y,w,h) as whatever they're
    /// framing, not nested inside it - avoids depending on the framed object's
    /// own rect being finalized yet.</summary>
    private static void AddBorder(Transform parent, float x, float y, float w, float h, Color color, float thickness)
    {
        CreatePanel(parent, "BorderTop", x, y, w, thickness, color);
        CreatePanel(parent, "BorderBottom", x, y + h - thickness, w, thickness, color);
        CreatePanel(parent, "BorderLeft", x, y, thickness, h, color);
        CreatePanel(parent, "BorderRight", x + w - thickness, y, thickness, h, color);
    }

    private static Text CreateLabel(Transform parent, string name, float x, float y, float w, float h, string content, Color color, int fontSize, TextAnchor alignment, FontStyle style = FontStyle.Normal)
    {
        RectTransform rt = CreateRect(parent, name, x, y, w, h);
        Text text = rt.gameObject.AddComponent<Text>();
        text.font = UIFonts.Default;
        text.text = content;
        text.color = color;
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.alignment = alignment;
        text.raycastTarget = false;
        text.horizontalOverflow = HorizontalWrapMode.Overflow;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    private static RectTransform CreateButtonRect(Transform parent, string name, float x, float y, float w, float h, Color bg, string label, Color labelColor, int fontSize)
    {
        RectTransform rt = CreateRect(parent, name, x, y, w, h);
        Image img = rt.gameObject.AddComponent<Image>();
        img.color = bg;
        Button button = rt.gameObject.AddComponent<Button>();
        button.targetGraphic = img;
        CreateLabel(rt, "Label", 0f, 0f, w, h, label, labelColor, fontSize, TextAnchor.MiddleCenter);
        return rt;
    }

    private static Button CreateButton(Transform parent, string name, float x, float y, float w, float h, Color bg, string label, Color labelColor, int fontSize, UnityEngine.Events.UnityAction onClick)
    {
        RectTransform rt = CreateButtonRect(parent, name, x, y, w, h, bg, label, labelColor, fontSize);
        Button button = rt.GetComponent<Button>();
        if (onClick != null) button.onClick.AddListener(onClick);
        return button;
    }
}
