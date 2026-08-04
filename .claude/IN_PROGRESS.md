# In progress

Work that is **started but not finished**, and open questions blocking it.
The third bucket alongside the other two:

| File | Holds |
|------|-------|
| `IMPLEMENTED.md` | Runs in the game — C# exists **and** scene/prefab references it |
| `IN_PROGRESS.md` | Code written but not wired, found-but-unfixed bugs, decisions waiting on the user |
| `ROADMAP.md` | Designed, no code |

The split that matters is between this file and `IMPLEMENTED.md`: **written is
not wired.** A finished, correct, compiling class that no scene object
references does nothing at all, and Unity reports no error for it
(`CLAUDE.md` §0). That is the single most expensive bug class in this project,
so it gets its own bucket instead of a footnote in the "done" list.

Rule: an item leaves this file in one direction only — **finished** (write it
into `IMPLEMENTED.md`), **deferred** (move to `ROADMAP.md`), or **dropped**
(delete it, and say why in the commit). Nothing lingers here as "someday".

Last updated: 2026-08-04. Branch: `testing`.

---

## 1. Two dead systems remain — one fixed

All **serialized-data only**, no C# to write. This is `CLAUDE.md` §0's bug
class, currently live in the repo. Verified 2026-08-04 by GUID grep against
`Main.unity` / `Player.prefab` / `Enemy.prefab`.

| What's dead | Why | Fix |
|---|---|---|
| ~~XP / levels / stat points~~ | Fixed 2026-08-04, see below. | — |
| **Enemy gear drops** | No `LootTable` asset was ever authored; `Enemy.prefab` serializes `lootTable: {fileID: 0}`. | Author one `LootTable` asset, assign it on the prefab. |
| **5 stat-only equipment slots** | `Main.unity` wires 10 `slotUIs`/`slotLabels`; `EquipmentPanelUI.SlotOrder` now has 15. Length guards stop the throw, so Belt/Ring1/Ring2/Trinket1/Trinket2 just have no UI square. | Build 5 more slot UIs in the scene. |

**Next action:** one throwaway headless script doing both remaining fixes
(`CLAUDE.md` §1 loop — Editor must be closed first). Wire the private
`[SerializeField]` arrays via `SerializedObject`, and log a sentinel per fix —
separate greps, since a partial success here looks identical to a full one.

Now unresolved for real (not "harmless until wired" anymore): `Enemy.prefab`
has **no `xpReward` key**, so it silently falls back to the C# default of 10
for every enemy. Now that `PlayerProgression` is live, this is a live tuning
gap — every enemy currently grants identical XP regardless of difficulty.

### 1a. Leveling & stat points — FIXED 2026-08-04, moved to `IMPLEMENTED.md`

`PlayerProgression` is now a component on `Player.prefab`
(`Assets/Editor/WirePlayerProgression.cs`, run headless, deleted after).
Verified live, not just in the YAML: a play-mode check
(`Assets/Editor/VerifyProgressionWiring.cs`, also deleted after) spawned the
real player via `GameManager`, called `AddXP(100)`, and confirmed
`OnLevelUp` fired once, `Level` incremented, and `AvailableStatPoints`
incremented — then exited 0. Full description now lives in
`IMPLEMENTED.md` → "Leveling & stat points".

Also closes item 4 below — that same headless run was a full recompile with
zero errors and zero warnings, covering `0821405f7` too.

Checked and ruled out during this fix: `Stats.Level1Default()` (ATK 1, AGI 1,
maxHp 101) is the plain pre-spend baseline, not a hidden pre-spent version of
the 10 starting points — no double-grant risk.

### 1b. Enemy loot — the drop path that never rolls

Also moved out of `IMPLEMENTED.md`. `EnemyController.Die()` is written to roll
`LootTable` (ScriptableObject, weighted drops) and spawn an
`EquipmentDropPickup` on the enemy's cell, and `GameManager` does already
track spawned drops for floor-transition cleanup. That half works.

What's missing is data: **no `LootTable` asset was ever authored**, and
`Enemy.prefab` serializes `lootTable: {fileID: 0}`. The roll is skipped.

When authoring the asset, know that `LootTable` returns the shared
ScriptableObject **template** — two drops of one entry are the *same
reference*, which is why `Equipment.Equip` early-returns on an already-worn
item. Per-drop stat rolls are `ROADMAP.md` → "Item generation".

## 2. Currency — designed, needs a decision before any code

Design is written up in `ROADMAP.md` → "Currency: gold & gems". Decided:
gold + gems, gold persists across runs, four gold sinks (shop, selling from
bag, upgrade/reforge, priced respec).

**Blocked on:** persistence means building a save system, and the save
*format* commits to what else persists — level, stat points, bag, equipped
gear. That choice picks the genre (meta-progression vs roguelike-with-a-bank),
so it cannot be deferred past the format. Parked in `ROADMAP.md` →
"Open / undecided".

Not started, and correctly behind weapon-driven attack patterns in the build
order.

## 3. ~~Deferred: source-comment citation sweep~~ — DONE 2026-08-04

All 38 `COMBAT_DESIGN.md §N` comments across 22 `.cs` files repointed to
`IMPLEMENTED.md`/`ROADMAP.md`/`IN_PROGRESS.md` section titles, each read in
context rather than mechanically replaced — several old numbers (`§1`, `§2c`)
meant different things in different files depending on whether the claim
next to them was shipped or planned, so a blind `sed` would have mis-mapped
some. Confirmed zero remaining hits by grep, and a full headless recompile
came back with zero errors and zero warnings (comment-only change, no
behavior touched).

`CLAUDE.md` §6 already says to cite by **section title, not number**, so this
class of rot shouldn't recur.

## 4. ~~Unverified: the 15-slot commit never got a headless compile~~ — CLOSED

Resolved as a side effect of §1a's headless run 2026-08-04: same project,
zero `error CS` / `warning CS` in the log, so `0821405f7` (Torso→Chest,
Arms→Hands, +5 slots) is now confirmed to compile clean.
