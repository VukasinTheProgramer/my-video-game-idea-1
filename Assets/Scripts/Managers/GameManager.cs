using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

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
    [SerializeField] private EquippableItem startingWeapon;
    [Tooltip("Flavour starting gear equipped as-authored - NOT routed through ItemGenerator (unlike startingWeapon above), so each one's bonusStats/weaponType stays exactly what's on the asset instead of being rerolled from that slot's stat budget. Use for zero-stat cosmetic pieces or a deliberately fixed-damage starter weapon.")]
    [SerializeField] private EquippableItem[] startingGear;
    [SerializeField] private SignPost signPrefab;
    [Tooltip("Two chest sizes, same LootChest script, different footprint data (see LootChest.Footprint). ChooseChestPrefab rolls which one spawns.")]
    [FormerlySerializedAs("chestPrefab")]
    [SerializeField] private LootChest smallChestPrefab;
    [SerializeField] private LootChest largeChestPrefab;
    [Tooltip("Third chest variant, floor 3's guaranteed test fixture only - not part of the normal random SpawnChests roll (see SpawnFloorTestFixture).")]
    [SerializeField] private LootChest specialChestPrefab;

    // Every per-floor tuning knob lives here instead of as fields on this class,
    // per CLAUDE.md's rule 4 ("Data that designers tune = ScriptableObject.
    // Logic = component or static resolver. Never both in one class.") - this
    // class stayed the orchestrator, the numbers moved to FloorScalingConfig.
    [SerializeField] private FloorScalingConfig scaling;

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
    private readonly List<SignPost> spawnedSigns = new List<SignPost>();
    private readonly List<LootChest> spawnedChests = new List<LootChest>();

    private void Awake()
    {
        if (Instance != null && Instance != this)
        {
            Destroy(gameObject);
            return;
        }
        Instance = this;
    }

    /// <summary>Called by MainMenuUI's Start button - the dungeon doesn't exist
    /// until this runs, not just visually hidden behind a menu.</summary>
    public void BeginRun()
    {
        GenerateFloor();

        // Shown once, here, rather than gated some other way - BeginRun itself only
        // ever runs once per session (MainMenuUI's Start button calls it exactly
        // once), so no extra "have I shown this" flag is needed. Direct UI call from
        // GameManager, not an event - same already-established precedent as
        // FloorCompleteUI/VictoryScreenUI being called directly from GameManager/
        // BattleManager, not the CombatFeedback-listener pattern CLAUDE.md rule 3
        // reserves for combat-only feedback.
        MessagePopupUI.Instance.Show(
            "Welcome",
            "Welcome, adventurer. You've found yourself in quite a bit of trouble, " +
            "but don't worry - we'll guide you through it.");
    }

    private void GenerateFloor()
    {
        if (dungeonGenerator == null)
        {
            Debug.LogError("GameManager: assign a DungeonGenerator in the inspector.");
            return;
        }
        if (scaling == null)
        {
            Debug.LogError("GameManager: assign a FloorScalingConfig in the inspector.");
            return;
        }

        DespawnPreviousFloor();

        // Same flatness boundary LayerAwareFloorStep already uses for stat scaling
        // (floorStep == 0), so the tutorial layout and "everything is level 1" cover
        // exactly the same floors - one shared check, not two that could drift apart.
        List<RectInt> rooms;
        if (LayerAwareFloorStep() == 0)
        {
            rooms = dungeonGenerator.GenerateLinear(scaling.layer0RoomCount, scaling.layer0RoomSize, scaling.layer0CorridorLength, scaling.layer0CorridorRow);
        }
        else
        {
            dungeonGenerator.SetRoomCount(RoomCountForFloor());
            rooms = dungeonGenerator.Generate();
        }

        if (rooms.Count == 0)
        {
            Debug.LogError("GameManager: dungeon generation produced no rooms. Check DungeonGenerator settings.");
            return;
        }

        SpawnOrMovePlayer();
        SpawnEnemies(rooms.Count);
        SpawnBoss(rooms.Count);
        SpawnItems(rooms.Count);
        SpawnChests(rooms.Count);
        SpawnTutorialSign();
        SpawnFloorTestFixtures(rooms.Count);

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

        foreach (SignPost sign in spawnedSigns)
        {
            if (sign != null) Destroy(sign.gameObject);
        }
        spawnedSigns.Clear();

        foreach (LootChest chest in spawnedChests)
        {
            if (chest != null) Destroy(chest.gameObject);
        }
        spawnedChests.Clear();
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

            if (startingGear != null && startingGear.Length > 0)
            {
                if (player.TryGetComponent(out Equipment gearEquipment))
                {
                    foreach (EquippableItem template in startingGear)
                    {
                        if (template == null) continue;
                        // Instantiate, not ItemGenerator.Generate: these are flavour
                        // pieces (a plain shirt, a 1-attack starter weapon) meant to
                        // keep their authored stats exactly, not get rerolled from
                        // the slot's stat budget like a real drop would.
                        EquippableItem gear = Instantiate(template);
                        gearEquipment.Equip(gear.slot, gear);
                    }
                    player.RefillHealth();
                }
                else
                {
                    Debug.LogWarning("GameManager: player prefab has no Equipment component; startingGear ignored.");
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
    private int LayerAwareFloorStep() => Mathf.Max(0, CurrentFloor - 1 - scaling.layer0FloorCount);

    /// <summary>Rooms grow with depth, capped so generation stays reasonable.</summary>
    private int RoomCountForFloor()
    {
        int floorStep = LayerAwareFloorStep();
        int extraRooms = scaling.floorsPerExtraRoom > 0 ? floorStep / scaling.floorsPerExtraRoom : 0;
        return Mathf.Min(dungeonGenerator.BaseRoomCount + extraRooms, scaling.maxRoomCount);
    }

    private void SpawnEnemies(int roomCount)
    {
        if (enemyPrefab == null) return;

        int floorStep = LayerAwareFloorStep();
        int enemiesPerRoom = Mathf.Min(
            1 + (scaling.floorsPerExtraEnemy > 0 ? floorStep / scaling.floorsPerExtraEnemy : 0),
            scaling.maxEnemiesPerRoom);
        var floorBonus = new Stats
        {
            maxHp = floorStep * scaling.enemyHealthPerFloor,
            attack = (floorStep / 2) * scaling.enemyDamagePerFloor,
        };
        int goldFloorBonus = floorStep * scaling.goldBonusPerFloor;
        int xpFloorBonus = floorStep * scaling.xpBonusPerFloor;

        // Rooms after the player's starting room get enemies; deeper floors pack in more.
        for (int roomIndex = 1; roomIndex < roomCount; roomIndex++)
        {
            for (int i = 0; i < enemiesPerRoom; i++)
            {
                Vector2Int spawnCell = dungeonGenerator.RandomCellInRoom(roomIndex);
                if (DungeonGrid.IsOccupied(spawnCell)) continue;

                EnemyController enemy = Instantiate(enemyPrefab);
                enemy.ApplyStatBonus(floorBonus);
                ApplyEarlyFloorHealthScaling(enemy);
                ApplyEarlyFloorAttackScaling(enemy);
                enemy.SetGoldFloorBonus(goldFloorBonus);
                enemy.SetXpFloorBonus(xpFloorBonus);
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
                ApplyEarlyFloorHealthScaling(enemy);
                ApplyEarlyFloorAttackScaling(enemy);
                enemy.SetGoldFloorBonus(goldFloorBonus);
                enemy.SetXpFloorBonus(xpFloorBonus);
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

    /// <summary>Floors 1-5 only: scales an already-spawned enemy's maxHp up
    /// exponentially by floor (FloorScalingConfig.earlyFloorHealthGrowthRate^(floor-1)),
    /// on top of whatever floorBonus already applied - a deliberate felt-difficulty
    /// ramp for the tutorial floors, requested on top of Layer 0's normal flat
    /// scaling (CLAUDE.md rule 5: surfaced here, not silently folded into
    /// LayerAwareFloorStep's existing "no scaling" formula).</summary>
    private void ApplyEarlyFloorHealthScaling(EnemyController enemy)
    {
        if (CurrentFloor < 1 || CurrentFloor > 5) return;

        float multiplier = Mathf.Pow(scaling.earlyFloorHealthGrowthRate, CurrentFloor - 1);
        if (multiplier <= 1f) return;

        int extraHp = Mathf.RoundToInt(enemy.Stats.maxHp * (multiplier - 1f));
        enemy.ApplyStatBonus(new Stats { maxHp = extraHp });
    }

    /// <summary>Floors 1-5 only: same exponential-ramp mechanism as
    /// ApplyEarlyFloorHealthScaling, applied to attack instead of maxHp -
    /// requested as a follow-up 2026-08-06 so damage keeps pace with the HP
    /// curve instead of staying flat through Layer 0.</summary>
    private void ApplyEarlyFloorAttackScaling(EnemyController enemy)
    {
        if (CurrentFloor < 1 || CurrentFloor > 5) return;

        float multiplier = Mathf.Pow(scaling.earlyFloorAttackGrowthRate, CurrentFloor - 1);
        if (multiplier <= 1f) return;

        int extraAttack = Mathf.RoundToInt(enemy.Stats.attack * (multiplier - 1f));
        enemy.ApplyStatBonus(new Stats { attack = extraAttack });
    }

    /// <summary>Floor 5's mini-boss (LAYERS.md -> "Boss stat & loot multipliers",
    /// "Mini (floor 5)" as a base, overridden to 3x max HP / 2x ATK per explicit
    /// request 2026-08-06 - stronger than LAYERS.md's documented 2x/+50% baseline).
    /// One extra enemy in the last room, boosted via the same EnemyController/
    /// LootTable machinery every other enemy uses - no dedicated boss class/
    /// prefab/sprite, CLAUDE.md rule 6 ("no speculative abstraction"). This is the
    /// stat/loot half of LAYERS.md's design only - ROADMAP.md's separate "Boss
    /// with telegraphed attacks" scripted-behavior system is unbuilt and
    /// explicitly out of scope here. Loot's slotChanceMultiplier (3.7) approximates
    /// "13 slots" by scaling the existing 10 (10 x 3.7 gives ~1.152 x 3.7 ≈ 4.26
    /// expected items/kill, matching LAYERS.md's ~4.25 target) rather than
    /// inventing 3 new undocumented per-slot percentages - unchanged by this
    /// stat-multiplier bump.</summary>
    private void SpawnBoss(int roomCount)
    {
        if (enemyPrefab == null || CurrentFloor != 5 || roomCount == 0) return;

        Vector2Int cell = dungeonGenerator.RoomCenter(roomCount - 1);
        if (DungeonGrid.IsOccupied(cell)) return;

        EnemyController boss = Instantiate(enemyPrefab);
        boss.SpawnAt(cell);
        // Boss gets the same floor-5 exponential health/attack curves every plain
        // enemy on this floor gets FIRST, so "3x"/"2x" below mean 3x/2x an actual
        // floor-5 enemy's stats (30hp/6atk -> 90hp/12atk), not 3x/2x the boss's own
        // unscaled prefab base - same reasoning for both stats, 2026-08-06.
        ApplyEarlyFloorHealthScaling(boss);
        ApplyEarlyFloorAttackScaling(boss);

        Stats current = boss.Stats;
        var bossBonus = new Stats
        {
            maxHp = current.maxHp * 2,  // +200% on top of current = 3x total
            attack = current.attack,    // +100% on top of current = 2x total
        };
        boss.ApplyStatBonus(bossBonus);
        boss.SetLootBonus(3.7f, 2f, guaranteesMinRarity: true, guaranteedMinRarity: Rarity.Uncommon);

        // No dedicated boss sprite yet - a visible "this one's bigger" cue in the
        // meantime, same hand-tuned-placeholder spirit as the chest sprites.
        boss.transform.localScale *= 1.5f;

        boss.OnDeath += HandleEnemyDeath;
        spawnedEnemies.Add(boss);
    }

    private void SpawnItems(int roomCount)
    {
        if (itemPrefab == null) return;

        for (int roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            if (UnityEngine.Random.value > scaling.itemSpawnChance) continue;

            Vector2Int spawnCell = dungeonGenerator.RandomCellInRoom(roomIndex);
            if (DungeonGrid.IsOccupied(spawnCell)) continue;

            HealthPotionPickup item = Instantiate(itemPrefab);
            item.transform.position = GridUtils.CellToWorld(spawnCell);
            spawnedItems.Add(item);
        }
    }

    /// <summary>
    /// Layer 0's welcome sign - floor 1 only, placed directly beside the player's
    /// actual spawn cell (not a fixed absolute cell, so it stays adjacent regardless
    /// of room size/shape tuning). Not a general per-floor sign system yet -
    /// ROADMAP.md doesn't define one, this is a single hand-placed hint, same
    /// "hardcode until a real design exists" spirit as EnemyController's per-type
    /// xpReward/goldReward.
    /// </summary>
    private void SpawnTutorialSign()
    {
        if (signPrefab == null || CurrentFloor != 1) return;

        Vector2Int cell = dungeonGenerator.RoomCenter(0) + new Vector2Int(1, 0);
        if (DungeonGrid.IsOccupied(cell) || DungeonGrid.HasItem(cell) || DungeonGrid.GetInteractable(cell) != null) return;

        SignPost sign = Instantiate(signPrefab);
        sign.transform.position = GridUtils.CellToWorld(cell);
        spawnedSigns.Add(sign);
    }

    private void SpawnChests(int roomCount)
    {
        // First 5 floors only ever get the hand-placed test fixtures below
        // (floor 2's chest, floor 3's special one) - the random roll resumes
        // from floor 6 onward (explicit one-off request, 2026-08-06).
        if (CurrentFloor <= 5) return;
        if (smallChestPrefab == null && largeChestPrefab == null) return;

        for (int roomIndex = 0; roomIndex < roomCount; roomIndex++)
        {
            if (UnityEngine.Random.value > scaling.chestSpawnChance) continue;

            LootChest prefab = ChooseChestPrefab();
            if (prefab == null) continue;

            // Origin is the footprint's bottom-left cell (matches LootChest's own
            // doc comment) - RandomCellInRoom only guarantees THIS cell is inside
            // the room, so a footprint wider than 1x1 still needs every extra cell
            // validated below before committing to this placement.
            Vector2Int origin = dungeonGenerator.RandomCellInRoom(roomIndex);
            if (!CanPlaceFootprint(origin, prefab.Footprint)) continue;

            LootChest chest = Instantiate(prefab);
            chest.transform.position = GridUtils.CellToWorld(origin);
            spawnedChests.Add(chest);
        }
    }

    /// <summary>Picks small or large per scaling.largeChestChance, falling back to
    /// whichever prefab is actually assigned if only one is.</summary>
    private LootChest ChooseChestPrefab()
    {
        bool wantsLarge = UnityEngine.Random.value < scaling.largeChestChance;
        if (wantsLarge && largeChestPrefab != null) return largeChestPrefab;
        return smallChestPrefab != null ? smallChestPrefab : largeChestPrefab;
    }

    /// <summary>True only if every cell in the footprint (not just origin) is
    /// walkable floor with nothing already on it - a footprint wider than 1x1 can
    /// otherwise have its far cell land in a wall or on top of something else.</summary>
    private bool CanPlaceFootprint(Vector2Int origin, Vector2Int footprint)
    {
        for (int x = 0; x < footprint.x; x++)
        {
            for (int y = 0; y < footprint.y; y++)
            {
                Vector2Int cell = origin + new Vector2Int(x, y);
                if (!DungeonGrid.IsWalkable(cell)) return false;
                if (DungeonGrid.IsOccupied(cell) || DungeonGrid.HasItem(cell) || DungeonGrid.GetInteractable(cell) != null) return false;
            }
        }
        return true;
    }

    /// <summary>
    /// Test-purpose fixtures (explicit one-off request, 2026-08-06): floor 2 gets a
    /// sign explaining chests plus a small chest, floor 3 gets a sign explaining the
    /// special chest plus the special chest itself (both guaranteed, on top of
    /// whatever SpawnChests' normal roll would also produce - floors 1-5 never roll
    /// randomly, see SpawnChests, so these two floors are the only chests before
    /// floor 6), floor 4 gets a sign only (no chest - SpawnFixture's chest
    /// placement is a no-op when passed null) explaining the bag/character menu and
    /// how to equip gear. Same "hardcode a one-off, don't build a system" spirit as
    /// SpawnTutorialSign; the shared shape (sign in room 0, chest in room 1) lives
    /// in SpawnFixture below instead of being duplicated per floor.
    /// </summary>
    private void SpawnFloorTestFixtures(int roomCount)
    {
        if (CurrentFloor == 2)
        {
            SpawnFixture(roomCount,
                "Chests hold gold and gear. Walk up to one and press E to open it. " +
                "Large chests (two tiles wide) have better odds and rarer loot than small ones.",
                smallChestPrefab != null ? smallChestPrefab : largeChestPrefab);
        }
        else if (CurrentFloor == 3)
        {
            SpawnFixture(roomCount,
                "This chest is special - it always contains at least one Rare item or " +
                "better, has better odds for everything else too, and holds far more " +
                "gold than an ordinary chest.",
                specialChestPrefab);
        }
        else if (CurrentFloor == 4)
        {
            SpawnFixture(roomCount,
                "Press B to open your bag, or C for your character menu - both show " +
                "your inventory and equipped gear together. To equip an item, drag it " +
                "from the bag onto its matching slot, or right-click it and press Equip.",
                null);
        }
    }

    /// <summary>Places one hand-picked sign (room 0, explaining signMessage) + one
    /// chest (room 1, chestPrefab) pair - the shared shape behind both of
    /// SpawnFloorTestFixtures' floors.</summary>
    private void SpawnFixture(int roomCount, string signMessage, LootChest chestPrefab)
    {
        if (signPrefab != null)
        {
            Vector2Int signCell = dungeonGenerator.RoomCenter(0) + new Vector2Int(1, 0);
            if (!DungeonGrid.IsOccupied(signCell) && !DungeonGrid.HasItem(signCell) && DungeonGrid.GetInteractable(signCell) == null)
            {
                SignPost sign = Instantiate(signPrefab);
                sign.SetMessage(signMessage);
                sign.transform.position = GridUtils.CellToWorld(signCell);
                spawnedSigns.Add(sign);
            }
        }

        if (chestPrefab != null && roomCount > 1)
        {
            Vector2Int chestOrigin = dungeonGenerator.RoomCenter(1);
            if (CanPlaceFootprint(chestOrigin, chestPrefab.Footprint))
            {
                LootChest chest = Instantiate(chestPrefab);
                chest.transform.position = GridUtils.CellToWorld(chestOrigin);
                spawnedChests.Add(chest);
            }
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
