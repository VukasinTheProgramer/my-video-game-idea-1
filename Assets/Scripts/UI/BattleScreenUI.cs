using System;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

/// <summary>
/// Full-screen battle overlay: covers the dungeon view and shows just the
/// player and the one enemy they engaged, with HP bars and an Attack
/// button/keypress (IMPLEMENTED.md -> "Battle screen (encounter flow)" -
/// Bit Heroes-style battle screen, driven by BattleManager).
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
    [SerializeField] private Image enemyIcon;
    [SerializeField] private Slider playerHealthBar;
    [SerializeField] private Slider enemyHealthBar;
    [SerializeField] private Text enemyNameText;
    [Tooltip("Top-of-screen \"Your Turn\" / \"Enemy Turn\" label. Place this above the portraits in the Canvas; the attack/ability button(s) below stay at the bottom (IMPLEMENTED.md -> \"Battle screen (encounter flow)\" layout).")]
    [SerializeField] private Text turnIndicatorText;
    [SerializeField] private Button attackButton;

    private Entity player;
    private Entity enemy;
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

    public void Show(PlayerController player, EnemyController enemy, Action onAttackPressed)
    {
        this.player = player;
        this.enemy = enemy;
        this.onAttackPressed = onAttackPressed;

        player.OnHealthChanged += HandlePlayerHealthChanged;
        enemy.OnHealthChanged += HandleEnemyHealthChanged;

        // Space/Return is also the EventSystem's Submit binding, so a Button that's
        // still selected from an earlier mouse click would re-fire every time the
        // player presses Space to attack (e.g. re-opening the equipment panel behind
        // the battle screen). Clearing the selection makes Space mean only "attack".
        if (EventSystem.current != null) EventSystem.current.SetSelectedGameObject(null);

        SetIcon(playerIcon, player);
        SetIcon(enemyIcon, enemy);
        // Instantiate() appends "(Clone)", which would otherwise show up as the
        // battle header on every single fight.
        if (enemyNameText != null) enemyNameText.text = enemy.name.Replace("(Clone)", string.Empty).Trim();

        HandlePlayerHealthChanged(player, player.CurrentHealth, player.MaxHealth);
        HandleEnemyHealthChanged(enemy, enemy.CurrentHealth, enemy.MaxHealth);

        SetInputEnabled(true);
        if (root != null) root.SetActive(true);
    }

    public void Hide()
    {
        if (player != null) player.OnHealthChanged -= HandlePlayerHealthChanged;
        if (enemy != null) enemy.OnHealthChanged -= HandleEnemyHealthChanged;
        player = null;
        enemy = null;
        onAttackPressed = null;

        SetInputEnabled(false);
        if (root != null) root.SetActive(false);
    }

    public void SetInputEnabled(bool enabled)
    {
        inputEnabled = enabled;
        if (attackButton != null) attackButton.interactable = enabled;
    }

    /// <summary>Updates the top-of-screen "whose turn" label. Initiative is decided by
    /// Agility once, at the start of the battle (BattleManager.StartBattle); this just
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

    private void HandleEnemyHealthChanged(Entity entity, int current, int max)
    {
        if (enemyHealthBar == null) return;
        enemyHealthBar.maxValue = max;
        enemyHealthBar.value = current;
    }

    private static void SetIcon(Image image, Entity entity)
    {
        if (image == null) return;
        SpriteRenderer renderer = entity.GetComponent<SpriteRenderer>();
        image.sprite = renderer != null ? renderer.sprite : null;
        image.enabled = image.sprite != null;
    }
}
