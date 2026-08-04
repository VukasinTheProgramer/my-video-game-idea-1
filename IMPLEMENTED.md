# Implemented

What's built in the game right now. Reference game for combat feel: **Bit
Heroes Quest** (Kongregate). See `ROADMAP.md` for anything designed but not
yet built.

"Built" means the **C# exists** — it does *not* prove the scene or prefabs
reference it (`CLAUDE.md` §0: that's this project's worst bug class). Sections
carrying **⚠️** describe finished code that never runs because serialized data
doesn't point at it. Verify anything load-bearing before relying on it:

```bash
grep -h '^guid:' Assets/Scripts/Path/Thing.cs.meta
grep -c "<that-guid>" Assets/Scenes/Main.unity Assets/Prefabs/Player.prefab
```

---

## Core loop

Grid-based turn-based dungeon crawler (`TurnManager`): player takes one
action per turn (move via `PlayerController.TryAct`), then every registered
enemy (`EnemyController`) takes a turn. Combat itself happens on a separate
**battle screen**, not inline on the grid (see below).

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

## Leveling & stat points — ⚠️ WRITTEN BUT NOT WIRED, DOES NOTHING

**This section describes code that never executes.** `PlayerProgression` is
**not a component on `Player.prefab`** (which carries only `PlayerController`,
`DirectionalSpriteAnimator`, `Equipment`, `Inventory`), and nothing adds it at
runtime. Every caller fetches it defensively — `progression?.AddXP(xpReward)`
in `EnemyController.Die`, `player.GetComponent<PlayerProgression>()` in
`EquipmentPanelUI` — so XP is never granted, no level-up ever fires, and no
stat point is ever spendable. Silently: no error, no warning.

Adding the component to the prefab is the whole fix; the logic below is
complete and correct. Kept in this file rather than `ROADMAP.md` because the
code is written, not planned.

`PlayerProgression` component (intended to sit on the player next to
`PlayerController`):

- **XP**: flat amount per enemy (`EnemyController.xpReward`, default 10,
  hand-tuned per enemy like `lootTable`), granted to the killer on `Die()`.
- **XP curve**: escalating cost per level —
  `xpToNextLevel = baseXPToLevel2 + (level - 1) * xpGrowthPerLevel`
  (defaults 100, +50/level). Both tunable in the Inspector. No level cap.
- **Points per level-up**: 1, spendable on **Attack / Health / Agility
  only** (Magic/DEF/MDEF stay gear-only). Flat 1:1 — 1 point = +1 to that
  stat (or +1 Max HP for Health).
- **Starting pool**: 10 free points at character creation, spent through
  the same system as level-up points.
- **No respec** — once spent, permanent.
- UI lives in `EquipmentPanelUI`: Level/XP readout, available-points count,
  one + button each for Attack/Health/Agility (disabled at 0 points).
- Events (`OnXPChanged`, `OnLevelUp`, `OnStatPointsChanged`) let UI react
  without polling.

## Equipment

15 slots exist on `EquipmentSlot`: **Head, Neck, MainHand, OffHand,
Shoulders, Chest, Hands, Back, Legs, Feet** (rendered as layered LPC sprites
via `Equipment`, kept in sync with the base body's animation frame) plus
**Belt, Ring1, Ring2, Trinket1, Trinket2** (stat-only — no LPC art, so they
never render a visible layer, they just contribute to `TotalBonusStats`).
`EquipmentLayerOrder.Fixed` has a sorting-order entry for every slot,
including a shared `StatOnlyOrder` constant for the 5 stat-only ones.

⚠️ `Main.unity` still wires only **10** `slotUIs`/`slotLabels`. The length
guards in `EquipmentPanelUI.Refresh` mean this doesn't throw — the last five
(Belt, Ring1, Ring2, Trinket1, Trinket2) simply have no UI square, so they
can't be equipped from the panel even though `Equipment` handles them fine.

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

`ItemSlotUI`: one square per item, icon + rarity-colored outline border,
right-click to open a tooltip (`ItemTooltipUI`) showing stats with an
Equip/Unequip action button. Bag slots are pooled/built at runtime, no
prefab needed.

`EquipmentDropPickup`/`HealthPotionPickup` both derive from an abstract
`ItemPickup`; picking up a drop adds it to the bag, never auto-equips.

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
- On win: resumes the floor via `TurnManager.EndPlayerTurn()`. On player
  death: `GameOverUI` takes over.
- Not built: fleeing a battle, multi-enemy encounters, party members.

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

## Enemy loot — ⚠️ NO DROPS ACTUALLY HAPPEN

`EnemyController.Die()` is written to roll `LootTable` (ScriptableObject,
weighted drops) and spawn an `EquipmentDropPickup` on the enemy's cell, and
`GameManager` does track spawned drops for floor-transition cleanup.

But **no `LootTable` asset has ever been created** — only the script — and
`Enemy.prefab` serializes `lootTable: {fileID: 0}` (null). So the roll is
skipped and enemies drop nothing. The XP grant on the same code path is dead
too, for the separate reason above.

Fix is two pieces of data, no code: author a `LootTable` asset and assign it
on `Enemy.prefab`.

## Key files

| Area | File |
|------|------|
| Stats & combat math | `Assets/Scripts/Core/Stats.cs`, `Assets/Scripts/Core/CombatResolver.cs` |
| Leveling | `Assets/Scripts/Core/PlayerProgression.cs` |
| Entity base | `Assets/Scripts/Entities/Entity.cs` |
| Player / enemy | `Assets/Scripts/Entities/PlayerController.cs`, `Assets/Scripts/Entities/EnemyController.cs` |
| Equipment | `Assets/Scripts/Equipment/Equipment.cs`, `EquippableItem.cs`, `EquipmentSlot.cs`, `WeaponType.cs`, `Rarity.cs`, `RarityVisuals.cs` |
| Bag | `Assets/Scripts/Equipment/Inventory.cs` |
| Items on the floor | `Assets/Scripts/Items/ItemPickup.cs`, `HealthPotionPickup.cs`, `EquipmentDropPickup.cs`, `LootTable.cs` |
| Battle screen | `Assets/Scripts/Managers/BattleManager.cs`, `Assets/Scripts/UI/BattleScreenUI.cs` |
| Equipment/bag UI | `Assets/Scripts/UI/EquipmentPanelUI.cs`, `ItemSlotUI.cs`, `ItemTooltipUI.cs` |
| Feedback | `Assets/Scripts/UI/DamageNumberSpawner.cs`, `DamageNumberMotion.cs`, `UI/CameraShake.cs`, `Core/CameraFollow.cs` |
| Turn loop / grid | `Assets/Scripts/Core/TurnManager.cs`, `DungeonGrid.cs`, `GridUtils.cs` |
