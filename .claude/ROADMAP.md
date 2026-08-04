# Roadmap

Things designed and/or discussed but **not built yet**. When something here
gets implemented, move its writeup into `IMPLEMENTED.md` (condensed to
reflect what actually shipped) and delete it from here. When you tell me
about something new you want to add later, it goes here first.

---

## Weapon handedness & shields

`EquippableItem` needs a `Handedness` field (per-item, not fixed per
`WeaponType` — Sword/Axe/Mace each come in both 1H and 2H variants; Dagger
is always 1H, Hammer always 2H). A `Shield` item kind, OffHand-only, never
in MainHand — carries Block (halves incoming damage on trigger).

Equip-time rules to add in `Equipment.Equip`:
- Equipping a 2H item into MainHand auto-unequips OffHand.
- Equipping a Shield into OffHand requires a 1H item in MainHand.
- Equipping a 1H Sword/Axe/Mace/Dagger into OffHand (dual-wield) requires a
  1H item in MainHand.
- MainHand never accepts a Shield.
- v1 scope: OffHand item only contributes stats, no second attack per turn.

UI: `EquipmentPanelUI` should gray out/block-drop OffHand while a 2H item
is equipped.

## Weapon-driven attack patterns

No hotkey ability system — the equipped weapon type changes what the
existing "walk into enemy" input does:

| Weapon | Hands | Pattern | Scales with |
|--------|-------|---------|--------------|
| Sword | 1H or 2H | Single target, adjacent | ATK |
| Axe | 1H or 2H | Cleave: target + adjacent cells, reduced dmg | ATK |
| Mace | 1H or 2H | Single target, chance to stun | ATK |
| Hammer | 2H only | Single target, higher stun chance, heavier dmg | ATK |
| Spear | 2H only | Pierce: up to 2 cells in a line | ATK |
| Dagger | 1H only, dual-wieldable | Single target, +crit, free step alongside hit | AGI |
| Bow | 2H only | Ranged up to 3 cells in a line | AGI |
| Staff | 2H only | Ranged 2 cells | MAG |

Implementation: `AttackPatternResolver.GetTargets(weaponType, attackerCell,
targetCell)` replaces the single-target call in `Entity.Attack`. Unarmed
falls back to current single-target behavior. Bow/Staff need
`PlayerController.TryAct` to allow attacking non-adjacent cells in line of
fire.

This is the next build-order item — everything above it (stats, damage
numbers, loot, leveling, equipment slots) is done and playable on its own.

**Build order within this item**, split by whether it's a pure targeting
change or needs new combat state:

1. **Pure targeting changes first — Sword → Axe → Spear → Bow → Staff.**
   Just changes which cells `GetTargets` returns; no new state on `Entity`
   or `CombatResolver`, so this is the low-risk/high-value slice to build
   first.
2. **Special effects as a follow-up pass — Mace, Hammer, Dagger.** These
   aren't pure damage-formula work:
   - Mace/Hammer's stun needs an enemy to skip its next turn (new state on
     `Entity`/`EnemyController.TakeTurn`, not just a targeting change).
   - Dagger's "free step alongside the hit" needs to interact with
     `MoveTo`/turn-ending (attacking without spending the whole turn's
     move). Build this after the base targeting pass is proven.

## Stat rolls by handedness

1H Sword/Axe/Mace/Dagger can roll `DEF` as a bonus stat; their 2H
counterparts (and Spear/Bow/Staff) exclude DEF and MDEF from their roll
pool entirely — that budget goes into ATK/AGI/MAG/crit/etc. instead. Needs
two stat pools (`oneHandedRollableStats`, `twoHandedRollableStats`) checked
at item-generation time.

## Item generation: floor & rarity stat scaling

Right now `EquippableItem.itemLevel` and `bonusStats` are hand-authored per
template — the same "Sword" template has identical stats regardless of
which floor it drops on. Planned: `GenerateItem(template, floor, rarity)`
rolls final stats at drop time:

```
itemStats = baseTemplateStats * floorMultiplier(floor) * rarityMultiplier(rarity)
```

| Rarity | Bonus stat rolls | Multiplier | Notes |
|--------|-------------------|------------|-------|
| Common | 1 | 1.0x | baseline |
| Rare | 2 | 1.15x | |
| Epic | 3 | 1.3x | |
| Legendary | 4 | 1.5x | + guaranteed weapon-defining affix |
| Set | 4 | 1.5x | + set bonus at 2/4/6 pieces worn |
| Mythic | 5 | 1.7x | + one unique hand-authored passive |
| Runic | 6 | 1.9x | + empty rune socket(s) |

Also needed: `SetId` on `EquippableItem` + `Equipment` tracking equipped
set-piece counts for tiered bonuses; `MythicSkill` reference for Mythic's
unique passive; `RuneSocketCount` (1-3) + a `Rune` consumable item type for
Runic sockets. Floor multiplier reuses the same scaling hook `GameManager`
already applies to enemy stats.

## Currency: gold & gems

Nothing exists today — no wallet, no prices, no shop, no gold anywhere in
the codebase.

Two currencies with deliberately non-overlapping jobs: **gold buys more
attempts, gems buy better odds.** The failure mode to design against is gems
becoming "just a lot of gold", at which point the second currency is dead
weight.

| | Gold | Gems |
|---|---|---|
| Volume | Constant trickle | Rare |
| Source | Every enemy, every floor clear, selling junk | Bosses, low-% roll on high-rarity enemies |
| Buys | Potions, gear stock, upgrades, respec | Rerolls, guaranteed rarity, revive, runes |
| Intended feel | Spend freely | Hoard and agonize |

Gems are **earned only** — no real-money path, and gold must never convert
into gems (that collapses the two back into one currency).

### Sources

Gold drops from `EnemyController.Die()` as a rolled range scaled by floor,
reusing the same per-floor scaling hook `GameManager` already applies to
enemy stats. Plus a floor-clear bonus, plus selling bag items (price derived
from `Rarity` + `itemLevel`).

### Sinks

A currency without a sink is a score counter, and there is nothing to buy in
the game today — so gold ships *with* at least one sink or it ships dead.

| Sink | Currency | Notes |
|------|----------|-------|
| Merchant shop | Gold | Rotating stock per floor tier: potions + gear. |
| Sell from bag | Gold (income) | Also the release valve the bag currently lacks — `Equipment.Equip`/`Unequip` never destroy an item and there is no drop action anywhere, so junk accumulates in an unlimited bag with nowhere to go. |
| Item upgrade / reforge | Gold | Bump `itemLevel`, or reroll `bonusStats`. Depends on `GenerateItem(template, floor, rarity)` above — reforge is a re-roll through that same function, not a second stat-rolling code path. |
| Stat respec | Gold | **Reverses a shipped design call.** `PlayerProgression` is documented today as "no respec — once spent, permanent". Priced respec is a deliberate reversal of that, not a bug fix. |

Gem sinks: reroll an item's affixes with a rarity floor, guaranteed-rarity
purchase, revive-on-death, and buying `Rune`s once Runic sockets exist.
Every one of these depends on the item-generation work above, which is why
gems come last in the build order.

### Persistence — the expensive part

Decided: **currency persists across runs.** Death currently reloads the
scene (`GameOverUI`) and every bit of state is rebuilt from prefabs, so
there is no save system to hang this on. Persistent gold means building one.

Minimum viable: a `SaveData` blob as JSON in
`Application.persistentDataPath` (not `PlayerPrefs` — this grows), written
on wallet change and floor transition, read in `GameManager.Start`.

The trap: the moment a save file exists, "what else persists?" stops being
hypothetical. Level, stat points, bag contents, and equipped gear are all
equally saveable, and each choice changes the genre — persistent gear is
Bit Heroes-style meta progression, wiped gear is a roguelike with a bank.
**Answer that before writing the save format**, because the format is the
commitment. Parked under Open/undecided below.

### Where it lives

Mirror `Inventory`: a `Wallet` MonoBehaviour on the player alongside
`Inventory`/`PlayerProgression` — two ints, `Add`, `TrySpend` returning
`bool`, and `OnGoldChanged`/`OnGemsChanged` events so UI reacts without
polling (same event pattern `PlayerProgression` already uses).

`TrySpend` returns `bool` for the same reason `ItemPickup.PickUp` and
`DungeonGrid.PlaceItem` do (CLAUDE.md §4): the caller commits the
transaction **only** on `true`, so a purchase can never go through on
insufficient funds.

### Build order

1. `Wallet` + gold from `EnemyController.Die` + a HUD counter. Playable but
   purposeless on its own.
2. Selling from the bag — `ItemTooltipUI` gains a Sell action next to
   Equip/Unequip. First real sink, and it needs no new UI screen.
3. Merchant shop screen.
4. Save system + persistence — only after the persistence-scope question is
   answered.
5. Gems and gem sinks last, since each depends on item generation.

All of this sits *after* weapon-driven attack patterns, which is still the
next build item.

## Enemy archetypes

Only one enemy behavior exists today (leashed wander/chase + melee, i.e.
"Brute" — see `IMPLEMENTED.md` → "Enemy AI — leashed wander/chase"). Add an
`EnemyArchetype` enum/ScriptableObject that changes `EnemyController.TakeTurn`:

| Archetype | Behavior |
|-----------|----------|
| Brute | Current behavior — leashed wander/chase + melee, high HP/ATK, low AGI |
| Skirmisher | Melee, avoids being surrounded, higher AGI |
| Ranged | Stays 2-4 cells away, attacks in lines, backs off if approached |
| Support | Heals/buffs nearby enemies, priority target |
| Boss | Scripted rotation (big hit -> summon -> telegraphed charge) |

Build Ranged first (biggest tactical shift), then Skirmisher, then Support.

## Boss with telegraphed attacks

The turn before a boss attacks, tint the target cell(s) red (semi-transparent
`SpriteRenderer` quad via `GridUtils.CellToWorld`, cleared right before the
hit resolves) so the player can react. Build alongside the first boss, not
standalone.

## Companion

One companion entity that fights alongside the player during the enemy
phase, reusing the archetype AI pointed at enemies instead of the player.
Deferred until core combat (attack patterns, archetypes) is proven.

## Retro pixel style — remaining items

- **Resolution & camera discipline** (Editor-side): pick one tile size
  (16x16 or 32x32) project-wide, set `Filter Mode: Point`,
  `Compression: None`, `Pixels Per Unit` = tile size on every sprite, add
  Unity's Pixel Perfect Camera (2D URP) to Main Camera.
- **Palette** (art-side): pick one limited palette from
  lospec.com/palette-list (e.g. DB32, Resurrect 64) and hold every tile/
  sprite/UI element to it.
- **Pixel font**: download "Press Start 2P" or a Kenney UI font, assign to
  `DamageNumberSpawner.font` and every `Text` component currently on
  Unity's default font (`BattleScreenUI`, `EquipmentPanelUI`,
  `ItemTooltipUI`). Editor-only, no script changes.
- **Segmented/chunky HP bar**: `HealthBarUI` currently drives a plain
  smooth `Slider`. Either reskin the Slider's fill sprite (art-only) or
  replace it with a row of heart/pip icons driven by
  `HealthBarUI.HandleHealthChanged`.
- **1px-border panels**: 9-slice pixel-art border sprites on every UI
  panel instead of Unity's default rounded panel (art-only, `Image`
  already supports sliced sprites).
- **Audio**: nothing exists yet. `AudioManager` self-bootstrapping
  singleton, SFX via jsfxr/Bfxr (hit, crit, pickup, floor transition),
  one loopable chiptune track per floor tier.

## Open / undecided

- **What persists besides currency** once a save system exists — level,
  stat points, bag, equipped gear. Blocks the save format, and effectively
  picks the genre (meta-progression vs roguelike-with-a-bank).
- Whether priced respec fully replaces `PlayerProgression`'s "no respec"
  rule or stays limited (once per floor? escalating cost per use?).
- Level cap for `PlayerProgression` (none currently).
- Whether Magic/DEF/MDEF ever become point-allocatable (e.g. a "cast
  without a Staff" mechanic) — currently gear-only by design.
- Rarity name text color in `ItemTooltipUI` (currently plain white,
  could match `RarityVisuals` outline colors).
