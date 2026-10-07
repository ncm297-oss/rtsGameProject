# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session **2026-10-07-1415** (fourth full session of 2026-10-07, cap 8). Sim
M3-6 and view M3-V3 ACCEPTed with 0 fix rounds; data D3 **REJECT-hold** (the content is right and verified; it can't
land on a green `main` until the sim fixes 25 of its own test fixtures: BUG-0112). Counters after 1415: sim 3 / 4, view
3 / 4, data 3 / 4. Both code tracks' M3 work is complete, so **next session is the end-of-M3 hardening on sim and
view**; the data track STOPs (D3 lands through the sim's session). **Bug numbers for the next session: sim from
BUG-0114 (0114-0121, then 0133+), view from BUG-0124 (0124-0131), data from BUG-0133 if it runs.**

## Where we are

- `main` after this session's merge (sim, then view; the data branch is **not** merged): M0, M1, M2 Done; **M3 6 / 8**
  (resources, gather loop, placement + construction + repair, production + rally + pop, **Age II research and
  unlocks + Forge upgrades**, **the HUD**). Left: "factions fully defined in data" (D3 held, then D4) and "Playable:
  build a full Malazan base and reach Age II" (owner playtest; instructions under STATE "For your review", M3-V3
  entry). 29 open bugs (S3 16, S4 13), no S1 / S2.
- **Held branch `studio/2026-10-07-1415-data` (D3, 2 commits: 8bcca04 implement, 1687f56 QA)**: faction `techs.json`
  descriptions, `buildings.json` `requires` on 8 buildings, `docs/factions/*.md` Techs tables, tests under
  `sim/Rts.Sim.Tests/Content/` + `QA/Content/`, a golden regen that **must be discarded** (take the sim's side, then
  regenerate once after the merge), bug files BUG-0090 / 0111 / 0132 + README rows + the D3 QA report + a coverage
  row. QA'd PASS_WITH_ISSUES; the Producer verified criteria 1-7.
- Producer checks at ACCEPT (see the session log): all three branches build 0 warnings; sim and view non-Perf suites
  green; view smoke PASS; BUG-0112 reproduced (sim branch + D3 `buildings.json`: 26 failures in the named classes).
- Shared-file rule unchanged: docs/03 (one subsection per track), docs/01 change log (append one row), coverage.md
  (append), `studio/bugs/README.md` (append rows). Keep every side at merge, sim then view then data.
- **Golden rule:** only one track regenerates `cross_map_seed1.replay` per session. Next session that is the **sim**
  (D3's data-hash move). Checkpoints must stay byte-identical; the log says which lines moved and why.
- **Process rule for every builder:** never kill processes you did not start. Absolute paths only. Never touch the
  owner's main checkout.

## Sim track

### Current session plan: M3-H2 — end-of-M3 sim hardening, landing D3 (hardening, QA full)

**Goal.** M3's sim side is complete; this is its hardening session. The first job unblocks the data track: merge the
held D3 branch and fix the sim-owned test fixtures that assume no building has a requirement (BUG-0112), so D3's
building requirements land on a green `main`. Then the plateau / seal perf bugs (BUG-0097 / 0095 / 0096), the
unmeetable-requirements loader check (BUG-0100) and the small items.

**Scope, in order (stop when the size budget is spent; (a) and (b) are mandatory).**
- **(a) Land D3 (BUG-0112).** `git merge origin/studio/2026-10-07-1415-data` into the session branch (fetch it first;
  keep every side of `studio/**` and `docs/**`; for `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` take `main`'s
  version, then regenerate once at the end: `data-hash` + checksum only, every `k` line byte-identical). Fix the 25 rows:
  `Stress/ConstructionFuzzStressTests` (`FiveHundredActions_OracleAgrees_InvariantsHold` seeds 2-8,
  `FiveHundredPlacements_EveryVerdictMatchesTheFloodOracle` seeds 1-8: the flood oracle compares `CanPlace` with
  geometry only, a gated type now answers `Requires` first), `NeverSealTests.FiveHundredRandomLegalPlacements_NeverLeaveAPocket`
  seeds 1-8 (the "n placed" coverage floor starves when random types are refused for `Requires`) and
  `RequirementFuzzTests.Twins_..._EveryTick` seeds 2-3 (its fixture rewrites Malazan's requires but inherits
  Whirlwind's shipped ones; the "flipped at least N times" floor fails). Preferred fix: a shared test helper that loads
  the shipped data with every building's `requires` cleared (`TestData.WithoutBuildingRequires()` or similar) for the
  geometry oracles, and for the requirement fuzz, rewrite **both** factions' requires in the fixture. Don't weaken any
  invariant or floor; the oracles must still see the same number of placements as before D3. Then the whole non-Perf
  suite green with D3's files, `DataValidationTests` + `Content` + `QA/Content` green (D3's own tests), golden regen.
- **(b) BUG-0097 + BUG-0095 (S3)**: a per-cell plateau id (connected same-level component, computed at load / on
  `NavGrid` level changes, with its own bounding box), `FreeCellSearch` bounded by the plateau instead of
  `World.LevelBounds`; push-out leftovers spread (ring search continues) or wait; a per-plateau "no free cell this
  tick" memo so 20 halls waiting on a packed plateau don't each re-walk it (1.7-1.9 ms a tick today). Pins: QA's
  `ProductionQaTests...Bug0097Pin` flips to the wanted behaviour; `SimHardeningQaTests` full-plateau report becomes a
  bound.
- **(c) BUG-0096 (S3)**: a `SealsGround` refusal memo per tick keyed on (type, anchor, `BlockVersion`) or a cheap ring
  pre-test before the labelled flood; 100 refused SealsGround Builds in a tick from 33 ms to under 2 ms.
- **(d) BUG-0100 (S3)**: unmeetable requirements are load errors: a def naming another faction's building, a common
  tech naming any faction's building (one error at the entry); an any-of whose reachable members (not themselves
  requiring the tech, directly or transitively) number fewer than `count` (one error at the field). Un-skip QA's
  `ARequirementThatCanNeverBeMet_IsOneError`; replace the `BUG0100_Today_*` pins.
- **(e) If budget remains:** BUG-0113 (an author-readable "expected a whole number" message without CLR type names; a
  valid large load timed in `DataLoaderQaTests`), BUG-0094 (`ResourceMaps.Spawn` never-seal check or opt-in flag),
  BUG-0099 item 2 (an effect matching no unit: one error), door fuzz `CommandDoorFuzzStressTests` kinds 11-15,
  docs/03 M3-5 / M3-6 polish (the "Not yet" lines, the any-of cycle note once (d) lands).
- **Out:** M4 combat, AI, anything in `game/**` except `game/data/factions/**` arriving through the D3 merge.

**Acceptance criteria.**
1. The D3 branch is merged (its two commits in the history, or its diff reproduced exactly); `game/data/factions/*/buildings.json`
   carry the 8 requires; the faction techs descriptions and the pages' Techs tables are D3's; D3's tests pass unchanged.
2. The non-Perf suite is green with D3's data; no oracle or coverage floor weakened (the construction fuzz still
   records ≥ the pre-D3 number of placements per seed; the never-seal floor unchanged; the requirement fuzz flips ≥ its
   floor on every seed); the golden regenerated once with every checkpoint byte-identical.
3. BUG-0097: a spawn or push-out on a full plateau never lands on another plateau; 20 halls waiting on a packed
   plateau under 0.3 ms a tick; BUG-0095: no two leftovers share a cell (or they wait), measured in `SimHardeningQaTests`.
4. BUG-0096: 100 SealsGround refusals in one tick under 2 ms; the answer unchanged (QA's flood oracle agrees).
5. BUG-0100: the three repros are one error each; shipped + D3 data still load with 0 errors.
6. Twins + conservation fuzz (production, research, requirement, construction) green; `StateHashTests` audit shows
   the plateau ids / memos are derived (not hashed) or hashed deliberately with the golden note.
7. Perf alone: `TightBlob2500` ≤ 4.5 ms, every M3 Perf row within its limit; `AllocationTests` 0 bytes.
8. Docs: docs/03 "Known limits" / M3-4 / M3-6 "Not yet" lines updated; bug files set to `fixed` with test names;
   README rows; coverage row appended; `dotnet build` 0 warnings.

**Design references.** docs/03 "Implementation (M3-4)" (`FreeCellSearch`, level box), "(M3-H1)" (push-out rings,
cheap-first order), "(M3-6)" (resolution, gates, any-of), "Known limits"; docs/02 "Buildings" (Requires column); the
bug files' Notes sections.

**Tests required.** Fixed rows for BUG-0112 (no new skips); regression tests that failed first for BUG-0097 / 0095 /
0096 / 0100; the fuzz suites; `ReplayGoldenTests`; `AllocationTests` rows for the new memos.

**Constraints most at risk.** Weakening a test to make D3 pass (forbidden: fix the fixture, not the floor); two golden
regens (the data branch carries one: discard it); hashing derived state (plateau ids, memos); per-tick allocation in
the plateau search; touching `game/data/common/**` (D4's file: leave `common/techs.json` alone).

### After M3-H2 (sim)
M4 combat: attack / damage formula with the worked example from docs/02 first (one slice ~800 lines, new system), then
attack-move / chase / retaliation, projectiles, death. The view and data tracks finish M3 meanwhile.

## View track

### Current session plan: M3-H2 — end-of-M3 view hardening (hardening, QA standard)

**Goal.** The view's M3 feature work is done; this session works its debt so the owner's playtest and M4's combat
feedback start from a clean HUD. Inbox playtest complaints, if any arrive before the session, come first.

**Scope, in order.**
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
- **Out:** the scripted Playable run (the session after, once D3 is on `main`), M4 views, fog.

**Acceptance criteria.**
1. `QaV3Test -- --strict` PASS (BUG-0123 rows are `Check`s): 300 repair ticks 0 bytes; a greyed button's labels at
   ≤ 60 % alpha in a screenshot read by the test (or pixel-sampled).
2. `QaH2Test -- --seeds 1,6,21` PASS with the documented bound.
3. BUG-0122: no mid-word wrap on any shipped building name at 1280 x 720; 25 Shift-clicks on one anchor send 3 Builds
   (one per worker) + 0 duplicates; the ghost's anchor equals the cursor cell on the same frame during a pan; the
   reason text ≥ 14 px at 30 m.
4. BUG-0107: a 100 % site and a finished building differ in hue (pixel test); a resource redraw count unchanged across
   10 builds / cancels.
5. Docs figures current (BUG-0105); bug files `fixed` with test names; README rows; coverage row.
6. 300 idle frames with panel + card + strip + flag 0 bytes; `--bench 10` avg under 1.5 ms; smoke PASS; every scene
   PASS; non-Perf green; `ViewApi` diff read-only (hash twin).

**Design references.** docs/02 "HUD layout"; docs/03 "Implementation (M3-V1 .. M3-V3)", "(M2-H2)" (the allocation
rule), the bug files.

**Tests required.** `QaV3Test` strict rows; dev scene rows per fix (`ProductionHudTest`, `CommandCardTest`,
`EconomyViewTest` additions); `QaH2Test` seeds; xUnit `ViewApi` rows for any new pure helper.

**Constraints most at risk.** Player-facing text in C# (none: `ui.json`); per-frame allocation (the whole point of
(a)); the sim untouched outside `ViewApi/`; `CanPlace` still once per frame.

### After M3-H2 (view)
The M3 Playable tick: the owner's "M3 playable ok" in the inbox, or a scripted headless run of the full-base + Age II
path through the real HUD (a `game/tests` scene, after D3 is on `main`). Then M4 view work (combat feedback: attack
animations / hit flashes / hp bars for units, death; the fog shader) once the sim's M4 lands.

## Data track

### Current session plan: STOP (cheap)

D3 is on its branch and lands through the sim's M3-H2 (one golden regen per session). No schema is waiting for
content (`abilities.json` / `statuses.json` and the tower fields are M4). If the inbox carries text tweaks for D1 / D2 /
M3-5 / D3, hold them for D4 (the session after D3 lands) rather than run a data task beside the sim's regen; the
Producer notes them under the data row in STATE so they aren't lost.

### After (data)
**D4**, the session after D3 lands: `common/techs.json` names and descriptions (Age II and the six Forge upgrades)
pinned to docs/02 "Tech"; BUG-0132 (pin messages name both values and the side); golden `data-hash` regen. Then STOP
until M4's `abilities.json` / `statuses.json` schemas and the tower attack / sight / detector fields.

## Watch-outs (all tracks)

- **Merge order next session:** sim first (it carries D3 + the golden), then view. The data track doesn't run.
- The D3 branch's golden regen (`FA1CB635CA8F63BF`) is wrong once merged on top of M3-6: take `main`'s golden at the
  merge and regenerate once at the end of the sim's work.
- Two hardening sessions at once: both suites and the view's scene loop run together; Perf rows can fail from CPU
  contention; a failure counts only alone. `TightBlob2500` has 0.02 ms of headroom alone.
- The view's hardening must not touch `game/data/common/ui.json` keys the card reads (the sim's enums are stable
  now); adding keys is fine.
- `CLAUDE.md` is the owner's file: still says "Current milestone: M1" (suggested text under For your review, M1 entry).
- Builders: absolute paths only; never touch the owner's main checkout; worktrees on short paths; never kill
  processes you did not start.
