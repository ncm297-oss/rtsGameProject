# Handoff: brief for the current session

## Resumption 2026-10-08-1814 (session 1435 died mid-way; same plan, all three tracks resumed)

Base `dd5b5b9` (main's code = `660a19c`; green per the 1435 plan check). Every track goes **straight to QA** on its
branch as it stands; the briefs below are unchanged. Fix rounds (cap 2 per session): sim **1 used, 1 left**; view
**round 1 in flight** (fix present, re-check pending; 1 more after it); data **0 used**.
- **Sim** `studio/2026-10-08-1435-sim` @ `b90539f`: implement 977db1d, QA FAIL (3 S2), fix 34931b8, QA re-check round 1
  **PASS_WITH_ISSUES committed 18:17 by the 1435 QA agent** (BUG-0214 / 0217 fixed; BUG-0212 / 0213 view-owned, fixed on
  the view branch). Non-Perf 3,922 / 13 / 0; Perf alone 139 / 3. QA now: confirm, don't redo (fog filter + the memory row
  + golden + Perf fog rows alone + scene loop 27 / 29 with only MinimapTest / SfxTest red).
- **View** `studio/2026-10-08-1435-view` @ `e1f3333`: QA PASS_WITH_ISSUES (S3 BUG-0220 / 0221 / 0223, S4 0222); the
  recovered commit is the unreviewed fix round 1: BUG-0221 (`ImpactMarks.Age`), BUG-0223 (`TerrainHeight.MaxUnder` rim
  rule), `--no-combat` in SfxTest + QaM24Test (BUG-0212 / 0213), 3 QA rows un-skipped. **Known gap:** docs/03's
  `--no-combat` scene list (line ~3741) still omits SfxTest / QaM24Test. QA re-check round 1 now; scene loop must be 33 / 33
  with SfxTest / QaM24Test run several times.
- **Data** `studio/2026-10-08-1435-data` @ `03711a1`: QA PASS_WITH_ISSUES, only S4 BUG-0230 (stays open; data hardening).
  QA confirm (content filter, no `game/data` diff), then ACCEPT.
- **Conductor:** process 45600 (claude.exe, started 14:35) was still committing in the sim worktree at 18:17; make sure
  the 1435 session is dead before builders / QA touch the worktrees. Merge order sim, view, data; the integration scene
  loop (33 scenes) must be fully green after sim + view merge.

Written by the Producer at the PLAN of session **2026-10-08-1435** (base `660a19c`; third full session of 2026-10-08,
cap 8). Three tracks, all **feature** sessions (the 4th since each track's last hardening for sim and view: their
hardening sessions come next; data 2 / 4). Sim **M4-3a** (fog of war, QA full), view **BUG-0210 then M4-V3** (QA
standard, **starts from the held branch `origin/studio/2026-10-08-0913-view`**), data **D6** (QA light).
**Bug ids this session:** sim from **BUG-0211**, view from **BUG-0220**, data from **BUG-0230** (BUG-0210 is taken).

## Where we are

- **M0, M1, M2, M3 Done.** **M4: 5 / 10 criteria** on `main` (1 attack orders incl. the view's held half, 2 damage, 3
  projectiles / splash / friendly fire, 4 death / corpses / rubble, the counter-triangle rows). Left: 5 fog +
  high-ground vision + fog shader + building ghosts (M4-3), 6 abilities / statuses / zones (M4-4), 7 stealth /
  detection (M4-5), 8 the four signature abilities, 10 the fog-on sandbox Playable.
- **Plan-time checks (Producer, this session):** `main` at 660a19c builds with 0 warnings; smoke PASS (tick 93);
  `MinimapTest` **reproduced red by the Producer on `main`** ("39 of 40 Moving 3 s after the minimap order", BUG-0210,
  S2, the only S1/S2); non-Perf sim suite **3,850 / 3,862 (12 skipped, each a filed bug) / 0 failed** (20 m 25 s, run
  alongside the smoke and the MinimapTest repro).
- **Found at plan time:** `tools/qa/scene-loop.ps1` is **not on `main`**; it exists only on the held view branch. The sim
  track's QA fetches it into the scratch directory (`git show origin/studio/2026-10-08-0913-view:tools/qa/scene-loop.ps1
  > <scratch>/scene-loop.ps1`; it takes the repo root from its own location, so copy it to `tools/qa/` in the worktree
  **without committing it**, or run it with `$PSScriptRoot` pointed at a copy under `tools/qa/`). It lands on `main`
  with the view's merge this session.
- **Hardening counters after this session (if all three ACCEPT):** sim 4 / 4, view 4 / 4, data 2 / 4: the next session
  is a hardening session for sim and view.

## Sim track

### Current session plan: M4-3a, fog of war (three states per player), the high-ground vision rule, vision-gated targeting

**Goal.** Fog of war exists in the rules: every player has an unexplored / explored / visible grid, recomputed every
4 ticks from its own units' and buildings' sight with the high-ground rule applied while stamping (low ground can't
see up), combat only acquires what the owner can see, and an attack from high ground reveals the attacker to the
victim's owner for 2 s. The view reads the grid read-only (the M4-V4 fog shader), M5's AI will read through it.

**Scope (in):**
- New `sim/Rts.Sim/Vision/` (`FogStore` per player: `byte[]` of map cells, 0 unexplored / 1 explored / 2 visible;
  `VisionSystem.Run` in **phase 12**; `VisionConstants`: `UpdateInterval` 4 ticks, `LipRadius` 4 m, `HighGroundRevealTicks`
  40). Every update, per player: every visible cell becomes explored, then every live own unit (`UnitDef.Sight`) and
  every live own building (new `sight`, below; a site under construction sees too) stamps a precomputed circle mask
  (one mask per distinct radius, built at load; squared-integer distances from the cell centre, no trig). Players may
  all update on the same tick or be spread across the 4 ticks (`tick % 4 == player % 4`): builder's choice, documented.
  An initial stamp at start-up before the first tick's commands, so start positions are explored at tick 0.
- **High ground in the stamp:** a cell is visible from a viewer only if `Heightmap.LevelAt(cell) <= viewerLevel` or the
  cell is within 4 m of the viewer; the viewer's level is `LevelAt` of its own cell (a ramp reports the lower level, so a
  unit on a ramp sees as from the lower plateau). No flying units exist yet (note the rule, no code).
- **Buildings' `sight`:** optional `sight` field (meters) on a building in `buildings.json`, default from a new required
  `rules.json` `"buildingSight"` (Producer default **12**; docs/02 gives sight only for the Watch Tower). The sim ships
  the rule and `"sight": 24` on `malazan_watchtower` and `whirlwind_lookout_tower` (those two lines are the only
  `game/data/factions/` edits; the data track does not touch `game/data/` this session). Validation: `sight` > 0 and
  ≤ `DataLimits.MaxSight` (new, 64), wrong type / unknown field = `DataError`; in `ContentHash`.
- **Targeting through fog (`CombatSystem`):** the scan (`PickTarget`), the in-range / in-sight checks and retaliation
  skip an enemy unit or building that the scanning unit's owner cannot see: its cell is `visible` for that owner, or
  the unit is **revealed** to that owner. An explicit `Attack` on an unseen target is dropped like a forbidden one. A
  chased or ordered target that stops being seen counts as "out of sight" for the existing give-up rules (BUG-0137 /
  0150). A friendly fire splash victim needs no visibility (it's a hit, not an acquisition).
- **High-ground reveal:** when a hit lands (melee hit or projectile impact that damages an enemy unit or building) from
  an attacker standing on a **higher level** than the victim, the attacker is revealed to the victim's owner until
  `tick + 40` (refreshed by every such hit). Store it hashed and allocation-free (a per-unit per-player expiry array or
  a small fixed ring of (unit, player, until); builder's choice, cost documented). A recycled slot never inherits a
  reveal. The attacker's level: at firing for a projectile (one byte per projectile slot) or at the hit for melee.
- **Read-only surface for the view (sim-owned, in `World.Fog`):** `Visibility(player)` (`ReadOnlySpan<byte>`,
  row-major cells), `Version(player)` (+1 per update that ran for that player), `IsVisible(player, cell)`,
  `IsExplored(player, cell)`, `CanSeeUnit(player, slot)` (cell visible or revealed), `CanSeeBuilding(player, slot)`.
  Documented in docs/03 "For the view (M4-V4)".
- **Hashing:** explored bits and reveals are state (hashed, packed if hashing costs more than ~20 µs a tick); visible
  bits and the version are derived every update (not hashed; a `StateHashTests` row proves it, like the plateau ids),
  so a save (M6) re-stamps at load (document it).
- Fold-ins (a few lines each): BUG-0184 item 1 (`<=` with a small epsilon on the lead test, or document the rounding
  and keep the skipped row skipped with the reason); the `ProjectileImpact.Position` doc comment (it is the impact
  point, which a led shot moves).
- Golden `cross_map_seed1.replay` regenerated **once** (`data-hash` for `rules.json` / `buildings.json`, `k` lines for
  the hashed fog) with the proof in criterion 6.

**Scope (out):** towers' `attack` / `detector` fields and buildings that shoot (M4-3b); the "last known buildings"
ghost list (M4-3b); the docs/02 placement rule "footprint explored by the player" (M4-3b, with the construction fuzz
fixtures); `Detected` / stealth / the Revealed status (M4-5); zones' vision effects (M4-4); `PlayerView` (M5); flying
units; any `game/` code (the shader, unit hiding and the minimap fog are the view's M4-V4); any change under
`sim/Rts.Sim/ViewApi/`, `sim/Rts.Sim.Tests/Content/`, `QA/Content/`, `docs/factions/`.

**Acceptance criteria:**
1. Schema: `buildings.json` accepts an optional `sight`; `rules.json` requires `buildingSight`; both watch towers carry
   `"sight": 24`; a negative / zero / string / > 64 `sight`, an unknown field, and a missing `buildingSight` are
   `DataError`s naming the id and field; `DataValidationTests` green; `GameData.ContentHash` covers both.
2. Stamp: on a flat test map, after the first update a lone unit with sight 18 at the centre makes exactly the disc of
   cells whose centre is within 18 m visible and nothing else (compared to a brute-force oracle); a building stamps from
   its footprint centre with its `sight`; an enemy's units never contribute; visible → explored when the unit leaves,
   explored persists for the match; a move at tick t shows at the next update tick and not before; a dead unit's circle
   is gone at the next update; the initial stamp exists at tick 0.
3. High ground, on a hand-built two-level map with a ramp: a level-0 unit 10 m from a level-1 cell does not see it; a
   level-1 unit sees level-0 cells in its radius; cells within 4 m are seen from any level; a unit on the ramp sees as
   level 0; a unit on the plateau top sees the ramp; the brute-force oracle agrees on every cell for 3 levels x 4 seeds
   of generated maps.
4. Targeting: a level-0 Crossbowman (range 15) 10 m from a still level-1 enemy deals 0 damage over 200 ticks and never
   takes a target; the level-1 Crossbowman shoots the level-0 one; from the first hit the low unit acquires the shooter
   and fires back within 40 ticks; 40 ticks after the last high-ground hit with nothing else seeing it, the reveal is
   gone and the low unit's next scan takes nothing. An explicit `Attack` on an unseen enemy is dropped (no target set);
   a chased target walking up a plateau out of vision is given up by the existing rule. On a **flat** map every
   `Scenario/CounterTriangleTests` row prints the same survivors and time as before (the scan radius is already the
   unit's `Sight`, so its own stamp covers every candidate; the Catapult's range 24 > sight 18 is the one shipped unit
   that outranges its sight: on its own it acquires at 18 m as today; an explicit Attack on a target no own unit or
   building sees is dropped, which M4-3b's last-known-buildings list relaxes for buildings; say so in docs/03).
5. Determinism and hashing: a `StateHashTests` row for every new hashed array (explored bits, reveals, the projectile
   level byte) and a row proving visible bits and versions are derived; `Stress/FogFuzzStressTests`: 4 seeds x 2,500
   ticks on generated 3-level maps, both players' armies attack-moving across levels, twins hash-equal every tick and a
   replay round trip equal.
6. Golden regenerated once with the reason in the commit, plus the proof that only the hashed fog moved: with the fog
   arrays excluded from the hash by a **local, uncommitted** edit (no shipped switch), every `k` line equals the
   pre-change golden; the report states the method.
7. Perf, alone, first run in a fresh process: a new Perf row (`Fog2500OnePlayer`, 128 map, 2,500 units of one player
   spread over the map, 300 ticks): fog's average cost ≤ 0.25 ms per tick amortized, 0 B; a second row with 1,000 v
   1,000 on a 3-level map ≤ 0.4 ms for fog; `TightBlob2500_OnePlayer` ≤ 4.6 ms (if it fails alone **only** by fog's
   separately measured cost, the bound may move to at most 4.8 ms with the numbers in the test's comment and the
   report; any other cause is a defect); `MixedBrawl500v500` ≤ 4 ms. Blob hint: stamping the same cell with the same
   radius and level twice in one update is skippable (a per-cell "stamped with radius r" byte cleared each update);
   spreading sources over the 4 ticks into a back buffer is also fine.
8. Scene loop (the script fetched from the held view branch, see "Where we are") 30 / 31 with only `MinimapTest` red
   (BUG-0210 is the view's); smoke PASS; any other red scene is this task's.
9. Every changed existing assertion listed in the report with its reason (a low unit that used to acquire a high
   target; nothing else), no threshold loosened; non-Perf green; 0 warnings; under `game/` only `rules.json` and the
   two `sight: 24` lines; nothing under `ViewApi/`, `Content/`, `QA/Content/`, `docs/factions/`.
10. Docs: docs/03 "Vision, detection, fog" rewritten to the implementation (stagger, what is hashed, the reveal rule,
    the target-validity rule, the view surface, what M4-3b / M4-5 still owe), the phase 12 line, the data-format table
    (`sight`, `buildingSight`); docs/02 "Vision and fog of war" gets a one-line note that a same-level attacker outside
    every own sight circle is not revealed (only high-ground attacks reveal until M4-5's Revealed status); docs/01 one
    change-log row (Producer decisions: `buildingSight` 12 m default, sites see, reveal keyed on the firing level, visible
    bits derived).

**Design references:** docs/02 "Vision and fog of war" and "High ground" (lines 247-274: three states, circular sight,
no terrain line-of-sight, ramp = lower level, `Level[cell] <= viewerLevel`, 4 m lip, 2 s attacker reveal, no damage
bonus); docs/02 Buildings table (Watch Tower sight 24); docs/03 "Vision, detection, fog" (per-player `byte[]`, every
4 ticks, the per-cell compare in the stamp, the target validity check) and the tick phases 7 / 10 / 11 / 12 / 14.

**Tests required:** `Vision/FogStampTests`, `Vision/HighGroundVisionTests` (with the brute-force oracle),
`Combat/FogTargetingTests`, `Combat/HighGroundRevealTests`, `Data/BuildingSightLoaderTests`, the `StateHashTests` rows,
`Stress/FogFuzzStressTests`, the two Perf rows, the golden regen with its proof.

**Constraints:** no Godot, no wall clock, no `System.Random`; no per-tick allocation (masks at load, per-player arrays
at `World` construction; `World.Neighbors` is shared scratch never kept across phases); no `Dictionary` iteration; no
trig (`SimMath` only; the circle masks use squared integer distances); every number in data or a named constant with
its docs/02 source; no player-facing text; the sim never reads the view. Scene loop from the held branch (above).
Size: a new system, so ~800-1,000 rule lines; if the reveal or the targeting gate pushes past 1,200, ship the reveal
as the first thing cut (document it as M4-3b) rather than the targeting gate.

### After this session (sim)

1. **The sim's hardening session (4 / 4):** BUG-0144, 0149, 0157, 0151, 0134, 0184, 0158, the `FieldBuildFairnessQaTests`
   memory-bound re-baseline, BUG-0113 / 0094, plus anything M4-3a leaves.
2. Then M4-3b (towers shoot + `detector` field, the last-known buildings list, placement needs explored ground), M4-4
   abilities / statuses / zones (schema → data track), M4-5 stealth / detection, the four signature abilities, the
   fog-on sandbox.

### Watch-outs (sim)

- `TightBlob2500` is at 4.3-4.54 of 4.6 ms: fog must not add per-unit work to the tick it runs in beyond the budget
  above. `World.Neighbors` is shared scratch (phases 7-11).
- `ProjectileStore` is SoA with slot reuse: the firing-level byte must be written at `Fire` and read at `Land`.
- The golden moves for two reasons at once (`data-hash` and hashed fog); the proof in criterion 6 is the gate.

## View track

### Current session plan: BUG-0210 first, then M4-V3 projectile visuals

**Branch to start from: `origin/studio/2026-10-08-0913-view` (ad7f5cc, the accepted M4-V2 work with `main` at 941a35a
merged in).** First thing: merge `origin/main` (660a19c: a studio state commit and the data track's D5 merge; expected
conflicts only in `studio/bugs/README.md` / `studio/qa/coverage.md`, append both sides). The conductor merges this
branch into `main` after the sim's.

**Goal.** Make `main`'s scene loop green again (BUG-0210, S2) so the held M4-V2 work merges, then draw the projectiles
the sim has been flying since M4-2b: bolts, arrows and magic bolts in a straight line, catapult stones and sharpers on
an arc, with a mark where each one lands.

**Scope (in):**
- **BUG-0210:** `game/tests/MinimapTest.cs` runs the Match with `--no-combat` as BUG-0147's five scenes (launch
  arguments only; every `Check` stays). Before changing it, print the failing slot's `Alive` / `Hp` / `State` / `Goal`
  once from the current scene to confirm the unit was killed; if it is alive and stopped on a plain Move, that is a
  **sim regression**: file it (S2, sim) and tell the Producer in the report. Scene loop 31 / 31.
- **M4-V3:** `game/scripts/ProjectileViews.cs`: two pooled MultiMeshes sized from `World.Projectiles.Capacity` (aimed: a
  short streak oriented along its flight; lob: a small stone), one instance per live slot per frame, position
  interpolated `PrevPosition → Position` by the render alpha (**never extrapolate**: a re-led bolt can step 1.8 m in a
  tick, BUG-0184), on `TerrainHeight` plus a launch height (~1.2 m). A lob gets a drawn arc: height = `apex x 4 t (1 - t)`
  with t = distance flown / total flight, apex from the flight length (a placeholder curve until M6). Flight progress
  needs the launch point: a read-only `ViewApi.ProjectileTracker` (view state only) that records each slot's launch
  point and total length on the frame it first sees the slot alive (`PrevPosition` is the launch point on the firing
  tick; a slot reused within one tick shows as a position jump: handle it) and drops it when the slot dies.
- **Impact marks** from `World.Impacts` read on `SimRunner.Ticked` (emptied at the next tick's start): `Hit` → a short
  flash (0.2 s); a miss → a small dust puff (0.3 s); a lob's landing → a burst scaled to its kind. Pooled like
  `DeathMarkers` (a ring, cap 512, oldest replaced), 0 B per frame.
- Fold in **BUG-0190 item 2** (a NaN entry guard in `UnitPicker.ResolveEnemy`, two lines) and item 1 if cheap (a corpse
  disc at the highest terrain sample under its radius).
- `--no-hud`, `--no-combat`, `--screenshot`, 8x speed all unaffected; a screenshot of a fight with bolts and a stone in
  the air, looked at.

**Scope (out):** any sim change outside `sim/Rts.Sim/ViewApi/` (new read-only code only); the fog shader, unit hiding
and ghosts (M4-V4, after the sim's M4-3a merges); real projectile models, trails, sounds (M6); the minimap's Attack half
(M4-V4); splash-radius rings (the impact carries no radius; a request if wanted).

**Acceptance criteria:**
1. `MinimapTest` passes on `main` + this branch with `--no-combat`, every `Check` unchanged, and the report states what
   the failing slot was (dead, or alive + the sim bug id). Scene loop 31 / 31; smoke PASS.
2. `game/tests/ProjectileViewTest.tscn` (seeds 1 / 6, A + click on the enemy army, 1x and 8x): every frame, live drawn
   instances == `World.Projectiles.Count`; each drawn position within one tick's step of the slot's `Position`; a lob
   instance's height above the terrain is > 0.5 m at mid-flight and ≤ 0.2 m at launch and landing; an aimed shot never
   leaves the segment `PrevPosition → Position`; impact marks appear on the frame after a tick with `Impacts.Count > 0`
   and are gone within their lifetime; after the fight no instance is visible.
3. 0 B per frame over 300 steady brawl frames with ≥ 100 shots in flight (`--units 200`); the tracker and the marker
   ring never allocate after `_Ready`; a `--units 500` archer-heavy spawn past `ProjectileCapacity` shows nothing wrong
   (shots lost cleanly in the sim).
4. Hash twin: the match's sim hash equals a bare twin's every checkpoint with the views running (ViewApi reads only).
5. BUG-0190 item 2 fixed with a test; item 1 either fixed with a scene check or left open with the reason.
6. Docs: docs/03 "Implementation (M4-V3)" (the tracker, interpolation, the arc, the marks, the pool sizes); the
   `--no-combat` paragraph lists `MinimapTest`; non-Perf green, view Perf 11 / 11 alone, 0 warnings; nothing under
   `game/data/factions/`, `sim/Rts.Sim/` outside `ViewApi/`.

**Design references:** docs/03 "For the view (M4-V3)" (Requests 21 in STATE: `Projectiles` spans, `Impacts`, interpolate
not extrapolate); docs/02 "Projectiles" (aimed vs lob); docs/03 "Debug tooling" (`--no-combat` is developer-only).

**Tests required:** `ProjectileViewTest.tscn` (criteria 2-4), `ViewApi/ProjectileTrackerTests` (slot reuse within a
tick, a slot that dies before the view's first frame, capacity), `ViewApi/UnitPickerTests` NaN row, the `MinimapTest`
change; `tools/qa/scene-loop.ps1` in the report.

**Constraints:** views hold no gameplay state; ViewApi additions read spans and never write sim state (hash twin);
no per-frame allocation; player-facing text from `ui.json` only (none expected); C# only.

### After this session (view)

1. **The view's hardening session (4 / 4):** BUG-0190 leftovers, BUG-0148, BUG-0126 items 3-6, the export-hygiene notes,
   the minimap dot-timing flake (contention), anything M4-V3 leaves.
2. Then M4-V4: the fog shader from `World.Fog.Visibility` / `Version` (an R8 texture uploaded on version change), units
   hidden when `!CanSeeUnit(local, slot)`, the minimap fog layer and its Attack half, `placement.unexplored` text when
   the sim's M4-3b rule lands; ability feedback (M4-4); the fog-on sandbox Playable.

### Watch-outs (view)

- `tools/qa/scene-loop.ps1` exists only on this branch until it merges; QA runs it from the worktree as before.
- Minimap dot timing rows (`ViewHardeningQaTests.DotRefresh_2000Units_WorstLayouts`) flake under CPU contention; a
  failure counts only alone.
- A re-led bolt bends slightly for a turning walker; interpolation hides it.

## Data track

### Current session plan: D6, the first balance report, the Sapper self-splash note, BUG-0200

**Goal.** Give the owner the numbers behind M4-2b's balance notes so he can decide, without changing any shipped number:
how wide each counter-triangle win is at equal cost, what margin the data track proposes as the target, and what the
Sapper's self-splash costs in practice with the options to fix it. Plus the two D5 pin gaps (BUG-0200).

**Scope (in):**
- **BUG-0200:** fold QA's `AgesRuleQaTests.AgesTrailingClause_MatchesTheAnyOfRule` check into `TechContentTests.G`
  (keep QA's row or retire it with a note); reword `UnitContentTests.C`'s "file attack.targets" message so an explicit
  `"targets": "all"` reads as the style rule it is ("targets written out although it is the default").
- **Balance report:** a content test (`Content/CounterTriangleMarginsTests`) that runs the eight equal-cost pairs of
  `Scenario/CounterTriangleTests` through the same harness (reuse its public helpers; do not edit it) in both seats and
  prints one table: pair, winner, surviving cost of the winner / the pair's cost, time to the last death. Pin only
  what docs/02 pins (the winner); **no margin assertion** (margins are the owner's call). Put the table in a new
  "Balance baseline (M4-2b, 2026-10-08)" section at the end of `docs/factions/malazan.md` and `whirlwind.md` (the
  Malazan page carries the full table, the Whirlwind page its own pairs), with the proposed target margins **as a
  proposal**, e.g. "the winner keeps 35-60 % of its cost; Line v Shock is at 85-100 %". The proposal and the
  reasoning go in the report for the Producer's For your review entry.
- **Sapper self-splash (BUG-0182 item 2):** a content test that measures, on a flat map, 4 Sappers + 4 Heavy Infantry v
  8 Horse Raiders (and the same with 4 Sappers alone): friendly-fire deaths, self-inflicted damage, the fight's outcome,
  under (a) the shipped data, (b) a scratch in-memory copy with `minRange` 2 m on the Sapper's attack, (c) a copy with
  splash 1 m (the sim's `TestSim` data-copy helpers, as `DataWithoutBuildingRequires` does: no file change). Print the
  three rows; assert only that the shipped data loads and the rows run. Report the numbers and the Producer's default
  (accept as the Sapper's trade-off, "Fragile; wants an escort", docs/factions/malazan.md) with the alternatives.
- Note on both pages' Watch Tower / Lookout Tower rows: nothing to change this session (the sim adds `sight` to the
  schema this session; the data track fills the tower rows' `sight` in D7 once it is on `main`).

**Scope (out):** any `game/data/**` edit (the sim edits `rules.json` and both `buildings.json` this session); any
number, name or description change; any C# outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`; the golden; docs/02
(the Producer rewrites shared docs at ACCEPT if the report warrants it).

**Acceptance criteria:**
1. BUG-0200: `TechContentTests.G` fails on both tail mutations of the docs/02 "Ages" clause (QA's row kept or retired
   with a note), naming the field; `UnitContentTests.C`'s message for an explicit `"targets": "all"` says the style rule.
2. The margins table test runs all 8 pairs in both seats, prints the table, pins only the winner, and the two faction
   pages carry the table with the date and the sim commit it came from.
3. The Sapper test prints the three rows (shipped, `minRange` 2 m, splash 1 m) with friendly-fire deaths, self damage and
   the outcome; no shipped data changed (`git diff --stat` shows nothing under `game/data/`).
4. The report carries: the proposed target margin band with its reasoning, which pairs fall outside it, and the Sapper
   recommendation with the numbers; the content filter green; non-Perf green; 0 warnings.

**Design references:** docs/02 "Faction template" (the Counters / Countered by columns: the winners), "Template
baseline stats" (Malazan is the balance reference), "Projectiles" (splash, friendly fire); `docs/factions/malazan.md`
(the Sapper: "Fragile; wants an escort"), `whirlwind.md`; the sim's `Scenario/CounterTriangleTests` (harness and the
current margins: Line v Shock 1,036-1,216 of ~1,200; Shock v Ranged 840-960; Ranged v casters 810-1,088).

**Tests required:** `Content/CounterTriangleMarginsTests`, `Content/SapperSplashReportTests`, the `TechContentTests.G`
and `UnitContentTests.C` changes.

**Constraints:** content tests only; no edits to `Scenario/CounterTriangleTests` or any sim code; scratch data copies in
memory, never files; the faction pages' existing tables byte-identical (the pins read them).

### After this session (data)

1. Owner review tweaks from the inbox, always first.
2. D7: the towers' `sight` on both pages and in both `buildings.json` once M4-3a is on `main` (the page tables get a
   Sight column, pinned); then the towers' `attack` / `detector` after M4-3b; `abilities.json` / `statuses.json` after
   M4-4; the full balance pass (QA standard) once the fog-on sandbox gives numbers; `ai.json` (M5); M7-M9 factions.

### Watch-outs (data)

- Only the "Ages" paragraph of docs/02 is parsed by the content tests (plus the faction pages).
- `docs/factions/shadow.md` "Edur Ram: attacks buildings only" has no test until Shadow data exists (M8).

## Watch-outs (all tracks)

- **Merge order:** sim, then view (from the held branch), then data. Expected shared-file conflicts: docs/03 (one
  subsection per track), docs/01 (rows appended), `studio/bugs/README.md`, `studio/qa/coverage.md` (append both sides).
  The data track's page edits and the sim's `buildings.json` edits are different files.
- **Scene loop is part of the sim gate (BUG-0210):** the script is on the held view branch only until the view merges;
  the sim's QA fetches it (see "Where we are"). Expect 30 / 31 on the sim branch (only `MinimapTest` red), 31 / 31 on
  the view branch.
- **Perf:** the category in one process fails 8-9 wall-clock rows on base and head alike (machine load); a Perf failure
  counts only alone, first run in a fresh process (BUG-0158).
- **Owner note 2026-10-08 (processed):** art direction is grounded / realistic; do not pull the look test or art work
  forward (M6 stays). Fonts, music and the export templates remain to download; the Producer lists them when M5 starts.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file; suggested text under For your review, M1 entry).
- Sessions: a lock's age alone does not prove the previous session dead; check for running processes before resuming.
