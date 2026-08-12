using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// Bottom-left quick-use potion button, plus the chooser that opens when more
/// than one kind is held.
///
/// Click behaviour, as specified: nothing held does nothing; exactly one kind
/// held drinks it immediately (no menu for a menu of one); two or more opens a
/// chooser listing each kind with its count.
///
/// Self-bootstrapping like DamageNumberSpawner/the outro screens (CLAUDE.md
/// §6) - it builds its own hierarchy under the scene's Canvas, so there is no
/// scene wiring to forget. Binds to the player through
/// GameManager.OnPlayerSpawned rather than re-resolving GameManager.Instance
/// per click (rule 2).
/// </summary>
public class PotionQuickUseUI : MonoBehaviour
{
    private const float ButtonSize = 64f;
    private const float ScreenMargin = 16f;
    private const float RowHeight = 34f;
    private const float ChooserWidth = 210f;

    private static readonly Color ReadyTint = Color.white;
    // Desaturated + dimmed rather than a countdown label: "not usable right now"
    // is the only thing the player has to read off the button, and a greyed icon
    // says that at a glance where "9f" needed decoding.
    private static readonly Color CooldownTint = new Color(0.42f, 0.42f, 0.42f, 0.75f);

    /// <summary>The collapsed button always shows the small potion. Loaded from
    /// Resources rather than a [SerializeField]: this component creates itself at
    /// runtime (see Bootstrap), so an inspector-assigned field could never be
    /// filled in and would silently render nothing (CLAUDE.md §0).</summary>
    private const string ButtonIconItemPath = "Items/SmallHealthPotion";

    private Sprite buttonIcon;

    private PotionBag bag;
    private Entity playerEntity;

    private Image iconImage;
    private Text countText;
    private RectTransform chooserRoot;
    private readonly List<GameObject> chooserRows = new List<GameObject>();

    [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
    private static void Bootstrap()
    {
        // Runs per scene load; the null guard keeps a second one from appearing
        // if this ever runs in a scene that already has one.
        if (FindFirstObjectByType<PotionQuickUseUI>() != null) return;
        // RectTransform up front, not a plain Transform: a UI child anchors against
        // its parent's RectTransform, so a plain-Transform root leaves the button's
        // bottom-left anchoring with nothing to resolve against and strands it in
        // the middle of the screen.
        new GameObject(nameof(PotionQuickUseUI), typeof(RectTransform)).AddComponent<PotionQuickUseUI>();
    }

    private void Start()
    {
        Canvas canvas = FindFirstObjectByType<Canvas>();
        if (canvas == null)
        {
            Debug.LogError($"{name}: no Canvas in the scene - the potion HUD can never render.");
            enabled = false;
            return;
        }

        var iconSource = Resources.Load<ConsumableItem>(ButtonIconItemPath);
        if (iconSource == null || iconSource.icon == null)
        {
            Debug.LogError($"{name}: no ConsumableItem with an icon at Resources/{ButtonIconItemPath} - the quick-use button will render empty.");
        }
        else buttonIcon = iconSource.icon;

        BuildHierarchy(canvas);

        // Hidden until a run actually starts (GameManager.OnPlayerSpawned) - the
        // button self-bootstraps on scene load, which is also the main-menu
        // screen, and there's no bag/player to use yet at that point.
        gameObject.SetActive(false);

        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlayerSpawned += HandlePlayerSpawned;
            // Floor changes move the cooldown along, so the label has to refresh
            // even when nothing about the held potions changed.
            GameManager.Instance.OnFloorChanged += HandleFloorChanged;
            if (GameManager.Instance.Player != null) HandlePlayerSpawned(GameManager.Instance.Player);
        }

        Refresh();
    }

    private void OnDestroy()
    {
        if (GameManager.Instance != null)
        {
            GameManager.Instance.OnPlayerSpawned -= HandlePlayerSpawned;
            GameManager.Instance.OnFloorChanged -= HandleFloorChanged;
        }
        if (bag != null) bag.OnChanged -= Refresh;
    }

    private void HandlePlayerSpawned(PlayerController player)
    {
        if (player == null) return;
        if (bag != null) bag.OnChanged -= Refresh;

        gameObject.SetActive(true);
        playerEntity = player;
        bag = player.GetComponent<PotionBag>();
        if (bag != null) bag.OnChanged += Refresh;
        Refresh();
    }

    private void HandleFloorChanged(int floor) => Refresh();

    /// <summary>H is the quick-use hotkey and behaves exactly like clicking the
    /// button - including opening the chooser when more than one kind is held,
    /// so the key is never a hidden second code path that can drift from it.</summary>
    private void Update()
    {
        if (Input.GetKeyDown(KeyCode.H)) HandleClick();
    }

    private void BuildHierarchy(Canvas canvas)
    {
        transform.SetParent(canvas.transform, false);

        // Stretch the root over the whole canvas so "bottom-left" below means the
        // bottom-left of the SCREEN. Left at its default centred zero-size rect,
        // every child anchor is measured from the middle of the canvas instead.
        var rootRect = GetComponent<RectTransform>();
        rootRect.anchorMin = Vector2.zero;
        rootRect.anchorMax = Vector2.one;
        rootRect.offsetMin = Vector2.zero;
        rootRect.offsetMax = Vector2.zero;
        rootRect.localScale = Vector3.one;

        var buttonGO = new GameObject("QuickUseButton", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
        buttonGO.transform.SetParent(transform, false);

        var buttonRect = buttonGO.GetComponent<RectTransform>();
        buttonRect.anchorMin = Vector2.zero;
        buttonRect.anchorMax = Vector2.zero;
        buttonRect.pivot = Vector2.zero;
        buttonRect.anchoredPosition = new Vector2(ScreenMargin, ScreenMargin);
        buttonRect.sizeDelta = new Vector2(ButtonSize, ButtonSize);

        // Fully transparent, not removed: the Image is what makes the button's
        // whole rect clickable. Alpha 0 still receives raycasts, so this keeps the
        // hit area without drawing the grey plate.
        buttonGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0f);
        buttonGO.GetComponent<Button>().onClick.AddListener(HandleClick);

        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
        iconGO.transform.SetParent(buttonGO.transform, false);
        var iconRect = iconGO.GetComponent<RectTransform>();
        iconRect.anchorMin = Vector2.zero;
        iconRect.anchorMax = Vector2.one;
        iconRect.offsetMin = new Vector2(6f, 6f);
        iconRect.offsetMax = new Vector2(-6f, -6f);
        iconImage = iconGO.GetComponent<Image>();
        iconImage.preserveAspect = true;
        iconImage.raycastTarget = false;

        countText = CreateLabel("Count", buttonGO.transform, TextAnchor.LowerRight, 16);

        // Static hotkey hint - without it the H binding is invisible.
        Text hotkeyHint = CreateLabel("HotkeyHint", buttonGO.transform, TextAnchor.UpperLeft, 12);
        hotkeyHint.text = "H";
        hotkeyHint.color = new Color(1f, 1f, 1f, 0.6f);

        chooserRoot = new GameObject("Chooser", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image)).GetComponent<RectTransform>();
        chooserRoot.SetParent(transform, false);
        chooserRoot.anchorMin = Vector2.zero;
        chooserRoot.anchorMax = Vector2.zero;
        chooserRoot.pivot = Vector2.zero;
        chooserRoot.anchoredPosition = new Vector2(ScreenMargin, ScreenMargin + ButtonSize + 8f);
        chooserRoot.sizeDelta = new Vector2(ChooserWidth, RowHeight);
        chooserRoot.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.85f);
        chooserRoot.gameObject.SetActive(false);
    }

    private static Text CreateLabel(string label, Transform parent, TextAnchor anchor, int fontSize)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = new Vector2(2f, 2f);
        rect.offsetMax = new Vector2(-2f, -2f);

        var text = go.GetComponent<Text>();
        text.font = UIFonts.Default;
        text.fontSize = fontSize;
        text.alignment = anchor;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private int CurrentFloor => GameManager.Instance != null ? GameManager.Instance.CurrentFloor : 1;

    /// <summary>Distinct potion kinds held, with counts, ordered by tier.</summary>
    private List<KeyValuePair<ConsumableItem, int>> GroupHeldPotions()
    {
        var grouped = new List<KeyValuePair<ConsumableItem, int>>();
        if (bag == null) return grouped;

        var index = new Dictionary<ConsumableItem, int>();
        foreach (ConsumableItem potion in bag.Potions)
        {
            if (potion == null) continue;
            if (index.TryGetValue(potion, out int at)) grouped[at] = new KeyValuePair<ConsumableItem, int>(potion, grouped[at].Value + 1);
            else
            {
                index[potion] = grouped.Count;
                grouped.Add(new KeyValuePair<ConsumableItem, int>(potion, 1));
            }
        }

        grouped.Sort((a, b) => a.Key.tier.CompareTo(b.Key.tier));
        return grouped;
    }

    private void Refresh()
    {
        if (iconImage == null) return; // Start hasn't built the hierarchy yet

        List<KeyValuePair<ConsumableItem, int>> grouped = GroupHeldPotions();
        int total = 0;
        foreach (var entry in grouped) total += entry.Value;

        // Fixed icon (the small potion), not whatever is held: this is the "drink
        // a potion" affordance, so it should look the same every time rather than
        // swapping art as the bag changes. Which potion actually gets drunk is
        // decided on click - directly when only one kind is held, via the chooser
        // otherwise.
        iconImage.sprite = buttonIcon;
        iconImage.enabled = buttonIcon != null;

        countText.text = total > 0 ? total.ToString() : string.Empty;

        // Greyed out whenever it can't be pressed to any effect - either the
        // cooldown is running or there's nothing to drink. No countdown text: the
        // dimming alone carries "not interactable".
        bool usable = bag != null && total > 0 && !bag.IsOnCooldown(CurrentFloor);
        iconImage.color = usable ? ReadyTint : CooldownTint;
        countText.color = usable ? Color.white : new Color(1f, 1f, 1f, 0.5f);

        if (chooserRoot.gameObject.activeSelf) BuildChooserRows(grouped);
    }

    private void HandleClick()
    {
        if (bag == null) return;

        // Toggling closed has to work even with 0-1 potions left, otherwise using
        // the second-to-last potion from the chooser would strand the panel open.
        if (chooserRoot.gameObject.activeSelf)
        {
            chooserRoot.gameObject.SetActive(false);
            return;
        }

        List<KeyValuePair<ConsumableItem, int>> grouped = GroupHeldPotions();
        if (grouped.Count == 0) return;            // nothing held - nothing happens
        if (grouped.Count == 1)
        {
            UsePotion(grouped[0].Key);             // only one kind - drink it, no menu
            return;
        }

        BuildChooserRows(grouped);
        chooserRoot.gameObject.SetActive(true);
    }

    private void BuildChooserRows(List<KeyValuePair<ConsumableItem, int>> grouped)
    {
        foreach (GameObject row in chooserRows) Destroy(row);
        chooserRows.Clear();

        for (int i = 0; i < grouped.Count; i++)
        {
            ConsumableItem potion = grouped[i].Key; // capture the object, never the index (CLAUDE.md §3)
            int count = grouped[i].Value;

            var rowGO = new GameObject($"Row_{potion.displayName}", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(Button));
            rowGO.transform.SetParent(chooserRoot, false);
            var rowRect = rowGO.GetComponent<RectTransform>();
            rowRect.anchorMin = new Vector2(0f, 1f);
            rowRect.anchorMax = new Vector2(1f, 1f);
            rowRect.pivot = new Vector2(0.5f, 1f);
            rowRect.offsetMin = new Vector2(4f, 0f);
            rowRect.offsetMax = new Vector2(-4f, 0f);
            rowRect.anchoredPosition = new Vector2(0f, -i * RowHeight - 4f);
            rowRect.sizeDelta = new Vector2(0f, RowHeight - 4f);
            rowGO.GetComponent<Image>().color = new Color(1f, 1f, 1f, 0.08f);
            rowGO.GetComponent<Button>().onClick.AddListener(() => UsePotion(potion));

            var rowIconGO = new GameObject("Icon", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            rowIconGO.transform.SetParent(rowGO.transform, false);
            var rowIconRect = rowIconGO.GetComponent<RectTransform>();
            rowIconRect.anchorMin = new Vector2(0f, 0.5f);
            rowIconRect.anchorMax = new Vector2(0f, 0.5f);
            rowIconRect.pivot = new Vector2(0f, 0.5f);
            rowIconRect.anchoredPosition = new Vector2(4f, 0f);
            rowIconRect.sizeDelta = new Vector2(24f, 24f);
            var rowIcon = rowIconGO.GetComponent<Image>();
            rowIcon.sprite = potion.icon;
            rowIcon.enabled = potion.icon != null;
            rowIcon.preserveAspect = true;
            rowIcon.raycastTarget = false;

            Text rowLabel = CreateLabel("Label", rowGO.transform, TextAnchor.MiddleLeft, 14);
            rowLabel.rectTransform.offsetMin = new Vector2(32f, 2f);
            int healPercent = Mathf.RoundToInt(potion.healPercent * 100f);
            rowLabel.text = $"{potion.displayName}  +{healPercent}%  x{count}";

            chooserRows.Add(rowGO);
        }

        chooserRoot.sizeDelta = new Vector2(ChooserWidth, grouped.Count * RowHeight + 4f);
    }

    private void UsePotion(ConsumableItem potion)
    {
        if (bag == null || playerEntity == null) return;

        bag.TryUse(potion, playerEntity, CurrentFloor);
        // Closed regardless of the outcome: on a refusal (cooldown, already at
        // full HP) leaving the panel open reads as an unresponsive button, and
        // the collapsed button already shows why via its cooldown label.
        chooserRoot.gameObject.SetActive(false);
        Refresh();
    }
}
