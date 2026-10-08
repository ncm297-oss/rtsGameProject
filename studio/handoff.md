# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-07-1715** (fifth full session of 2026-10-07, cap 8). Base
`52fe53e` (= `main`: 0 warnings, non-Perf green, **smoke FAIL** on `ui.json: missing placement.requires`, BUG-0124).
Two held branches from 1415 land this session: **view first** (`origin/studio/2026-10-07-1415-view`, 5f89068, M3-V3 +
merge of `main`), **then sim** (which merges `origin/studio/2026-10-07-1415-data`, D3, and fixes the sim-owned fixtures
BUG-0112). Data STOPs (cheap). Bug numbers: sim from BUG-0114 (0114-0121, then 0133+; BUG-0132 arrives with D3), view
from BUG-0125 (0125-0131, then 0140+).

## Where we are

- `main` (52fe53e): M0, M1, M2 Done; **M3 5 / 8** (resources, gather loop, placement + construction + repair,
  production + rally + pop, Age II research and unlocks + Forge upgrades). Held: the HUD's last part M3-V3 (view
  branch, BUG-0124 S2: a fixture queues Age II at a bare Town Hall) and D3 (data branch, BUG-0112 S3: its eight building
  `requires` break 25 sim-owned fixtures). Left after those: "fully defined in data" (D3 lands it; D4 is text polish),
  the HUD tick (M3-V3 re-land), "Playable" (owner playtest or a scripted run). 30 open bugs (S2 1: BUG-0124; S3 16,
  S4 13). Counters: sim 3 / 4, view 3 / 4, data 3 / 4; both code tracks take their end-of-M3 hardening now.
- Producer checks at PLAN: `ui.json` on `main` has no `requires` key (the smoke red is exactly that); the view branch's
  `ui.json` has `placement.requires` "Needs more" and `train` / `research` `requires` "Locked"; D3's `buildings.json`
  carries `["malazan_barracks"]` on the Shock Hall and `["age_ii"]` on the three Age II buildings (same shape for
  Whirlwind); the D3 golden regen is `871E8C5BB99E9374 → FA1CB635CA8F63BF` and **must be discarded** (it predates M3-6's
  `24D5CCAC37652EAE`). The data and view branches overlap only on `studio/bugs/README.md` and `studio/qa/coverage.md`
  (append-only: keep both sides).
- Shared-file rule unchanged: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md
  (append), `studio/bugs/README.md` (append rows). Keep every side at merge, view then sim.
- **Golden rule:** only one track regenerates `cross_map_seed1.replay` per session: the **sim** this session (D3's
  `data-hash` move). Checkpoints (`k` lines) must stay byte-identical; the log says which lines moved and why.
- **Process rule for every builder:** never kill processes you did not start. Absolute paths only. Never touch the
  owner's main checkout.

## Sim track

### Current session plan: M3-H2 — end-of-M3 sim hardening, landing D3 (hardening, QA full)

**Goal.** M3's sim side is complete; this is its hardening session. The first job unblocks the data track and M3: merge
the held D3 branch and fix the sim-owned test fixtures that assume no building has a requirement (BUG-0112), so D3's
building requirements land on a green `main`. Then the plateau / seal perf bugs (BUG-0097 / 0095 / 0096), the
unmeetable-requirements loader check (BUG-0100) and small items.

**Scope, in order (stop when the size budget is spent; (a) and (b) are mandatory).**
- **(a) Land D3 (BUG-0112).** `git fetch origin` then `git merge origin/studio/2026-10-07-1415-data` into the session
  branch (keep every side of `studio/**` and `docs/**`; for `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` take
  `main`'s version, then regenerate **once at the end**: `data-hash` + `checksum` only, every `k` line byte-identical).
  Fix the 25 rows: `Stress/ConstructionFuzzStressTests` (`FiveHundredActions_OracleAgrees_InvariantsHold` seeds 2-8,
  `FiveHundredPlacements_EveryVerdictMatchesTheFloodOracle` seeds 1-8: the flood oracle compares `CanPlace` with
  geometry only, a gated type now answers `Requires` first), `NeverSealTests.FiveHundredRandomLegalPlacements_NeverLeaveAPocket`
  seeds 1-8 (the "n placed" coverage floor starves when random types are refused for `Requires`) and
  `RequirementFuzzTests.Twins_..._EveryTick` seeds 2-3 (its fixture rewrites Malazan's requires but inherits
  Whirlwind's shipped ones; the "flipped at least N times" floor fails). Preferred fix: a shared test helper that loads
  the shipped data with every building's `requires` cleared (`TestData.WithoutBuildingRequires()` or similar) for the
  geometry oracles, and for the requirement fuzz, rewrite **both** factions' requires in the fixture. Don't weaken any
  invariant or floor; the oracles must still see at least the pre-D3 number of placements per seed (print both counts
  in the log). Don't edit D3's own tests (`Content/`, `QA/Content/`) except to resolve a merge conflict. Then the whole
  non-Perf suite green with D3's files, `DataValidationTests` + `Content` + `QA/Content` green, golden regen.
  **Smoke with the real integrated state:** `main`'s `ui.json` lacks `placement.requires`, so smoke on the session branch
  fails on exactly that line. Before reporting, copy the view's file in uncommitted
  (`git checkout origin/studio/2026-10-07-1415-view -- game/data/common/ui.json`), run `tools/qa/smoke.ps1` (must PASS)
  and the `game/tests` scene loop (`EconomyViewTest`, `CommandCardTest`, `QaV1Test`, `QaV2Test --strict` at least),
  then restore it (`git checkout HEAD -- game/data/common/ui.json`) and **never commit it**. Report which scenes fail
  because of D3's building locks (the view's session makes them robust; a view-only scene failure does not block you).
- **(b) BUG-0097 + BUG-0095 (S3)**: a per-cell plateau id (connected same-level component, computed at load / on
  `NavGrid` level changes, with its own bounding box), `FreeCellSearch` bounded by the plateau instead of
  `World.LevelBounds`; push-out leftovers spread (ring search continues) or wait; a per-plateau "no free cell this
  tick" memo so 20 halls waiting on a packed plateau don't each re-walk it (1.7-1.9 ms a tick today). Pins: QA's
  `ProductionQaTests.ASpawnOnAFullPlateau_LandsOnAnotherPlateauOfTheSameLevel_Bug0097Pin` flips to the wanted
  behaviour; `SimHardeningQaTests` full-plateau report becomes a bound.
- **(c) BUG-0096 (S3)**: a `SealsGround` refusal memo per tick keyed on (type, anchor, `BlockVersion`) or a cheap ring
  pre-test before the labelled flood; 100 refused SealsGround Builds in a tick from 33 ms to under 2 ms.
- **(d) BUG-0100 (S3)**: unmeetable requirements are load errors: a def naming another faction's building, a common
  tech naming any faction's building (one error at the entry); an any-of whose reachable members (not themselves
  requiring the tech, directly or transitively) number fewer than `count` (one error at the field). Un-skip QA's
  `RequirementQaTests.ARequirementThatCanNeverBeMet_IsOneError`; replace the three `BUG0100_Today_*` pins.
- **(e) If budget remains:** BUG-0113 (an author-readable "expected a whole number" message without CLR type names; a
  valid large load timed in `DataLoaderQaTests`), BUG-0094 (`ResourceMaps.Spawn` never-seal check or opt-in flag),
  BUG-0099 item 2 (an effect matching no unit: one error), door fuzz `CommandDoorFuzzStressTests` kinds 11-15,
  docs/03 M3-5 / M3-6 polish (the "Not yet" lines, the any-of cycle note once (d) lands).
- **Out:** M4 combat, AI, `game/data/common/**` (D4's `techs.json` text is not yours this session), anything in
  `game/**` except `game/data/factions/**` arriving through the D3 merge and the uncommitted `ui.json` smoke trick.

**Acceptance criteria.**
1. The D3 branch is merged (its two commits 8bcca04 / 1687f56 in the history); `game/data/factions/*/buildings.json`
   carry the 8 requires; the faction `techs.json` descriptions and the pages' Techs tables are D3's; D3's tests pass
   unchanged (`Content/TechContentTests`, `BuildingContentTests`, `RequiresText`, `QA/Content/*`).
2. The non-Perf suite is green with D3's data; no oracle or coverage floor weakened (the construction fuzz still
   records ≥ the pre-D3 number of placements per seed; the never-seal floor unchanged; the requirement fuzz flips ≥ its
   floor on every seed); the golden regenerated once, `data-hash` + `checksum` only, every checkpoint byte-identical.
3. BUG-0097: a spawn or push-out on a full plateau never lands on another plateau; 20 halls waiting on a packed
   plateau under 0.3 ms a tick; BUG-0095: no two leftovers share a cell (or they wait), measured in `SimHardeningQaTests`.
4. BUG-0096: 100 SealsGround refusals in one tick under 2 ms; the answer unchanged (QA's flood oracle agrees).
5. BUG-0100: the three repros are one error each; shipped + D3 data still load with 0 errors.
6. Twins + conservation fuzz (production, research, requirement, construction) green; `StateHashTests` audit shows
   the plateau ids / memos are derived (not hashed) or hashed deliberately with the golden note.
7. Perf alone: `TightBlob2500` ≤ 4.5 ms, every M3 Perf row within its limit; `AllocationTests` 0 bytes.
8. Smoke PASS with the view's `ui.json` copied in (uncommitted; not in the diff); the scene-loop result reported.
9. Docs: docs/03 "Known limits" / M3-4 / M3-6 "Not yet" lines updated; bug files set to `fixed` with test names
   (BUG-0112 included); README rows; coverage row appended; `dotnet build` 0 warnings.

**Design references.** docs/03 "Implementation (M3-4)" (`FreeCellSearch`, level box), "(M3-H1)" (push-out rings,
cheap-first order), "(M3-6)" (resolution, gates, any-of), "Known limits"; docs/02 "Buildings" (Requires column); the
bug files' Notes sections.

**Tests required.** Fixed rows for BUG-0112 (no new skips); regression tests that failed first for BUG-0097 / 0095 /
0096 / 0100; the fuzz suites; `ReplayGoldenTests`; `AllocationTests` rows for the new memos.

**Constraints most at risk.** Weakening a test to make D3 pass (forbidden: fix the fixture, not the floor); two golden
regens (the data branch carries one: discard it); hashing derived state (plateau ids, memos); per-tick allocation in
the plateau search; committing the view's `ui.json`; touching `game/data/common/**`.

### After M3-H2 (sim)
M4 combat: attack / damage formula with the worked example from docs/02 first (one slice ~800 lines, new system), then
attack-move / chase / retaliation, projectiles, death. The view and data tracks finish M3 meanwhile.

## View track

### Current session plan: M3-V3b — re-land M3-V3 (BUG-0124), then the view's end-of-M3 hardening (hardening, QA standard)

**Goal.** Get the HUD onto `main` and the smoke gate green: the M3-V3 branch is verified except that its fixtures were
written before Age II had requirements. Bring in `origin/studio/2026-10-07-1415-view` (5f89068; it already contains
`main` at da654c6), fix the test setups, prove every scene against the real gating and against D3's building locks,
and spend what budget remains on the view's debt. This counts as the view's end-of-M3 hardening session.

**First:** `git fetch origin` then `git merge origin/studio/2026-10-07-1415-view` into the session branch (a clean
fast-forward-like merge: the only commit it lacks is `main`'s state-only 52fe53e). Do not rebase or squash it.

**Scope, in order ((0) is mandatory; stop when the size budget is spent).**
- **(0) BUG-0124 (S2):** every test that researches Age II spawns two finished halls of distinct slots first (a
  Barracks and an Armory through the dev `SpawnBuilding`, which ignores requirements): `ViewApi/ProductionHudTests`
  (the `[laborer, age_ii, laborer]` row and any other that queues a tech with requirements), `ProductionHudHashTwinTests`
  if it researches, `game/tests/ProductionHudTest.cs` criterion 3 (strip + Age II flash), `QaV3Test` (`GreyingEveryFrame`
  must now also see `research.requires` on a bare Town Hall, `QueueStripEveryFrame`, the Age II row of its hash twin),
  and `QA/ViewApi/ProductionHudQaTests` 600-tick twin. Add one explicit row: a bare Town Hall's Age II button reads
  `research.requires` ("Locked") and a press enqueues nothing. Then: `dotnet test` non-Perf green, `tools/qa/smoke.ps1`
  PASS, **every** `game/tests` scene PASS (`QaV2Test --strict`; `QaV3Test --strict` may fail only the BUG-0123 row
  unless (a) is done). Repeat the scene loop once with the held D3 `buildings.json` files copied in from
  `origin/studio/2026-10-07-1415-data` (`git checkout origin/studio/2026-10-07-1415-data -- game/data/factions/malazan/buildings.json game/data/factions/whirlwind/buildings.json`;
  **restore with `git checkout HEAD -- ...` before committing; never commit them**): placement rows that use the
  Wickan Corral / Cadre Tower / Engineers' Yard / Watchtower must expect `placement.requires` or use an Age I type;
  fix any scene that assumes those are placeable. Report both loops.
- **(a) BUG-0123 (S3)**: the selection panel's hp text allocation-free under repair (two labels, "now" from a
  once-built int-string table up to the largest building hp, or a reused buffer; 300 repair ticks 0 bytes in
  `QaV3Test` `Allocation`, turned into a `Check` with `--strict`); greyed production buttons visibly dim (Modulate on
  the cell's labels when `Disabled` changes, one write per reason change); a windowed screenshot looked at.
- **(b) BUG-0104 (S3)**: `QaM27Test`'s march bound: document per seed and pin seed 1 only, or give the march room
  (order across before A + click); `QaH2Test -- --seeds 21` passes.
- **(c) BUG-0122 (S4)**: "Quartermaster's Depot" wraps mid-word (word wrap / smaller font); Shift-click floods
  (skip a placement at the last placed anchor); the ghost after `RtsCamera` in tree order; the reason text scaled with
  zoom and a redder red.
- **(d) BUG-0107 (S4)**: a site tone distinct from the finished building; the cargo cube visible at 60 m; the minimap
  resource layer redrawn only on fells (diff `Resources.Count` / a resource-only version).
- **(e) BUG-0105 (S4)**: refresh the stale docs/03 figures (minimap refresh 0.28 ms; Sfx exit wait up to 93 ms); if
  the refresh row is within 5 % of its limit, split the forced redraw out of the timed part.
- **(f) If budget remains:** `BuildingPicker.PickRay` terrain occlusion (a box behind a hill should lose to the hill)
  with a QA row on a seed where it matters; a resource-node right-click through the box pick (prop heights).
- **Out:** the scripted Playable run (next view session, once D3 is on `main`), M4 views, fog, any `ui.json` key the
  card already reads (adding keys is fine), the sim outside `ViewApi/`.

**Acceptance criteria.**
0. BUG-0124: the merged branch's non-Perf suite green; smoke PASS; every scene PASS, and again with D3's
   `buildings.json` in place (uncommitted); a bare Town Hall's Age II button reads "Locked" and a press enqueues
   nothing; the `[laborer, age_ii, laborer]` rows pass with two halls spawned. **This alone is enough to merge** if the
   budget runs out; items 1-6 below apply to whatever of (a)-(f) was done.
1. `QaV3Test -- --strict` PASS (BUG-0123 rows are `Check`s): 300 repair ticks 0 bytes; a greyed button's labels at
   ≤ 60 % alpha in a screenshot read by the test (or pixel-sampled).
2. `QaH2Test -- --seeds 1,6,21` PASS with the documented bound.
3. BUG-0122: no mid-word wrap on any shipped building name at 1280 x 720; 25 Shift-clicks on one anchor send 3 Builds
   (one per worker) + 0 duplicates; the ghost's anchor equals the cursor cell on the same frame during a pan; the
   reason text ≥ 14 px at 30 m.
4. BUG-0107: a 100 % site and a finished building differ in hue (pixel test); a resource redraw count unchanged across
   10 builds / cancels.
5. Docs figures current (BUG-0105); bug files `fixed` with test names (BUG-0124 included); README rows; coverage row.
6. 300 idle frames with panel + card + strip + flag 0 bytes; `--bench 10` avg under 1.5 ms; smoke PASS; every scene
   PASS; non-Perf green; `ViewApi` diff read-only (hash twin); `git diff --stat origin/main` shows no `game/data/factions/**`.

**Design references.** docs/02 "HUD layout"; docs/03 "Implementation (M3-V1 .. M3-V3)", "(M2-H2)" (the allocation
rule), the bug files.

**Tests required.** `QaV3Test` strict rows; dev scene rows per fix (`ProductionHudTest`, `CommandCardTest`,
`EconomyViewTest` additions); `QaH2Test` seeds; xUnit `ViewApi` rows for any new pure helper.

**Constraints most at risk.** Player-facing text in C# (none: `ui.json`); per-frame allocation (the whole point of
(a)); the sim untouched outside `ViewApi/`; `CanPlace` still once per frame; committing D3's `buildings.json`.

### After (view)
The M3 Playable tick: the owner's "M3 playable ok" in the inbox, or a scripted headless run of the full-base + Age II
path through the real HUD (a `game/tests` scene, after D3 is on `main`). Then M4 view work (combat feedback: attack
animations / hit flashes / hp bars for units, death; the fog shader) once the sim's M4 lands.

## Data track

### Current session plan: STOP (cheap)

D3 lands through the sim's M3-H2 this session (one golden regen per session; the `Content/` test folder is D3's and
would collide). No schema is waiting for content (`abilities.json` / `statuses.json` and the tower fields are M4). The
inbox is empty; any text tweaks that arrive go to D4.

### After (data)
**D4**, next session: `common/techs.json` names and descriptions (Age II and the six Forge upgrades) pinned to docs/02
"Tech"; BUG-0132 (pin messages name both values and the side); golden `data-hash` regen if the hash covers text (check
first; if it does not, no regen). **Ownership note for the Producer:** the owner's rule keeps `game/data/common/` with
the sim; D4 is a text-only edit of `displayName` / `description` in `common/techs.json`. Plan it as a named exception
in the data brief (strings only, the sim does not touch the file that session) and record the refinement under For
your review. Then STOP until M4's `abilities.json` / `statuses.json` schemas and the tower attack / sight / detector
fields.

## Watch-outs (all tracks)

- **Merge order this session: view first** (it turns `main`'s smoke green), **then sim** (it carries D3 + the golden).
  The data track doesn't run. Conductor: after merging the sim on top of the view, run smoke **and** the `game/tests`
  scene loop; if only a view-owned scene fails because of D3's building locks, hold the sim branch (no owner
  notification: the next view session fixes the scene) rather than ESCALATE. Expected conflicts when merging the sim
  after the view: `studio/bugs/README.md`, `studio/qa/coverage.md`, `docs/01-vision.md` change log, docs/03 (both
  sides kept, append-only).
- **Rule from BUG-0112 / BUG-0124:** when a sim task changes a gate (`CanTrain` / `CanPlace` / `CanResearch` / data
  validation), every other track's QA merges the sim branch (or `main` once the sim has landed) before its final run,
  and the Producer reruns the **merged** result. A stand-in for enum members is not a test of the gating.
- Both builders use uncommitted copies of the other track's files for their smoke / scene loops (sim: the view's
  `ui.json`; view: D3's `buildings.json`). The Producer checks `git diff --stat origin/main` on each branch for leaks.
- The D3 branch's golden regen (`FA1CB635CA8F63BF`) is wrong once merged on top of M3-6: take `main`'s golden at the
  merge and regenerate once at the end of the sim's work.
- Two hardening sessions at once: both suites and the view's scene loop run together; Perf rows can fail from CPU
  contention; a failure counts only alone. `TightBlob2500` has 0.02 ms of headroom alone.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths; never kill
  processes you did not start.
