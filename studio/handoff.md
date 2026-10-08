# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-08-0313** (sim M4-2a ACCEPT, view M4-V1 ACCEPT, data STOP
planned). The next Producer PLAN rewrites the "Current session plan" sections; what follows is the recommendation.
**Bug ids next session:** sim from **BUG-0180**, view from **BUG-0190**, data from **BUG-0200** (disjoint blocks; the
sim used 0150-0157, the view 0160 this session).

## Where we are

- **M0, M1, M2, M3 Done.** M3 was signed off at this ACCEPT (retro in docs/05): BUG-0146 fixed, every track hardened,
  coverage rows ✅, no open S1 / S2.
- **M4: 3 / 10 criteria** (1 attack / attack-move / chase / retaliation / priorities, 2 damage formula, 4 death /
  corpses / rubble). Next: projectiles + splash + friendly fire + min range (M4-2b, sim), the Attack-target click in the
  HUD (M4-V2, view), fog (M4-3), abilities / statuses / zones (M4-4, schema → data), stealth / detection (M4-5), the
  counter-triangle scenarios, the fog-on sandbox Playable.
- Open bugs after the merge: **28** (S1 0, **S2 0**, S3 14, S4 14). Hardening counters: sim 2 / 4, view 2 / 4, data 0 / 4.
- **Integration watch-outs:** (1) the conductor merges sim then view: conflicts expected only in docs/03 (two
  "Implementation" subsections near the same anchor), `studio/bugs/README.md` and `studio/qa/coverage.md` (append both
  sides); no scratch merge was possible this session (sandbox), so the first thing next session is the usual repo
  health check on `main` (build, non-Perf tests, smoke, and the scene loop count: expect 28 headless + 5 Shot +
  Playable). (2) Replay format 4 is on `main`: `ReplayPlayer.Run(replay, data)` plays with the header's `combat`; the
  view's docs/03 "Debug tooling" line "until format 4" is now stale (one line, view's next task). (3) The Perf category
  run in one process fails 8-9 wall-clock rows on base and head alike (a long-running python process on this PC); each
  passes alone. A Perf failure counts only alone. `TightBlob2500` is at 4.35-4.47 of 4.6 ms. **New (BUG-0158, S4):** on
  this PC a process that runs the 2,500 blob for more than ~2 s sees later runs at 7.2 ms (sustained-load throttle;
  identical on base and head), so QA's `TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree` fails alone today: not a
  code failure; the budget row (one short run) passes. Builders: measure Perf rows one at a time and read the first run. (4) `CLAUDE.md` still says
  "Current milestone: M1" (owner's file; suggested text under For your review, M1 entry).

## Sim track

### Current session plan (recommended): M4-2b — projectiles with travel time and misses, splash with falloff, friendly fire, minimum range; the counter-triangle scenario rows (feature, QA full, bugs from BUG-0180)

**Goal.** Give ranged, caster and siege units their attacks (M4 criterion 3 and the "Scenario tests for the counter
triangle" criterion): a shot is a projectile that flies to where the target *was*, hits only if the target is still
near the impact point, and splashes with falloff; Sappers and Catapults hurt their own side at half damage. With this
every unit of both factions fights, which the sandbox Playable and the M5 AI need.

**Scope.**
- **Schema (sim-owned, `game/data/common/projectiles.json`, new):** one entry per projectile id already referenced by
  `units.json` (`bolt`, `arrow`?, `magic_bolt`, `catapult_stone`, `sharper`, and whatever ids the Whirlwind file uses:
  grep both files first and ship exactly those entries, the minimum its tests need; the data track fills nothing this
  session): `kind` (`aimed` / `lob`), `speed` (m/s: aimed 25, lob 12 per docs/02), `hitTolerance` (m, aimed only,
  default 0.3). Validated at load: every `attack.projectile` id must exist (today they are unresolved strings:
  `AttackDef.Projectile`), unknown fields and bad kinds are `DataError`s; `DataLimits`; `ContentHash` covers it
  (`data-hash` moves once with the golden regen). `CanFight` opens to every attack with a value (ranged / caster / siege
  with a projectile now fight: the M4-2a "dropped" rule for them ends, docs/01 row).
- **Projectile entities:** a flat SoA store (`ProjectileStore`, capacity in `SimConfig`, generational handles not needed:
  a projectile is fire-and-forget), spawned at the damage point of a ranged attack (the wind-up end) aimed at the
  target's position at that moment, moving `speed x 50 ms` a tick in a straight line (no gravity sim, the view draws the
  arc), resolved in phase 11 when it reaches the impact point: an **aimed** projectile hits its target if the target is
  alive and within `radius + hitTolerance` of the impact point (else it misses and lands: nothing); a **lob** always
  explodes at the impact point. Hashed (position, owner, damage parameters) when any exist; no allocation (a
  preallocated store, a per-tick splash scratch list); deterministic order (slot order).
- **Splash** (`attack.splash` > 0): on impact (or on a melee hit with splash, if any), every unit within the radius takes
  `damage x f(d)`: 100 % within 40 % of the radius, linear to 50 % at the edge (docs/02 "Splash and friendly fire");
  enemies always; **own and allied units only when `friendlyFire`**, at 50 % (after the falloff); **friendly fire never
  damages buildings**; enemy buildings in the radius take structure damage. Damage per victim through `DamageCalc.Compute`
  with the victim's own class and armor. Hits queue into the same phase-11 resolution so mutual kills still land.
- **Minimum range** (`attack.minRange`): a target nearer than it cannot be fired at: the unit does not swing (no wind-up),
  and under a chase / attack-move it re-picks on its next scan (a nearer enemy of equal priority wins); an explicit Attack
  on a too-near target keeps the target and waits (it does not step back: no kiting AI in this slice; document it).
- **Counter-triangle scenario rows** (`Scenario/CounterTriangleTests`): equal-cost groups on a flat map, both attack-moved
  into each other: Line beats Shock (Heavy Infantry vs Horse Raiders), Shock beats Ranged (Lancers vs Desert Archers),
  Ranged beats Light (Crossbowmen vs Raiders... check docs/02 "Faction template" for the intended pairs and use the
  pairs the doc names), Siege beats buildings (a Catapult kills a Tent faster than the same cost of Heavy Infantry).
  Each row asserts the winner and prints the survivors; if the shipped numbers don't give the doc's winner, the row
  fails and the report says which pair and by how much (the data track's balance pass fixes numbers; the sim does not
  tune data).
- **BUG-0156 (S3), fold in if a few lines:** a unit hitting a *building* in reach re-picks after its current swing when
  a unit is hitting it (the tier-0 priority); Producer default. Keep `AttackOrderTests` /`FightReissueQaTests` green.
- **Replay:** no format change (no new command). Golden regenerated once for `data-hash` only; every `k` line identical
  (the golden's path has no fights).
- **Docs:** docs/03 "Implementation (M4-2b)" (the store, phases, hit rule, splash, friendly fire, min range, cost),
  "Combat" subsection updates, docs/02 nothing unless a number is missing (then say so); docs/01 rows for the Producer
  decisions below.
- **Out:** flying units (M9), fog (M4-3), abilities (M4-4), towers' attacks (M4-3 with sight / detector), the view's
  projectile visuals (the view's M4-V3), kiting / step-back behaviour.

**Acceptance criteria.**
1. `projectiles.json` loads; every `attack.projectile` id resolves; loader tests for a missing id, a bad kind, a
   negative speed, unknown fields; `DataValidationTests` green on shipped data; `data-hash` moved once with the golden.
2. A Crossbowman at 8 m fires at the wind-up end; the bolt arrives after `ceil(8 / 25 / 0.05)` ticks; a standing target
   takes the worked-example damage (`9 x 0.6 x 1.3 = 7.02 → 7 - 1 = 6` vs a Raider); a target that moved more than
   `radius + 0.3` m off the impact point is missed (0 damage, no event); a slow target almost never is (row: 100 shots at
   a walking Heavy Infantry hit ≥ 95 %; at a galloping Horse Raider crossing at 5+ m, < 50 %).
3. A Catapult's stone always explodes at the impact point: splash falloff 100 % inside 40 % of the radius, 50 % at the
   edge (rows at 0, 0.4 r, 0.7 r, r, r + ε); own units in the radius take 50 % (friendly fire), own buildings 0; a
   Cadre Mage's `magic_bolt` splash hits enemies only (no `friendlyFire`).
4. Minimum range: a Catapult never fires at a Raider inside its min range (0 damage over 200 ticks), fires at one just
   outside; under attack-move it re-picks a unit outside min range when one is in sight.
5. Every unit of both factions deals damage in a 1 v 1 against a held Tent or Laborer (table-driven over `units.json`);
   `CanFight` is true for every shipped unit; the M4-2a "Attack dropped for a unit that cannot fight" rows flip to
   "obeys".
6. Counter-triangle rows pass (or fail with the pair and margin named, reported: that is a data issue for D-next, not a
   REJECT reason if everything else holds, Producer's call at ACCEPT).
7. Determinism: `CombatFuzzTests` + QA fuzz with ranged units (6 seeds x 3,000 ticks, twins equal every tick, replay
   round-trip); the reflection hash audit covers the projectile store; `StateHashTests` row for projectiles in flight.
8. Perf: 500 v 500 mixed brawl (ranged + melee) under 4 ms alone; 1,000 projectiles in flight cost under 0.3 ms a tick, 0 B
   a tick; `TightBlob2500` alone ≤ 4.6 ms; the gather rows unchanged.
9. Full suite incl. Perf green alone; 0 warnings; nothing under `game/` except `game/data/common/projectiles.json`,
   nothing under `ViewApi/`, `Content/`, `QA/Content/`, `game/data/factions/`; docs per scope.

**Design references.** docs/02 "Combat: Stats" (min range, splash, friendly-fire flag), "Damage formula", "Projectiles"
(25 m/s aimed at the position at firing, hit within collision radius + 0.3 m; lobs 12 m/s always explode), "Splash and
friendly fire" (40 % / 50 % falloff, 50 % friendly, never buildings), "Faction template" (the counter pairs), docs/03
"Implementation (M4-1)" / "(M4-2a)" (phases 7 / 10 / 11, `PendingHit`, `CanFight`, the hash blocks).

**Constraints.** No Godot in `Rts.Sim`; no wall clock / `System.Random`; trig through `SimMath`; no `Dictionary`
iteration; no per-tick allocation (the projectile store and splash scratch preallocated in `World`); every number from
data (`projectiles.json`, `units.json`); no new strings; docs in the same commit; absolute paths; `git diff --stat
origin/main` before reporting; never kill processes you did not start.

**Producer decisions to record (docs/01, owner may revisit):** (a) projectiles fly straight at constant speed (the view
draws any arc); (b) a miss lands harmlessly (no ground splash for aimed shots); (c) a too-near target under an explicit
Attack is waited on, not kited; (d) splash hits buildings of the enemy as structure damage, never own buildings;
(e) `hitTolerance` is per projectile (default 0.3 m) so a data author can make bolts and arrows differ.

## View track

### Current session plan (recommended): M4-V2 — the Attack-target order in the HUD, the cursor and card feedback; BUG-0160 items 1 / 3 / 4; the stale "until format 4" line (feature, QA standard, bugs from BUG-0190)

**Goal.** Let the player tell a unit to attack *this* enemy (M4-2a's `Command.Attack` is on `main`): a right-click on an
enemy unit or building attacks it, A + click on an enemy attacks it (A + click on ground stays attack-move), the panel
and cursor say so. The docs/02 Controls table: "Right-click: context command: move, attack, ...", "A + click: Attack-move".

**Scope.**
1. **Picking an enemy:** extend the existing ray picks (`ViewApi.BuildingPicker.PickRay` already picks buildings; add a
   unit pick: nearest live unit whose drawn body the ray hits, through the spatial hash or the store's spans, read-only,
   allocation-free) so a click can resolve to an enemy unit or building. Own units keep the selection semantics (a
   left-click selects); a **right-click on an enemy unit / building** with fighters selected enqueues `Command.Attack`
   for each selected unit (workers too: they obey an explicit Attack), Shift queues it; a right-click on an own unit is a
   Move to it as today. **A + click on an enemy** = Attack; A + click on ground = AttackMove (unchanged). Units that
   cannot fight yet get the order dropped by the sim (until M4-2b lands; nothing to do in the view).
2. **Feedback:** the `Command` sound as for any order; the selection panel's state text for an `Ordered` unit reads the
   existing "Attacking" when it swings and a new `ui.json` `states.ordered_attack` ("Attacking target"? pick a short
   label; it is data) while it chases; an attack cursor hint is optional (no cursor art before M6): a red outline ring
   on the targeted enemy for 0.5 s is enough (pooled, 0 B). The F12 second line adds the selected unit's target slot.
3. **Minimap right-click** on an enemy dot: keep as a Move (the docs say "move or attack order"; the attack half waits for
   fog in M4-3 when dots become reliable; say so in docs/03).
4. **BUG-0160 items 1, 3, 4** (a few lines each): move the F12 line below the K / L label or shorten it; corpse discs
   readable by team (owner colour at 60 % with a darker rim, or the owner colour at 35 % only for the fill and a 1-px
   rim at 100 %: builder's pick, screenshot it); `DebugOverlay`'s "K" / "L" fallback literals → empty like `ResourceBar`.
   Item 2 (a unit hit before its first frame never flashes): compare a new slot's first hp with the type's max and light
   it if below (document the rule: "a unit first seen below full hp flashes once").
5. Docs/03 "Debug tooling": the `--no-combat` replay line (format 4 is on `main`: `ReplayPlayer.Run(replay, data)` plays
   with the header). Docs/03 "Implementation (M4-V2)".
6. **Headless scene** `game/tests/AttackOrderViewTest.tscn`: a 10 v 10 setup; right-click an enemy → every selected unit
   holds that target (`UnitStore.Target`) within a tick; A + click on an enemy → same; A + click on ground → AttackMove
   mode; Shift + right-click two enemies → the queue holds the second as an Attack entry (`QueuedTarget`); a right-click
   on an enemy building → a building target; the panel's text; the hash twin (the command stream replayed bare).
7. **Out:** projectile visuals (M4-V3, after M4-2b), the fog shader (M4-3), cursor art and attack animations (M6),
   Patrol.

**Acceptance criteria.**
1. Right-click and A + click on an enemy unit or building send `Command.Attack` per selected unit (Shift queues); on
   ground the old behaviour; proven in the scene on two seeds and by a `ViewApi` pick test (1,000 rays over a brawl:
   every pick names the drawn unit under the ray or none; 0 own units picked as targets).
2. The panel reads the `ui.json` label for an ordered attacker (chasing and swinging); no C# literal.
3. The target ring (if built) is pooled, 0 B per frame in a steady brawl, and gone after 0.5 s.
4. BUG-0160 items 1, 3, 4 fixed with a screenshot of the corpse discs looked at; item 2 fixed or re-filed with the rule.
5. The docs/03 "until format 4" line corrected; docs/03 "Implementation (M4-V2)".
6. Scene loop green (count reported), smoke PASS, non-Perf green, view Perf rows green alone, 0 warnings; nothing under
   `sim/Rts.Sim/` outside `ViewApi/`, nothing under `game/data/factions/`, golden untouched.

**Design references.** docs/02 "Controls and camera" (right-click context command; A + click; minimap right-click),
docs/03 "Orders and unit states" (kind 16, `QueuedTarget`), "Implementation (M4-2a)" (the drop rules, `Ordered`),
"Implementation (M3-V2)" / "(M3-V4)" (ray picks, the drawn shapes), "Implementation (M4-V1)".

**Constraints.** Views hold no gameplay state; `ViewApi` additions read-only, never change the tick or the hash;
no per-frame allocation in steady state; player-facing text only in `ui.json`; C# only; absolute paths; `git diff
--stat origin/main` before reporting; never kill processes you did not start.

## Data track

### Current session plan (recommended): D5 — pin the ram's `attack.targets` to the Whirlwind page; the docs/02 "Ages" wording drift with the `TechContentTests.G` parser; BUG-0155 (feature, QA light, bugs from BUG-0200)

**Goal.** The first data task since D4: the `attack.targets` schema is on `main` (M4-2a), so the faction pages and the
content tests should pin it; and the two D4 leftovers (docs/02 wording, the case-sensitive name check).

**Scope.**
- `docs/factions/whirlwind.md` Units table (or its notes): a "Targets" column or a note the tests read, "buildings" for
  the Battering Ram, "all" (or blank) for every other unit of both factions; `Content/UnitContentTests` pins
  `attack.targets` to the page for every unit (a Malazan units table row too, all `all`). No data value changes (the ram
  already says `buildings`): no golden regen.
- docs/02 "Ages": "level-2 Forge upgrades" → "level II Forge upgrades"; "Requires two Age I production buildings or a
  Forge" → the exact any-two-of rule as the data has it (`requiresAnyOf` count 2 of Infantry Hall, Ranged Hall, Shock
  Hall, Forge); update `TechContentTests.G`'s parser in the same commit so the bullets and the test move together.
- BUG-0155: `TechContentTests.H`'s name check any-case and covering faction tech names (QA's `SharedTechTextQaTests`
  already does; make the dev test match).
- Also pin, if cheap: the "attacks buildings only" sentence on the Whirlwind page's Battering Ram note must agree with
  the data's `targets` (a sentence ↔ field check like the "needs ..." one).
- **Out:** any number change (the balance pass waits for the M4 sandbox and the counter-triangle rows), tower fields
  (M4-3), abilities (M4-4), `projectiles.json` (sim-owned; the sim ships it in M4-2b).
- **Files:** `docs/factions/whirlwind.md`, `docs/factions/malazan.md`, `docs/02-game-design.md` "Ages" paragraph only,
  `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. The sim's M4-2b must not touch docs/02 "Ages" or
  the faction pages that session (it ships `projectiles.json` and docs/03 only).

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

## Watch-outs (all tracks)

- **Golden:** only the sim regenerates next session (`data-hash` for `projectiles.json`), once, with the reason; the
  view and data tracks touch neither the golden nor `game/data/**` (view: `ui.json` labels only).
- **Merge order:** sim, then view, then data. Shared-file touches expected: docs/03 (one subsection per track), docs/01
  (rows appended), `studio/bugs/README.md` and `studio/qa/coverage.md` (append-only, keep every side). The data track
  owns docs/02 "Ages" and the faction pages that session; the sim leaves them alone.
- **Sessions:** a lock's age alone does not prove the previous session dead; check for running processes before
  resuming; long sessions refresh the lock. The sandbox may refuse scratch clones / worktrees in the scratchpad: the
  Producer's integration check is then the conductor's merge gate plus a diff-level disjointness review (say so in the
  log).
- `TightBlob2500` 4.35-4.47 ms of 4.6: M4-2b's per-unit work (projectile scans) must stay inside it; measure alone.
- Hardening is due after two more feature sessions per track (sim 2 / 4, view 2 / 4) or at M4's end, whichever first.
