# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-08-0913** (base `a8e3a0b` = `origin/main`; second full session
of 2026-10-08, cap 8). All three tracks run: sim **M4-2b**, view **M4-V2**, data **D5**. The ACCEPT of this session
rewrites this file. **Bug ids this session:** sim from **BUG-0180**, view from **BUG-0190**, data from **BUG-0200**
(disjoint blocks; the previous session used 0150-0158 and 0160).

## Where we are

- **M0, M1, M2, M3 Done.** M3 signed off at the 0313 ACCEPT (retro in docs/05).
- **M4: 3 / 10 criteria** (1 attack / attack-move / chase / retaliation / priorities, 2 damage formula, 4 death /
  corpses / rubble). This session: projectiles + splash + friendly fire + min range (M4-2b, sim), the Attack-target click
  in the HUD (M4-V2, view), the `attack.targets` page pins + docs/02 "Ages" wording (D5, data).
- **Repo health at PLAN (on `main`, `a8e3a0b`):** build 0 warnings; smoke PASS (tick 91, `Rts.Sim 0.0.1`, no ERROR);
  non-Perf sim suite **3,649 / 3,660 (11 skipped, each a filed bug) / 0 failed** (18 m 42 s). Open bugs **27** (S1 0, **S2 0**, S3 13, S4 14). Hardening counters:
  sim 2 / 4, view 2 / 4, data 0 / 4 (this session makes sim 3 / 4, view 3 / 4, data 1 / 4).
- **Docs drift fixed at PLAN by the Producer:** the docs/03 "Debug tooling" sentence that said a `--no-combat` replay
  needs `ReplayPlayer.Run(..., combat: false)` "until format 4" now says format 4 records `combat 0` itself. **The view
  track must not edit that sentence** (it is in the sim worktree's plan commit).
- **Integration watch-outs:** (1) merge order sim, then view, then data; shared-file touches expected only in docs/03
  (one new "Implementation" subsection per track, near the same anchor), docs/01 (rows appended), `studio/bugs/README.md`
  and `studio/qa/coverage.md` (append both sides). (2) Only the sim regenerates the golden this session (`data-hash` for
  `projectiles.json`), once, with the reason; view and data touch neither the golden nor `game/data/**` (view: `ui.json`
  labels only). (3) The Perf category in one process fails 8-9 wall-clock rows on base and head alike (machine load); a
  Perf failure counts only alone, and `TightBlob2500` is at 4.35-4.47 of 4.6 ms: measure one row at a time, read the
  first run (BUG-0158: later runs in one process see a sustained-load throttle). (4) `CLAUDE.md` still says "Current
  milestone: M1" (owner's file; suggested text under For your review, M1 entry).

## Sim track

### Current session plan: M4-2b — projectiles with travel time and misses, splash with falloff, friendly fire, minimum range; the counter-triangle scenario rows (feature, QA full, bugs from BUG-0180)

**Goal.** Give ranged, caster and siege units their attacks (M4 criterion 3 and the "Scenario tests for the counter
triangle" criterion): a shot is a projectile that flies to where the target *was*, hits only if the target is still near
the impact point, and splashes with falloff; Sappers and Catapults hurt their own side at half damage. With this every
unit of both factions fights, which the sandbox Playable, the view's projectile visuals (M4-V3) and the M5 AI need.

**Scope.**
- **Schema (sim-owned, `game/data/common/projectiles.json`, new):** exactly the five ids `units.json` references today:
  `arrow` (Desert Archer), `bolt` (Crossbowman), `magic_bolt` (Cadre Mage, Priest), `sharper` (Sapper),
  `catapult_stone` (Catapult). Fields: `kind` (`aimed` / `lob`), `speed` (m/s: aimed 25, lob 12 per docs/02
  "Projectiles"), `hitTolerance` (m, aimed only, default 0.3). Validated at load: every `attack.projectile` id must
  exist (today `AttackDef.Projectile` is an unresolved string), unknown fields, bad kinds, non-positive speed and a
  `hitTolerance` on a lob are `DataError`s; `DataLimits` bounds; `ContentHash` covers the file (`data-hash` moves once
  with the golden regen). `CanFight` opens to every attack with a value: the M4-2a "dropped for a unit that cannot
  fight yet" rule ends (docs/01 row; the Battering Ram keeps `buildings` and has no projectile: it is a melee siege unit).
- **Projectile entities:** a flat SoA store (`ProjectileStore`, capacity in `SimConfig`; no generational handles, a
  projectile is fire-and-forget), spawned at the damage point of a ranged attack (the wind-up end) aimed at the
  target's position at that moment, moving `speed x 0.05` m a tick in a straight line (no gravity: the view draws any
  arc), resolved in phase 11 when it reaches the impact point: an **aimed** projectile hits its target if the target is
  alive and within `radius + hitTolerance` of the impact point (else it misses and lands: nothing); a **lob** always
  explodes at the impact point. Hashed (position, owner, damage parameters) whenever any exist; no allocation (a
  preallocated store, a per-tick splash scratch list); deterministic order (slot order). **For the view's M4-V3 (next
  view task):** read-only spans on the store: `Count` / `Alive`, `Position`, `PrevPosition`, `Target` point,
  `ProjectileTypeId`, `Owner`, plus a per-tick list of impacts (`World.Impacts`: position, projectile type, hit or
  miss) emptied at the next tick's start like `World.Deaths`. Document them in docs/03 and under STATE "Requests" 20.
- **Splash** (`attack.splash` > 0): on impact (a lob's always, an aimed hit's, and a melee hit's if a melee attack ever
  carries splash), every unit within the radius takes `damage x f(d)`: 100 % within 40 % of the radius, linear to 50 %
  at the edge (docs/02 "Splash and friendly fire"); enemies always; **own and allied units only when `friendlyFire`**,
  at 50 % after the falloff; **friendly fire never damages buildings**; enemy buildings in the radius take structure
  damage. Damage per victim through `DamageCalc.Compute` with the victim's own class and armor. Hits queue into the
  same phase-11 resolution so mutual kills still land.
- **Minimum range** (`attack.minRange`): a target nearer than it cannot be fired at: the unit does not swing (no
  wind-up), and under a chase / attack-move it re-picks on its next scan (a nearer enemy of equal priority wins only
  if outside min range); an explicit Attack on a too-near target keeps the target and waits (no step-back / kiting in
  this slice; document it).
- **Counter-triangle scenario rows** (`Scenario/CounterTriangleTests`): equal-cost groups on a flat map, both
  attack-moved into each other, both factions' pairs: Line beats Shock (`malazan_heavy_infantry` v
  `whirlwind_horse_raider`; `whirlwind_raider` v `malazan_wickan_lancer`), Shock beats Ranged (`malazan_wickan_lancer`
  v `whirlwind_desert_archer`; `whirlwind_horse_raider` v `malazan_crossbowman`), Ranged beats Light (the casters:
  `malazan_crossbowman` v `whirlwind_priest`; `whirlwind_desert_archer` v `malazan_cadre_mage`), Siege beats buildings
  (a `malazan_catapult` kills a Whirlwind Tent faster than the same cost of Heavy Infantry; a `whirlwind_battering_ram`
  likewise vs a Malazan building). Each row asserts the winner and prints the survivors and the time; if the shipped
  numbers don't give the doc's winner, the row fails and the report names the pair and the margin (that is a data issue
  for the data track's balance pass; the sim does not tune `game/data/factions/**`).
- **BUG-0156 (S3), fold in if a few lines:** a unit hitting a *building* in reach re-picks after its current swing
  when a unit is hitting it (the tier-0 priority); Producer default. Keep `AttackOrderTests` / `FightReissueQaTests`
  green. If it is more than a few lines, leave it (hardening).
- **Replay:** no format change (no new command). Golden regenerated once for `data-hash` only; every `k` line
  identical (the golden's path has no fights). `CommandDoorFuzzStressTests` needs no new kind.
- **Docs:** docs/03 "Implementation (M4-2b)" (the store, phases, hit rule, splash, friendly fire, min range, the view
  spans, cost), "Combat" subsection updates; docs/02 nothing unless a number is missing (then say so); docs/01 rows for
  the Producer decisions below. **Do not edit** docs/02 "Ages" or `docs/factions/**` (the data track's this session).
- **Out:** flying units (M9), fog (M4-3), abilities (M4-4), towers' attacks (M4-3), the view's projectile visuals
  (M4-V3), kiting / step-back, any number change under `game/data/factions/**`.

**Size.** A new system with perf risk: aim for ~800 rule lines (the store, the resolution, min range, loader); the
scenario rows and tests on top. If the counter-triangle rows push the budget, ship them as report rows first and say so.

**Acceptance criteria.**
1. `projectiles.json` loads with exactly the five ids; every `attack.projectile` id resolves; loader tests for a
   missing id, a bad kind, a non-positive speed, unknown fields, a `hitTolerance` on a lob; `DataValidationTests`
   green on shipped data; `data-hash` moved once with the golden, `k` lines identical.
2. A Crossbowman at 8 m fires at the wind-up end; the bolt arrives after `ceil(8 / 25 / 0.05)` ticks; a standing
   Raider takes the worked-example damage (`9 x 0.6 x 1.3 = 7.02 → 7 - 1 = 6`); a target that moved more than
   `radius + 0.3` m off the impact point is missed (0 damage, no event); rows: 100 shots at a walking Heavy Infantry
   hit ≥ 95 %; at a galloping Horse Raider crossing at 5+ m, < 50 %.
3. A Catapult's stone always explodes at the impact point: splash falloff 100 % inside 40 % of the radius, 50 % at the
   edge (rows at 0, 0.4 r, 0.7 r, r, r + ε); own units in the radius take 50 % (friendly fire), own buildings 0; a
   Cadre Mage's `magic_bolt` splash hits enemies only (no `friendlyFire`); enemy buildings in a splash take structure
   damage.
4. Minimum range: a Catapult never fires at a Raider inside its min range (0 damage over 200 ticks), fires at one just
   outside; under attack-move it re-picks a unit outside min range when one is in sight; an explicit Attack on a
   too-near target waits (documented).
5. Every unit of both factions deals damage in a 1 v 1 against a held Tent or Laborer (table-driven over `units.json`);
   `CanFight` is true for every shipped unit; the M4-2a "Attack dropped for a unit that cannot fight" rows flip to
   "obeys".
6. Counter-triangle rows exist for the eight pairs above and pass, or fail with the pair and margin named and reported
   (a data issue for D-next, not a REJECT reason if everything else holds: Producer's call at ACCEPT).
7. Determinism: `CombatFuzzTests` + QA fuzz with ranged units (6 seeds x 3,000 ticks, twins equal every tick, replay
   round-trip); the reflection hash audit covers the projectile store; `StateHashTests` row for projectiles in flight.
8. Perf: 500 v 500 mixed brawl (ranged + melee) under 4 ms alone; 1,000 projectiles in flight cost under 0.3 ms a tick
   and 0 B; `TightBlob2500` alone ≤ 4.6 ms (first run in a fresh process); the gather rows unchanged.
9. The view spans (`ProjectileStore` read-only members, `World.Impacts`) exist, are documented, and are not hashed
   beyond the store's own state (impacts are derived, cleared each tick).
10. Full suite incl. Perf green alone; 0 warnings; nothing under `game/` except `game/data/common/projectiles.json`,
    nothing under `ViewApi/`, `Content/`, `QA/Content/`, `game/data/factions/`, `docs/factions/`; docs per scope.

**Design references.** docs/02 "Combat: Stats" (min range, splash, friendly-fire flag, `attack.targets`), "Damage
formula", "Projectiles" (25 m/s aimed at the position at firing, hit within collision radius + 0.3 m; lobs 12 m/s
always explode), "Splash and friendly fire" (40 % / 50 % falloff, 50 % friendly, never buildings), "Faction template"
(the counter table and template bonuses), docs/03 "Implementation (M4-1)" / "(M4-2a)" (phases 7 / 10 / 11,
`PendingHit`, `CanFight`, the hash blocks).

**Tests required.** Loader tests (criterion 1); `ProjectileTests` (flight time, hit / miss rule, lob); `SplashTests`
(falloff rows, friendly fire, buildings); `MinRangeTests`; the table-driven every-unit-fights row; `CounterTriangleTests`;
`CombatFuzzTests` extended; `StateHashTests` row; Perf rows (criterion 8); `ReplayGoldenTests` green after the regen.

**Constraints.** No Godot in `Rts.Sim`; no wall clock / `System.Random`; trig through `SimMath`; no `Dictionary`
iteration; no per-tick allocation (the projectile store and splash scratch preallocated in `World`); every number from
data (`projectiles.json`, `units.json`); no new player-facing strings; docs in the same commit; absolute paths; `git diff
--stat origin/main` before reporting; never kill processes you did not start.

**Producer decisions to record (docs/01, owner may revisit):** (a) projectiles fly straight at constant speed (the view
draws any arc); (b) a miss lands harmlessly (no ground splash for aimed shots); (c) a too-near target under an explicit
Attack is waited on, not kited; (d) splash hits enemy buildings as structure damage, never own buildings; (e)
`hitTolerance` is per projectile (default 0.3 m) so a data author can make bolts and arrows differ; (f) the ram stays a
melee siege unit (no projectile).

## View track

### Current session plan: M4-V2 — the Attack-target order in the HUD, cursor and card feedback; BUG-0160 items 1-4 (feature, QA standard, bugs from BUG-0190)

**Goal.** Let the player tell a unit to attack *this* enemy (M4-2a's `Command.Attack` is on `main`): a right-click on an
enemy unit or building attacks it, A + click on an enemy attacks it (A + click on ground stays attack-move), the panel
and a short target ring say so. The docs/02 Controls table: "Right-click: context command: move, attack, ...",
"A + click: Attack-move".

**Scope.**
1. **Picking an enemy:** extend the existing ray picks (`ViewApi.BuildingPicker.PickRay` already picks buildings; add a
   unit pick: nearest live unit whose drawn body the ray hits, through the spatial hash or the store's spans, read-only,
   allocation-free) so a click can resolve to an enemy unit or building. Own units keep the selection semantics (a
   left-click selects); a **right-click on an enemy unit / building** with units selected enqueues `Command.Attack` for
   each selected unit (workers too: they obey an explicit Attack), Shift queues it; a right-click on an own unit is a
   Move to it as today. **A + click on an enemy** = Attack; A + click on ground = AttackMove (unchanged). Units that
   cannot fight yet get the order dropped by the sim on the base commit (M4-2b opens `CanFight` this session; nothing to
   do in the view either way).
2. **Feedback:** the `Command` sound as for any order; the selection panel's state text for an `Ordered` unit reads the
   existing "Attacking" when it swings and a new `ui.json` `states.ordered_attack` label while it chases (short; it is
   data, the builder picks the words and quotes them in the report); a **red outline ring** on the targeted enemy for
   0.5 s (pooled, 0 B; no cursor art before M6). The F12 second line adds the selected unit's target slot.
3. **Minimap right-click** on an enemy dot: stays a Move (the attack half waits for fog in M4-3 when dots become
   reliable; say so in docs/03).
4. **BUG-0160 items 1, 3, 4** (a few lines each): move the F12 line below the K / L label or shorten it; corpse discs
   readable by team (owner colour at 60 % with a darker rim, or the owner colour fill with a 1-px rim at 100 %: builder's
   pick, screenshot it and look); `DebugOverlay`'s "K" / "L" fallback literals → empty like `ResourceBar`. **Item 2** (a
   unit hit before its first frame never flashes): compare a new slot's first hp with the type's max and light it if
   below (document: "a unit first seen below full hp flashes once").
5. Docs/03 "Implementation (M4-V2)"; the "Debug tooling" minimap note. **Do not edit** the "No-combat flag" paragraph
   (the Producer fixed its stale "until format 4" sentence at PLAN).
6. **Headless scene** `game/tests/AttackOrderViewTest.tscn`: a 10 v 10 setup; right-click an enemy → every selected unit
   holds that target (`UnitStore.Target`) within a tick; A + click on an enemy → same; A + click on ground → AttackMove
   mode; Shift + right-click two enemies → the queue holds the second as an Attack entry (`QueuedTarget`); a right-click
   on an enemy building → a building target; the panel's text; the hash twin (the command stream replayed bare).
7. **Out:** projectile visuals (M4-V3, after M4-2b lands), the fog shader (M4-3), cursor art and attack animations (M6),
   Patrol, anything under `sim/Rts.Sim/` outside `ViewApi/`.

**Acceptance criteria.**
1. Right-click and A + click on an enemy unit or building send `Command.Attack` per selected unit (Shift queues); on
   ground the old behaviour; proven in the scene on two seeds and by a `ViewApi` pick test (1,000 rays over a brawl:
   every pick names the drawn unit under the ray or none; 0 own units picked as targets).
2. The panel reads the `ui.json` label for an ordered attacker (chasing and swinging); no C# literal.
3. The target ring is pooled, 0 B per frame in a steady brawl, and gone after 0.5 s.
4. BUG-0160 items 1, 3, 4 fixed with a screenshot of the corpse discs looked at; item 2 fixed or re-filed with the rule.
5. Docs/03 "Implementation (M4-V2)" present; the minimap note.
6. Scene loop green (count reported: expect 29 headless + 5 Shot + Playable), smoke PASS, non-Perf green, view Perf rows
   green alone, 0 warnings; nothing under `sim/Rts.Sim/` outside `ViewApi/`, nothing under `game/data/factions/`, golden
   untouched.

**Design references.** docs/02 "Controls and camera" (right-click context command; A + click; minimap right-click),
docs/03 "Orders and unit states" (kind 16, `QueuedTarget`), "Implementation (M4-2a)" (the drop rules, `Ordered`),
"Implementation (M3-V2)" / "(M3-V4)" (ray picks, the drawn shapes), "Implementation (M4-V1)".

**Tests required.** `AttackOrderViewTest.tscn` (criterion 1, 2, 3 rows and the hash twin); a `ViewApi` unit-pick test
in the view's test folder; the BUG-0160 rows (`CombatViewTest` / `QaV5Test` additions: first-frame flash rule, F12
layout); the existing scene loop.

**Constraints.** Views hold no gameplay state; `ViewApi` additions read-only, never change the tick or the hash;
no per-frame allocation in steady state; player-facing text only in `ui.json`; C# only; absolute paths; `git diff
--stat origin/main` before reporting; never kill processes you did not start.

## Data track

### Current session plan: D5 — pin `attack.targets` to the faction pages; the docs/02 "Ages" wording drift with the `TechContentTests.G` parser; BUG-0155 (feature, QA light, bugs from BUG-0200)

**Goal.** The first data task since D4: the `attack.targets` schema is on `main` (M4-2a), so the faction pages and the
content tests should pin it; and the two D4 leftovers (docs/02 "Ages" wording, the case-sensitive name check).

**Scope.**
- `docs/factions/whirlwind.md` and `docs/factions/malazan.md` Units tables (or their notes): a "Targets" column or a
  note the tests read: "buildings" for the Battering Ram, "all" (or blank = all) for every other unit of both factions;
  `Content/UnitContentTests` pins `attack.targets` to the page for all 14 units. No data value changes (the ram already
  says `buildings`): no golden regen.
- docs/02 "Ages": "level-2 Forge upgrades" → "level II Forge upgrades"; "Requires two Age I production buildings or a
  Forge" → the exact any-two-of rule as the data has it (`requiresAnyOf` count 2 of Infantry Hall, Ranged Hall, Shock
  Hall, Forge); update `TechContentTests.G`'s parser in the same commit so the bullets and the test move together.
- BUG-0155: `TechContentTests.H`'s name check any-case and covering faction tech names (QA's `SharedTechTextQaTests`
  already does; make the dev test match).
- Also pin, if cheap: the "attacks buildings only" sentence in the Whirlwind page's Battering Ram note must agree with
  the data's `targets` (a sentence ↔ field check like the "needs ..." one).
- **Out:** any number change (the balance pass waits for the counter-triangle rows and the M4 sandbox), tower fields
  (M4-3), abilities (M4-4), `projectiles.json` (sim-owned; the sim ships it this session: do not reference its ids in
  tests yet).
- **Files:** `docs/factions/whirlwind.md`, `docs/factions/malazan.md`, `docs/02-game-design.md` "Ages" paragraph only,
  `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. The sim's M4-2b does not touch docs/02 "Ages" or
  the faction pages.

**Acceptance criteria.**
1. `Content/UnitContentTests` pins `attack.targets` for all 14 units to the pages; flipping the ram to `all` in a
   scratch copy fails naming the unit and field.
2. docs/02 "Ages" reads "level II" and the exact any-two-of rule; `TechContentTests.G` parses the new wording and still
   pins `age_ii.requiresAnyOf`.
3. BUG-0155 fixed (`TechContentTests.H` any-case, faction names covered; a mutation with "moranth supply" in a shared
   text fails).
4. No `game/data/**` change; golden untouched; non-Perf suites green; 0 warnings; the For your review table says
   "nothing numeric changed".

**Design references.** docs/02 "Combat: Stats" (`attack.targets`), "Ages"; `docs/factions/whirlwind.md` Battering Ram
note ("attacks buildings only"); docs/03 "Implementation (M4-2a)" (`attack.targets` semantics).

**Tests required.** `Content/UnitContentTests` targets row (criterion 1); `TechContentTests.G` / `.H` updated
(criteria 2-3); `DataValidationTests` green.

**Constraints.** No C# outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`; no `game/data/**` edits; player-facing
text only in data (none changes here); absolute paths; `git diff --stat origin/main` before reporting.

## Watch-outs (all tracks)

- **Golden:** only the sim regenerates this session (`data-hash` for `projectiles.json`), once, with the reason.
- **Merge order:** sim, then view, then data. Shared-file touches expected: docs/03 (one subsection per track), docs/01
  (rows appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only, keep every side). The data track
  owns docs/02 "Ages" and the faction pages this session; the sim leaves them alone.
- **Sessions:** a lock's age alone does not prove the previous session dead; check for running processes before
  resuming; long sessions refresh the lock. The sandbox may refuse scratch clones / worktrees in the scratchpad: the
  Producer's integration check is then the conductor's merge gate plus a diff-level disjointness review (say so in the
  log).
- `TightBlob2500` 4.35-4.47 ms of 4.6: M4-2b's per-unit work (projectile scans, splash queries) must stay inside it;
  measure alone, first run.
- Hardening is due after one more feature session per track (sim 3 / 4, view 3 / 4 after this one) or at M4's end,
  whichever first.
