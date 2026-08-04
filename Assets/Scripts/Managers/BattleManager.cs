using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Bit Heroes-style battle screen: getting within engage range of an enemy
/// on the dungeon grid (not just bumping directly into it - either side can
/// close the distance) no longer resolves combat inline. Instead it freezes
/// the dungeon (player movement, every other enemy's AI) and hands control
/// to a dedicated 1v1 turn-based fight against just that enemy, shown via
/// BattleScreenUI. When the fight ends, the dungeon resumes and (on a win)
/// the rest of the floor gets to react, same as any other player action.
///
/// Reuses Entity.Attack/CombatResolver/damage numbers/hit flash as-is - the
/// battle screen is a different *place* combat happens, not a different
/// combat system.
/// </summary>
public readonly struct BattleResultSummary
{
    public readonly int DamageDealt;
    public readonly int DamageTaken;
    public readonly int XPEarned;
    public readonly int GoldEarned;
    public readonly IReadOnlyList<EquippableItem> ItemsDropped;

    public BattleResultSummary(int damageDealt, int damageTaken, int xpEarned, int goldEarned, IReadOnlyList<EquippableItem> itemsDropped)
    {
        DamageDealt = damageDealt;
        DamageTaken = damageTaken;
        XPEarned = xpEarned;
        GoldEarned = goldEarned;
        ItemsDropped = itemsDropped;
    }
}

public class BattleManager : MonoBehaviour
{
    public static BattleManager Instance { get; private set; }

    [SerializeField] private BattleScreenUI screenUI;
    [Tooltip("Delay before the enemy's turn resolves, so the player can see their own hit land first.")]
    [SerializeField] private float enemyTurnDelaySeconds = 0.6f;
    [Tooltip("Manhattan-distance range at which getting close to an enemy opens the battle screen. 1 = adjacent (old bump-to-attack threshold); raise it to have monsters \"notice\" you from further away.")]
    [Min(1)] [SerializeField] private int engageRange = 1;

    public bool IsActive { get; private set; }
    public int EngageRange => engageRange;

    private PlayerController player;
    private EnemyController enemy;
    private PlayerProgression progression;
    private bool startedOnPlayerTurn;

    // Set the instant either side dies - distinct from IsActive, which now
    // stays true through the victory/level-up outro screens (so the floor
    // stays frozen), while this stops turn logic (OnPlayerAttackPressed/
    // RunEnemyTurn) immediately so nothing keeps swinging at a dead entity.
    private bool fightOver;

    // Accumulated for the victory screen - reset in StartBattle, read in
    // HandleVictory before anything gets torn down.
    private int playerDamageDealt;
    private int playerDamageTaken;
    private int xpEarned;
    private int goldEarned;
    private readonly List<EquippableItem> itemsDropped = new List<EquippableItem>();
    private int? leveledUpToLevel;

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;

        // Without a screen there is no Attack control, so a battle could be entered
        // but never acted on - IsActive would stay true forever and freeze both
        // player input and every enemy. Fail loudly instead of soft-locking.
        if (screenUI == null)
        {
            Debug.LogError("BattleManager: screenUI is unassigned. Battles are disabled; combat falls back to inline grid attacks.");
        }
    }

    /// <summary>
    /// Starts a battle if player and enemy are already within engageRange -
    /// call this after either one moves (PlayerController.TryAct,
    /// EnemyController.TakeTurn). No-op if a battle's already active, either
    /// is dead, or they're too far apart. Returns whether a battle started.
    /// </summary>
    public bool TryEngageIfInRange(PlayerController player, EnemyController enemy)
    {
        if (IsActive || player == null || enemy == null || player.IsDead || enemy.IsDead) return false;
        if (!GridUtils.WithinRange(player.Cell, enemy.Cell, engageRange)) return false;

        StartBattle(player, enemy);
        return true;
    }

    /// <summary>Unconditionally starts a battle. Prefer TryEngageIfInRange unless you already know they're in range.</summary>
    public void StartBattle(PlayerController player, EnemyController enemy)
    {
        if (IsActive || player == null || enemy == null) return;

        // No screen means no way to input an attack; staying out of battle lets the
        // callers' inline-attack fallback keep the game playable.
        if (screenUI == null) return;

        this.player = player;
        this.enemy = enemy;
        IsActive = true;
        fightOver = false;

        playerDamageDealt = 0;
        playerDamageTaken = 0;
        xpEarned = 0;
        goldEarned = 0;
        itemsDropped.Clear();
        leveledUpToLevel = null;

        // An enemy can start a battle during the enemy phase. Ending the *player's*
        // turn afterwards would then run a second enemy phase, so remember whose
        // turn this fight interrupted.
        startedOnPlayerTurn = TurnManager.Instance == null
            || TurnManager.Instance.State == TurnState.PlayerTurn;

        player.OnDeath += HandleAnyDeath;
        enemy.OnDeath += HandleAnyDeath;
        player.OnAttackResolved += HandlePlayerAttackResolved;
        enemy.OnAttackResolved += HandleEnemyAttackResolved;
        enemy.OnXPGranted += HandleXPGranted;
        enemy.OnGoldGranted += HandleGoldGranted;
        enemy.OnLootDropped += HandleLootDropped;

        progression = player.GetComponent<PlayerProgression>();
        if (progression != null) progression.OnLevelUp += HandleLevelUp;

        screenUI?.Show(player, enemy, OnPlayerAttackPressed);

        // Bit Heroes ties initiative to Agility (its turn-rate formula factors in
        // Power+Agility for how *often* you act; we're a simple 1v1 back-and-forth
        // rather than a continuous tick engine, so this is the faithful
        // simplification: whoever's faster acts first, then it alternates as
        // normal - see IMPLEMENTED.md -> "Battle screen (encounter flow)").
        bool playerActsFirst = player.Stats.agility >= enemy.Stats.agility;
        screenUI?.SetTurn(playerActsFirst);

        if (playerActsFirst)
        {
            screenUI?.SetInputEnabled(true);
        }
        else
        {
            StartCoroutine(RunEnemyTurn(isOpeningMove: true));
        }
    }

    private void OnPlayerAttackPressed()
    {
        if (!IsActive || fightOver) return;

        screenUI?.SetTurn(isPlayerTurn: false);
        player.Attack(enemy);

        // A death triggers HandleAnyDeath synchronously (Entity.OnDeath fires
        // before Attack returns), so fightOver is already true here if so.
        if (fightOver) return;

        StartCoroutine(RunEnemyTurn(isOpeningMove: false));
    }

    private IEnumerator RunEnemyTurn(bool isOpeningMove)
    {
        screenUI?.SetInputEnabled(false);
        yield return new WaitForSeconds(enemyTurnDelaySeconds);

        if (!fightOver)
        {
            enemy.Attack(player);
        }

        if (!fightOver)
        {
            screenUI?.SetTurn(isPlayerTurn: true);
            screenUI?.SetInputEnabled(true);
        }
    }

    private void HandlePlayerAttackResolved(Entity attacker, Entity defender, CombatResult result) => playerDamageDealt += result.Damage;
    private void HandleEnemyAttackResolved(Entity attacker, Entity defender, CombatResult result) => playerDamageTaken += result.Damage;
    private void HandleXPGranted(int amount) => xpEarned = amount;
    private void HandleGoldGranted(int amount) => goldEarned = amount;

    private void HandleLootDropped(ItemPickup pickup)
    {
        if (pickup?.PendingItem != null) itemsDropped.Add(pickup.PendingItem);
    }

    private void HandleLevelUp(int newLevel) => leveledUpToLevel = newLevel;

    private void HandleAnyDeath(Entity deadEntity)
    {
        if (fightOver) return;
        fightOver = true;
        StopAllCoroutines();

        if (deadEntity == enemy) HandleVictory();
        else HandlePlayerDeath();
    }

    /// <summary>
    /// Enemy died: show the victory screen (and, if this kill leveled the
    /// player up, the level-up screen after it) before the floor is allowed
    /// to resume. IsActive deliberately stays true through both screens -
    /// GameManager's floor-advance wait and the player/enemy turn-skip
    /// checks all key off it, so the dungeon stays frozen exactly as long as
    /// an outro screen is on top of it.
    /// </summary>
    private void HandleVictory()
    {
        screenUI?.Hide();

        var summary = new BattleResultSummary(playerDamageDealt, playerDamageTaken, xpEarned, goldEarned, itemsDropped);
        VictoryScreenUI.Instance.Show(summary, AfterVictoryContinue);
    }

    private void AfterVictoryContinue()
    {
        if (leveledUpToLevel.HasValue)
        {
            LevelUpUI.Instance.Show(leveledUpToLevel.Value, FinishVictorySequence);
        }
        else
        {
            FinishVictorySequence();
        }
    }

    private void FinishVictorySequence()
    {
        IsActive = false;
        UnsubscribeBattleEvents();

        player = null;
        enemy = null;
        progression = null;

        // Let the rest of the floor react now that the fight's over - same as
        // any other player action ending its turn. Skipped when an enemy started
        // the fight during its own phase, since that phase will end on its own.
        if (startedOnPlayerTurn && TurnManager.Instance != null)
        {
            TurnManager.Instance.EndPlayerTurn();
        }
    }

    /// <summary>Player died: no outro screen - GameOverUI takes over independently
    /// via the player's own OnDeath, so this only needs to tear the battle down.</summary>
    private void HandlePlayerDeath()
    {
        IsActive = false;
        UnsubscribeBattleEvents();

        screenUI?.Hide();

        player = null;
        enemy = null;
        progression = null;
    }

    private void UnsubscribeBattleEvents()
    {
        if (player != null)
        {
            player.OnDeath -= HandleAnyDeath;
            player.OnAttackResolved -= HandlePlayerAttackResolved;
        }
        if (enemy != null)
        {
            enemy.OnDeath -= HandleAnyDeath;
            enemy.OnAttackResolved -= HandleEnemyAttackResolved;
            enemy.OnXPGranted -= HandleXPGranted;
            enemy.OnGoldGranted -= HandleGoldGranted;
            enemy.OnLootDropped -= HandleLootDropped;
        }
        if (progression != null) progression.OnLevelUp -= HandleLevelUp;
    }
}
