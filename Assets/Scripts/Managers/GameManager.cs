using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;

/// <summary>
/// Entry point for a dungeon run: generates the layout, spawns the player
/// in the first room, spawns one enemy per remaining room. Once every
/// spawned enemy is dead, generates the next floor and moves the (still
/// alive) player into it. Put this on an empty GameObject alongside a
/// TurnManager.
/// </summary>
public class GameManager : MonoBehaviour
{
    public static GameManager Instance { get; private set; }

    [SerializeField] private DungeonGenerator dungeonGenerator;
    [SerializeField] private PlayerController playerPrefab;
    [SerializeField] private EnemyController enemyPrefab;
    [SerializeField] private HealthPotionPickup itemPrefab;
    [SerializeField] [Range(0f, 1f)] private float itemSpawnChance = 0.5f;
    [SerializeField] private EquippableItem startingWeapon;

    [Header("Floor scaling (all values are per-floor growth; floor 1 = prefab defaults)")]
    [Tooltip("Layer 0 (.claude/LAYERS.md) is flat - \"no depth scaling of any kind\" across its first N floors, room/enemy count included. Every field below this one is pinned to its floor-1 value through here; scaling (the old additive formula, offset to start from floor Layer0FloorCount+1 instead of floor 2) only resumes past it, as a placeholder until Layer 1's real exponential curve is designed (LAYERS.md -> \"Open / undecided\" leaves hard-jump-vs-continuous at that boundary unresolved - this picks continuous, not a settled design).")]
    [SerializeField] private int layer0FloorCount = 10;
    [Tooltip("Extra rooms are added every N floors.")]
    [SerializeField] private int floorsPerExtraRoom = 2;
    [SerializeField] private int maxRoomCount = 14;

    [Tooltip("An extra enemy per room is added every N floors.")]
    [SerializeField] private int floorsPerExtraEnemy = 4;
    [SerializeField] private int maxEnemiesPerRoom = 3;

    [Tooltip("Bonus enemy max health per floor.")]
    [SerializeField] private int enemyHealthPerFloor = 2;

    [Tooltip("Bonus enemy damage, applied every 2 floors to keep it gentler than health.")]
    [SerializeField] private int enemyDamagePerFloor = 1;

    [Tooltip("Bonus gold per enemy kill, added per floor past the first (ROADMAP.md -> \"Currency: gold & gems\").")]
    [SerializeField] private int goldBonusPerFloor = 1;

    public int CurrentFloor { get; private set; } = 1;
    public event Action<int> OnFloorChanged;

    /// <summary>The live player, or null before the first floor is generated.</summary>
    public PlayerController Player => player;

    /// <summary>
    /// Fires once when the player is first spawned. UI binds through this rather
    /// than FindObjectOfType in Start(), since script execution order between
    /// this manager and UI Start() methods isn't guaranteed.
    /// </summary>
    public event Action<PlayerController> OnPlayerSpawned;

    private PlayerController player;
    private readonly List<EnemyController> spawnedEnemies = new List<EnemyController>();
    private readonly List<HealthPotionPickup> spawnedItems = new List<HealthPotionPickup>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    private void Start()
    {
        GenerateFloor();
    }

    private void GenerateFloor()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogError("GameManager: assign a DungeonGenerator in the inspector.");
            return;
        }

        DespawnPreviousFloor();

        dungeonGenerator.SetRoomCount(RoomCountForFloor());
        var rooms = dungeonGenerator.Generate();
        if (rooms.Count == 0)
        {
            Debug.LogError("GameManager: dungeon generation produced no rooms. Check DungeonGenerator settings.");
            return;
        }

        SpawnOrMovePlayer();
        SpawnEnemies(rooms.Count);
        SpawnItems(rooms.Count);

        OnFloorChanged?.Invoke(CurrentFloor);
    }

    /// <summary>
    /// Destroys anything left over from the last floor. DungeonGrid.Reset() (inside
    /// Generate) only clears the registries - without this the actual GameObjects
    /// survive as uncollectable ghosts on the new floor.
    /// </summary>
    private void DespawnPreviousFloor()
    {
        foreach (HealthPotionPickup item in spawnedItems)
        {
            if (item != null) Destroy(item.gameObject);
        }
        spawnedItems.Clear();

        foreach (EnemyController enemy in spawnedEnemies)
        {
            if (enemy != null) Destroy(enemy.gameObject);
        }
        spawnedEnemies.Clear();
    }

    private void SpawnOrMovePlayer()
    {
        if (playerPrefab == null) return;

        Vector2Int spawnCell = dungeonGenerator.RoomCenter(0);

        if (player == null)
        {
            player = Instantiate(playerPrefab);
            if (startingWeapon != null)
            {
                if (player.TryGetComponent(out Equipment equipment))
                {
                    // Routed through ItemGenerator, not equipped raw - the template's
                    // own hand-authored bonusStats is dead data once anything reads
                    // stats through the generator (IMPLEMENTED.md -> "Item
                    // generation"); equipping it directly here would have quietly
                    // left the starting weapon far weaker than every generated drop.
                    EquippableItem starting = ItemGenerator.Generate(startingWeapon, 0, startingWeapon.rarity);
                    equipment.Equip(starting.slot, starting);
                    // Starting gear can raise maxHp, but Awake already set
                    // CurrentHealth from baseStats alone and RefreshEquipmentStats
                    // only ever clamps downward - without this the run begins
                    // permanently short of full HP.
                    player.RefillHealth();
                }
                else
                {
                    Debug.LogWarning("GameManager: player prefab has no Equipment component; startingWeapon ignored.");
                }
            }
            OnPlayerSpawned?.Invoke(player);
        }
        player.SpawnAt(spawnCell);
    }

    /// <summary>Floor step used by every depth-scaling formula below - pinned to 0
    /// for Layer 0's floors so nothing scales there (LAYERS.md -> "Layer 0", "no
    /// depth scaling of any kind"), then resumes counting from floor
    /// layer0FloorCount+1 for whatever comes after (see the field's own tooltip).</summary>
    private int LayerAwareFloorStep() => Mathf.Max(0, CurrentFloor - 1 - layer0FloorCount);

    /// <summary>Rooms grow with depth, capped so generation stays reasonable.</summary>
    private int RoomCountForFloor()
    {
        int floorStep = LayerAwareFloorStep();
        int extraRooms = floorsPerExtraRoom > 0 ? floorStep / floorsPerExtraRoom : 0;
        return Mathf.Min(dungeonGenerator.BaseRoomCount + extraRooms, maxRoomCount);
    }

    private void SpawnEnemies(int roomCount)
    {
        if (enemyPrefab == null) return;

        int floorStep = LayerAwareFloorStep();
        int enemiesPerRoom = Mathf.Min(
            1 + (floorsPerExtraEnemy > 0 ? floorStep / floorsPerExtraEnemy : 0),
            maxEnemiesPerRoom);
        var floorBonus = new Stats
        {
            maxHp = floorStep * enemyHealthPerFloor,
            attack = (floorStep / 2) * enemyDamagePerFloor,
        };
        int goldFloorBonus = floorStep * goldBonusPerFloor;

        // Rooms after the player's starting room get enemies; deeper floors pack in more.
        for (int roomIndex = 1; roomIndex < roomCount; roomIndex++)
        {
            for (int i = 0; i < enemiesPerRoom; i++)
            {
                Vector2Int spawnCell = dungeonGenerator.RandomCellInRoom(roomIndex);
                if (DungeonGrid.IsOccupied(spawnCell)) continue;

                EnemyController enemy = Instantiate(enemyPrefab);
                enemy.ApplyStatBonus(floorBonus);
                enemy.SetGoldFloorBonus(goldFloorBonus);
                enemy.SpawnAt(spawnCell);
                enemy.OnDeath += HandleEnemyDeath;
                spawnedEnemies.Add(enemy);
            }
        }

        // The floor only ever advances from HandleEnemyDeath, so a floor with no
        // enemies is an unwinnable dead end (reachable with roomCount 1, or if
        // every candidate cell came back occupied). Force one so the run continues.
        if (spawnedEnemies.Count == 0 && roomCount > 0)
        {
            Vector2Int fallbackCell = dungeonGenerator.RoomCenter(roomCount - 1);
            if (!DungeonGrid.IsOccupied(fallbackCell))
            {
                EnemyController enemy = Instantiate(enemyPrefab);
                enemy.ApplyStatBonus(floorBonus);
                enemy.SetGoldFloorBonus(goldFloorBonus);
                enemy.SpawnAt(fallbackCell);
                enemy.OnDeath += HandleEnemyDeath;
                spawnedEnemies.Add(enemy);
                Debug.LogWarning($"GameManager: floor {CurrentFloor} spawned no enemies normally; placed one fallback enemy so the floor is completable.");
            }
            else
            {
                Debug.LogError($"GameManager: floor {CurrentFloor} has no enemies and no free fallback cell - the run cannot advance.");
            }
        }
    }

    private void SpawnItems(int roomCount)
    {
        if (itemPrefab == null) return;

        for (int roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            if (UnityEngine.Random.value > itemSpawnChance) continue;

            Vector2Int spawnCell = dungeonGenerator.RandomCellInRoom(roomIndex);
            if (DungeonGrid.IsOccupied(spawnCell)) continue;

            HealthPotionPickup item = Instantiate(itemPrefab);
            item.transform.position = GridUtils.CellToWorld(spawnCell);
            spawnedItems.Add(item);
        }
    }

    private void HandleEnemyDeath(Entity deadEnemy)
    {
        // Entity.Die() sets IsDead before firing OnDeath, so the dying
        // enemy already reads as dead here even though it isn't destroyed yet.
        foreach (EnemyController enemy in spawnedEnemies)
        {
            if (enemy != null && !enemy.IsDead) return; // someone's still alive
        }

        StartCoroutine(AdvanceFloorWhenSafe());
    }

    /// <summary>
    /// The killing blow can land inside BattleManager's own attack call, while the
    /// battle screen is still up and BattleManager still holds references to
    /// entities on this floor. Regenerating right there would teleport the player
    /// out of a fight they haven't visibly won and hand TurnManager a stale enemy
    /// list (this manager is subscribed to OnDeath before BattleManager, so its
    /// handler runs first). Waiting for the battle to close keeps the floor
    /// transition where it belongs: after the fight resolves.
    /// </summary>
    private IEnumerator AdvanceFloorWhenSafe()
    {
        // Always give up at least one frame first. A coroutine body runs
        // synchronously up to its first yield, so without this the whole floor
        // teardown would execute inside Entity.Die() - while that entity's other
        // OnDeath subscribers (BattleManager among them, since this manager
        // subscribes first) still haven't been called.
        yield return null;

        while (BattleManager.Instance != null && BattleManager.Instance.IsActive)
        {
            yield return null;
        }

        bool continuePressed = false;
        FloorCompleteUI.Instance.Show(CurrentFloor, () => continuePressed = true);
        yield return new WaitUntil(() => continuePressed);

        CurrentFloor++;
        GenerateFloor();
    }
}
