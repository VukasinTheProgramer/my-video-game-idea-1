# CLAUDE.md — working agreement for this project

Unity 2D top-down turn-based pixel dungeon crawler. Unity **6000.0.80f1**, macOS.
Reference game for combat feel: **Bit Heroes Quest**.

Read this before touching anything. `COMBAT_DESIGN.md` is the design spec —
this file is *how to work on the project without breaking it*.

---

## 0. The one rule that matters most

**The code being correct proves nothing about the game working.**

Three separate full code reviews of this project (3093 lines, four parallel
reviewers) missed the three worst bugs in it, because all three lived in
**serialized data**, not C#:

| What looked fine | What was actually true |
|---|---|
| `BattleManager.cs` — complete, correct, 150 lines | Not referenced by the scene at all. `Instance` was always null. Battles never happened. |
| `EquippableItem` had stats fields, `CombatResolver` read them | All 6 item assets serialized only `slot` + `displayName`. Gear was 100% cosmetic; every weapon read as unarmed. |
| `Entity.baseStats` with a sensible default | `Enemy.prefab` had no `baseStats:` key at all → every enemy silently got `Stats.Level1Default()` (101 HP, DEF 5) instead of 10 HP. |

Unity writes **no YAML key** for a `[SerializeField]` that didn't exist when the
asset was authored. On load the field silently falls back to its C# initializer.
Nothing errors. Nothing warns. The compiler is happy.

So: **when behavior contradicts correct-looking code, inspect the data first.**

```bash
# Is this script actually in the scene? 0 hits = it never runs.
grep -h '^guid:' Assets/Scripts/**/Thing.cs.meta
grep -c "<that-guid>" Assets/Scenes/Main.unity

# Does this asset actually have the field?
grep -E "weaponType|weaponDamage|bonusStats" Assets/Equipment/Items/*.asset

# Diff a suspect prefab against a known-good sibling
grep -A 12 baseStats Assets/Prefabs/Player.prefab
```

After **any** change to a `[SerializeField]` name/type, sweep every asset and
prefab that uses it. A rename is a data migration, not a refactor.

---

## 1. How work gets done here (the loop)

There is no way to drive the Unity Editor GUI from this environment. Only the
user can click Play. Every scene/prefab/asset change goes through a throwaway
headless script.

```bash
# 1. Close the Editor first — it holds the project lock.
pkill -f "MacOS/Unity -projectPath"

# 2. Write Assets/Editor/OneTimeThing.cs with a public static Run()

# 3. Run headless
/Applications/Unity/Hub/Editor/6000.0.80f1/Unity.app/Contents/MacOS/Unity \
  -batchmode -nographics -projectPath "$(pwd)" \
  -executeMethod OneTimeThing.Run -quit -logFile /tmp/out.log

# 4. Grep for YOUR sentinel. Exit code 0 does NOT mean the method succeeded.
grep -E "THING_RESULT|error CS" /tmp/out.log

# 5. Delete the one-shot script, recompile clean, relaunch the Editor
rm Assets/Editor/OneTimeThing.cs*
open -a /Applications/Unity/Hub/Editor/6000.0.80f1/Unity.app \
  --args -projectPath "$(pwd)" -executeMethod OpenMainScene.Run
```

### Headless gotchas, all learned the hard way

- **Always log a unique sentinel** and grep it. A `Debug.LogError` inside `Run()`
  still exits 0.
- **`GameObject.Find` misses inactive objects.** Panels here start hidden. Walk
  `GetRootGameObjects()` → `GetComponentsInChildren<Transform>(true)`.
- **Wire private `[SerializeField]`s** via `new SerializedObject(c)` →
  `FindProperty(name)` → `ApplyModifiedProperties()`.
- **Play-mode tests:** entering play mode triggers a **domain reload** that wipes
  statics *and* `EditorApplication.update` subscriptions. Persist state in
  `SessionState`, re-subscribe from an `[InitializeOnLoad]` static constructor,
  and finish with `EditorApplication.Exit(0|1)` — **not** `-quit`. Without this
  the run hangs forever and orphans a Unity process.
- **`timeout` does not exist on this machine** (zsh, no coreutils). Rely on the
  tool's own timeout.
- **VS Code / OmniSharp lies.** It shows stale errors for minutes after new
  sibling `.cs` files appear. This has produced two false alarms. **The headless
  compile is the only source of truth.** Fix = reload the VS Code window.

### Verification is not optional

Non-trivial logic leaves one runnable check behind. Pattern that works here: a
`SessionState`-driven play-mode script that asserts invariants and logs
`X_RESULT: PASS/FAIL`, then deletes itself. Two suites already paid for
themselves by catching that `StartCoroutine` runs synchronously up to its first
`yield` (see §4).

Always confirm a clean full compile — **zero errors *and* zero warnings** —
before saying something is done.

---

## 2. Architecture

Single scene: `Assets/Scenes/Main.unity`. Single build target entry.

### Static registries (not MonoBehaviours)

- **`DungeonGrid`** — the source of truth: walkable cells, `Entity` occupancy,
  and one `ItemPickup` per cell. Reset by `DungeonGenerator.Generate()`.
- **`GridUtils`** — cell↔world math, Manhattan distance, `CardinalDirections`.
- **`CombatResolver`** — stateless. The **only** damage formula in the project.
- **`EquipmentLayerOrder`** — sprite sorting constants (see §3).
- **`LpcSpriteFormat`** — LPC frame counts (walk 9, slash 6, hurt 6, 64px).

### Singletons (`Instance`, set in `Awake`)

`GameManager` · `TurnManager` · `BattleManager` · `DamageNumberSpawner`
(self-bootstrapping — creates itself on first access, so it needs no scene object).

### Flow

```
GameManager.Start
  └─ GenerateFloor
       ├─ DespawnPreviousFloor      (destroys enemies/items/drops)
       ├─ DungeonGenerator.Generate (rooms → corridors → tilemap; resets DungeonGrid)
       ├─ SpawnOrMovePlayer         (player is created ONCE, then only moved)
       ├─ SpawnEnemies              (+ per-floor stat bonus via ApplyStatBonus)
       └─ SpawnItems

TurnManager: PlayerTurn ⇄ EnemyTurn
  PlayerController.Update → TryAct(direction)
      wall            → return, turn NOT spent
      empty cell      → MoveTo + maybe pick up item
      enemy in range  → BattleManager.TryEngageIfInRange → battle screen
      else            → EndPlayerTurn
```

### Combat: ONE system, TWO places

This has caused confusion once already. `CombatResolver.Resolve` is the single
formula; `Entity.Attack` is its only caller. There are two *entry points*:

1. **Battle screen** (`BattleManager` + `BattleScreenUI`) — the intended path.
2. **Inline grid attack** — a deliberate, commented **fallback** for when no
   `BattleManager` is in the scene.

**Do not "consolidate" these by deleting the fallback.** It is by design
(`COMBAT_DESIGN.md` §0).

### Rendering: layered equipment

`Entity` (body `SpriteRenderer`) + `DirectionalSpriteAnimator` (owns all
direction/action state) + `Equipment` (one child `EquipmentLayer` per equipped
slot). The animator drives `Equipment.ApplyFrame(direction, action, frameIndex)`
on **every** body sprite change — that is the sync mechanism. Never build a
second animation state machine; extend the existing one.

---

## 3. Landmines — specific things that have already bitten

### Sprite sorting is absolute, not relative
All on sorting layer 0: **floor tilemap 0, potion 1, character body 2, damage
numbers 100.** Every worn equipment layer must be **> 2** or it hides behind the
body; the "behind" variant must be **between 0 and 2** or it hides under the
floor. Both bugs shipped simultaneously (`Feet=1`, `Legs=2`, `Behind=-1`).
`EquipmentLayerOrder.BodyOrder` documents the body's value — if you change the
prefab's `m_SortingOrder`, update that constant.

### Never capture a collection index in a deferred UI callback
The equipment tooltip captured a bag **index**; sorting the bag between opening
the tooltip and pressing Equip equipped the *wrong item*. **Capture the object.**
Applies to any pooled/sorted list UI.

### Unity serializes an unassigned array as length 0, not null
Guard both: `arr != null && arr.Length > 0 && arr[i] != null`. A null *element*
assigned to `SpriteRenderer.sprite` blanks the sprite (this made the player
invisible mid-swing). Worse: an exception thrown inside a coroutine skips the
cleanup line after it — one `IndexOutOfRange` in `RunAction` permanently wedged
`actionRoutine != null` and froze facing for the rest of the run.

### `StartCoroutine` runs synchronously up to the first `yield`
So "defer this" is a **lie** unless the coroutine actually yields first. Floor
regeneration was still executing inside `Entity.Die()` — while that entity's
other `OnDeath` subscribers hadn't run yet. If you need to escape the current
call stack, `yield return null` **unconditionally** before the work.

### Event subscription order is load order
`GameManager` subscribes to enemy `OnDeath` before `BattleManager` does, so its
handler wins. Don't rely on ordering — but know it exists when tracing.

### Destruction is deferred to end of frame
After `Destroy(go)`, the object is still non-null this frame but Unity's `==`
reports null *later*. `TurnManager` snapshots its enemy list before iterating for
exactly this reason. `Entity.OnDestroy` releases grid occupancy so a destroyed
entity can't leave a cell permanently blocked.

### Space / Return is the EventSystem's Submit binding
Raw `Input.GetKeyDown(KeyCode.Space)` also re-fires whatever `Button` was last
clicked. Clear the selection (`SetSelectedGameObject(null)`) when opening a
screen that uses Space.

### LPC sheets are top-down, Unity texture space is bottom-up
Row index must be flipped: `(rowsInFile - 1 - rowIndex)`. Without it every
direction comes out mirrored.

---

## 4. Error handling conventions

**Fail loudly at the boundary; degrade safely inside.**

- **Unwired `[SerializeField]` that the system cannot work without** →
  `Debug.LogError` in `Awake` **and** make the feature no-op so it can't
  soft-lock. `BattleManager` with a null `screenUI` used to set `IsActive = true`
  with no way to attack — freezing player input and every enemy, forever, with an
  empty console. Now it errors and lets the inline fallback carry the game.
- **Never silently discard player property.** `COMBAT_DESIGN.md` §2½: an item is
  never destroyed. `PickUp` returns `bool`; the caller destroys the pickup **only**
  on `true`, otherwise it goes back on the grid. Same reasoning made
  `DungeonGrid.PlaceItem` return `bool` instead of overwriting an occupied cell.
- **Guard optional components, error on required ones.** `TryGetComponent` +
  `LogWarning` for optional; `[RequireComponent]` for genuinely required.
- **Soft-lock guards are worth their code.** The floor only advances when every
  enemy dies, so a floor that spawns zero enemies is an unwinnable dead end →
  detect it and force one enemy in.
- **Clamp, and say which direction.** `RefreshEquipmentStats` clamps HP *down*
  only (so gear-swapping can't free-heal); `RefillHealth` exists separately for
  spawn-time. Naming the asymmetry stopped it being "fixed" into a heal exploit.
- Only `Mathf.Min`/`Max` a value once and centrally — the documented 50% life
  steal cap existed in the doc and in *no* code path, letting stacked affixes
  out-heal all incoming damage.

---

## 5. Bug-vs-design boundary

**Fix bugs. Surface design decisions — don't silently redesign.**

Live example, deliberately **not** changed: `COMBAT_DESIGN.md` §1's flat
`max(1, raw - DEF)` combined with §1a's baseline DEF 5 and ATK 1 means enemy hits
and enemy **crits** both deal exactly 1 damage, while the UI shows a yellow crit
number. The code implements the doc faithfully — the *doc* produces the
degenerate result. Changing the formula would be redesigning combat feel. It was
reported with two options for the user to choose from instead.

When the doc and the code disagree, say so explicitly and state which you think
is wrong. Several such divergences were found; in some the code was right
(`Hammer` should parry — §1's list predates §2a) and in others the doc was
(`WeaponType.None` must **not** parry).

---

## 6. Style

Match the surrounding code — it has a consistent voice worth preserving.

- Comments explain **why**, and cite the spec section (`COMBAT_DESIGN.md §2a`).
  Comments that merely restate the code are noise.
- When taking a deliberate shortcut, say so and name the upgrade path. Existing
  examples: rarity "motion outline" is a colour pulse because a real marching-ants
  border needs a custom shader; the hit flash tints red because
  `SpriteRenderer.color` is multiplicative and cannot brighten to white.
- Prefer the smallest change **once you understand the flow**. Fix the root cause
  in the shared function rather than guarding each caller.
- No speculative abstractions. A `HashSet` for an 8-value enum was *deleted* in
  favour of a one-line comparison — the indirection was where a real bug hid.
- Self-building UI components (`ItemSlotUI`, `ItemTooltipUI`, `DamageNumberSpawner`)
  construct their own hierarchy in `Awake` so they work whether hand-placed or
  spawned at runtime, with no prefab. Keep that pattern for new UI pieces.

---

## 7. Current state

**Working:** floor generation + progression, BFS pathfinding, turn loop, 10-slot
layered equipment with direction-aware rendering, stat-driven combat
(crit/dodge/parry/life steal), 1v1 Bit Heroes-style battle screen, character panel
(stats + equipment + bag, right-click for item tooltip with Equip/Unequip),
damage numbers, hit flash, screen shake on crit, floor scaling, game over.

**Known gaps — do not assume these work:**

- **No `LootTable` asset exists** (only the script), so `EnemyController.Die`
  never drops gear. `COMBAT_DESIGN.md` §7 step 3 is marked ✅ but is
  non-functional.
- **Damage numbers are invisible during battles** — world-space `TextMesh` at
  dungeon positions, behind a 0.97-alpha overlay canvas.
- Battle turns are **manual** (Attack button / Space), not auto-resolving.
- Strictly **1v1**. No party, no multi-enemy encounters, no fleeing.
- §2c item scaling by floor/rarity does not exist — `LootTable` returns the shared
  ScriptableObject **template**, so two drops of one entry are the *same
  reference*. (This is why `Equipment.Equip` must early-return when re-equipping
  an already-worn item.)
- Not built: weapon-driven attack patterns (§4), enemy archetypes beyond Brute
  (§3), boss telegraphs, companion (§5), pixel-perfect camera, audio.
- §1a's skill points (`attackPoints`/`unspentSkillPoints = 10`) don't exist;
  `Stats.Level1Default()` hardcodes the pre-spend baseline.
- **`README.md` is badly stale** — it claims no Unity project exists yet and
  describes AI that was replaced by BFS. Treat this file and `COMBAT_DESIGN.md`
  as authoritative.
