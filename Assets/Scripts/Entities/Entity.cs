using System;
using UnityEngine;

/// <summary>
/// Base class for anything that lives on the dungeon grid and can take damage:
/// the player and enemies both derive from this. Handles grid occupancy,
/// health, and a simple slide-into-place move animation.
/// </summary>
public class Entity : MonoBehaviour
{
    [Header("Stats")]
    [SerializeField] protected Stats baseStats = Stats.Level1Default();
    [SerializeField] protected float moveAnimSeconds = 0.12f;

    [Header("Hit feedback")]
    // SpriteRenderer.color is a multiplicative tint, so it can only darken/
    // recolor a sprite, never brighten it to true white - flashing to a
    // strong red reads clearly as "just hit" without needing a custom
    // shader. A literal white flash (COMBAT_DESIGN.md §6) would need an
    // additive-blend material as a later upgrade.
    [SerializeField] private Color hitFlashColor = new Color(1f, 0.35f, 0.35f);
    [SerializeField] private float hitFlashSeconds = 0.08f;

    public int CurrentHealth { get; protected set; }

    /// <summary>baseStats plus every equipped item's bonusStats (COMBAT_DESIGN.md §2),
    /// if this Entity has an Equipment component. Computed fresh each access, not cached.</summary>
    public Stats Stats => equipment != null ? baseStats + equipment.TotalBonusStats : baseStats;
    public int MaxHealth => Stats.maxHp;

    /// <summary>Weapon family currently in MainHand — drives CombatResolver's
    /// scaling/defense stat pick and Parry eligibility (§1, §4). Unarmed
    /// (None) if there's no Equipment component or MainHand is empty.</summary>
    public virtual WeaponType EquippedWeaponType => equipment != null ? equipment.EquippedWeaponType : WeaponType.None;

    /// <summary>Flat weapon damage from the equipped MainHand item (§2). 0 if unarmed.</summary>
    public virtual int WeaponDamage => equipment != null ? equipment.WeaponDamage : 0;

    public Vector2Int Cell { get; protected set; }
    public bool IsDead { get; protected set; }

    public event Action<Entity> OnDeath;
    public event Action<Entity, int, int> OnHealthChanged; // entity, current, max
    public event Action<Entity, Entity, CombatResult> OnAttackResolved; // attacker, defender, result

    private SpriteRenderer spriteRenderer;
    private DirectionalSpriteAnimator directionalAnimator;
    private Equipment equipment;
    private Coroutine slideRoutine;
    private Coroutine hitFlashRoutine;

    protected virtual void Awake()
    {
        spriteRenderer = GetComponent<SpriteRenderer>();
        directionalAnimator = GetComponent<DirectionalSpriteAnimator>();
        equipment = GetComponent<Equipment>();
        CurrentHealth = Stats.maxHp;
    }

    /// <summary>Place this entity on the grid for the first time (no animation).</summary>
    public virtual void SpawnAt(Vector2Int cell)
    {
        Cell = cell;
        transform.position = VisualPosition(cell);
        DungeonGrid.SetOccupant(cell, this);
    }

    /// <summary>
    /// Cell-to-world offset by half a unit on X: entity sprites use a bottom-center
    /// pivot (so SpriteRenderer.flipX mirrors them in place instead of jumping a
    /// full cell when facing changes), while the grid/tilemap are corner-anchored.
    /// </summary>
    private static Vector3 VisualPosition(Vector2Int cell)
    {
        return GridUtils.CellToWorld(cell) + new Vector3(0.5f, 0f, 0f);
    }

    /// <summary>
    /// Buffs this entity's stats (deeper-floor scaling, COMBAT_DESIGN.md §2c).
    /// Call right after Instantiate: Awake has already set CurrentHealth from
    /// the prefab's baseStats, so it's refilled here to the new max.
    /// </summary>
    public void ApplyStatBonus(Stats bonus)
    {
        baseStats += bonus;
        CurrentHealth = Stats.maxHp;
        OnHealthChanged?.Invoke(this, CurrentHealth, Stats.maxHp);
    }

    /// <summary>
    /// Called by Equipment after any Equip/Unequip so CurrentHealth stays valid
    /// against the new total max (COMBAT_DESIGN.md §2). Only clamps down if max
    /// HP decreased (e.g. unequipping a +HP item); doesn't top up CurrentHealth
    /// if max HP increased, so gear-swapping can't be used to free-heal.
    /// </summary>
    public void RefreshEquipmentStats()
    {
        int newMax = Stats.maxHp;
        if (CurrentHealth > newMax) CurrentHealth = newMax;
        OnHealthChanged?.Invoke(this, CurrentHealth, newMax);
    }

    /// <summary>
    /// Sets CurrentHealth to the current max. For spawn-time setup only (starting
    /// gear can raise maxHp after Awake already read baseStats) - deliberately not
    /// called on in-run gear swaps, which must not become a free heal.
    /// </summary>
    public void RefillHealth()
    {
        CurrentHealth = Stats.maxHp;
        OnHealthChanged?.Invoke(this, CurrentHealth, Stats.maxHp);
    }

    /// <summary>Turn to face a direction without moving (e.g. when attacking an adjacent cell).</summary>
    public void Face(Vector2Int direction)
    {
        if (direction == Vector2Int.zero) return;

        if (directionalAnimator != null)
        {
            directionalAnimator.SetFacing(direction);
        }
        else if (direction.x != 0 && spriteRenderer != null)
        {
            spriteRenderer.flipX = direction.x < 0;
        }
    }

    /// <summary>Attempt to move to an adjacent walkable, unoccupied cell.</summary>
    public virtual void MoveTo(Vector2Int newCell)
    {
        if (!DungeonGrid.CanMoveTo(newCell)) return;

        Face(newCell - Cell);

        DungeonGrid.ClearOccupant(Cell);
        Cell = newCell;
        DungeonGrid.SetOccupant(Cell, this);

        // Only cancel the in-flight slide, not every coroutine this entity owns.
        if (slideRoutine != null) StopCoroutine(slideRoutine);
        slideRoutine = StartCoroutine(SlideTo(VisualPosition(newCell)));
    }

    private System.Collections.IEnumerator SlideTo(Vector3 target)
    {
        Vector3 start = transform.position;
        float t = 0f;
        while (t < moveAnimSeconds)
        {
            t += Time.deltaTime;
            transform.position = Vector3.Lerp(start, target, t / moveAnimSeconds);
            yield return null;
        }
        transform.position = target;
        slideRoutine = null;
    }

    public virtual void Attack(Entity target)
    {
        Face(target.Cell - Cell); // swing toward the target, not wherever we last walked
        if (directionalAnimator != null) directionalAnimator.PlayAttack();

        CombatResult result = CombatResolver.Resolve(this, target);
        OnAttackResolved?.Invoke(this, target, result);
        DamageNumberSpawner.Instance.ShowAttackResult(target, result);
        // COMBAT_DESIGN.md §6: crits get a small screen shake. Called directly for
        // the same reason as DamageNumberSpawner above - only one listener today.
        if (result.WasCrit) CameraShake.Shake(0.05f, 0.12f);

        if (result.WasDodged) return; // no damage, no hurt animation, no life steal

        target.TakeDamage(result.Damage);
        if (result.LifeStolen > 0) Heal(result.LifeStolen);
    }

    public virtual void TakeDamage(int amount)
    {
        if (IsDead) return;

        CurrentHealth = Mathf.Max(0, CurrentHealth - amount);
        OnHealthChanged?.Invoke(this, CurrentHealth, Stats.maxHp);

        if (amount > 0) PlayHitFlash();

        if (CurrentHealth == 0)
        {
            Die();
        }
        else if (directionalAnimator != null)
        {
            directionalAnimator.PlayHurt();
        }
    }

    public virtual void Heal(int amount)
    {
        if (IsDead || amount <= 0) return;

        CurrentHealth = Mathf.Min(Stats.maxHp, CurrentHealth + amount);
        OnHealthChanged?.Invoke(this, CurrentHealth, Stats.maxHp);
        DamageNumberSpawner.Instance.ShowHeal(this, amount);
    }

    private void PlayHitFlash()
    {
        if (spriteRenderer == null) return;
        if (hitFlashRoutine != null) StopCoroutine(hitFlashRoutine);
        hitFlashRoutine = StartCoroutine(HitFlashRoutine());
    }

    private System.Collections.IEnumerator HitFlashRoutine()
    {
        Color original = spriteRenderer.color;
        spriteRenderer.color = hitFlashColor;
        yield return new WaitForSeconds(hitFlashSeconds);
        spriteRenderer.color = original;
        hitFlashRoutine = null;
    }

    protected virtual void Die()
    {
        IsDead = true;
        DungeonGrid.ClearOccupant(Cell);
        OnDeath?.Invoke(this);
        Destroy(gameObject);
    }

    /// <summary>
    /// Release the grid cell even when destroyed without dying (floor teardown).
    /// A cell left pointing at a destroyed entity reads as occupied to Pathfinder
    /// but null to PlayerController.TryAct, which then burns the player's turn on
    /// a move that CanMoveTo silently rejects.
    /// </summary>
    protected virtual void OnDestroy()
    {
        if (DungeonGrid.GetOccupant(Cell) == this) DungeonGrid.ClearOccupant(Cell);
    }
}
