using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

public enum TurnState
{
    PlayerTurn,
    EnemyTurn
}

/// <summary>
/// Drives the turn-based loop: the player acts once, then every registered
/// enemy takes a turn in sequence, then control returns to the player.
/// Singleton so PlayerController/EnemyController can call TurnManager.Instance
/// without a scene reference.
/// </summary>
public class TurnManager : MonoBehaviour
{
    public static TurnManager Instance { get; private set; }

    [Header("Enemy phase pacing")]
    [Tooltip("Preferred gap between each enemy acting. Compressed automatically so the phase never exceeds the budget below.")]
    [SerializeField] private float staggerBetweenEnemies = 0.05f;

    [Tooltip("Hard cap on how long the whole enemy phase may take to play out, regardless of enemy count.")]
    [SerializeField] private float enemyPhaseBudgetSeconds = 0.3f;

    [Tooltip("Extra time after the last enemy acts so its move/attack animation can finish.")]
    [SerializeField] private float animationTailSeconds = 0.12f;

    public TurnState State { get; private set; } = TurnState.PlayerTurn;
    public event Action<TurnState> OnTurnStateChanged;

    private readonly List<EnemyController> enemies = new List<EnemyController>();

    /// <summary>Currently registered (alive) enemies - used for battle engage-range proximity checks, see BattleManager.</summary>
    public IReadOnlyList<EnemyController> Enemies => enemies;

    // Reused snapshot buffer so each enemy phase doesn't allocate a new list.
    private readonly List<EnemyController> turnOrderBuffer = new List<EnemyController>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    public void RegisterEnemy(EnemyController enemy)
    {
        if (!enemies.Contains(enemy)) enemies.Add(enemy);
    }

    public void UnregisterEnemy(EnemyController enemy)
    {
        enemies.Remove(enemy);
    }

    /// <summary>Call this after the player has completed one action (move or attack).</summary>
    public void EndPlayerTurn()
    {
        if (State != TurnState.PlayerTurn) return;
        SetState(TurnState.EnemyTurn);
        StartCoroutine(RunEnemyTurns());
    }

    private IEnumerator RunEnemyTurns()
    {
        // Snapshot so an enemy dying mid-loop (removed from the list) doesn't break iteration.
        turnOrderBuffer.Clear();
        turnOrderBuffer.AddRange(enemies);

        // A fixed per-enemy delay makes the phase scale linearly with enemy count -
        // unplayable once deeper floors spawn dozens. Instead the stagger is squeezed
        // to fit a fixed budget, so the phase costs roughly the same with 3 enemies
        // or 40. Turn *logic* still resolves strictly in order, so there's no risk of
        // two enemies claiming the same cell; only the visuals overlap.
        int count = turnOrderBuffer.Count;
        float stagger = count > 1
            ? Mathf.Min(staggerBetweenEnemies, enemyPhaseBudgetSeconds / count)
            : 0f;

        // Yielding once per enemy costs a full frame even when the requested stagger
        // is shorter than one, so a per-enemy yield keeps the phase linear in enemy
        // count no matter how small the stagger gets - defeating the budget above.
        // Instead accumulate the intended delay and only give up a frame once a
        // frame's worth has built up, letting several enemies resolve per frame.
        float pending = 0f;

        foreach (var enemy in turnOrderBuffer)
        {
            if (enemy == null) continue; // may have died from a previous enemy's action, etc.
            enemy.TakeTurn();

            if (stagger <= 0f) continue;

            pending += stagger;
            if (pending < Time.deltaTime) continue;

            pending = 0f;
            yield return null;
        }

        // Let the final enemy's slide/swing finish before returning control.
        if (animationTailSeconds > 0f) yield return new WaitForSeconds(animationTailSeconds);

        SetState(TurnState.PlayerTurn);
    }

    private void SetState(TurnState newState)
    {
        State = newState;
        OnTurnStateChanged?.Invoke(newState);
    }
}
