using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One enemy's icon + name + HP bar inside a multi-enemy battle screen
/// (ROADMAP.md -> "Weapon-driven attack patterns" - Axe cleave/Spear pierce can
/// pull more than one enemy into an encounter). Builds its own child hierarchy
/// in Awake, same idiom as ItemSlotUI, so BattleScreenUI can pool these at
/// runtime with no prefab (mirrors EquipmentPanelUI.bagSlotPool).
///
/// Owns its own OnHealthChanged subscription across however many turns the
/// fight lasts - Bind/Unbind, not a stateless per-refresh rebind like a bag
/// square, since a battle can run many turns before the encounter ends.
/// </summary>
[RequireComponent(typeof(RectTransform))]
public class BattleEnemyPanelUI : MonoBehaviour
{
    [SerializeField] private Color aliveFillColor = new Color(0.8f, 0.2f, 0.2f);
    [SerializeField] private Color deadTint = new Color(0.4f, 0.4f, 0.4f, 0.6f);

    private Image iconImage;
    private Text nameText;
    private Slider hpSlider;
    private Image hpFillImage;
    private CanvasGroup canvasGroup;

    private EnemyController boundEnemy;
    private SpriteRenderer boundRenderer;

    private void Awake()
    {
        BuildHierarchy();
    }

    private void BuildHierarchy()
    {
        if (iconImage != null) return; // already built

        canvasGroup = gameObject.GetComponent<CanvasGroup>();
        if (canvasGroup == null) canvasGroup = gameObject.AddComponent<CanvasGroup>();

        var iconGO = new GameObject("Icon", typeof(RectTransform), typeof(Image));
        iconGO.transform.SetParent(transform, false);
        iconImage = iconGO.GetComponent<Image>();
        iconImage.preserveAspect = true;
        SetAnchors(iconGO.GetComponent<RectTransform>(), 0f, 0.35f, 1f, 1f);

        var nameGO = new GameObject("Name", typeof(RectTransform), typeof(Text));
        nameGO.transform.SetParent(transform, false);
        nameText = nameGO.GetComponent<Text>();
        nameText.font = UIFonts.Default;
        nameText.alignment = TextAnchor.MiddleCenter;
        nameText.fontSize = 14;
        nameText.color = Color.white;
        SetAnchors(nameGO.GetComponent<RectTransform>(), 0f, 0.2f, 1f, 0.35f);

        var sliderGO = new GameObject("HpBar", typeof(RectTransform), typeof(Slider));
        sliderGO.transform.SetParent(transform, false);
        SetAnchors(sliderGO.GetComponent<RectTransform>(), 0f, 0f, 1f, 0.2f);
        hpSlider = sliderGO.GetComponent<Slider>();
        hpSlider.interactable = false;
        hpSlider.transition = Selectable.Transition.None;
        hpSlider.direction = Slider.Direction.LeftToRight;
        hpSlider.minValue = 0f;

        var bgGO = new GameObject("Background", typeof(RectTransform), typeof(Image));
        bgGO.transform.SetParent(sliderGO.transform, false);
        SetStretch(bgGO.GetComponent<RectTransform>());
        bgGO.GetComponent<Image>().color = new Color(0f, 0f, 0f, 0.6f);

        var fillAreaGO = new GameObject("Fill Area", typeof(RectTransform));
        fillAreaGO.transform.SetParent(sliderGO.transform, false);
        SetStretch((RectTransform)fillAreaGO.transform);

        var fillGO = new GameObject("Fill", typeof(RectTransform), typeof(Image));
        fillGO.transform.SetParent(fillAreaGO.transform, false);
        SetStretch(fillGO.GetComponent<RectTransform>());
        hpFillImage = fillGO.GetComponent<Image>();
        hpFillImage.color = aliveFillColor;

        hpSlider.fillRect = fillGO.GetComponent<RectTransform>();

        iconImage.enabled = false;
    }

    private static void SetAnchors(RectTransform rect, float minX, float minY, float maxX, float maxY)
    {
        rect.anchorMin = new Vector2(minX, minY);
        rect.anchorMax = new Vector2(maxX, maxY);
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    private static void SetStretch(RectTransform rect)
    {
        rect.anchorMin = Vector2.zero;
        rect.anchorMax = Vector2.one;
        rect.offsetMin = Vector2.zero;
        rect.offsetMax = Vector2.zero;
    }

    public void Bind(EnemyController enemy)
    {
        BuildHierarchy(); // safety if Bind is somehow called before Awake

        Unbind();
        boundEnemy = enemy;
        if (enemy == null) return;

        boundRenderer = enemy.GetComponent<SpriteRenderer>();
        iconImage.sprite = boundRenderer != null ? boundRenderer.sprite : null;
        iconImage.enabled = iconImage.sprite != null;

        // Instantiate() appends "(Clone)" - same trim BattleScreenUI's old single-enemy path used.
        nameText.text = enemy.name.Replace("(Clone)", string.Empty).Trim();

        canvasGroup.alpha = 1f;
        hpFillImage.color = aliveFillColor;

        enemy.OnHealthChanged += HandleHealthChanged;
        HandleHealthChanged(enemy, enemy.CurrentHealth, enemy.MaxHealth);
    }

    public void Unbind()
    {
        if (boundEnemy != null) boundEnemy.OnHealthChanged -= HandleHealthChanged;
        boundEnemy = null;
        boundRenderer = null;
    }

    /// <summary>Re-reads the bound enemy's current body sprite. Bind only snapshots it
    /// once, so the hurt/attack frames DirectionalSpriteAnimator drives on the world
    /// SpriteRenderer never reached this portrait - it sat frozen for the whole fight.
    /// Driven by BattleScreenUI's LateUpdate rather than a per-frame event, since the
    /// animator has no per-frame callback and polling one cached reference is cheaper
    /// than adding one.</summary>
    public void RefreshIcon()
    {
        if (boundRenderer == null || iconImage == null) return;
        if (ReferenceEquals(iconImage.sprite, boundRenderer.sprite)) return;

        iconImage.sprite = boundRenderer.sprite;
        iconImage.enabled = iconImage.sprite != null;
    }

    /// <summary>Dims the panel when its enemy dies - stays visible (not hidden) so the
    /// player can see who they killed mid-fight, rather than the roster silently shrinking.</summary>
    public void MarkDead()
    {
        canvasGroup.alpha = 0.5f;
        if (hpFillImage != null) hpFillImage.color = deadTint;
    }

    private void HandleHealthChanged(Entity entity, int current, int max)
    {
        if (hpSlider == null) return;
        hpSlider.maxValue = max;
        hpSlider.value = current;
    }

    private void OnDestroy()
    {
        Unbind();
    }
}
