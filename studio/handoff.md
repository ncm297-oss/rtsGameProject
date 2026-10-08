# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-07-2014** (sixth full session of 2026-10-07, cap 8). Base
`16fa184` = `origin/main`. Checks at PLAN: build 0 warnings; smoke PASS on `main` (tick 86, no ERROR); non-Perf suite
rerun (result in the session log); no open S1 / S2; M3 at 7 / 8 (only "Playable" left). Bug numbers this session: sim
from **BUG-0135**, view from **BUG-0145**, data from **BUG-0155**.

## Where we are

- `main`: M0, M1, M2 Done; M3 7 / 8 (resources, gather loop, placement + construction + repair, production + rally +
  pop, Age II + unlocks + Forge upgrades, factions fully defined in data, HUD). Open bugs 19: S3 10, S4 9.
- Hardening counters: sim 0 / 4, view 0 / 4, data 3 / 4 (D4 this session is the data track's end-of-M3 hardening).
- **M3 sign-off at this session's ACCEPT** if the view's scripted Playable run passes and D4 lands: every track's
  hardening done, coverage rows ✅, no S1 / S2. Then the retro, M4 set to Next.
- **Golden rule this session:** the **data track (D4)** regenerates `cross_map_seed1.replay` (`data-hash` + `checksum`
  only; every `k` line byte-identical) because tech descriptions are in `ContentHash`. The sim's M4-1 touches **no**
  data file, **no** replay format, and keeps every `k` line byte-identical (new per-unit fields hashed only when
  non-default, the M1-7 precedent). Merge order sim, view, data; the conductor reruns `ReplayGoldenTests` on the merge.

## Sim track

### Current session plan: M4-1 — combat slice 1 (feature, QA full, bugs from BUG-0135)

**Goal.** Units fight: an attack-moving unit acquires enemies in sight and kills them with the damage formula; idle and
holding units fight back. This is the sim half of M4's first two criteria ("attack-move, chase, retaliation, target
acquisition priorities" and "damage formula with type x class table and bonuses; unit tests include the worked example")
plus death. Melee only; projectiles, the explicit `Attack(target)` order, fog, abilities and stealth are later slices.

**Scope (in).**
1. `DamageCalc.Compute(attack, damageType, bonusVs, armorClass, armor)` in one place (docs/03 "Combat implementation"):
   `raw = attack x table[type][class] x bonusVs[class]`; `max(1, round(raw) - armor)` for melee / pierce / siege,
   `max(1, round(raw))` for magic (`ignoresArmor` from `damage_table.json`). Round = half up, computed as
   `floor(raw + 0.5)` in float; document it. Attack and armor include `TechState.Bonus` (attack / armor) for the owner.
2. Per-unit combat state in `UnitStore` (struct arrays, no lists): `Hp` (set to the def's hp at alloc), `Target`
   (handle, unit or building: a kind + handle, or two handles), `CooldownTicks`, `WindupTicks`, `LastAttacker`
   (handle, for the retaliation priority), `AnchorPosition` for the retaliation leash. All reset on Alloc / Free.
   **Hash:** every new field goes into `StateHash`, but **conditionally** (added only when it differs from its
   default: hp below max, a live target, a running cooldown or wind-up, an attacker set), as M1-7 did with queue entries,
   so a world with no fight hashes exactly as before and the golden's `k` lines do not move. `StateHashTests`'s
   reflection audit classifies the new fields.
3. **Target acquisition** (phase 7, `OrderSystem` or a new `CombatSystem.Acquire`): Idle, AttackMoving, Holding
   units scan every 4 ticks, staggered by slot (`slot % 4 == tick % 4`), for enemies within **sight** (`UnitDef.Sight`)
   through the spatial hash only. Priority from docs/03: enemies attacking me > units that can attack (attack value >
   0) > other units > buildings; then nearest (squared float distance); ties to the lowest slot. One pass, no sort, no
   allocation (preallocated scratch). Enemy = any other owner (teams come with M6). Buildings: scan the building
   store's live entries by distance to the footprint's nearest cell (or a grid lookup); bounded and measured.
4. **Chase / retaliation / hold:** an **AttackMoving** unit chases its target (walks toward it; re-picks each scan if a
   better target appears under the priority rule; when none is in sight it resumes its attack-move leg to the original
   destination, which it keeps). An **Idle** unit hit or scanning an enemy retaliates and chases up to its **sight
   radius from its anchor** (where it stood), then walks back to the anchor and goes Idle (Producer default; docs/03
   gets the rule). A **Holding** unit never moves: it attacks whatever comes into range, else stands. A Moving unit
   (plain `Move`) never acquires or retaliates. Gathering / Returning / Building workers do not retaliate (they are
   workers; the player pulls them). `Stop` / `Move` / `HoldPosition` / any other unqueued order clears the target.
5. **Attack timing** (phase 10 `CombatSystem.Run`): in range (`attack.range + both radii`, edge to edge, same as docs
   "Range in meters, edge to edge") and cooldown 0 → state `Attacking`, wind-up starts (`WindupTicks` from data);
   at the wind-up point a melee hit is **queued** (attacker, target, damage); then cooldown (`CooldownTicks`) runs; a
   target leaving range during wind-up still takes the hit if within range + 0.5 m at the damage point, else the
   wind-up is lost (Producer default; document). Facing turns to the target. An `Attacking` unit does not move.
6. **Damage and death** (phase 11): queued hits apply in attacker slot order, so tick order never decides who "shot
   first" within a tick (both units of a mutual kill die). Unit hp ≤ 0 → dead: freed (handle recycled, pop released
   through the ledger, selection-safe through the generation), its target slot cleared on everyone targeting it
   (re-acquire next scan). A per-tick **death event buffer** (preallocated: victim handle, victim type, owner, killer
   owner, position; cleared at phase 13) and per-player `Kills` / `Losses` counters (hashed) for the view and stats.
   Building hp → `BuildingStore.Damage` (already frees at 0: an opening change; its queue refunds, pop provided goes).
7. **Docs:** docs/03 "Implementation (M4-1)" (fields, phases, scan cadence, priority, leash, wind-up rule, hash scheme,
   what the Perf rows measure), "Orders and unit states" updated (`Chasing` / `Attacking` real now; `Attack(target)`
   still "not yet", planned for M4-2 with a replay-format bump carrying a target handle), docs/02 untouched unless a
   number is wrong, docs/01 change-log row for the Producer defaults (leash, wind-up grace, workers don't retaliate).

**Out of scope:** projectiles / misses / splash / friendly fire / min range (M4-2); the explicit `Attack(target)` unit
order and any `Command` field or replay-format change (M4-2); fog / vision (M4-3); abilities, statuses, zones (M4-4);
stealth (M4-5); towers' attacks; corpses / rubble (view); the counter-triangle scenario tests (M4-2+); **any change
under `game/data/**`** (the data track moves `data-hash` this session); `ViewApi` (the view asks later); AI.

**Acceptance criteria.**
1. `DamageCalcTests`: the worked example `9 x 0.6 x 1.3 = 7.0 → 7 - 1 = 6`; every cell of the 4 x 5 table at armor 0
   and at armor ≥ raw (floor 1); magic ignores armor; `bonusVs` missing = 1; a tech-boosted attacker and a tech-boosted
   defender (Melee Weapons +1, Armor +1) change the result by exactly 1 each.
2. Two enemy Heavy Infantry attack-moved into each other: first hit at tick `windup`, hits every `cooldown` ticks
   after, exact damage from the formula each hit (`10 x 1.0 x 1.0 - 3 = 7`), and both die within the computed tick
   count; with equal stats and simultaneous engagement both die the same tick (phase-11 rule).
3. Acquisition priority: a scene with (an enemy worker nearer, an enemy soldier farther) picks the soldier; (two
   soldiers, one attacking me) picks the attacker; (only a building in sight) picks the building; ties to the lowest
   slot; nothing outside sight is picked; a Moving unit picks nothing; a Holding unit attacks in range and never moves
   (position bit-identical over the fight); a Gathering worker never retaliates.
4. Retaliation leash: an Idle unit chased past its sight radius from its anchor stops, walks back and is Idle at the
   anchor cell; an AttackMoving unit with no enemy in sight resumes its leg and arrives at its destination.
5. Death: the dead unit's handle is stale the same tick, its pop is released (ledger recount equals the fuzz oracle),
   every attacker targeting it re-acquires on its next scan, the death event is in the tick's buffer with the killer's
   owner, `Kills` / `Losses` count. A building killed by melee is freed and its cells follow the pocket rule.
6. Hash and replay: every new field flips `StateHash` when changed on a live unit (reflection audit); **the golden
   `cross_map_seed1.replay` is byte-identical** (`ReplayGoldenTests` green with no regen; the file is not in the diff);
   two sims running a 1,000-tick 200 v 200 brawl from the same seed agree on `StateHash` every tick, and a recorded
   replay of it plays back to the same checkpoints.
7. Fuzz (`CombatFuzzTests`, Stress): 6 seeds x 3,000 ticks, two players, random AttackMove / Move / Stop / Hold / Gather
   / Train / spawns / frees / building spawns and destroys, with invariants every tick: hp in (0, max], no live unit
   targets a dead handle at phase end, no unit moves while `Attacking`, Holding units never move, pop recount exact,
   kills + losses balance deaths, finite positions, twins equal.
8. Perf (Serial, `[Trait("Category","Perf")]`): 500 v 500 melee brawl (Heavy Infantry, 128 map) average tick < 4 ms,
   report the first 200 ticks; the M1 500-unit criterion and `TightBlob2500` (one player, 2,500 units) not worse than
   `main` by more than 0.1 ms (**scanning in a world with no enemy in sight must be near free**: a per-owner count per
   spatial-hash bucket or equivalent early-out; do not query units when no enemy bucket is in radius); 300 idle scans
   over a 2,500-unit one-player blob 0 bytes (`AllocationTests` row) and a brawl tick 0 bytes.
9. Non-Perf green; 0 warnings; docs per 7; `git diff --stat origin/main` shows nothing under `game/`, `tools/`,
   `sim/Rts.Sim/ViewApi/`, the test `Content/` folders or the golden replay.

**Design references:** docs/02 "Combat" (Stats, Damage formula, type x class table, Death); docs/03 "Tick model"
(phases 7, 10, 11, 13), "Orders and unit states" (priority list, scan every 4 ticks staggered, retaliate when hit while
idle), "Combat implementation" (`DamageCalc.Compute`, queued melee damage applied in phase 11), "Determinism";
`game/data/common/damage_table.json`; `UnitDef.Attack` (`AttackDef`), `Sight`, `Hp`, `Armor`, `ArmorClass`;
`TechState.Bonus`; `SpatialHash.QueryRadius` / `NearestEnemy`; `BuildingStore.Damage`; `PlayerLedger.AddHalfPop`.

**Tests required:** `DamageCalcTests`, `CombatTests` (criteria 2-5), `CombatFuzzTests` (7), `CombatPerfTests` (8),
`StateHashTests` rows + audit, `AllocationTests` rows, `DeterminismTests` brawl twin + replay round trip,
`ReplayGoldenTests` untouched and green. QA adds its own under `QA/` and `Stress/`.

**Constraints most at risk:** no allocation per tick (scan results in preallocated scratch; no LINQ); the spatial hash
is the only unit query (no O(n^2)); `SimMath` only (facing); no `Dictionary` iteration; chase goals go through the
existing movement path (a chaser's goal is its target's cell, re-set only when that cell changes and only on a scan
tick, so the flow-field cache is not thrashed: measure 250 chasers after 250 fleeing units and report it; if it thrashes,
steer straight at a target within 8 m on the same plateau and document the limit); no data edits; `warnings as errors`.
Size: ~800-1,000 sim lines (a new system: slice 1 only). If the budget runs out, buildings-as-targets (criterion 3's
building case and 5's building death) are the part to defer to M4-2, said so in the report.

### After M4-1 (sim)
M4-2: `Attack(target)` order (replay format 4 with a target handle; the sim regenerates the golden that session, the
data track STOPs or does not touch it), projectiles (travel, miss rule, splash falloff, friendly fire, min range),
counter-triangle scenario tests. M4-3 fog + high-ground vision; M4-4 abilities / statuses / zones + the four named
abilities (schema → data track); M4-5 stealth / detection. Sim debt for its next hardening: BUG-0134 (S3), BUG-0133,
BUG-0113, BUG-0094 (S4).

## View track

### Current session plan: M3-V4 — the M3 Playable proof, BUG-0125, BUG-0126 items 1-2 (feature, QA standard, bugs from BUG-0145)

**Goal.** Close M3's last criterion ("the owner builds a full Malazan base and reaches Age II") without waiting for the
owner: a headless scene that plays the owner's playtest script through the real HUD and checks every step against the
sim; then fix the one S3 regression from M3-V3b and the two wording nits that D3's locks made visible.

**Scope (in).**
1. `game/tests/M3PlayableTest.tscn` (+ `.cs`), printing `M3 PLAYABLE TEST PASS` / `FAIL`, run at 8x speed on the real
   `Match` with the shipped data (seed 1, then seed 6), every step through injected input on the real scene (the
   `Viewport.PushInput` path the QA scenes use), each checked against sim state the next frame, with the tick each
   step completed printed:
   box-select the five workers → right-click the nearest mine (all five `Gathering` after arriving) → click the Town
   Hall (panel shows its name and hp; card shows Laborer on Q and Age II on W greyed "Locked") → Q three times (three
   queue squares, head progress bar) → click the third square (cancelled, 50 gold back) → right-click a forest with
   the hall selected (rally flag; a new Laborer walks there and gathers) → select workers, B then E, click on green
   (Legion Barracks site placed, built) → B, A (Armory) → B, Q (Billet; cap 10 → 18 in the resource bar) → when the
   Barracks and Armory are finished, click the hall: Age II live; W → it researches; "Age II" flash → click the Armory:
   Melee Weapons live, Melee Weapons II "Locked"; Q (Melee Weapons researched) → V, W (Engineers' Yard) placed after
   Age II (before it: ghost red "Locked") → Sapper trained there (Age 2 and a live Sapper) → select a Heavy Infantry
   trained from the Barracks: the panel reads attack with the green "+1". The script may spawn extra gold / wood through
   the dev path only if the brief's budget would otherwise take longer than the fixed tick limit; say so in the log.
   A hash twin of the same command stream in xUnit (`ViewApi` read-only proof) or the scene's own twin, like QaV3.
2. **BUG-0125:** `ResourcePicker.PickRay` tests the drawn shape (a tree: trunk + cone; a mine: box + half-block), not
   the footprint column, so a click on open ground north of a node is a Move again and a click on the canopy still
   means the tree. Flip `PickRayQaTests` measurement rows to checks: open ground taken for a node = 0 (3,000 rays),
   drawn node taken for ground = 0, column-vs-drawn mismatch well under the old 68 %.
3. **BUG-0126 items 1-2:** a locked building's ghost reads **"Locked"** (`ui.json` `placement.requires`, the value
   changes from "Needs more") and its build-menu button is greyed with that reason when `CanPlace` would answer
   `Requires` (the same greying rule the production card uses); Age II's button reads "Researched" when `HasTech` and
   "In a queue" when it is queued, whatever the sim's first refusal reason (view-side precedence over `Requires`).
4. Docs/03 "Implementation (M3-V4)" (the scene, the pick shape, the precedence rule); bug files 0125 / 0126 (items 1-2
   fixed, 3-6 open); coverage row note; tick "Playable" in docs/05 with the scene named (at ACCEPT, by the Producer).

**Out of scope:** M4 views (hp bars, hit flashes, deaths) until the sim's M4-1 is on `main`; BUG-0126 items 3-6; the
sim outside `sim/Rts.Sim/ViewApi/`; `game/data/factions/**`; any `ui.json` key the card already reads other than
`placement.requires`'s value and new keys for the two Age II states if `research.researched` / `research.queued` do
not exist yet (view-only file, the view's by the M3-V2 exception; the sim and data tracks do not touch it).

**Acceptance criteria.**
1. `M3PlayableTest.tscn` PASS headless on seeds 1 and 6 within a fixed tick budget stated in the scene (reaches Age 2
   with a live Sapper and a Heavy Infantry showing "+1"); every step's tick printed; FAIL with the step name on any
   mismatch (prove it once by breaking a step in a scratch run, say which).
2. `tools/qa/smoke.ps1` PASS; every scene in `game/tests` PASS; non-Perf green (both suites); view Perf rows alone.
3. `PickRayQaTests`: open ground taken for a node 0 / 3,000; drawn node taken for ground 0; the tree-canopy case of
   M3-V3b still a Gather (`QaV3bTest` and the M3-V3b dev rows stay green).
4. With the shipped (D3) data: the Cadre Tower ghost reads "Locked" and its V-menu button is greyed "Locked" before
   Age II, live after; with Age II queued and a hall lost the button reads "In a queue"; researched reads "Researched"
   (`ProductionHudTests` rows + a `QaV3bTest` row flipped from NOTE to check).
5. `ViewApi` changes read-only (hash twin green; no sim writes); `git diff --stat origin/main` shows nothing under
   `sim/Rts.Sim/` outside `ViewApi/`, nothing under `game/data/factions/`, nothing in the golden replay.
6. Docs and bug files per scope 4; 0 warnings.

**Design references:** STATE "For your review" M3-V3 entry (the owner's script, verbatim), docs/02 "Economy",
"Buildings", "Tech / Ages"; docs/03 "Implementation (M3-V1 / V2 / V3)", "Right-click context"; BUG-0125 (the drawn
shapes in `PropsView`), BUG-0126 items 1-2; `World.CanPlace` / `CanResearch` reason order (M3-6).

**Tests required:** the scene (1), `ProductionHudTests` rows (4), `PickRayQaTests` flips (3), hash twin (5).

**Constraints most at risk:** views hold no gameplay state; `ViewApi` never writes; player-facing text only in
`ui.json`; the scene drives the real controllers (no shortcuts through sim commands except the stated dev spawns).
Size: ~600 scene lines, ~150 game-code lines, ~100 `ViewApi` lines, tests.

### After (view)
M4 view work once M4-1 is on `main`: unit hp bars and hit flashes, death (view freed, a 10 s corpse marker), the kill
counter on F12, placeholder attack feedback; then the fog shader with M4-3. View debt: BUG-0126 items 3-6, the
edge-pan note, export hygiene (M6).

## Data track

### Current session plan: D4 — common techs text + BUG-0132 + the golden regen (hardening: the data track's end-of-M3 debt; QA light; bugs from BUG-0155)

**Goal.** Finish the data track's M3 debt: the shared techs' player-facing text (`game/data/common/techs.json`
`displayName` / `description` of Age II and the six Forge upgrades, placeholder wording from the sim's M3-5) written
for the player and pinned to docs/02, the D3 test messages fixed (BUG-0132), and the golden regenerated once because
descriptions are in `ContentHash`. **Named exception** (recorded at the 1715 ACCEPT): `common/techs.json` is the sim's
file; this session the data track edits only its text fields and the sim's M4-1 does not open it.

**Scope (in).**
1. `common/techs.json` strings only. Rules: a description names every number the effect carries (+1 / +2 totals, the
   30 → 30 s etc. are not in these; "+2 in all" stays on level II), its requirement in words ("Needs Melee Weapons and
   Age II"), and no faction's building names in a shared tech (the player sees "Cadre Tower", not "Caster Hall": Age II
   should say what it unlocks in faction-neutral words, e.g. "your advanced buildings, your unique unit, level II Forge
   upgrades and your faction upgrade"). Keep the names ("Age II", "Melee Weapons", "Melee Weapons II", "Ranged
   Weapons", "Ranged Weapons II", "Armor", "Armor II") unless docs/02 says otherwise (it doesn't). Where the placeholder
   already reads well, keep it and pin it; do not reword for its own sake. Quote every final string in the report.
2. `Content/TechContentTests`: new rows pinning the seven shared techs to docs/02 "Forge upgrades" / "Ages" (cost,
   time, the +1 / +2 effect amount, `requires` = level I + `age_ii` on level II, `researchedAt`), and the description
   rules above (effect amount named, `RequiresText.Needs` matches `requires`, no faction building name in a shared
   tech's text).
3. **BUG-0132:** `TechContentTests` A / B / C messages name the tech, the field, and both values as "page X vs data Y";
   `RequiresText.Needs` also reads "requires" / "after", or a test asserts no shipped description uses those words.
4. **Golden:** `RTS_REGEN_GOLDEN=1` once; the diff of `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` is exactly
   the `data-hash` and `checksum` lines (every `k` line byte-identical); say the old and new `data-hash` in the report.
5. `docs/02-game-design.md` "Tech" untouched (numbers already match); if a wording there is wrong, say so instead of
   editing. Bug file 0132 `fixed` with the test names; README row; coverage note.

**Out of scope:** any numeric field; any faction file (`game/data/factions/**` stay untouched this session); C# outside
`sim/Rts.Sim.Tests/Content/` and `QA/Content/`; `ui.json` (the view's); docs/03.

**Acceptance criteria.**
1. Seven shared techs: names unchanged, descriptions final, each quoted in the report; no faction building name in any
   shared tech's text; every effect amount and every requirement named (tests prove it).
2. `Content` + `QA/Content` + `DataValidationTests` + `TechLoaderTests` + `ReplayGoldenTests` green; one-sided edits
   (page cost, data cost, data description dropping "Age II") each fail with a message naming tech, field, page value
   and data value (show three runs).
3. The golden diff is `data-hash` + `checksum` only; `DataContentHashTests` green.
4. `git diff --stat origin/main` shows only `game/data/common/techs.json`, the golden, files under
   `sim/Rts.Sim.Tests/Content/` or `QA/Content/`, `studio/**`, `docs/01-vision.md` (one change-log row).
5. Non-Perf green; 0 warnings.

**Design references:** docs/02 "Tech" (Ages, Forge upgrades table); docs/03 "Implementation (M3-5)" (effects, totals
rule: level II adds one more); CLAUDE.md rule 8 (player-facing text from data).

**Tests required:** per scope 2-3; the golden test.

**Constraints most at risk:** the ownership exception (strings only in `common/techs.json`); one track moves
`data-hash` (this one); QA light: criteria, build / tests, docs conformance.

### After (data)
STOP until M4's schemas land: `abilities.json` / `statuses.json` (M4-4), tower attack / sight / detector fields (M4-1
does not add them; M4-3), BUG-0090's remaining item; then the M4 balance pass (QA standard) after the sandbox; `ai.json`
build orders with M5. Owner review tweaks from the inbox come first whenever present.

## Watch-outs (all tracks)

- **Golden:** data regenerates (`data-hash` + `checksum`); sim keeps `k` lines identical and changes no data; view
  touches neither. If the sim finds it cannot keep the `k` lines identical, it stops and reports rather than
  regenerating; the Producer then decides (defer the hash field or hold D4's regen).
- **Merge order:** sim, view, data. Shared-file touches expected: docs/03 (one subsection per track), docs/01 change
  log (one row each, appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only, keep both sides).
  Every builder: `git diff --stat origin/main` before reporting.
- **Gate-change rule:** the sim's M4-1 adds no gate; the view's scene reads the shipped data; D4 changes no value a
  fixture reads (strings only). Still, the view's QA merges `main` before its final run if the sim lands first.
- Three suites and a scene loop at once: Perf rows can fail from contention; a failure counts only alone.
  `TightBlob2500` 4.27 ms of 4.5: M4-1's idle scanning must stay near free in a one-player world (criterion 8).
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths; never kill processes
  you did not start.
