# In progress

Work that is **started but not finished**, and open questions blocking it.
The third bucket alongside the other two:

| File | Holds |
|------|-------|
| `IMPLEMENTED.md` | Code that exists (⚠️ marks code that exists but never runs) |
| `ROADMAP.md` | Designed, not built |
| `.claude/IN_PROGRESS.md` | Mid-flight work, found-but-unfixed bugs, decisions waiting on the user |

Rule: an item leaves this file in one direction only — **finished** (write it
into `IMPLEMENTED.md`), **deferred** (move to `ROADMAP.md`), or **dropped**
(delete it, and say why in the commit). Nothing lingers here as "someday".

Last updated: 2026-08-04. Branch: `testing`.

---

## 1. Three dead systems — found, diagnosed, NOT fixed

All three are **serialized-data only**, no C# to write. This is `CLAUDE.md`
§0's bug class, currently live in the repo. Verified 2026-08-04 by GUID grep
against `Main.unity` / `Player.prefab` / `Enemy.prefab`.

| What's dead | Why | Fix |
|---|---|---|
| **XP / levels / stat points** | `PlayerProgression` is not a component on `Player.prefab`, and nothing `AddComponent`s it at runtime. Every caller null-guards (`progression?.AddXP(xpReward)`), so it no-ops silently — no error, no warning. | Add the component to `Player.prefab`. |
| **Enemy gear drops** | No `LootTable` asset was ever authored; `Enemy.prefab` serializes `lootTable: {fileID: 0}`. | Author one `LootTable` asset, assign it on the prefab. |
| **5 stat-only equipment slots** | `Main.unity` wires 10 `slotUIs`/`slotLabels`; `EquipmentPanelUI.SlotOrder` now has 15. Length guards stop the throw, so Belt/Ring1/Ring2/Trinket1/Trinket2 just have no UI square. | Build 5 more slot UIs in the scene. |

**Next action:** one throwaway headless script doing all three (`CLAUDE.md`
§1 loop — Editor must be closed first). Wire the private `[SerializeField]`
arrays via `SerializedObject`, and log a sentinel per fix; three separate
greps, since a partial success here looks identical to a full one.

Also unresolved: `Enemy.prefab` has **no `xpReward` key**, so it silently
falls back to the C# default of 10. Harmless today only because the XP path
is dead — becomes a real tuning bug the moment `PlayerProgression` is wired.

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

## 3. Deferred: source-comment citation sweep

~40 comments across 25 `.cs` files still cite `COMBAT_DESIGN.md §N`, which no
longer exists (`Stats.cs` "§1/§1a", `Entity.cs` "§6", `Rarity.cs` "§2c", …).
Comment-only, so zero compile risk, but each needs a judgment call on which
new section it means — not a `sed` job.

`CLAUDE.md` §6 now says to cite by **section title, not number**, so anything
touched from here on should follow that.

## 4. Unverified: the 15-slot commit never got a headless compile

`0821405f7` (Torso→Chest, Arms→Hands, +5 slots) was committed without a clean
compile run — the Editor was open and `CLAUDE.md` §1 requires closing it.
Comment and enum changes only, and the enum ordinals were checked to be
stable (`slot: 5` still resolves to Chest, new members appended not inserted),
but **"probably fine" is not the standard this project holds** — §1 wants zero
errors *and* zero warnings confirmed.

Fold this into the §1 headless run above: same script, same shutdown, one
compile check covers both.
