# Turn-Based Pixel Dungeon

A 2D top-down turn-based pixel dungeon crawler in Unity. Procedurally generated
floors, grid movement, one action per turn, and stat-driven loot-powered combat
resolved on a dedicated 1v1 battle screen.

Combat feel is modelled on **Bit Heroes Quest**: gear is the progression system,
and fighting an enemy transitions out of the dungeon view into a turn-based
encounter screen.

- **Engine:** Unity **6000.0.80f1** (Unity 6 LTS)
- **Scene:** `Assets/Scenes/Main.unity` (the only scene)
- **Design spec:** [`COMBAT_DESIGN.md`](COMBAT_DESIGN.md)
- **Contributor / AI-assistant working notes:** [`CLAUDE.md`](CLAUDE.md)

---

## Running it

1. Open the project folder in Unity Hub with editor **6000.0.80f1**.
2. Open `Assets/Scenes/Main.unity`.
3. Press **Play**.

The dungeon generates, the player spawns in room 0 with a starting weapon
equipped, and one or more enemies spawn in each remaining room.

## Controls

| Input | Action |
|---|---|
| **WASD** / **arrow keys** | Move one cell (also picks up items you step on) |
| Walk toward an enemy | Enters the battle screen once within engage range |
| **Space** / **Enter**, or the **Attack** button | Attack, during a battle |
| Portrait or equipment button (bottom-right) | Open/close the character panel |
| **Right-click** an item (equipped slot or bag) | Item tooltip with stats + Equip/Unequip |
| Sort buttons | Sort bag by rarity or item level |

Walking into a wall does **not** consume your turn. Clearing every enemy on a
floor advances you to the next, deeper floor.

---

## Project layout

```
Assets/
  Scenes/Main.unity            the entire game
  Prefabs/                     Player, Enemy, HealthPotion
  Equipment/Items/             6 EquippableItem assets (dagger, longsword,
                               shield, plate torso/legs, helm)
  Sprites/Tileset/             pixel art (see Credits)
    LPC-Equipment/             raw per-piece LPC equipment library
  Editor/
    LpcSpriteSlicer.cs         slices LPC sheets into per-frame sprites
    OpenMainScene.cs           -executeMethod helper for headless launches
  Scripts/
    Core/
      DungeonGrid.cs           walkable cells, entity occupancy, items
      GridUtils.cs             cell <-> world math, distances
      TurnManager.cs           PlayerTurn/EnemyTurn state machine
      CombatResolver.cs        the single damage formula
      Stats.cs                 stat block + operator +
      Pathfinder.cs            BFS shortest path
      LpcSpriteFormat.cs       LPC frame-count constants
    Entities/
      Entity.cs                base: grid, health, movement, attack
      PlayerController.cs      input -> one grid action
      EnemyController.cs       chase + engage AI, loot on death
      DirectionalSpriteAnimator.cs  4-directional walk/attack/hurt clock
    Equipment/
      Equipment.cs             10 slots as layered child sprites
      EquipmentSlot.cs / EquippableItem.cs / EquipmentLayer.cs
      EquipmentLayerOrder.cs   sprite sorting orders
      Inventory.cs             unlimited bag
      Rarity.cs / RarityVisuals.cs / WeaponType.cs
    Dungeon/DungeonGenerator.cs   rooms + corridors + tilemap
    Items/                     ItemPickup base, equipment drops, potions, LootTable
    Managers/
      GameManager.cs           run lifecycle, floor generation + scaling
      BattleManager.cs         battle screen encounter flow
    UI/                        battle screen, character panel, item slots +
                               tooltip, damage numbers, health/floor/turn
```

## Architecture in one paragraph

`DungeonGrid` is a static registry and the source of truth for the map.
`GameManager`, `TurnManager`, and `BattleManager` are singletons. `TurnManager`
alternates player and enemy phases; `PlayerController` converts input into
exactly one action per turn. Combat has **one** resolver (`CombatResolver`) with
two entry points: the battle screen (intended) and an inline grid attack kept as
a deliberate fallback. Characters render as a bare body plus one child
`SpriteRenderer` per equipped item, kept in sync by `DirectionalSpriteAnimator`
driving `Equipment.ApplyFrame` on every body sprite change.

Read [`CLAUDE.md`](CLAUDE.md) before making changes — it documents the
non-obvious pitfalls (sprite sorting is absolute, serialized assets go stale
after refactors, `StartCoroutine` runs synchronously to the first `yield`).

---

## What works

Floor generation and progression with per-floor difficulty scaling · BFS
pathfinding · turn loop · 10-slot layered equipment with direction-aware
rendering (weapons tuck behind the body when facing away) · stat-driven combat
with crit / dodge / parry / life steal · 1v1 battle screen with Agility-based
initiative · character panel showing live stats, equipment and bag · right-click
item tooltips · floating damage numbers · hit flash · game over.

## Known gaps

Don't assume these work — they're specified but not finished:

- **No `LootTable` asset exists** (only the script), so enemies never actually
  drop gear.
- **Damage numbers are invisible during battles** — they're world-space text at
  dungeon positions, hidden behind the battle overlay.
- Battle turns are **manual**, not auto-resolving.
- Strictly **1v1** — no party members, no multi-enemy encounters, no fleeing.
- Item stats are hand-authored per template; the design's floor/rarity stat
  scaling (§2c) isn't implemented, so loot doesn't get better with depth.
- Not built: weapon-driven attack patterns (cleave/pierce/ranged), enemy
  archetypes beyond the basic chaser, boss telegraphs, companion, pixel-perfect
  camera, audio.
- Skill-point allocation doesn't exist; starting stats are hardcoded.

---

## Credits and licensing

**Read this before distributing the game.** The art is not public domain and
carries share-alike obligations.

| Asset | Source | License |
|---|---|---|
| Character bases & equipment (`Sprites/Tileset/LPC-Equipment`, `Knight`) | [Universal LPC Spritesheet Character Generator](https://github.com/sanderfrenken/Universal-LPC-Spritesheet-Character-Generator) | **GPL 3.0**, **CC-BY-SA 3.0**, **OGA-BY 3.0** |
| Dungeon tileset | 0x72 DungeonTileset II | see `Sprites/Tileset/README_0x72_DungeonTilesetII.txt` |
| Character sprites | RunninBlood | see `Sprites/Tileset/Character/README_RunninBlood.txt` |

Per-asset authors and licenses are listed in:
- `Assets/Sprites/Tileset/Knight/CREDITS_LPC.txt`
- `Assets/Sprites/Tileset/LPC-Equipment/LICENSE_LPC_Repo.txt`

LPC art requires **attribution**, and CC-BY-SA / GPL are **copyleft**: derivative
artwork must be released under compatible terms, and shipping these assets means
crediting every listed author. This constrains a closed-source commercial
release — if that's the goal, either replace the art with assets you own or hold
a permissive licence for, or confirm the obligations you're accepting. Get proper
advice rather than relying on this paragraph.

Unity Personal is free for PC development under Unity's revenue threshold.
