# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-08-0313** (base `f173c33` = `origin/main`; first session of
2026-10-08, cap 8). `main` is green: build 0 warnings, smoke PASS (tick 88, `Rts.Sim 0.0.1`, no ERROR). **Bug ids this
session:** sim from **BUG-0150**, view from **BUG-0160**, data from **BUG-0170** (disjoint blocks).

## Where we are

- M0, M1, M2 Done. **M3: 8 / 8 criteria**, every track's end-of-M3 hardening done, coverage rows ✅. **Not signed off:
  BUG-0146 (S2, sim)** is open. Sign-off at this session's ACCEPT once it is fixed and QA-verified.
- M4: criterion 2 (damage formula) ticked; criterion 1 lacks the `Attack(target)` order (this session); criterion 4 has
  death and building destruction in the sim, corpses / rubble are the view's (this session).
- Open bugs: 25: S1 0, **S2 1 (BUG-0146)**, S3 13, S4 11. Hardening counters: sim 1 / 4, view 1 / 4, data 0 / 4.
- **Integration watch-outs:** (1) `main`'s scene loop has five red M2 scenes (BUG-0147: DebugOverlay, Orders, QaH1,
  QaH2, QaM27; QaM24 passes); the gate (build, tests, smoke) is green; the view fixes them first. (2) The attached replay
  `studio/bugs/BUG-0146-seed21-wood-wedge.replay` is headered with the pre-D4 `data-hash` 702859B867AAC412; the sim's
  un-skip must substitute the hash in code (or rewrite the file through `ReplayFormat` so the checksum follows), never
  loosen the row. (3) Three suites plus scenes at once make Perf rows fail from contention; a failure counts only alone
  (`TightBlob2500` is at 4.43-4.51 of 4.6 ms).

## Sim track

### Current session plan: M4-2a — BUG-0146 (S2) first, then the `Attack(target)` order and `attack.targets` (feature, QA full, bugs from BUG-0150)

**Goal.** Close the one S2 that holds M3's sign-off (gatherers wedge 1.7 m short of a tree open on one side and never
gather), then give M4 criterion 1 its last piece: the explicit `Attack(target)` order, with replay format 4 so matches
and replays carry it, and the `attack.targets` data field that makes the Battering Ram buildings-only (BUG-0139).

**Scope, Part 1 (must come first; S2 outranks features): BUG-0146.**
- Root-cause the wedge in `Economy/EconomySystem` / the arrival rule: a gatherer's walk to a node open on one side ends
  "arrived" (crowd arrival / `GoalInset` / flow-field cell) at a point outside `Reach` (1.25 m), and the 20-tick retry
  walks to the same spot. Fix so that a gatherer with a live, exposed node either reaches it (within `Reach` of the
  footprint edge) or works it; a retry that ends out of reach must pick a different approach cell / inset or push in
  behind the queue, never repeat the same arrival for the rest of the match. Keep the "queue at a mine's edge" behaviour
  (several workers waiting on one node is fine; standing still forever with no one between you and the node is not).
- Regression: un-skip `QA/GatherWedgeQaTests.Seed21PlayableReplay_NoGathererStandsOutOfReachForever` (the file's header
  is the pre-D4 `data-hash` 702859B867AAC412: substitute the expected hash in code, or rewrite the file through
  `ReplayFormat` so its checksum follows; do not loosen the assertion), and add a hand-built row: a 1 x 1 tree with
  three sides blocked (trees north / west / east, a building to the south-west as in the bug's map) and four gatherers
  queued on it: every one of them gathers within a bound (say 600 ticks), wood rises every delivery.
- Look at the dev-seen two-cell corridor oscillation (seed 6, same bug file) while there; fix if it is the same arrival
  rule, otherwise file it as its own bug with what you saw.
- Golden: keep every `k` line identical if the fix changes no arrival on the golden's one-player path; if it does,
  regenerate once in the same commit and say why in docs/03.

**Scope, Part 2: `Attack(target)` (M4-2a).** Do this only after Part 1 is green. If Part 1 takes more than half the
budget, stop after Part 1 plus the `attack.targets` field (below), say so in the report, and the Producer accepts
Part 1 alone as this task (Part 2 moves to the next session).
- `Command.Attack(player, unit, targetHandle, isBuilding[, queued])`, `CommandKind.Attack = 16`, a unit order,
  Shift-queueable (`QueueKind` / a queued target handle: extend the queue storage so a queued Attack keeps its handle,
  hashed like `QueueTypeId`). Dropped at apply like a bad Gather: a dead / recycled target, an own target (or an ally's,
  there are none yet), a target the attacker's `attack.targets` forbids, a unit whose `CanFight` is false (ranged /
  caster / siege-with-projectile still cannot fight until M4-2b: the order is dropped, documented).
- Rule (docs/03 "Orders" + "Implementation (M4-1)"): the explicit target outranks the scan (no re-pick while it lives),
  the retaliation leash does not apply (an ordered attacker chases until the target dies or another order ends it); the
  give-up memory still ends an unreachable chase (then the unit goes Idle in place, not "Returning"). A worker takes an
  Attack order (it fights only when told). When the target dies the unit goes Idle (then scans as an Idle unit would)
  or pops its queue. `Attack` on a building works through the same path (target `TargetIsBuilding`).
- **Replay format 4**: the command line carries the target handle (index + generation) and `isBuilding`; the header
  records `combat` (`SimConfig.Combat`), so `ReplayPlayer.Run(replay, data)` needs no `combat:` argument (the parameter
  stays as an override for tests recorded off); format 3 files still load (`combat` defaults to true). Golden regenerated
  once for the format bump in the same commit, every `k` line identical (unless Part 1 moved them: then one regen with
  both reasons).
- **`attack.targets` schema field** on `attack` in `units.json`: `"units"` / `"buildings"` / `"all"`, default `all`
  when absent; validated at load (unknown value = `DataError`); respected by the scan, retaliation, holders and the
  explicit order. Set the Battering Ram's data to `"buildings"` (the only content entry this task writes; the data
  track STOPs this session, so no file collision), closing BUG-0139. `data-hash` moves once (the same golden regen).
- CLI: nothing new needed; `tools/Rts.Cli` must still build and run.
- Docs: docs/03 "Orders" (kind 16, queue storage), "Implementation (M4-2a)" (the explicit-target rule, leash exemption,
  the format 4 header, `attack.targets`), "Save/load and replays" (format 4); docs/02 "Stats" gets the `targets` field
  in one line; docs/01 change-log rows for the Producer decisions below.
- **Out:** projectiles, misses, splash, friendly fire, minimum range (M4-2b), fog (M4-3), abilities (M4-4), the view's
  F-key / cursor path (the view's next task), `Patrol`, formations.

**Acceptance criteria.**
1. `GatherWedgeQaTests.Seed21PlayableReplay_NoGathererStandsOutOfReachForever` is un-skipped and green with its assertion
   unchanged (the hash substitution is in code or the file is rewritten through `ReplayFormat`; the replay still plays
   bit-exact to its checkpoints).
2. A new hand-built row (a 1 x 1 tree open on one side, four queued gatherers) is green: every gatherer delivers at least
   once within 600 ticks and no gatherer stands `Gathering` with velocity 0 out of reach for more than 2 retry periods
   (40 ticks) while no unit stands between it and the node. The row fails on the pre-fix code (say so in the report).
3. Economy conservation and gather fuzz rows stay green; the gather efficiency Perf rows (200 workers alone) move by at
   most 10 %; `TightBlob2500` alone stays under 4.6 ms.
4. `Command.Attack` (kind 16): unit tests for drop rules (dead / recycled / own / forbidden-by-targets / cannot-fight),
   Shift-queue (a queued Attack keeps its handle and starts when the unit is Idle), the explicit target held across
   scans while an enemy is nearer, chase past the leash (an ordered attacker follows a target 40 m from its anchor), the
   give-up on an unreachable target, Idle after the kill, an Attack on a building.
5. `attack.targets`: loader tests (default `all`, each value, a bad value is a `DataError`), `DataValidationTests` green
   on shipped data, the Battering Ram ignores units (never acquires, never retaliates against, drops an `Attack` on
   one) and hits buildings; `QA/CombatFriendExceptionQaTests` and the ram row of BUG-0139 un-skipped and green.
6. Replay format 4: a format 3 file loads with `combat` true; a recorded match with Attack orders round-trips (record,
   play back, checkpoints equal); `ReplayGoldenTests` green with the golden regenerated once in the same commit, `k`
   lines identical (or the reason given); `CommandDoorFuzzStressTests` covers kind 16.
7. Determinism: twin runs with Attack orders hash equal every tick (`CombatFuzzTests` extended with Attack on 6 seeds x
   3,000 ticks); the reflection hash audit covers the new queue handle storage.
8. Full suite incl. Perf green alone; 0 warnings; nothing under `game/`, `tools/` (except the CLI if it needs the new
   kind), `sim/Rts.Sim/ViewApi/**`, `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`; the only `game/data/factions/`
   edit is the ram's `targets`; docs updated per scope.

**Design references.** docs/03 "Orders and unit states" (the order list, queue storage, "any unit order ends the
engagement"), "Implementation (M4-1)" (priority, leash, give-up memory, the worker rule, the ram note, the combat switch
and format 4), "Save/load and replays"; docs/02 "Combat: Stats" and the Battering Ram line in `docs/factions/whirlwind.md`
("attacks buildings only"); the BUG-0146 file's map and repro.

**Constraints.** No Godot in `Rts.Sim`; no wall clock or `System.Random`; trig through `SimMath`; no `Dictionary`
iteration; no per-tick allocation (a queued handle lives in a flat array like `QueuePosition`); stats from data (the
ram's rule is the `targets` field, not an id check); player-facing text none (no new strings); docs in the same commit.
Absolute paths only; `git diff --stat origin/main` before reporting; never touch the owner's main checkout; never kill
processes you did not start.

**Producer decisions for this task (record in docs/01, owner may revisit):** (a) an explicit Attack is exempt from the
retaliation leash but not from the give-up memory; (b) a worker obeys an explicit Attack; (c) an Attack order to a unit
that cannot fight yet (no melee attack) is dropped, not queued, until M4-2b; (d) `attack.targets` default `all`.

## View track

### Current session plan: M4-V1 — BUG-0147's five scenes, then the first combat views (feature, QA standard, bugs from BUG-0160)

**Goal.** Make `main`'s scene loop green again (five M2 scenes whose start armies now fight), then show the fighting
that M4-1 put in the rules: hp bars, hit flashes, deaths with corpses, rubble, kill / loss counts, live hp in the panel.
This is the view half of M4 criterion 4 ("Death, corpses, building destruction and rubble").

**Scope.**
1. **BUG-0147 first.** Give `LaunchOptions` a `--no-combat` flag (takes no value) that sets `SimConfig.Combat = false`
   on the match (the sim's test / tooling switch; say in docs/03 it is a dev flag, never a game option) and run the five
   red scenes (DebugOverlay, Orders, QaH1, QaH2, QaM27) with it, or with `--units 0` / `--no-bases` where the scene does
   not need armies. Expectations are not loosened. The whole loop is green on `main` + this branch (32 scenes today;
   report the count). Also run `M3PlayableTest` (seed 1 and 6) once on this branch and report the ticks.
2. **Combat views** (all through `ViewApi` read-only accessors and the sim's public spans; nothing in `sim/` outside
   `ViewApi/`):
   - **Unit hp bars**: drawn above a unit only when `UnitStore.Hp < UnitDef.Hp` (as the building bars do), green → red by
     fraction, scaled with zoom like the cargo cube; batched (one MultiMesh or a pooled set), 0 B / frame when nothing
     changes.
   - **Hit flash**: the victim's view tints white (or the faction colour brightened) for ~0.15 s when its hp dropped
     this tick (compare the snapshot's hp with the last frame's; no sim event needed).
   - **Death**: when a handle appears in `World.Deaths` (read between ticks; emptied at the next tick's start), free the
     unit view and leave a flat placeholder corpse marker (a dark disc / quad of the unit's radius, faction-tinted) for
     10 s, then remove it; a building death leaves a rubble marker (a low grey box over the footprint) for 20 s. Markers
     are pooled; a death storm (500 in one tick) allocates nothing per frame after warm-up and never exceeds a cap (say
     2,000 markers; oldest replaced).
   - **Counts**: `World.Kills` / `Losses` per player on the F12 overlay and a compact "K n / L n" in the resource bar
     (the text label keys go in `ui.json` `hud` section; no C# literal).
   - **Selection panel**: live hp (`UnitStore.Hp` / `UnitDef.Hp`), allocation-free through the existing int-string table
     (BUG-0123's); the building hp already live. "Attacking" state text is already in `ui.json`.
   - **Headless scene** `game/tests/CombatViewTest.tscn`: a scripted 20 v 20 brawl (attack-move both sides through
     commands) that checks each frame: a bar exists exactly for hurt live units, a flash exactly on units whose hp fell,
     a corpse marker per death event with the right lifetime, rubble on a building death (spawn a building and let the
     enemy kill it), the counter equals `World.Kills` / `Losses`, markers are removed on time; plus the hash twin (the
     view's command stream replayed bare equals the HUD's checkpoints).
3. **Out:** the Attack-target cursor and F key (next view task, after M4-2a lands), projectile visuals (M4-2b), the fog
   shader (M4-3), death animations and real corpse models (M6 art pass), sounds for hits / deaths (M6).

**Acceptance criteria.**
1. `--no-combat` exists, documented in docs/03 "Debug tooling" as a dev flag; the five BUG-0147 scenes pass with it (or
   with no armies) and no scene's assertion was loosened; the full scene loop is green on this branch (count reported);
   BUG-0147 marked fixed.
2. Hp bars: only hurt live units show one; fraction and colour match `Hp / MaxHp` within one frame; none left behind
   after a death; 0 B / frame in a steady brawl after warm-up (measured in the scene).
3. Hit flash: every unit whose hp fell this tick flashes; a unit hit twice in 0.15 s stays lit; no flash on a unit that
   was not hit.
4. Death: every `DeathEvent` frees its view the same frame and leaves a corpse (units) or rubble (buildings) marker at
   the death position; markers expire at 10 s / 20 s of game time (scaled by speed); a 500-death tick keeps the frame
   under the cap and allocates nothing per frame afterwards; the hash twin is equal (views read only).
5. Counts: the resource bar and the F12 overlay show kills / losses that equal `World.Kills` / `Losses` every frame; the
   labels come from `ui.json`.
6. The selection panel's hp line is live and allocation-free (the BUG-0123 test extended to a damaged unit).
7. `CombatViewTest.tscn` passes on two seeds; smoke PASS; non-Perf suites green; view Perf rows green alone; 0 warnings;
   nothing under `sim/Rts.Sim/` outside `ViewApi/`, nothing under `game/data/factions/`, the golden untouched; docs/03
   "Implementation (M4-V1)".

**Design references.** docs/02 "Death" ("Views play a death animation and leave a corpse for ..."), docs/03 "Rendering
and presentation" (dumb views, one node per entity, snapshot interpolation), "Implementation (M3-V1)" (building bars,
cargo marker scaling), "Implementation (M3-V3)" (panel, int-string table), "Implementation (M4-1)" (`World.Deaths`,
`Kills` / `Losses`, `UnitStore.Hp`, `SimConfig.Combat`), docs/03 "Debug tooling" (launch flags).

**Constraints.** Views hold no gameplay state (a corpse marker is presentation: a position and a timer); `ViewApi`
additions read-only, never change the tick or the hash; no per-frame allocation in steady state; player-facing text only
in `ui.json`; C# only; absolute paths; `git diff --stat origin/main` before reporting; never kill processes you did not
start.

## Data track

### Current session plan: STOP (planned)

No schema the data track needs is on `main` at this session's base: `attack.targets` lands with the sim's M4-2a this
session (the sim sets the ram's value itself, as the one content entry its tests need; the data track pins it to
`docs/factions/whirlwind.md` next session), the tower attack / sight / detector fields come with M4-3, abilities with
M4-4. The inbox has no review tweaks. Next data task (next session, QA light): pin `attack.targets` on the ram to the
Whirlwind page's "attacks buildings only" line (`Content/UnitContentTests`), the docs/02 "Ages" wording drift ("level-2"
→ "level II"; "two Age I production buildings or a Forge" → the exact any-two-of rule) together with the
`TechContentTests.G` parser that reads it, and BUG-0155 (S4, the case-sensitive name check in `TechContentTests.H`).

## Watch-outs (all tracks)

- **Golden:** only the sim regenerates this session (format 4, `data-hash` for `attack.targets`, and / or a BUG-0146
  arrival change), once, in the commit that needs it, with the reason; the view touches neither the golden nor
  `game/data/**` except `ui.json`'s `hud` labels.
- **Merge order:** sim, then view. Shared-file touches expected: docs/03 (one subsection per track), docs/01 (rows
  appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only, keep both sides).
- **Sessions:** a lock's age alone does not prove the previous session dead; check for running processes before
  resuming, and long sessions should refresh the lock.
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; suggested text under For your review, M1 entry).
- `TightBlob2500` 4.43-4.51 ms of 4.6: M4-2a's per-unit work must stay inside it; measure alone.
- **M3 sign-off at ACCEPT** if BUG-0146 is fixed and QA-verified with no new S1 / S2: tick the roadmap status, write the
  retro, set M4 to Next, add the For your review entry.
