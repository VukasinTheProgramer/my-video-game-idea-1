# Combat System & Retro Pixel Style — Design Doc

Reference game: **Bit Heroes Quest** (Kongregate). Goal: bring its stat-driven,
loot-powered turn-based combat feel into the existing grid dungeon crawler,
while keeping the current one-action-per-turn roguelike structure for
*exploration*. Combat itself now transitions to a dedicated battle screen
(§0) rather than resolving inline on the grid, matching Bit Heroes' own
encounter flow.

What we keep from the current codebase: `TurnManager` loop, grid
movement via `PlayerController.TryAct`, `Entity` as the combat base
class, the layered `Equipment` rendering. What we add: a battle-screen
encounter flow, a real stat model, gear that matters (including
weapon-driven attack patterns), damage variance/crits, and enemy
archetypes.

---

## 0. Battle screen (encounter flow)

Getting close to an enemy on the dungeon grid no longer resolves combat
inline (walk-into-enemy-and-swing) — and it's not limited to literally
bumping into its cell either. As soon as the player and an enemy are
within **engage range** of each other, from either side closing the gap,
the dungeon freezes and a dedicated **1v1 battle screen** opens — "just me
and that enemy" — same idea as Bit Heroes' own transition from the
explore view into a turn-based encounter.

- `BattleManager.EngageRange` (default **1**, i.e. adjacent — same
  threshold the old bump-to-attack used): Manhattan-distance check via
  `GridUtils.WithinRange`. Raise it in the Inspector to have monsters
  "notice" you from further away, Bit Heroes-style aggro range.
- `BattleManager.TryEngageIfInRange(player, enemy)` is the single check
  both sides call: `PlayerController.TryAct` calls it after every move,
  scanning all enemies registered with `TurnManager` (not just whatever
  was directly bumped); `EnemyController.TakeTurn` calls it both before
  and after its own move, so an enemy walking toward the player can
  trigger the encounter too, not just the player walking toward it. Both
  fall back to the old direct adjacent-attack if no `BattleManager` exists
  in the scene yet, so nothing breaks before it's wired up.
- `BattleManager` (singleton, like `TurnManager`/`GameManager`) owns the
  fight: player presses Attack (button or Space/Enter) →
  `Entity.Attack` resolves via the existing `CombatResolver` → short delay
  → enemy's `Attack` resolves the same way → repeat until one side dies.
  Reuses `Entity.Attack` completely, so damage numbers, hit flash, crit/
  dodge/parry/life steal all just work inside the battle screen exactly
  as they do on the grid — the battle screen is a different *place*
  combat happens, not a different combat system.
- While a battle is active: `PlayerController.Update` stops reading
  movement input, and every other `EnemyController.TakeTurn` bails
  immediately — the rest of the floor is frozen, matching "just me and
  that enemy."
- On win: `BattleManager.EndBattle` calls `TurnManager.EndPlayerTurn()`,
  so the rest of the floor reacts afterward exactly as if the encounter
  had been one normal player action. On player death: skipped —
  `GameOverUI` (already subscribed to `player.OnDeath` independently)
  takes over.
- `BattleScreenUI`: full-screen overlay with player/enemy HP bars and an
  Attack control. **v1 simplification**: portraits are a snapshot of each
  Entity's current `SpriteRenderer.sprite` — there's no second camera or
  dedicated battle art/animation yet. Good enough to sell the screen
  transition; real portraits are a drop-in visual upgrade later, no
  `BattleManager` changes needed.
- **Layout**: `turnIndicatorText` ("Your Turn" / "Enemy Turn") sits at the
  top of the screen; `attackButton` (the first slot of what'll become an
  ability row once weapon-driven attacks exist, §4) sits at the bottom —
  matching Bit Heroes' own screen layout. Actual Canvas positioning is
  done in the Editor, same as everything else UI-side in this project.
- **Turn order**: Bit Heroes decides how *often* each side acts with a
  continuous turn-rate formula, `[(Power+Agility)/2]² / Power`, run on a
  tick-based multi-actor ATB system. That doesn't map cleanly onto this
  project's simple discrete 1v1 duel, and our AGI stat already pulls
  double duty (crit/dodge, §1) unlike Bit Heroes' pure-speed Agility. So
  `BattleManager.StartBattle` uses the direct, faithful simplification:
  **whoever has higher Agility acts first** (ties favor the player), then
  it alternates normally each turn after that. `BattleScreenUI.SetTurn`
  updates the top indicator every time the active side changes. A fuller
  "faster acts more often" gauge system (literal turn-rate ports) is a
  possible later upgrade, not built now.
- Not built yet: fleeing a battle, multi-enemy encounters (still strictly
  1v1), and party members (single player character only, matching the
  project's current scope).

## 1. Stat model

Replace the two hardcoded ints in `Entity` (`maxHealth`, `attackDamage`)
with a small stat block. Bit Heroes itself only really has Power/Stamina/
Agility (see its wiki), but we're splitting damage by weapon *type* (§4),
so we need a scaling stat per weapon family rather than one universal
damage stat. Researched a few other RPG stat systems (StraySpark's stat
design writeup, WrittenRealms) to sanity-check the secondary-stat list
below — dodge/parry/crit/lifesteal are all standard derived stats, kept
here as percentages with caps so none of them trivialize combat.

**Core stats** (leveling + gear, drive damage scaling and survivability):

| Stat | Effect |
|------|--------|
| **Attack (ATK)** | Damage scalar for melee weapons: Sword, Axe, Mace, Spear |
| **Agility (AGI)** | Damage scalar for finesse weapons: Bow, Dagger. Also feeds Crit and Dodge (see below) |
| **Magic (MAG)** | Damage scalar for Staff (and any future elemental/spell gear) |
| **Physical Defense (DEF)** | Flat reduction vs. ATK/AGI-scaled damage (Sword/Axe/Mace/Spear/Bow/Dagger), minimum 1 damage always gets through |
| **Magic Defense (MDEF)** | Flat reduction vs. MAG-scaled damage (Staff), minimum 1 damage always gets through |
| **Max HP** | Health pool |

Splitting defense means gear and enemies can now lean tanky-vs-physical or
tanky-vs-magic instead of one stat covering everything — a Staff enemy
that out-damages a heavily-armored (high DEF, low MDEF) player is a build
counter that falls out of this for free.

**Combat modifiers** (mostly 0 at baseline, pushed up by gear affixes;
AGI contributes a bit to Crit/Dodge automatically so it's never a dead
stat on finesse *or* melee builds):

| Stat | Effect | Cap |
|------|--------|-----|
| **Crit Chance** | Chance to deal Crit Damage instead of normal damage | 50% |
| **Crit Damage** | Multiplier applied on a crit (base 200%, gear adds more) | — |
| **Dodge** | Chance to fully avoid an attack (0 damage, "MISS") | 25% |
| **Parry** | Melee-only: chance to avoid an attack and take heavily reduced damage instead of zero, distinguishing it from Dodge. Only rolls if defender has a melee-capable weapon equipped (Sword/Axe/Mace/Spear/Dagger — not Bow/Staff) | 20% |
| **Life Steal** | % of damage dealt returned as healing to the attacker | 50% |

Formulas (start simple, tune later):

```
scalingStat = ATK          for Sword / Axe / Mace / Spear (unarmed defaults here too)
            = AGI          for Bow / Dagger
            = MAG          for Staff

defenseStat = DEF           for Sword / Axe / Mace / Spear / Bow / Dagger  (physical)
            = MDEF           for Staff                                    (magic)

rawDamage   = scalingStat + weaponDamage + random(-1, +1)

dodgeChance  = 2.5% + AGI_defender * 0.5%              (cap 25%)
parryChance  = AGI_defender * 0.25% + gearParryBonus    (cap 20%, melee-weapon defender only)
critChance   = 5% + AGI_attacker * 1% + gearCritBonus    (cap 50%)
critDamage   = rawDamage * (2.0 + gearCritDamageBonus)

-- resolution order: dodge check -> parry check -> crit check -> defenseStat --
if dodged:  damage = 0
elif parried: damage = max(1, rawDamage * 0.25 - defenseStat_defender)
elif crit:  damage = max(1, critDamage - defenseStat_defender)
else:       damage = max(1, rawDamage - defenseStat_defender)

lifeStolen  = round(damage * lifeSteal_attacker%)   (attacker heals this much)
```

Note: Parry stays physical-only (it's tied to having a melee weapon out to
block with) even though a Staff wielder now has MDEF — a caster just relies
on Dodge/MDEF instead of Parry, which is a reasonable build tradeoff.

Implementation:

- New `Stats` serializable class (`attack`, `agility`, `magic`, `defense`,
  `magicDefense`, `maxHp`, `critChance`, `critDamageBonus`, `dodge`,
  `parry`, `lifeSteal`) on `Entity`, replacing the current fields.
- `Entity.Attack` / `TakeDamage` route through a static `CombatResolver`
  that applies the formulas above and returns a `CombatResult` (damage,
  wasCrit, wasDodged, wasParried, lifeStolen) so UI can react to each case
  distinctly (§6 damage-number colors already assume crit/miss; add a
  parry variant).
- `ApplyStatBonus` becomes `ApplyStatBonus(Stats bonus)` — used by
  `GameManager` floor scaling.
- `CombatResolver` needs to know the attacker's equipped `WeaponType`
  (§4) to pick `scalingStat` — pass `Entity` in, not just raw stats.

**Idea parked for later** (came up in research, not needed for v1):
Speed/turn-order manipulation (Bit Heroes uses AGI for turn speed; we're
strictly sequential turn order so this doesn't apply unless that changes).

### 1a. Starting stats & leveling (Level 1)

Only **three** of the core stats are point-allocatable at the start —
**Attack, Health, Agility**. Magic stays gear-only (a Level 1 hero hasn't
trained in spellcasting; it only matters once a Staff is equipped, §4).
Physical and Magic Defense get a small **flat innate baseline** instead
of literal 0 — a Level 1 body has some natural toughness before any armor
— with everything past that coming from gear.

- **Base Max HP**: 100 flat, before any points.
- Each of Attack / Health / Agility **starts with 1 point already
  invested** (not 0) — that's 3 points spent by default.
- **10 free skill points** on top of that baseline, distributed however
  the player likes across the same three stats.
- **Point value is flat 1:1** for v1: 1 Health point = +1 Max HP, 1
  Attack point = +1 ATK, 1 Agility point = +1 AGI. No diminishing
  returns yet.

So at absolute baseline (before spending any of the 10 points): ATK 1,
AGI 1, Health 1 → **Max HP = 100 + 1 = 101**, matching the target. A
player who then dumps all 10 free points into Health ends at Health 11 →
Max HP 111, with ATK/AGI staying at 1 each; a more balanced spend might
look like ATK 4 / Health 4 / AGI 4 (1 baseline + 3 each, 9 of the 10
spent, 1 left over or rounded into one stat).

**Full Level 1 baseline, before any gear is equipped** (numbers are
starting placeholders, tune later):

| Stat | Base value | Source |
|------|-----------|--------|
| Max HP | 101 (100 + 1 Health point) | flat + points |
| ATK | 1 | points |
| AGI | 1 | points |
| MAG | 0 | gear-only, no baseline |
| DEF (physical) | 5 | flat innate toughness |
| MDEF (magic) | 5 | flat innate toughness |
| Crit Chance | 5% + AGI×1% = 6% | formula (§1) |
| Crit Damage | 200% (2.0×) | formula base |
| Dodge | 2.5% + AGI×0.5% = 3% | formula (§1) |
| Parry | AGI×0.25% = 0.25% | formula, melee-weapon only |
| Life Steal | 0% | gear-only, no baseline |

DEF/MDEF at 5 each means even an unarmored Level 1 character shrugs off a
little chip damage rather than taking full raw damage on every hit — gear
then stacks additively on top of that 5, same as it does for every other
stat.

Implementation:

- `attackPoints`, `healthPoints`, `agilityPoints` on the player entity,
  each defaulting to `1`, plus `unspentSkillPoints = 10` at character
  creation.
- `ATK = attackPoints`, `AGI = agilityPoints`, `MaxHP = 100 + healthPoints`
  — these feed `TotalStats` the same way gear bonuses do (§2), so nothing
  else in `CombatResolver` needs to know points exist at all.
- The allocation UI lives in `EquipmentPanelUI`, alongside the stats
  readout — Level/XP text, an "available points" count, and one + button
  each for Attack/Health/Agility. Buttons disable themselves when no
  points are available. No respec: once a point is spent it's permanent
  (matching Bit Heroes), so there's nothing to "un-apply" — spending just
  calls `Entity.ApplyStatBonus` the same way gear-independent floor
  scaling does (§2c), and the running stat totals are the only state that
  needs to persist.

### 1b. XP & leveling past Level 1

- **XP source**: flat per enemy, not computed from a formula — each
  `EnemyController` has its own `xpReward` int (default `10`), tuned by
  hand per enemy type/floor tier the same way `LootTable` already is.
  Granted to the killer via `PlayerProgression.AddXP` from `Die()`.
- **XP curve**: escalating cost per level, not a flat requirement —
  `xpToNextLevel = baseXPToLevel2 + (level - 1) * xpGrowthPerLevel`
  (defaults `100`, `+50` per level: L1→2 costs 100, L2→3 costs 150,
  L3→4 costs 200, ...). Both numbers are Inspector fields on
  `PlayerProgression`, tune without touching code. No level cap yet.
- **Points per level-up**: 1 (`pointsPerLevelUp`, also an Inspector
  field), same three stats as the Level 1 baseline — Attack, Health,
  Agility. Magic/DEF/MDEF still stay gear-only; that's unchanged from
  §1a, not revisited here.
- **Respec**: no. Permanent once spent, by design (see 1a above).
- **Implementation**: `PlayerProgression` (new component, sits next to
  `PlayerController`/`Entity` on the player) owns `Level`, `CurrentXP`,
  `AvailableStatPoints`, and `XPToNextLevel`; `AddXP` loops so one large
  XP gain can trigger multiple level-ups in one call. `OnXPChanged`,
  `OnLevelUp`, `OnStatPointsChanged` events let `EquipmentPanelUI` (or
  anything else later, e.g. a level-up popup/toast) react without
  polling.

## 2. Equipment with stats

`EquippableItem` currently only holds sprites. Add:

```csharp
public Stats bonusStats;      // added while equipped
public int weaponDamage;      // MainHand only, feeds damage formula
public Rarity rarity;         // Common, Rare, Epic, Legendary, Set, Mythic, Runic
```

- `Equipment.Equip/Unequip` notifies the owning `Entity`, which recomputes
  `TotalStats = baseStats + sum(equipped bonuses)`.
- Rarity drives stat budget and drop color (see style section) — this is
  the core Bit Heroes hook: loot is the progression system.
- Drops: enemies roll a loot table on `Die()`; item lands on their cell and
  is picked up like the existing `HealthPotionPickup` (generalize into an
  `ItemPickup` base).

### 2·½ Bag (Inventory) — no item drop, unlimited storage

Picking up a loot drop adds it to the player's bag rather than
auto-equipping it — the player decides when to equip. There is no "drop
item" action anywhere: once picked up, an item lives either on a body
slot or in the bag, never destroyed and never lost. The bag has
**unlimited capacity**.

- `Inventory` component (unlimited `List<EquippableItem>`, `Add`/`Remove`,
  `OnItemAdded`/`OnItemRemoved` events) sits alongside `Equipment` on the
  player.
- `Equipment.Equip(slot, item)` now also handles the swap: whatever was
  previously in that slot goes to the bag (`inventory.Add`), and the
  newly-equipped item is removed from the bag if it came from there
  (`inventory.Remove`) — so an item is never listed as both equipped and
  in the bag.
- `Equipment.Unequip(slot)` sends the removed item to the bag instead of
  discarding it — this *is* the "unequip" action; there's no separate
  drop-to-floor path.
- `EquipmentDropPickup.PickUp` calls `inventory.Add(item)`, not
  `equipment.Equip(...)` — matches the "picking up doesn't auto-equip"
  rule above.
- UI: `ItemSlotUI` (§6) is one item square — icon plus a rarity-colored
  outline frame, clickable. `EquipmentPanelUI` uses it for both the 10
  equipped slots (`slotUIs[]`, click to unequip) and the bag (spawned/
  pooled at runtime under `bagSlotContainer`, click to equip — whatever
  was equipped there swaps back into the bag automatically). No prefab
  needed for bag slots; they're built from code the same way
  `DamageNumberSpawner` builds its floating text.
- **Sorting**: `EquippableItem.itemLevel` (int, hand-authored per template
  for now — see §2c's "v1 scope" note on procedural rolling) plus the
  existing `rarity` drive two `Inventory` sort methods, `SortByRarity()`
  and `SortByItemLevel()`, each highest-first with the other stat as
  tiebreaker. `EquipmentPanelUI.SortBagByRarity()` /
  `SortBagByItemLevel()` wrap these for buttons and refresh the display.

### 2·0 Equipment slots

15 slots total. `EquipmentSlot` currently has 10 of them already — Head,
Neck, Shoulders, Chest (renamed from `Torso`), Hands (renamed from
`Arms`), Back, Legs, Feet, MainHand, OffHand — all backed by imported LPC
art (`Assets/Sprites/Tileset/LPC-Equipment/`), so those keep rendering as
layered sprites exactly like today; only the enum member names change,
not what art they point to.

Add **5 new slots**, all stat-only (no LPC art exists for these, so no
sprite layer — they just contribute to `TotalStats`):

| New slot | Notes |
|----------|-------|
| **Belt** | stat-only |
| **Ring 1** | stat-only |
| **Ring 2** | stat-only |
| **Trinket 1** | stat-only |
| **Trinket 2** | stat-only |

Implementation delta when this gets built:

- Add the 5 values to `EquipmentSlot.cs`.
- `EquipmentLayerOrder.Fixed` needs a sorting-order entry for each — even
  a stat-only slot goes through `Equipment.ApplyFrame`'s
  `layer.SetSortingOrder(...)` call, which will throw a
  `KeyNotFoundException` today if the slot's missing from that
  dictionary. Simplest fix: give the 5 new slots a shared sorting order
  constant (they never render anything, so the exact value doesn't
  matter) rather than assigning each one individually.
- `EquipmentPanelUI`'s slot list needs the 5 additions so they show up in
  the UI (as icon-only slots, no character-layer preview).
- Two Ring slots and two Trinket slots means `Equipment`'s internal
  `Dictionary<EquipmentSlot, ...>` keying by `EquipmentSlot` alone breaks
  — Ring 1/Ring 2 need distinct enum values (`Ring1`, `Ring2`, not a
  single `Ring` slot with a count), same for `Trinket1`/`Trinket2`. Two
  interchangeable items of the same "type" isn't a new concept here, just
  two literal enum entries.

### 2a. Weapon handedness & shields

`MainHand` and `OffHand` already exist as separate slots (`EquipmentSlot`),
so this is mostly a rule enforced at equip time rather than new plumbing.
Handedness is **per-item, not fixed per weapon type** — Sword, Axe, and
Mace each come in both 1H and 2H variants (e.g. Shortsword vs Greatsword),
so the game needs individual items to carry the flag rather than deriving
it from `WeaponType` alone. Dagger and Hammer are the only types locked to
one handedness.

- Add `Handedness { OneHanded, TwoHanded }` to `EquippableItem` (set per
  item, not inferred from `WeaponType`).
  - **Sword, Axe, Mace, Dagger**: items can be 1H (Dagger is always 1H,
    the other three roll either) — any 1H weapon can go in `MainHand`
    *or* be dual-wielded into `OffHand`.
  - **Hammer**: always 2H (split out from Mace as its own `WeaponType` —
    it's the heavy, no-1H-variant version; Mace keeps the option).
  - **Spear, Bow, Staff**: always 2H, unchanged.
- Add a `Shield` item kind: **`OffHand`-only**, can never be placed in
  `MainHand`. Contributes stats only, no attack pattern — this is also
  where **Block** lives (halves incoming damage on trigger), exclusive to
  shield gear.
- Equip rules, enforced in `Equipment.Equip`:
  - Equipping a **2H item** (any type, per its own `Handedness`) into
    `MainHand` auto-unequips whatever is in `OffHand`.
  - Equipping a **Shield** into `OffHand` requires `MainHand` to currently
    hold a 1H item; otherwise rejected (with UI feedback, not silent).
  - Equipping a **1H Sword/Axe/Mace/Dagger** into `OffHand` (dual-wield)
    also requires `MainHand` to hold a 1H item — any 1H combo is valid
    (Sword+Shield, Axe+Axe, Mace+Dagger, Dagger+Dagger, etc).
  - `MainHand` never accepts a `Shield`.
  - v1 scope: whatever's in `OffHand` — Shield or a second 1H weapon —
    only contributes its stats. The attack pattern always comes from
    `MainHand` (§4); the off-hand item doesn't add a second attack per
    turn. A bonus off-hand-strike proc is a natural follow-up once the
    base loop is proven, not needed for v1.
- UI note: `EquipmentPanelUI` should gray out / block-drop the `OffHand`
  slot while a 2H item is equipped, rather than only failing silently on
  drop.

### 2b. Stat rolls by handedness

Not every weapon can roll every stat — handedness gates whether **DEF**
can appear on a weapon drop, matching the sword-and-board fantasy (a 1H
weapon leaves a hand free to brace/parry; a 2H weapon commits fully to
offense):

- **1H Sword / Axe / Mace / Dagger**: may roll `DEF` as a bonus stat,
  alongside their normal ATK/AGI-leaning rolls.
- **2H Sword / Axe / Mace / Hammer / Spear / Bow / Staff**: `DEF` is
  excluded from their possible stat rolls entirely — that budget goes
  into ATK/AGI/MAG/crit/etc. instead. (MDEF stays excludable too, for the
  same reason — 2H weapons don't roll either defense stat.)
- Implementation: the loot-table/item-generation step (§2, "Drops") checks
  `Handedness` before picking which stat pool to roll from — two pools,
  `oneHandedRollableStats` (includes DEF) and `twoHandedRollableStats`
  (excludes DEF and MDEF).

### 2c. Stat scaling by floor & rarity

An item's actual stat values aren't fixed on the `EquippableItem`
template — they're rolled at drop time from two multipliers, so the same
"Sword" template found on floor 1 and floor 10 ends up meaningfully
different:

```
itemStats = baseTemplateStats * floorMultiplier(floorNumber) * rarityMultiplier(rarity)
```

- **Floor multiplier**: scales with dungeon depth (same floor-scaling hook
  `GameManager` already uses for `ApplyStatBonus` on enemies, §1) — e.g.
  roughly `1 + (floor - 1) * 0.15`, tuned later. Deeper floors, better
  gear, same as deeper floors already meaning tougher enemies.
- **Rarity multiplier**: Common baseline, then each tier steps up both the
  multiplier *and* how many bonus stat rolls the item gets. Mythic and
  Runic sit **above** Legendary in raw power (not just parity) — Runic is
  the top of the ladder:

  | Rarity | Bonus stat rolls | Stat multiplier (relative) | Notes |
  |--------|-------------------|------------------------------|-------|
  | Common | 1 | 1.0× | baseline |
  | Rare | 2 | 1.15× | |
  | Epic | 3 | 1.3× | |
  | Legendary | 4 | 1.5× | + guaranteed weapon-defining affix |
  | Set | 4 (same as Legendary) | 1.5× | + set bonus at 2/4/6 pieces worn |
  | Mythic | 5 | 1.7× | + one unique, mythic-only passive |
  | Runic | 6 | 1.9× | + empty rune socket(s) for player-chosen effects |

  Set stays at **Legendary parity** and wins on synergy (multi-piece
  bonuses) rather than raw numbers, matching Bit Heroes' own convention.
  Mythic and Runic are deliberately above that: Mythic is stronger than
  Legendary/Set *and* carries a hand-authored unique passive; Runic is the
  single strongest tier *and* has the socket customization on top — the
  true chase-item ceiling. Exact multiplier curve TBD/tunable, but the
  ordering (Common < Rare < Epic < Legendary = Set < Mythic < Runic) is
  fixed by this table now.
- **Set items**: belong to a named set (`SetId` on `EquippableItem`);
  `Equipment` tracks how many pieces of each set are currently equipped
  and applies tiered bonuses (e.g. 2pc: +stat, 4pc: +stat, 6pc: unlocks a
  passive) — mirrors Bit Heroes' own set design.
- **Mythic items**: `MythicSkill` reference on the item, granting one
  passive/triggered effect unique to that specific item while equipped
  (e.g. "10% chance on hit to reset Parry cooldown"). Hand-authored per
  item, not procedurally rolled.
- **Runic items**: have `RuneSocketCount` (1–3); a separate `Rune`
  consumable-item type can be socketed in for a chosen small effect
  (flat stat, proc, resistance). Sockets are itemization's "choose your
  own affix" lever — deferred design, but the slot count is rolled here
  alongside the other rarity-driven fields.
- Implementation: item generation becomes `GenerateItem(template, floor,
  rarity)` rather than templates carrying final numbers directly —
  templates define base stats + which stat pool they roll from (§2b),
  generation applies both multipliers and rolls the bonus-stat count.
- This is also where `weaponDamage` (§2, MainHand) gets its floor/rarity
  scaling — same formula, not a separate system.

## 3. Enemy archetypes

One `EnemyController` behavior isn't enough. Add an `EnemyArchetype` enum
(or ScriptableObject later) that changes `TakeTurn()`:

| Archetype | Behavior |
|-----------|----------|
| **Brute** | Current behavior: chase + melee. High HP/ATK, low AGI. |
| **Skirmisher** | Melee, but tries to keep from being surrounded; higher AGI (dodgy). |
| **Ranged** | Stays 2–4 cells away, attacks in straight lines; steps away if player closes. |
| **Support** | Heals/buffs nearby enemies instead of attacking; priority target. |
| **Boss** | End-of-floor. Uses a scripted rotation (big hit → summon → charge attack with 1-turn telegraph). |

Telegraphed boss attacks (highlight target cells one turn before impact)
give the player something to play around — key to making one-action turns
feel tactical.

## 4. Weapon-driven combat

No separate hotkey ability system. Instead, the equipped **weapon type**
determines the attack pattern — the "walk into enemy" input stays the
same, but what that input *does* changes with what's in the MainHand slot.
Every equipped item (weapon or not) still contributes stats per §2.

| Weapon type | Hands | Attack pattern | Scales with |
|-------------|-------|-----------------|-------------|
| **Sword** | 1H or 2H (per item) | Single target, adjacent cell | ATK |
| **Axe** | 1H or 2H (per item) | Hits target + all cells adjacent to it (cleave) at reduced damage | ATK |
| **Mace** | 1H or 2H (per item) | Single target, adjacent, chance to stun (enemy skips next turn) | ATK |
| **Hammer** | 2H only | Single target, adjacent, higher stun chance than Mace, heavier damage | ATK |
| **Spear** | 2H only | Hits up to 2 cells in a straight line (pierce) | ATK |
| **Dagger** | 1H only (dual-wieldable in OffHand) | Single target, adjacent, but +crit chance and player moves into a free step alongside the hit | AGI |
| **Bow** | 2H only | Ranged: can attack in a straight line up to 3 cells without needing to be adjacent | AGI |
| **Staff** | 2H only | Ranged 2 cells, damage scales off Magic instead of ATK (sets up elemental gear later) | MAG |

1H variants of Sword/Axe/Mace are shield-compatible (or dagger-off-hand
compatible) per §2a; their 2H variants hit harder / have a bigger pattern
but give up the OffHand slot.

Implementation:

- `WeaponType` enum on `EquippableItem` (only meaningful for `MainHand`).
- `AttackPatternResolver.GetTargets(weaponType, attackerCell, targetCell)`
  returns the set of cells actually hit, given the player's input direction
  — this replaces the single-target call in `Entity.Attack`.
- `PlayerController.TryAct` doesn't change its input handling at all: same
  "walk into occupied cell = attack" rule. It just asks the resolver which
  cells get hit instead of assuming one.
- Unarmed/no MainHand equipped falls back to plain single-target (current
  behavior) so the game still works before any gear drops.
- Range weapons (Bow, Staff) need `TryAct` to allow attacking a non-adjacent
  occupied cell in the weapon's line of fire, not just the 4 adjacent cells
  — the main input logic change needed here.

This is also a bigger art/UI hook than a hotkey system would've been: each
weapon type reads differently on screen (cleave arc, pierce line, ranged
projectile), which suits the pixel-feedback goals in §6.

## 5. Companion (later, optional)

Bit Heroes' signature is the pet/familiar fighting beside you. Grid
version: one companion entity that acts on the player's team during the
enemy phase, using the same archetype AI pointed at enemies. Defer until
core combat is done — it reuses everything above.

## 6. Retro pixel style guide

Status key used below: **✅ done** (already in the codebase, nothing to
build), **⚠️ partial** (built but not styled/finished), **⬜ not built**.

### 6a. Resolution & camera discipline — ⬜ not built (Editor-side)

Every sprite (tiles, characters, items, UI icons) must be authored at the
same base resolution and share one grid — mixing 16x16 and 32x32 source
art in the same scene is what makes pixel games look "off."

1. Pick **one** tile size for the whole project: 16x16 or 32x32 pixels.
   Once picked, every character/item/prop sprite is drawn as a multiple
   of that size (e.g. a 1x2-tile-tall character on a 16px grid = 16x32).
2. Per-sprite Import Settings (Inspector, for every sprite in the
   project): `Filter Mode: Point (no filter)`, `Compression: None`,
   `Pixels Per Unit` = the tile size chosen in step 1 (16 or 32).
3. Add Unity's **Pixel Perfect Camera** component (2D URP package,
   `Window > Package Manager` if not installed) to the Main Camera.
   Set `Assets Pixels Per Unit` to match step 2, `Reference Resolution`
   to a low internal resolution (e.g. 320x180 or 384x216), enable
   `Upscale Render Texture` and `Pixel Snapping`. This is what stops
   sprites shimmering/wobbling as they move — mandatory, not optional,
   for this look.
4. `GridUtils.CellSize` (currently `1f`) already assumes 1 world unit =
   1 tile; no script changes needed once Pixels Per Unit matches step 2.

### 6b. Palette — ⬜ not built (art-side, no code)

Pick **one** limited palette from [lospec.com/palette-list](https://lospec.com/palette-list)
and use it for every tile, sprite, and UI element — no exceptions, no
gradients outside it. Two well-known ones that fit a dark dungeon
crawler: **DB32** (Dawnbringer 32) or **Resurrect 64**. Bit Heroes' own
look is chunky low-detail sprites + saturated colors against dark
dungeon backgrounds — a small palette forces that consistency
automatically. This is an art constraint, not something to implement in
code; just note whichever palette you pick so future art matches.

### 6c. Rarity colors — ✅ done

Already implemented exactly as specced, in `RarityVisuals.cs`:
Common `(0.25, 0.85, 0.25)` green, Rare `(0.25, 0.55, 1)` blue, Epic
`(0.65, 0.25, 0.95)` purple, Legendary `(1, 0.85, 0.1)` yellow, Set
`(0.2, 0.9, 0.9)` cyan, Mythic `(0.9, 0.15, 0.15)` red, Runic same red
pulsing to black via `HasMotionOutline` (the only animated rarity).
Used today for item-slot outlines (`ItemSlotUI.cs`); nothing left to
build here unless you also want rarity color applied to item *name
text* in tooltips/bag (`ItemTooltipUI.cs` currently shows names in plain
white — flag if you want that added).

### 6d. Combat feedback

- **Damage numbers — ✅ done.** `DamageNumberSpawner.cs` already spawns
  floating `TextMesh` text with the exact spec'd colors: white normal,
  yellow + larger (`bigCharacterSize`) crit, gray "MISS", blue "PARRY",
  green "+N" heal. It has an empty `font` field ready for a pixel font —
  see 6e for exactly what to drop in there.
- **Hit flash — ⚠️ partial, spec updated to match reality.** `Entity.cs`
  flashes the sprite **red**, not white, for `hitFlashSeconds` (0.08s).
  This was a deliberate call, not a shortcut: `SpriteRenderer.color` is
  a *multiplicative* tint, so it can only darken/recolor a sprite, never
  brighten it toward true white — a literal white flash needs an
  additive-blend material/shader, which isn't in the project. Red flash
  reads clearly as "just hit" and is the intended final look unless you
  specifically want to add an additive-blend shader later just for this.
- **Screen shake — ⬜ not built.** Spec: create `CameraShake.cs` as a
  self-bootstrapping singleton (same pattern as `DamageNumberSpawner`)
  attached to the Main Camera. Expose `Shake(float amplitudePixels,
  float durationSeconds)`; on crit, call it with roughly 2–3px amplitude
  and ~0.1–0.15s duration. Trigger points: subscribe to
  `Entity.OnAttackResolved` and shake when `result.WasCrit`, plus a
  stronger shake (~4–5px) on boss hits once bosses exist (§3/§7 step 6).
  Implementation: offset `transform.localPosition` by a random point
  inside a shrinking circle each frame for the duration, then restore to
  zero — don't touch the Pixel Perfect Camera's own transform logic
  directly, shake a child/offset instead so it doesn't fight pixel
  snapping.
- **Telegraphs — ⬜ not built, depends on bosses.** Spec (for §7 step 6,
  "Boss with telegraphed attacks"): the turn before a boss attacks, tint
  the cell(s) it's about to hit red — e.g. a semi-transparent red
  `SpriteRenderer` quad instantiated over the target cell(s) via
  `GridUtils.CellToWorld`, cleared right before the attack resolves.
  Not worth building standalone; build it alongside the first boss.

### 6e. UI — ⚠️ partial

- **Pixel font — ⬜ not built, quick to add.** Download **"Press Start
  2P"** (free, Google Fonts) or a Kenney UI font
  ([kenney.nl/assets](https://kenney.nl/assets), free, CC0), import the
  `.ttf` into `Assets/Fonts/`, then drag it onto `DamageNumberSpawner`'s
  `font` field in the Inspector. Also assign it to every `Text`
  component currently using Unity's default font — that's
  `enemyNameText` and `turnIndicatorText` on `BattleScreenUI`, plus
  whatever `Text`/`TMP_Text` components exist on `EquipmentPanelUI` and
  `ItemTooltipUI`. This is entirely an Inspector task, no script changes.
- **Segmented/chunky HP bar — ⬜ not built.** `HealthBarUI.cs` currently
  drives a plain smooth Unity `Slider` — functionally correct, visually
  generic. Two ways to restyle, in order of effort:
  1. **Cheap**: keep the `Slider`, swap its Fill Area's `Image` sprite
     for a hand-drawn segmented/notched bar graphic (art-only change,
     no code).
  2. **Full segmented bar**: replace the `Slider` with a row of small
     heart/pip `Image` icons (e.g. one pip per 10 HP), and change
     `HealthBarUI.HandleHealthChanged` to enable/disable or fade pips
     based on `current`/`max` instead of setting `slider.value`. Only
     worth it if you want the literal Bit Heroes heart-row look rather
     than a bar.
- **1px-border panels — ⬜ not built.** For every UI panel (equipment
  panel, tooltip, battle screen), use a pixel-art 9-slice border sprite
  (`Image.Sprite Type: Sliced`, 1px border in the source art) instead of
  Unity's default rounded/soft panel sprite. Art-only change per panel;
  no script changes needed since `Image` already supports sliced
  sprites — just needs a border asset and the Sprite Editor's "Border"
  handles set on it.

### 6f. Audio — ⬜ not built

Nothing exists yet. Spec: add an `AudioManager.cs` self-bootstrapping
singleton (same pattern as `DamageNumberSpawner`) with an `AudioSource`
for SFX and a separate one for looping music. Generate short chiptune
SFX with **jsfxr** ([sfxr.me](https://sfxr.me), free, browser-based) or
**Bfxr** ([bfxr.net](https://www.bfxr.net), free) — export as `.wav`,
drop into `Assets/Audio/SFX/`. Hook points, all via existing events so
no new plumbing is needed:
- Hit / crit: subscribe to `Entity.OnAttackResolved`, play a hit SFX
  always, a distinct sharper/higher-pitched crit SFX when
  `result.WasCrit`.
- Pickup: `Inventory.OnItemAdded` / potion pickup path.
- Floor transition: wherever `GameManager` currently handles floor
  change cleanup.
- Music: one loopable chiptune track per floor "tier" (e.g. every 3–5
  floors) — swap the music `AudioSource.clip` on floor transition rather
  than layering tracks.

## 7. Build order

1. ✅ `Stats` class + `CombatResolver` (damage variance, crit, dodge) —
   refactor `Entity`, keep the game running identically with default stats.
2. ✅ Damage numbers + hit flash (feedback makes everything after testable).
3. ✅ `EquippableItem` stats + rarity + enemy loot drops.
4. Weapon-driven attack patterns: Sword (baseline) → Axe (cleave) →
   Spear (pierce) → Bow (ranged, needs the `TryAct` range change) → rest.
5. Enemy archetypes: Ranged first (biggest tactical change), then
   Skirmisher, Support.
6. Boss with telegraphed attacks.
7. Companion, pixel-perfect camera pass, audio.

Each step is playable on its own — no step depends on a later one.
