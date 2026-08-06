using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

/// <summary>
/// Bit Heroes-style battle screen: getting within engage range of an enemy
/// on the dungeon grid (not just bumping directly into it - either side can
/// close the distance) no longer resolves combat inline. Instead it freezes
/// the dungeon (player movement, every other enemy's AI) and hands control
/// to a dedicated turn-based fight against whichever enemies got pulled in,
/// shown via BattleScreenUI. When the fight ends, the dungeon resumes and (on a
/// win) the rest of the floor gets to react, same as any other player action.
///
/// Multi-enemy support (ROADMAP.md -> "Weapon-driven attack patterns", Axe
/// cleave/Spear pierce) generalizes the original strict 1v1 design: an
/// encounter can hold 1-N enemies, decided by AttackPatternResolver at the
/// moment the player initiates the attack (PlayerController.TryAct). Proximity-
/// based engagement (TryEngageIfInRange - enemy walks up to the player, or the
/// player walks into an enemy with no special pattern) always starts a 1-enemy
/// encounter, same as before this generalization; only PlayerController's new
/// weapon-pattern-driven attacks ever start a >1 encounter.
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
    [Tooltip("Delay before the enemy round resolves, so the player can see their own hit land first.")]
    [SerializeField] private float enemyTurnDelaySeconds = 0.6f;
    [Tooltip("How long a proximity-triggered encounter's enemy visibly closes the last bit of distance before the battle screen opens. Purely cosmetic - only used by TryEngageIfInRange, not weapon-pattern attacks (EngagePlayerAttack), where the player's own swing/shot already sold the approach.")]
    [SerializeField] private float approachDurationSeconds = 0.35f;
    [Tooltip("Chebyshev-distance range at which getting close to an enemy opens the battle screen - diagonals count as 1 away, same as the 4 cardinal neighbors. 1 = the full 8-cell ring around the player (old bump-to-attack threshold, now including diagonals); raise it to have monsters \"notice\" you from further away. Only used by proximity-based engagement (TryEngageIfInRange) - weapon-pattern-driven attacks (EngagePlayerAttack) start unconditionally, since PlayerController already proved adjacency/line-of-fire itself.")]
    [Min(1)] [SerializeField] private int engageRange = 1;

    public bool IsActive { get; private set; }
    public int EngageRange => engageRange;

    private PlayerController player;
    // Every enemy in the current encounter, with the damage multiplier the
    // player's attack pattern assigned it (Axe cleave's secondary targets get
    // AttackPatternResolver.AxeCleaveSecondaryMultiplier; everything else is 1).
    // Enemy-initiated attacks always ignore this and hit the player at full
    // damage - only Entity.Attack calls FROM the player use it.
    private readonly List<(EnemyController Enemy, float DamageMultiplier)> engaged = new List<(EnemyController, float)>();
    private PlayerProgression progression;
    private bool startedOnPlayerTurn;

    // Set the instant every engaged enemy is dead or the player dies - distinct
    // from IsActive, which now stays true through the victory/level-up outro
    // screens (so the floor stays frozen), while this stops turn logic
    // (OnPlayerAttackPressed/RunEnemyRound) immediately so nothing keeps
    // swinging once the encounter is decided.
    private bool fightOver;

    // Accumulated for the victory screen - reset in StartBattle, read in
    // HandleVictory before anything gets torn down. Summed across every
    // enemy in the encounter, not just one (bug fix: xpEarned/goldEarned used
    // to overwrite per-kill via `=`, which only mattered once fights could
    // kill more than one enemy).
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
    /// Starts a 1-enemy battle if player and enemy are already within
    /// engageRange - call this after either one moves (PlayerController.TryAct's
    /// undirected-move fallback, EnemyController.TakeTurn). No-op if a battle's
    /// already active, either is dead, or they're too far apart. Returns whether
    /// a battle started. Signature/behavior unchanged from the pre-multi-enemy
    /// version - always exactly one enemy, still range-gated.
    /// </summary>
    public bool TryEngageIfInRange(PlayerController player, EnemyController enemy)
    {
        if (IsActive || player == null || enemy == null || player.IsDead || enemy.IsDead) return false;
        // Chebyshev, not Manhattan: a diagonal neighbor should count as "1 away" too,
        // not 2 - engageRange == 1 means the full 8-cell ring around the player.
        if (!GridUtils.WithinChebyshevRange(player.Cell, enemy.Cell, engageRange)) return false;

        StartBattle(player, new List<(EnemyController, float)> { (enemy, 1f) }, playApproach: true);
        return true;
    }

    /// <summary>
    /// Starts a battle against exactly the given targets, unconditionally - no
    /// engageRange check, since PlayerController.TryAct already proved
    /// adjacency (melee weapons) or a clear line of fire (Bow/Staff) before
    /// calling this. This is what every weapon-initiated player attack goes
    /// through (ROADMAP.md -> "Weapon-driven attack patterns") - single target
    /// for most weapons, multiple for Axe cleave/Spear pierce. Dead/null
    /// targets are filtered out; no-ops if nothing valid remains.
    /// </summary>
    public bool EngagePlayerAttack(PlayerController player, IReadOnlyList<(EnemyController Enemy, float DamageMultiplier)> targets)
    {
        if (IsActive || player == null || player.IsDead || targets == null) return false;

        var valid = targets.Where(t => t.Enemy != null && !t.Enemy.IsDead).ToList();
        if (valid.Count == 0) return false;

        StartBattle(player, valid, playApproach: false);
        return true;
    }

    private void StartBattle(PlayerController player, IReadOnlyList<(EnemyController Enemy, float DamageMultiplier)> targets, bool playApproach)
    {
        if (IsActive || player == null || targets == null || targets.Count == 0) return;

        // No screen means no way to input an attack; staying out of battle lets the
        // callers' inline-attack fallback keep the game playable.
        if (screenUI == null) return;

        this.player = player;
        engaged.Clear();
        engaged.AddRange(targets);
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

        player.OnDeath += HandlePlayerDeath;
        player.OnAttackResolved += HandlePlayerAttackResolved;
        foreach (var (enemy, _) in engaged)
        {
            enemy.OnDeath += HandleEngagedEnemyDeath;
            enemy.OnAttackResolved += HandleEnemyAttackResolved;
            enemy.OnXPGranted += HandleXPGranted;
            enemy.OnGoldGranted += HandleGoldGranted;
            enemy.OnLootDropped += HandleLootDropped;
        }

        progression = player.GetComponent<PlayerProgression>();
        if (progression != null) progression.OnLevelUp += HandleLevelUp;

        // IsActive is already true at this point, which freezes player input and
        // every enemy's TakeTurn (EnemyController.cs, PlayerController.cs) - so
        // the approach coroutine, if any, plays over an already-frozen dungeon,
        // not a race against anything still moving.
        if (playApproach)
        {
            StartCoroutine(PlayApproachThenOpenScreen());
        }
        else
        {
            OpenBattleScreen();
        }
    }

    /// <summary>Slides every engaged enemy 60% of the way toward the player's visual
    /// position, purely cosmetic - never touches Cell/DungeonGrid, so grid occupancy
    /// and pathing are untouched throughout. Positions are snapped back to the real
    /// cell the instant it ends, right as the battle screen covers the map, so the
    /// reset isn't visible.</summary>
    private IEnumerator PlayApproachThenOpenScreen()
    {
        var starts = new Vector3[engaged.Count];
        var ends = new Vector3[engaged.Count];
        for (int i = 0; i < engaged.Count; i++)
        {
            EnemyController enemy = engaged[i].Enemy;
            if (enemy == null) continue;
            starts[i] = enemy.transform.position;
            ends[i] = Vector3.Lerp(starts[i], Entity.VisualPosition(player.Cell), 0.6f);
            enemy.Face(player.Cell - enemy.Cell);
        }

        float t = 0f;
        while (t < approachDurationSeconds)
        {
            t += Time.deltaTime;
            float frac = t / approachDurationSeconds;
            for (int i = 0; i < engaged.Count; i++)
            {
                EnemyController enemy = engaged[i].Enemy;
                if (enemy == null) continue;
                enemy.transform.position = Vector3.Lerp(starts[i], ends[i], frac);
            }
            yield return null;
        }

        foreach (var (enemy, _) in engaged)
        {
            if (enemy != null) enemy.transform.position = Entity.VisualPosition(enemy.Cell);
        }

        OpenBattleScreen();
    }

    private void OpenBattleScreen()
    {
        screenUI?.Show(player, engaged.Select(e => e.Enemy).ToList(), OnPlayerAttackPressed);

        // Bit Heroes ties initiative to Agility (its turn-rate formula factors in
        // Power+Agility for how *often* you act; we're a simple back-and-forth
        // rather than a continuous tick engine, so this is the faithful
        // simplification: whoever's faster acts first, then it alternates as
        // normal - see IMPLEMENTED.md -> "Battle screen (encounter flow)").
        // Generalized to N enemies by comparing against the fastest one - a
        // ponytail: simplification, since only relative-to-player ordering is
        // ever shown (each engaged enemy still just gets its own turn in the
        // enemy round, not a fully interleaved N-way initiative queue).
        float fastestEnemyAgility = engaged.Max(e => e.Enemy.Stats.agility);
        bool playerActsFirst = player.Stats.agility >= fastestEnemyAgility;
        screenUI?.SetTurn(playerActsFirst);

        if (playerActsFirst)
        {
            screenUI?.SetInputEnabled(true);
        }
        else
        {
            StartCoroutine(RunEnemyRound(isOpeningMove: true));
        }
    }

    private void OnPlayerAttackPressed()
    {
        if (!IsActive || fightOver) return;

        screenUI?.SetTurn(isPlayerTurn: false);

        // Snapshot before attacking: Entity.Die() fires OnDeath synchronously,
        // and HandleEngagedEnemyDeath mutates `engaged` - iterating the live
        // list while it's being trimmed mid-loop is exactly the hazard
        // TurnManager already snapshots its own enemy list to avoid (CLAUDE.md
        // §3, "Destruction is deferred to end of frame").
        var targets = engaged.ToList();
        foreach (var (enemy, multiplier) in targets)
        {
            // A cleave that kills every remaining engaged enemy flips fightOver mid-loop
            // (HandleEngagedEnemyDeath sets it the instant `engaged` empties) - stop
            // swinging at that point instead of attacking already-torn-down state.
            if (fightOver) break;
            if (enemy == null || enemy.IsDead) continue;
            player.Attack(enemy, multiplier);
        }

        if (fightOver) return;

        StartCoroutine(RunEnemyRound(isOpeningMove: false));
    }

    private IEnumerator RunEnemyRound(bool isOpeningMove)
    {
        screenUI?.SetInputEnabled(false);
        yield return new WaitForSeconds(enemyTurnDelaySeconds);

        if (!fightOver)
        {
            var attackers = engaged.Select(e => e.Enemy).ToList(); // same snapshot reasoning as OnPlayerAttackPressed
            foreach (var enemy in attackers)
            {
                if (fightOver) break;
                if (enemy == null || enemy.IsDead) continue;
                enemy.Attack(player);
            }
        }

        if (!fightOver)
        {
            screenUI?.SetTurn(isPlayerTurn: true);
            screenUI?.SetInputEnabled(true);
        }
    }

    private void HandlePlayerAttackResolved(Entity attacker, Entity defender, CombatResult result) => playerDamageDealt += result.Damage;
    private void HandleEnemyAttackResolved(Entity attacker, Entity defender, CombatResult result) => playerDamageTaken += result.Damage;
    private void HandleXPGranted(int amount) => xpEarned += amount;
    private void HandleGoldGranted(int amount) => goldEarned += amount;

    private void HandleLootDropped(EquippableItem item)
    {
        if (item != null) itemsDropped.Add(item);
    }

    private void HandleLevelUp(int newLevel) => leveledUpToLevel = newLevel;

    /// <summary>One engaged enemy died. Removes it from the encounter and only
    /// ends the fight once every engaged enemy is gone - a non-last death must
    /// NOT stop coroutines or set fightOver, since the enemy round (or the rest
    /// of a cleave's target loop) still needs to run for the survivors.</summary>
    private void HandleEngagedEnemyDeath(Entity deadEnemy)
    {
        if (fightOver) return;

        var match = engaged.FirstOrDefault(e => (Entity)e.Enemy == deadEnemy);
        if (match.Enemy != null) screenUI?.NotifyEnemyDefeated(match.Enemy);

        engaged.RemoveAll(e => (Entity)e.Enemy == deadEnemy);

        if (engaged.Count > 0) return;

        fightOver = true;
        StopAllCoroutines();
        HandleVictory();
    }

    private void HandlePlayerDeath(Entity deadPlayer)
    {
        if (fightOver) return;
        fightOver = true;
        StopAllCoroutines();

        IsActive = false;
        UnsubscribeBattleEvents();

        screenUI?.Hide();

        player = null;
        engaged.Clear();
        progression = null;
    }

    /// <summary>
    /// Every engaged enemy died: show the victory screen (and, if this fight
    /// leveled the player up, the level-up screen after it) before the floor is
    /// allowed to resume. IsActive deliberately stays true through both screens -
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
        engaged.Clear();
        progression = null;

        // Let the rest of the floor react now that the fight's over - same as
        // any other player action ending its turn. Skipped when an enemy started
        // the fight during its own phase, since that phase will end on its own.
        if (startedOnPlayerTurn && TurnManager.Instance != null)
        {
            TurnManager.Instance.EndPlayerTurn();
        }
    }

    private void UnsubscribeBattleEvents()
    {
        if (player != null)
        {
            player.OnDeath -= HandlePlayerDeath;
            player.OnAttackResolved -= HandlePlayerAttackResolved;
        }
        foreach (var (enemy, _) in engaged)
        {
            if (enemy == null) continue;
            enemy.OnDeath -= HandleEngagedEnemyDeath;
            enemy.OnAttackResolved -= HandleEnemyAttackResolved;
            enemy.OnXPGranted -= HandleXPGranted;
            enemy.OnGoldGranted -= HandleGoldGranted;
            enemy.OnLootDropped -= HandleLootDropped;
        }
        if (progression != null) progression.OnLevelUp -= HandleLevelUp;
    }
}
