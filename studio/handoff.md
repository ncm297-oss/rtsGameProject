# Handoff: current session plan

Written by the Producer at the PLAN of session 2026-10-06-0905 (2 / 8 today; base 1122999).
Both tracks run a **hardening** session: sim = the M1 end-of-milestone hardening (criteria 8 / 8
met, sign-off follows at ACCEPT); view = 4 / 4 feature sessions since its last hardening.
Bug numbers: sim from BUG-0058, view from BUG-0069.

## Where we are

- `main` at 1122999: build 0 warnings; non-Perf suite green (Producer rerun at this PLAN, see the
  session log); smoke PASS last session. Open bugs 25 (S3 15, S4 10), none S1/S2.
- M1: 8 / 8 criteria ticked; not Done until this hardening session lands and coverage shows ✅ for
  Unit / Invariant fuzz / Determinism on every M1 row (`studio/qa/coverage.md`: the tick-loop row's
  Invariant fuzz is still 🟡 from M1-1, to be re-assessed by QA this session).
- M2: 6 / 10; after this hardening: M2-5 debug overlay, M2-6 audio, M2-7 playable check.

## Sim track

### Current session plan: M1-9 — M1 end-of-milestone hardening (hardening, QA full)

Goal: close the M1 debt so the milestone can be signed off: holders really block, bad commands are
refused at the door, plug and perf regressions from M1-4d-3 are paid back, and docs/03's known-limits
list matches the code. Batch in this order; stop when ~1,500 changed lines or the session is used
up, and report what was left (open S3/S4 may stay, listed in the retro).

1. **BUG-0055 (S3)** holders are hard walls to their own army too (`IsHardWall` gains
   `|| u.Hold[j]`, or equivalent). Measure before/after on `CrowdRoutingTests` rows and the
   BUG-0044 perf rows; un-skip QA's `OrderQaTests` friendly-plug row. If a crowd row falls by more
   than 5 points, keep holders soft, document it in docs/03 and docs/01 (reverse the Producer
   decision with the numbers) and say so.
2. **BUG-0054 (S3)** `Simulation.Enqueue` rejects an undefined `CommandKind` and unknown `Flags`
   bits the same way it rejects an unknown player (no state change, no sequence advance);
   regression tests; un-skip the QA row. BUG-0056 item 1 with it (`Queued` on Spawn / Noop:
   reject or define; write the rule in docs/03).
3. **BUG-0044 (S3)** two-player 2,500-unit rows (contested blob 10.5 ms, 4 points 3.7 ms,
   crossing 5.4 ms): cache the `IsPlug` answer per enemy per tick, or run it only when the
   enemy touches blocked ground. Then make the one-player tight-blob perf row enforced at
   <= 4.5 ms (BUG-0047 remainder) and add a two-player contested-blob row (report at least).
4. **BUG-0045 (S3)** plugs of 5+ standing enemies (5 small infantry across a 2-cell choke, a
   5-cell corridor) must hold: raise `MaxPlugSpan` or make the plug test span-free (blocked
   ground on two opposite sides within the line's extent); the 5 skipped QA rows are the proof.
   Measure against item 3's rows (no second regression).
5. **BUG-0049 / BUG-0050 (S3)** re-bound the 64-goal row on seeds 1-80 (or make it report-only
   with the reason) and either find a terminating rule that brings random-goal give-ups back
   under 3% or record 4.7% as a known limit in docs/03 with the alternatives measured.
6. **BUG-0046 (S3)** sort `Constrain` clips by slot (or another stable key) so a walker's path
   no longer depends on neighbours' spawn order, or document it as a known limit in docs/03.
7. **BUG-0057 (S4)** items 2-4: check the `--record` path is writable before ticking; `play` on a
   0-checkpoint replay says "nothing compared" (or `run` warns when `--checkpoint > --ticks`);
   `DataError.ToString()` no `: :` for empty fields (view request 3). `CliTests` clean up
   `%TEMP%\rts-cli-tests` (or use a per-run temp dir).
8. BUG-0056 item 2 (dev corridor Hold test asserts walkers stay behind), the three blank lines at
   the top of `MovementSystem`, BUG-0047 nits; docs/03 sweep of the M1 sections (known-limits
   list = the code after this session; every "Implementation (M1-x)" paragraph still true).
9. Golden replay: if any movement rule changes (items 1, 3, 4, 6 may), regenerate once in the same
   commit with the reason, and diff the `k` lines in the commit message.

Out of scope: crowd cost in flow fields (BUG-0028 / 0032, after M4), BUG-0025 / 0026 / 0005 / 0023,
loader bugs 0008 / 0010 (M3 data task), any M3 feature, any `ViewApi` or `game/` change.

Acceptance criteria:
1. Items 1 and 2 landed (or item 1 reversed with numbers); the BUG-0055 and BUG-0054 QA rows
   are un-skipped and green.
2. Every landed bug has a regression test that failed first (named in the report) and its bug
   file says `fixed` with the commit.
3. `CrowdRoutingTests` / crowd stress bounds hold on the swept seeds; no bound loosened without
   the measured reason in the commit.
4. Perf: `PerfCriterionTests` (500 moving < 4 ms) green alone; 2,500 one-player tight blob
   <= 4.5 ms alone (enforced row); two-player contested blob measured and reported, lower than
   10.5 ms if item 3 landed.
5. Hash twins green for every changed scenario; golden either byte-identical or regenerated
   once with the reason.
6. Build 0 warnings, non-Perf suite green, Perf suite green alone; public setup API unchanged
   (`SimConfig`, `Simulation.Enqueue`, `Command.*` factories, `PeekCached`).
7. docs/03 M1 sections and the known-limits list match the code; the report lists which batch
   items were left and why.

Design references: docs/03 "Implementation (M1-4d-3)", "Implementation (M1-5)", M1-7 orders
section (Hold semantics), BUG-0044 / 0045 / 0055 files; docs/01 change log (Producer decision on
holders, 2026-10-05).

QA focus (full): hash twins and the golden on every movement change; crowd rows before/after on
the swept seeds; the BUG-0044 perf rows run alone (not during the view's run; a Perf failure counts
only when it fails alone); the un-skipped rows; replay fuzz with the new `Enqueue` rejections (every
hostile command row still never throws through `ReplayPlayer` or the CLI); holders of both players
in corridor and open-field shove storms (a holder never moves, nobody of either side passes a
holder plug in a 1-cell corridor); plugs of 5-8 small enemies and mixed radii; spawn-order
independence if item 6 landed. **Coverage:** re-assess the tick-loop row's Invariant fuzz column
(🟡 since M1-1) against today's fuzz (`OrderStressTests`, `ReplayQaTests.RecordRandom`,
`SimCoreStressTests`): mark ✅ with the test names if justified, else add the missing fuzz; every
M1 row must show ✅ for Unit, Invariant fuzz and Determinism for the sign-off.

Watch-outs: keep the public setup API additive (the view compiles against it); perf rows fail from
CPU contention while the view's QA runs; after this session the sim's feature counter resets and
M3 starts.

## View track

### Current session plan: M2-H1 — view hardening (hardening, QA standard)

Goal: pay the view debt before M2-5: the A-targeting state machine is consistent across the minimap
and the 3D view, the terrain height lookup can't throw, single scouts read on the minimap, and the
dev tests catch the mutants QA found. Batch in this order, stop at ~1,000 changed lines.

1. **BUG-0068 (S3)** a minimap right-click while A is armed cancels targeting and orders nothing
   (public `SelectionController.CancelTargeting()`, used by `Minimap`'s command branch); docs/03
   M2-3 paragraph says so; the `QaM23Test` print becomes a check.
2. **BUG-0067 (S4)** the double-tap window compares whole milliseconds (un-skip the QA row);
   targeting ends when the pruned selection becomes empty.
3. **BUG-0052 (S3)** `TerrainHeight.At` clamps in float before the `(int)` cast (NaN too); un-skip
   the 5 QA rows.
4. **BUG-0064 (S4)** minimap dots readable: 2x2 px dots or a 1 px dark outline so a lone Whirlwind
   dot differs from a ramp tick and a lone Malazan dot from a cliff lip; a note (or a min-filter)
   for maps over 220 cells; screenshot at 100 and 1,000 units, looked at.
5. **BUG-0053 (S4)** `UnitViewsTest` facing check on +y and a diagonal; the stale `SimRunner`
   remark; docs/03 picker box range (-1 to 9 m).
6. Dev `TerrainMeshBuilderTests`: a wall-count assertion on the default map (the dropped-wall
   mutant QA found).
7. Docs sweep of docs/03 M2 sections (M2-1 to M2-4 paragraphs match the code); export-hygiene
   items stay notes for M6.

Out of scope: M2-5 overlay, audio, any `sim/` change outside `sim/Rts.Sim/ViewApi/` (read-only
helpers only), `game/data/`, art, the edge-pan blocker.

Acceptance criteria:
1. `QaM23Test`: A armed + minimap right-click → no order issued, `Targeting` false; 3D right-click
   unchanged; Esc unchanged. Regression step in `OrdersTest` or `QaM23Test`.
2. BUG-0067 QA row un-skipped and green; a scene or unit test shows targeting off after the
   selection empties (free every selected unit, then click: the click selects, nothing is ordered).
3. BUG-0052: the 5 `TerrainHeightQaTests` rows un-skipped and green; NaN / ±Inf / 1e10 / MaxValue
   return a finite height on the map.
4. BUG-0064: a windowed `--screenshot` at 100 and 1,000 units, inspected: a single Whirlwind unit
   on a ramp cell and a single Malazan unit on a cliff-lip cell are each visible as a dot;
   `MinimapRasterTests` cover the new dot shape; 2,000 dots still 0 bytes per refresh.
5. BUG-0053 items done; `TerrainMeshBuilderTests` fails when walls are dropped (mutation stated
   in the report).
6. All scenes PASS headless (`OrdersTest`, `QaM23Test`, `SelectionTest`, `MinimapTest`,
   `QaM24Test`, `QaM22Test`, `UnitViewsTest`, `CameraClampTest`); smoke PASS; build 0 warnings;
   suite green; view hash twins unchanged.
7. docs/03 M2 sections match; bug files set to `fixed` with the commit.

Design references: docs/02 "Minimap", docs/03 "Implementation (M2-2)" to "(M2-4)", BUG-0068 /
0067 / 0052 / 0064 / 0053 files; the M2-3 brief rule "a right-click cancels A and orders nothing".

QA focus (standard): A-targeting state machine fuzz (A / Esc / minimap left / minimap right / 3D
right / S / H / selection emptied by deaths, in every order) through the real viewport; hash twins
unchanged; the un-skipped rows; `TerrainHeight.At` with every float special at both axes; the
screenshots at 100 and 1,000 units including the worst-case cells (ramp tick, cliff lip, border).

Watch-outs: `PeekCached` is for M2-5, not this session; `select_add` / `order_queue` / `group_add`
all sit on Shift; headless scene runs print Godot warning stack traces for expected "order
dropped" warnings (not errors).

## Shared watch-outs
- Merge order sim → view. STATE, the session log, the handoff and roadmap ticks are written in the
  sim worktree only.
- Expected shared-file overlap: `studio/bugs/README.md` (status column edits on different rows,
  new rows appended), `studio/qa/coverage.md` (sim re-assesses the M1 rows; view edits M2 rows),
  docs/03 (different sections).
- `CLAUDE.md` is the owner's file: still "Current milestone: M1", no CLI command line.
- Remote Control is unavailable in unattended sessions; don't retry it.
- At ACCEPT the Producer signs M1 off if its conditions hold (coverage ✅ x3 on every M1 row, no
  S1/S2, hardening done): retro in docs/05, M1 Done, M3 Next, For-your-review entry on how to
  try M1.
