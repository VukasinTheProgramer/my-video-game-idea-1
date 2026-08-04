using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Popup shown on right-clicking an ItemSlotUI: item name (rarity-colored),
/// its own stat bonuses, and one action button (Equip/Unequip) that performs
/// the actual equip swap. Builds its own hierarchy in Awake, same
/// self-building pattern as ItemSlotUI - drop it once under the equipment
/// panel and EquipmentPanelUI drives it via Show/Hide.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class ItemTooltipUI : MonoBehaviour
{
    private Text nameText;
    private Text statsText;
    private Text actionLabel;
    private Button actionButton;

    private void Awake()
    {
        BuildHierarchy();
        gameObject.SetActive(false);
    }

    private void BuildHierarchy()
    {
        if (nameText != null) return; // already built

        var bg = gameObject.GetComponent<Image>();
        if (bg == null) bg = gameObject.AddComponent<Image>();
        bg.color = new Color(0.05f, 0.05f, 0.05f, 0.95f);

        var layout = gameObject.GetComponent<VerticalLayoutGroup>();
        if (layout == null) layout = gameObject.AddComponent<VerticalLayoutGroup>();
        layout.padding = new RectOffset(10, 10, 10, 10);
        layout.spacing = 6f;
        layout.childControlWidth = true;
        layout.childForceExpandWidth = true;
        layout.childControlHeight = false;
        layout.childForceExpandHeight = false;

        var fitter = gameObject.GetComponent<ContentSizeFitter>();
        if (fitter == null) fitter = gameObject.AddComponent<ContentSizeFitter>();
        fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

        nameText = MakeText("NameText", 18, FontStyle.Bold);
        statsText = MakeText("StatsText", 14, FontStyle.Normal);

        var buttonGO = new GameObject("ActionButton", typeof(RectTransform), typeof(Image), typeof(Button), typeof(LayoutElement));
        buttonGO.transform.SetParent(transform, false);
        buttonGO.GetComponent<Image>().color = new Color(0.2f, 0.2f, 0.2f, 1f);
        buttonGO.GetComponent<LayoutElement>().preferredHeight = 30f;
        actionButton = buttonGO.GetComponent<Button>();

        var labelGO = new GameObject("Label", typeof(RectTransform), typeof(Text));
        labelGO.transform.SetParent(buttonGO.transform, false);
        var labelRect = labelGO.GetComponent<RectTransform>();
        labelRect.anchorMin = Vector2.zero;
        labelRect.anchorMax = Vector2.one;
        labelRect.offsetMin = Vector2.zero;
        labelRect.offsetMax = Vector2.zero;
        actionLabel = labelGO.GetComponent<Text>();
        actionLabel.alignment = TextAnchor.MiddleCenter;
        actionLabel.color = Color.white;
        actionLabel.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        actionLabel.fontSize = 14;
    }

    private Text MakeText(string name, int fontSize, FontStyle style)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text), typeof(LayoutElement));
        go.transform.SetParent(transform, false);
        var text = go.GetComponent<Text>();
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.fontStyle = style;
        text.color = Color.white;
        text.horizontalOverflow = HorizontalWrapMode.Wrap;
        text.verticalOverflow = VerticalWrapMode.Overflow;
        return text;
    }

    /// <summary>Populates and shows the tooltip. actionLabelText/onAction drive the single button ("Equip" or "Unequip").</summary>
    public void Show(EquippableItem item, string actionLabelText, System.Action onAction)
    {
        BuildHierarchy();
        // Hide rather than return: leaving the previous item's text and its stale
        // action listener live on a visible tooltip would let the button act on
        // something the player is no longer looking at.
        if (item == null)
        {
            Hide();
            return;
        }

        nameText.text = item.displayName;
        nameText.color = RarityVisuals.OutlineColor(item.rarity);
        statsText.text = BuildStatsText(item);

        actionLabel.text = actionLabelText;
        actionButton.onClick.RemoveAllListeners();
        actionButton.onClick.AddListener(() =>
        {
            onAction?.Invoke();
            Hide();
        });

        gameObject.SetActive(true);
        transform.SetAsLastSibling();
    }

    public void Hide()
    {
        gameObject.SetActive(false);
    }

    private static string BuildStatsText(EquippableItem item)
    {
        Stats s = item.bonusStats ?? new Stats();
        var lines = new System.Text.StringBuilder();
        lines.Append($"{item.slot} - Item Level {item.itemLevel}\n");
        if (item.weaponDamage != 0) lines.Append($"Weapon Damage: {item.weaponDamage}\n");
        if (s.attack != 0) lines.Append($"Attack: +{s.attack}\n");
        if (s.agility != 0) lines.Append($"Agility: +{s.agility}\n");
        if (s.magic != 0) lines.Append($"Magic: +{s.magic}\n");
        if (s.defense != 0) lines.Append($"Defense: +{s.defense}\n");
        if (s.magicDefense != 0) lines.Append($"Magic Defense: +{s.magicDefense}\n");
        if (s.maxHp != 0) lines.Append($"Max HP: +{s.maxHp}\n");
        if (s.critChanceBonus != 0) lines.Append($"Crit Chance: +{s.critChanceBonus:0.#}%\n");
        if (s.critDamageBonus != 0) lines.Append($"Crit Damage: +{s.critDamageBonus:0.#}%\n");
        if (s.dodgeBonus != 0) lines.Append($"Dodge: +{s.dodgeBonus:0.#}%\n");
        if (s.parryBonus != 0) lines.Append($"Parry: +{s.parryBonus:0.#}%\n");
        if (s.lifeSteal != 0) lines.Append($"Life Steal: +{s.lifeSteal:0.#}%\n");
        return lines.ToString().TrimEnd('\n');
    }
}
