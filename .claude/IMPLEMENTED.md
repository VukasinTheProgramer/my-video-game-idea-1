# Implemented

Features that **actually run in the game** — the C# exists *and* the scene or
prefab references it. Reference game for combat feel: **Bit Heroes Quest**
(Kongregate).

- `ROADMAP.md` — designed, no code yet.
- `IN_PROGRESS.md` — code written but **not wired**, so it never executes.
  Nothing belongs in this file until it's wired.

Entry bar for this file, because "the C# is correct" proved nothing here twice
(`CLAUDE.md` §0):

```bash
grep -h '^guid:' Assets/Scripts/Path/Thing.cs.meta
grep -c "<that-guid>" Assets/Scenes/Main.unity Assets/Prefabs/Player.prefab
# 0 hits = it never runs = it goes in IN_PROGRESS.md, not here
```

---

## Core loop

Grid-based turn-based dungeon crawler (`TurnManager`): player takes one
action per turn (move via `PlayerController.TryAct`), then every registered
enemy (`EnemyController`) takes a turn. Combat itself happens on a separate
**battle screen**, not inline on the grid (see below).

## Enemy AI — leashed wander/chase

Enemies don't roam the whole floor. Each is leashed to its own spawn cell
(`spawnCell`, set once in `SpawnAt`) and never takes a step outside a flat
square around it (`leashRadius`, default 2 — Chebyshev distance, i.e.
`|dx| <= radius && |dy| <= radius`, not walkable-path distance or the
enemy's actual room polygon).

Per turn (`EnemyController.TakeTurn`):
1. Engage/attack check runs first regardless of the leash — an enemy that's
   already adjacent still fights back even at the edge of its box.
2. If the player is within `aggroRange` (default 5, Manhattan distance): step
   one cell along the BFS-shortest path toward the player
   (`Pathfinder.FindNextStep`), **unless** that step would leave the leash
   box, in which case the enemy holds its ground instead of crossing out.
3. Otherwise (player far away): `wiggleChance` (default 30%) per turn to take
   one random cardinal step, staying inside the leash box. Mostly stands
   still — this is idle flavor, not a search.

Simplification, named on purpose: the leash box is flat-radius, not the
enemy's actual room shape — `DungeonGenerator` doesn't hand `EnemyController`
a room rect today, and spawn cells already land well inside a room via
`RandomCellInRoom`, so a radius-2 box rarely pokes through a wall in
practice. Upgrade path if that ever matters: pass the spawning room's
`RectInt` into `SpawnAt` and clamp to that instead of a flat square.

## Stat system

`Stats` (serializable class on `Entity`): `attack`, `agility`, `magic`,
`defense`, `magicDefense`, `maxHp`, plus percentage-based combat modifiers
`critChanceBonus`, `critDamageBonus`, `dodgeBonus`, `parryBonus`,
`lifeSteal`. `Entity.Stats` = `baseStats + equipment.TotalBonusStats`,
recomputed fresh each access.

`CombatResolver` (static) resolves one attack between two `Entity`s:

```
scalingStat = ATK for Sword/Axe/Mace/Spear (unarmed too), AGI for Bow/Dagger, MAG for Staff
defenseStat = DEF for all physical weapons, MDEF for Staff
rawDamage   = scalingStat + weaponDamage + random(-1, +1)

dodgeChance  = 2.5% + AGI_defender * 0.5%   (cap 25%)
parryChance  = AGI_defender * 0.25% + gear  (cap 20%, melee-weapon defender only)
critChance   = 5% + AGI_attacker * 1% + gear (cap 50%)
critDamage   = rawDamage * (2.0 + gearCritDamageBonus)

resolution order: dodge -> parry -> crit -> defenseStat
lifeStolen = round(damage * lifeSteal_attacker%)
```

`CombatResolver` also exposes public `Effective*` methods (crit/dodge/parry
chance, crit damage multiplier, life steal %) so UI can display the exact
numbers combat actually rolls against, with no separate/drifting copy.

Level 1 baseline (before gear): Max HP 101 (100 flat + 1 Health point), ATK
1, AGI 1, MAG 0 (gear-only), DEF 5, MDEF 5 (flat innate toughness, everything
past that from gear).

## Leveling & stat points

`PlayerProgression` (component on the player, next to `PlayerController`):

- **XP**: flat amount per enemy (`EnemyController.xpReward`, default 10,
  hand-tuned per enemy like `lootTable`), granted to the killer on `Die()`.
- **XP curve**: escalating cost per level —
  `xpToNextLevel = baseXPToLevel2 + (level - 1) * xpGrowthPerLevel`
  (defaults 100, +50/level). Both Inspector-tunable. No level cap.
- **Points per level-up**: 1, spendable on **Attack / Health / Agility only**
  (Magic/DEF/MDEF stay gear-only). Flat 1:1 — 1 point = +1 to that stat, or
  +1 Max HP for Health.
- **Starting pool**: 10 free points at character creation, spent through the
  same system as level-up points. Separate from `Stats.Level1Default()`,
  which is the plain pre-spend baseline — no double-grant.
- **No respec** — once spent, permanent. (`ROADMAP.md`'s currency design puts
  a gold-priced respec on the table, which would deliberately reverse this.)
- UI lives in `EquipmentPanelUI`: Level/XP readout, available-points count,
  one + button each for Attack/Health/Agility, disabled at 0 points.
- Events `OnXPChanged` / `OnLevelUp` / `OnStatPointsChanged` so UI reacts
  without polling.

## Equipment

**10 working slots**: Head, Neck, MainHand, OffHand, Shoulders, Chest, Hands,
Back, Legs, Feet — rendered as layered LPC sprites via `Equipment`, kept in
sync with the base body's animation frame.

`EquipmentSlot` also declares 5 stat-only slots (Belt, Ring1, Ring2, Trinket1,
Trinket2) with `EquipmentLayerOrder` entries and a shared `StatOnlyOrder`
constant, but `Main.unity` wires only 10 slot UIs — so they can't be equipped
from the panel and aren't usable yet. See `IN_PROGRESS.md`.

`EquippableItem` (ScriptableObject) fields: `slot`, `displayName`,
`bonusStats` (Stats, added while equipped), `weaponDamage` (MainHand only),
`rarity`, `itemLevel` (**hand-authored per template for now**, not rolled
per-drop), `weaponType`, plus the LPC animation frame sets. `weaponType`
drives `CombatResolver`'s scaling/defense stat pick, but does **not** yet
drive a distinct attack pattern (cleave/pierce/ranged) — every weapon type
currently resolves as the same single-target adjacent attack.

`Equipment.Equip`/`Unequip` swap items to/from the bag automatically —
there is **no drop action anywhere**; an item always lives either in a slot
or in the bag, never destroyed. No handedness rules are enforced yet (no 2H
auto-unequip of OffHand, no shield/dual-wield validation — any item can go
in any matching slot today).

**Rarity**: `Rarity` enum — Common, Rare, Epic, Legendary, Set, Mythic,
Runic (fixed power ordering, Mythic/Runic above Legendary). `RarityVisuals`
maps each to a color (Common green, Rare blue, Epic purple, Legendary
yellow, Set cyan, Mythic red, Runic red pulsing to black — the only
animated one). This is implemented as a **data tag + visual only** — rarity
does not yet scale an item's actual stat rolls (no floor/rarity multiplier
system, no set bonuses, no Mythic passives, no Runic sockets).

## Bag / Inventory

`Inventory` component: unlimited `List<EquippableItem>`, `Add`/`Remove`,
`OnItemAdded`/`OnItemRemoved` events. `SortByRarity()` and
`SortByItemLevel()` (highest first, other stat as tiebreaker), wrapped by
`EquipmentPanelUI.SortBagByRarity()`/`SortBagByItemLevel()`.

`ItemSlotUI`: one square per item, icon + rarity-colored outline border.
Two ways to equip/unequip, both routed through the same
`EquipmentPanelUI.EquipFromBag`/`UnequipSlot`/`UnequipItem` calls so there's
one code path either way:
- **Right-click** opens a tooltip (`ItemTooltipUI`) showing stats with an
  Equip/Unequip action button.
- **Drag and drop**: drag a bag item onto its matching equip slot (rejected
  if dropped on the wrong slot type — a helmet dropped on Boots does
  nothing, it doesn't equip into Head instead; dropping onto an *occupied*
  slot swaps — the displaced item goes back to the bag, same as
  `Equipment.Equip` always did), or drag an equipped item onto the bag to
  unequip it. Implemented via `IBeginDragHandler`/`IDragHandler`/
  `IEndDragHandler`/`IDropHandler` on `ItemSlotUI` itself, with
  `PointerEventData.pointerDrag` carrying the source slot to whichever slot
  is under the pointer on drop — standard UGUI drag/drop, no custom
  raycasting. An empty equip slot stays raycast-enabled-but-transparent
  (not fully disabled) specifically so it can still catch a drop.
  `BagDropZone` (a fully transparent `Image` + `IDropHandler` that
  `EquipmentPanelUI.Awake` attaches to `bagSlotContainer` itself) catches a
  drop anywhere in the empty space of the bag — not just on an occupied
  `ItemSlotUI` — so unequip-by-drag works even when the bag is completely
  empty; individual item slots are children rendered on top and still win
  the raycast at their own position.
  `ItemSlotUI.OnDrop` destroys the *source* slot's drag-ghost proactively,
  before invoking the equip/unequip callback — that callback triggers
  `Refresh()`, which can `SetActive(false)` the source bag slot the instant
  the bag shrinks by one, and a disabled GameObject never receives its own
  `OnEndDrag`, which used to leave the ghost icon stuck on screen.

Bag slots are pooled/built at runtime, no prefab needed.

`EquipmentDropPickup`/`HealthPotionPickup` both derive from an abstract
`ItemPickup`; picking up a drop adds it to the bag, never auto-equips.

## Currency

`Wallet` (component on the player, mirrors `Inventory`/`PlayerProgression`'s
pattern): `Gold` int, `AddGold`, `TrySpendGold` (returns `bool`, only commits
on `true` — same reasoning as `ItemPickup.PickUp`), `OnGoldChanged` event.
Gold only — no gems yet, see `ROADMAP.md` → "Currency: gold & gems".

`EnemyController.Die()` rolls a flat range (`goldRewardMin`/`Max`, default
2–5, hand-tuned per enemy like `xpReward`) plus a per-floor bonus GameManager
applies at spawn (`goldBonusPerFloor`, same mechanism as enemy stat scaling)
and grants it to the killer's `Wallet`. `GoldHUDUI` shows a live "Gold: N"
readout, always-visible HUD text next to the floor indicator, not inside the
toggled character panel.

No sink exists yet — gold currently has nothing to spend it on. See
`IN_PROGRESS.md` → "2. Currency" for the rest of the build order (selling,
shop, persistence, gems).

## Battle screen (encounter flow)

Combat is **not** inline on the grid. `BattleManager` (singleton) opens a
dedicated 1v1 battle screen when player and enemy come within
`EngageRange` (default 1 = adjacent, Manhattan distance via
`GridUtils.WithinRange`) of each other — checked from both sides
(`PlayerController.TryAct` after every move, `EnemyController.TakeTurn`
before and after its own move). Falls back to the old direct
adjacent-attack if no `BattleManager` exists in the scene.

- Rest of the floor freezes while a battle is active.
- **Turn order**: whoever has higher Agility acts first (ties favor the
  player), then turns alternate normally. (Simplified from Bit Heroes' own
  continuous tick-rate formula, which doesn't map onto this project's
  discrete 1v1 duel — see `BattleManager.StartBattle` comments.)
- `BattleScreenUI`: full-screen overlay, player/enemy HP bars, portraits
  (snapshot of each `SpriteRenderer.sprite` — no dedicated battle art yet),
  `turnIndicatorText` ("Your Turn"/"Enemy Turn") at the top, Attack button
  at the bottom (Space/Enter also works).
- Reuses `Entity.Attack`/`CombatResolver` completely — damage numbers, hit
  flash, crit/dodge/parry/life steal all work identically to grid combat.
- On win: shows the victory screen (see "Outro screens" below), which
  resumes the floor via `TurnManager.EndPlayerTurn()` once dismissed. On
  player death: `GameOverUI` takes over independently, no outro screen.
- Not built: fleeing a battle, multi-enemy encounters, party members.

## Outro screens

Three full-screen modals — dim backdrop + centered popup (title, body text,
Continue button; Space/Enter also confirms) — all self-bootstrapping
singletons (`VictoryScreenUI`, `LevelUpUI`, `FloorCompleteUI`, same pattern
as `DamageNumberSpawner`/`CameraShake`: no scene wiring, they build their own
UI under the scene's Canvas the first time they're shown). Shared layout
code lives in `ModalScreenUI`.

- **`VictoryScreenUI`**: shown by `BattleManager` the instant the enemy dies,
  right after hiding the fight overlay. Damage dealt/taken (both sides,
  summed across the whole fight via `Entity.OnAttackResolved`), XP earned,
  gold earned, items dropped (rarity-colored) — exact numbers via
  `EnemyController.OnXPGranted`/`OnGoldGranted`/`OnLootDropped`, not diffed
  from `Wallet`/`PlayerProgression` state (a diff breaks across a level-up's
  XP rollover).
- **`LevelUpUI`**: shown after Victory's Continue, only if that kill crossed
  a level threshold (`PlayerProgression.OnLevelUp`, captured as the *final*
  level reached — `AddXP` can fire it more than once off one big grant, this
  only ever shows once per kill).
- **`FloorCompleteUI`**: shown by `GameManager.AdvanceFloorWhenSafe` once
  every enemy on the floor is dead, before the next floor generates.

`BattleManager.IsActive` deliberately stays **true** through Victory (and
Level Up, if shown) — `GameManager`'s floor-advance wait and the
player/enemy turn-skip checks all key off it, so the dungeon stays frozen
for exactly as long as an outro screen is on top of it. A separate private
`fightOver` flag (set the instant either side dies) stops turn logic
(`OnPlayerAttackPressed`/`RunEnemyTurn`) immediately, independent of when
`IsActive` eventually clears.

Not built: an outro screen on the inline-fallback attack path (no
`BattleManager` in scene) — that path stays deliberately minimal by design,
see "Battle screen (encounter flow)" above.

## Combat feedback

- **Damage numbers**: `DamageNumberSpawner` (self-bootstrapping singleton)
  spawns floating text — white normal, yellow + larger on crit, gray
  "MISS" on dodge, blue "PARRY" on parry, green "+N" on heal/life steal.
- **Hit flash**: sprite flashes red (not white — `SpriteRenderer.color` is
  multiplicative, can't brighten to true white without an additive shader)
  for ~0.08s on `TakeDamage`.
- **Screen shake**: `CameraShake` (self-bootstrapping singleton) exposes a
  decaying `CurrentOffset`; `CameraFollow` adds it on top of its own
  follow position each frame (avoids two systems fighting over the
  camera's transform). Triggered on crit via `Entity.Attack`.

## Enemy loot

`EnemyController.Die()` rolls `LootTable` (ScriptableObject, weighted drops,
`dropChance` gate before the item pick) and spawns an `EquipmentDropPickup`
on the enemy's cell; `GameManager` tracks spawned drops for floor-transition
cleanup. `LootTable` returns the shared ScriptableObject **template**, so two
drops of one entry are the *same reference* — this is why `Equipment.Equip`
early-returns on an already-worn item.

One table exists, `Assets/Equipment/LootTables/DefaultLootTable.asset`
(30% `dropChance`, all 6 current `EquippableItem`s, Common weighted 3x
Rare), assigned to `Enemy.prefab` — every enemy currently rolls against it,
no per-enemy-type variation yet. Per-drop stat rolls (rarity/floor scaling
an item's actual numbers) are `ROADMAP.md` → "Item generation", not this.

## Key files

| Area | File |
|------|------|
| Stats & combat math | `Assets/Scripts/Core/Stats.cs`, `Assets/Scripts/Core/CombatResolver.cs` |
| Leveling | `Assets/Scripts/Core/PlayerProgression.cs` |
| Entity base | `Assets/Scripts/Entities/Entity.cs` |
| Player / enemy | `Assets/Scripts/Entities/PlayerController.cs`, `Assets/Scripts/Entities/EnemyController.cs` |
| Equipment | `Assets/Scripts/Equipment/Equipment.cs`, `EquippableItem.cs`, `EquipmentSlot.cs`, `WeaponType.cs`, `Rarity.cs`, `RarityVisuals.cs` |
| Bag | `Assets/Scripts/Equipment/Inventory.cs` |
| Currency | `Assets/Scripts/Core/Wallet.cs`, `Assets/Scripts/UI/GoldHUDUI.cs` |
| Items on the floor | `Assets/Scripts/Items/ItemPickup.cs`, `HealthPotionPickup.cs`, `EquipmentDropPickup.cs`, `LootTable.cs` |
| Battle screen | `Assets/Scripts/Managers/BattleManager.cs`, `Assets/Scripts/UI/BattleScreenUI.cs` |
| Outro screens | `Assets/Scripts/UI/VictoryScreenUI.cs`, `LevelUpUI.cs`, `FloorCompleteUI.cs`, `ModalScreenUI.cs` |
| Equipment/bag UI | `Assets/Scripts/UI/EquipmentPanelUI.cs`, `ItemSlotUI.cs`, `ItemTooltipUI.cs`, `BagDropZone.cs` |
| Feedback | `Assets/Scripts/UI/DamageNumberSpawner.cs`, `DamageNumberMotion.cs`, `UI/CameraShake.cs`, `Core/CameraFollow.cs` |
| Turn loop / grid | `Assets/Scripts/Core/TurnManager.cs`, `DungeonGrid.cs`, `GridUtils.cs` |
