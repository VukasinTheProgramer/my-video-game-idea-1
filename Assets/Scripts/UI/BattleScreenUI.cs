using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen battle overlay: covers the dungeon view and shows the player
/// plus every enemy in the current encounter, with HP bars and an Attack
/// button/keypress (IMPLEMENTED.md -> "Battle screen (encounter flow)" -
/// Bit Heroes-style battle screen, driven by BattleManager).
///
/// Multi-enemy support (ROADMAP.md -> "Weapon-driven attack patterns", Axe
/// cleave/Spear pierce): enemy portraits are a runtime-pooled row of
/// BattleEnemyPanelUI, mirroring EquipmentPanelUI.bagSlotPool/
/// EnsureBagPoolSize exactly - most fights are still 1 enemy, this just no
/// longer assumes it.
///
/// v1 simplification: portraits are a snapshot of each Entity's current
/// SpriteRenderer.sprite, not dedicated battle art/animation - there's no
/// second camera or battle-specific art yet. Good enough to make the
/// screen-transition feel real; swap in real portraits later without
/// touching BattleManager at all.
/// </summary>
public class BattleScreenUI : MonoBehaviour
{
    [SerializeField] private GameObject root;
    [SerializeField] private Image playerIcon;
    [SerializeField] private Slider playerHealthBar;
    [Tooltip("Where pooled BattleEnemyPanelUI instances are parented - an empty RectTransform with a GridLayoutGroup or HorizontalLayoutGroup, sized to fit multiple enemies side by side.")]
    [SerializeField] private RectTransform enemyPanelContainer;
    [SerializeField] private Vector2 enemyPanelSize = new Vector2(120f, 140f);
    [Tooltip("Top-of-screen \"Your Turn\" / \"Enemy Turn\" label. Place this above the portraits in the Canvas; the attack/ability button(s) below stay at the bottom (IMPLEMENTED.md -> \"Battle screen (encounter flow)\" layout).")]
    [SerializeField] private Text turnIndicatorText;
    [SerializeField] private Button attackButton;

    [Header("Combat feedback (IMPLEMENTED.md -> \"Combat feedback\") - UI-space so it renders on top of this full-screen overlay; DamageNumberSpawner's world-space text is hidden behind it")]
    [SerializeField] private float feedbackFloatDistance = 40f;
    [SerializeField] private float feedbackLifetimeSeconds = 0.7f;
    [SerializeField] private int feedbackFontSize = 20;
    [SerializeField] private int feedbackBigFontSize = 28;

    private readonly List<BattleEnemyPanelUI> enemyPanelPool = new List<BattleEnemyPanelUI>();
    private readonly Dictionary<EnemyController, BattleEnemyPanelUI> activePanels = new Dictionary<EnemyController, BattleEnemyPanelUI>();

    private Entity player;
    private SpriteRenderer playerRenderer;
    private Action onAttackPressed;
    private bool inputEnabled;

    private void Awake()
    {
        if (root != null) root.SetActive(false);
        if (attackButton != null) attackButton.onClick.AddListener(HandleAttackInput);
    }

    private void Update()
    {
        if (!inputEnabled) return;
        if (Input.GetKeyDown(KeyCode.Space) || Input.GetKeyDown(KeyCode.Return))
        {
            HandleAttackInput();
        }
    }

    /// <summary>Keeps every portrait on the current body frame while the screen is open.
    /// Show/Bind only snapshot the sprite once, so hurt and attack animations played
    /// well after the fight started never showed here (the doc comment above called
    /// the snapshot a v1 simplification - this is the cheap half of undoing it, no
    /// battle-specific art required). LateUpdate so it runs after the animator's
    /// coroutines have set this frame's sprite.</summary>
    private void LateUpdate()
    {
        if (root == null || !root.activeSelf) return;

        if (playerIcon != null && playerRenderer != null
            && !ReferenceEquals(playerIcon.sprite, playerRenderer.sprite))
        {
            playerIcon.sprite = playerRenderer.sprite;
            playerIcon.enabled = playerIcon.sprite != null;
        }

        foreach (BattleEnemyPanelUI panel in activePanels.Values)
        {
            panel.RefreshIcon();
        }
    }

    public void Show(PlayerController player, IReadOnlyList<EnemyController> enemies, Action onAttackPressed)
    {
        this.player = player;
        this.onAttackPressed = onAttackPressed;

        player.OnHealthChanged += HandlePlayerHealthChanged;

        // Space/Return is also the EventSystem's Submit binding, so a Button that's
        // still selected from an earlier mouse click would re-fire every time the
        // player presses Space to attack (e.g. re-opening the equipment panel behind
        // the battle screen). Clearing the selection makes Space mean only "attack".
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        playerRenderer = player.GetComponent<SpriteRenderer>();
        SetIcon(playerIcon, player);
        HandlePlayerHealthChanged(player, player.CurrentHealth, player.MaxHealth);

        BindEnemyPanels(enemies);

        SetInputEnabled(true);
        if (root != null) root.SetActive(true);
    }

    private void BindEnemyPanels(IReadOnlyList<EnemyController> enemies)
    {
        activePanels.Clear();
        EnsureEnemyPoolSize(enemies.Count);

        for (int i = 0; i < enemyPanelPool.Count; i++)
        {
            if (i < enemies.Count)
            {
                EnemyController enemy = enemies[i];
                enemyPanelPool[i].gameObject.SetActive(true);
                enemyPanelPool[i].Bind(enemy);
                activePanels[enemy] = enemyPanelPool[i];
            }
            else
            {
                enemyPanelPool[i].Unbind();
                enemyPanelPool[i].gameObject.SetActive(false);
            }
        }
    }

    /// <summary>Grows the pooled enemy-panel list to at least count, reusing existing
    /// panels on later fights instead of recreating them (EquipmentPanelUI.
    /// EnsureBagPoolSize's exact pattern).</summary>
    private void EnsureEnemyPoolSize(int count)
    {
        if (enemyPanelContainer == null) return;

        while (enemyPanelPool.Count < count)
        {
            var go = new GameObject($"EnemyPanel_{enemyPanelPool.Count}", typeof(RectTransform));
            go.transform.SetParent(enemyPanelContainer, false);
            go.GetComponent<RectTransform>().sizeDelta = enemyPanelSize;

            enemyPanelPool.Add(go.AddComponent<BattleEnemyPanelUI>());
        }
    }

    /// <summary>Called by BattleManager the instant one engaged enemy dies (not the
    /// whole encounter) - dims that enemy's panel immediately so a cleave/pierce kill
    /// is visible mid-fight instead of only reflecting at the final victory teardown.</summary>
    public void NotifyEnemyDefeated(EnemyController enemy)
    {
        if (activePanels.TryGetValue(enemy, out BattleEnemyPanelUI panel)) panel.MarkDead();
    }

    public void Hide()
    {
        if (player != null) player.OnHealthChanged -= HandlePlayerHealthChanged;
        player = null;
        playerRenderer = null;
        onAttackPressed = null;

        // A number still mid-float when the fight ends would otherwise freeze
        // here (Update doesn't run on components under an inactive GameObject)
        // and resume the instant this same pooled panel/icon reactivates for the
        // NEXT battle - showing up as a phantom hit before anyone's swung.
        ClearFeedback(playerIcon != null ? playerIcon.transform : null);

        foreach (var panel in enemyPanelPool)
        {
            panel.Unbind();
            panel.gameObject.SetActive(false);
            ClearFeedback(panel.transform);
        }
        activePanels.Clear();

        SetInputEnabled(false);
        if (root != null) root.SetActive(false);
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (attackButton != null) attackButton.interactable = enabled;
    }

    /// <summary>Updates the top-of-screen "whose turn" label. Initiative is decided by
    /// Agility once, at the start of the battle (BattleManager.StartBattle, generalized
    /// to "vs the fastest engaged enemy" for multi-enemy fights); this just
    /// reflects whose turn is currently active as it alternates from there.</summary>
    public void SetTurn(bool isPlayerTurn)
    {
        if (turnIndicatorText == null) return;
        turnIndicatorText.text = isPlayerTurn ? "Your Turn" : "Enemy Turn";
    }

    private void HandleAttackInput()
    {
        if (!inputEnabled) return;
        onAttackPressed?.Invoke();
    }

    private void HandlePlayerHealthChanged(Entity entity, int current, int max)
    {
        if (playerHealthBar == null) return;
        playerHealthBar.maxValue = max;
        playerHealthBar.value = current;
    }

    /// <summary>Shows one attack's outcome (damage/crit/miss/parry) over whichever
    /// portrait actually took the hit - BattleManager calls this from its existing
    /// OnAttackResolved subscriptions (both player-attacks-enemy and
    /// enemy-attacks-player), passing the defender straight through. No-ops if the
    /// target isn't currently shown on screen (e.g. it died and its panel was
    /// already unbound).</summary>
    public void ShowCombatFeedback(Entity target, CombatResult result)
    {
        RectTransform parent = null;
        if (target == player && playerIcon != null)
        {
            parent = playerIcon.rectTransform;
        }
        else if (target is EnemyController enemy && activePanels.TryGetValue(enemy, out BattleEnemyPanelUI panel))
        {
            parent = (RectTransform)panel.transform;
        }

        if (parent == null) return;

        var (text, color, big) = CombatFeedbackText.For(result);
        SpawnFeedback(parent, text, color, big);
    }

    private void SpawnFeedback(RectTransform parent, string text, Color color, bool big)
    {
        var go = new GameObject("CombatFeedback", typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);

        var rect = (RectTransform)go.transform;
        rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
        rect.anchoredPosition = Vector2.zero;
        rect.sizeDelta = new Vector2(140f, 40f);

        Text label = go.GetComponent<Text>();
        label.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        label.alignment = TextAnchor.MiddleCenter;
        label.text = text;
        label.color = color;
        label.fontSize = big ? feedbackBigFontSize : feedbackFontSize;
        label.fontStyle = big ? FontStyle.Bold : FontStyle.Normal;

        BattleFloatingTextMotion motion = go.AddComponent<BattleFloatingTextMotion>();
        motion.Init(feedbackFloatDistance, feedbackLifetimeSeconds);
    }

    private static void ClearFeedback(Transform parent)
    {
        if (parent == null) return;
        for (int i = parent.childCount - 1; i >= 0; i--)
        {
            Transform child = parent.GetChild(i);
            if (child.GetComponent<BattleFloatingTextMotion>() != null) Destroy(child.gameObject);
        }
    }

    private static void SetIcon(Image image, Entity entity)
    {
        if (image == null) return;
        SpriteRenderer renderer = entity.GetComponent<SpriteRenderer>();
        image.sprite = renderer != null ? renderer.sprite : null;
        image.enabled = image.sprite != null;
    }
}
