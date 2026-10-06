# Handoff: brief for the next session

Written by the Producer at ACCEPT of session 2026-10-05-1609 (both tracks accepted; the conductor
merges sim then view into `main`). The next PLAN replaces this with its "Current session plan".

## Where we are

- `main` after the merges: build 0 warnings; sim branch 1472 / 19 skipped / 0 failed (Perf rerun
  alone green), view branch 1438 / 19 / 0; smoke PASS; golden replay regenerated once this session
  (M1-4d-3) and must not move again unless a movement rule changes.
- Open bugs 18 (S3 12, S4 6), none block. BUG-0045 is S3 by Producer re-triage (plugs of 5+ enemies
  leak; pre-existing). Sessions today 5 / 8. Sim feature sessions since hardening 0 / 4; view 2 / 4.
- Bug numbers next session: sim from BUG-0054, view from BUG-0064.
- M1: 6 / 8 (perf test M1-7 and CLI M1-8 left, then the end-of-milestone hardening and sign-off).
  M2: 3 / 10 (camera, SimRunner interpolation, unit views); selection and orders half-done.

## Sim track: next task candidates

1. **M1-7 perf-test criterion + the view's request** (feature, QA full, budget 1,500; the perf test
   is small, ~150 lines; the commands ~600-800). Perf: a named, enforced test "500 moving units,
   average tick < 4 ms" over >= 100 ticks on the default map (today 0.17 ms; rows exist in
   `Stress/LocalMovementStressTests`, check whether one already matches the criterion's wording and
   is enforced), plus the docs/03 note that maps > 256 are unsupported for now (BUG-0023). Commands:
   `Stop` (drop goal, Idle, `WalkBack` reset), `HoldPosition` (Idle, never shoved, a hard wall to its
   own side too? decide and document; docs/02 "Controls" and docs/03 "Orders and unit states"),
   `AttackMove` (= `Move` until M4, but the kind is recorded so replays stay valid when M4 changes
   it), and shift-queued orders: a per-unit fixed-capacity order queue (hashed, in the replay
   checkpoint, QA reflection audit), next order issued on arrival or give-up. Keep `Command`'s
   existing kinds' bytes identical so the golden doesn't move; `ReplayFormat` must read/write the new
   kinds and payloads; `ArchitectureTests` scan. Public setup API additive.
2. **M1-8 CLI** in `tools/` (feature, QA light/standard): record / play `.replay`, print hashes and
   timings; reuse `ReplayPlayer`; a scenario runner for the cross-map march.
3. **M1 end-of-milestone hardening** (after 1-2): BUG-0044 (cache `IsPlug` per enemy per tick; then
   enforce the 2,500 tight-blob row, BUG-0047), BUG-0045 (span-free plug test; 5 QA rows un-skip),
   BUG-0049 (re-bound or report-only), BUG-0050 (accept as known limit or new rule), BUG-0046
   (sort clips or document), three blank lines at the top of `MovementSystem`'s class. Then sign-off:
   every M1 criterion verified, coverage ✅ Unit / Invariant fuzz / Determinism for all M1 systems.

## View track: next task candidates

1. **M2-4 minimap** (feature, QA standard; set a production-line budget of ~500, the view has run
   over three times): pure `ViewApi.MinimapRaster` (heightmap → per-cell colour bytes using the
   level tints, plus map ↔ minimap pixel transforms), a `TextureRect` in a corner, unit dots in
   faction colour (one `Image` update per tick or a MultiMesh of quads; measure), the camera's view
   trapezoid, left-click moves `RtsCamera` focus, right-click orders the selection to the map point
   (same `Command.Move` path as M2-2, off-map = nothing). Needs nothing from the sim. Headless scene
   `game/tests/MinimapTest.tscn` printing `MINIMAP TEST PASS`; screenshot inspected.
2. M2-3 (after request 1 lands on `main`): A / S / H / shift-queue, double-click type select, control
   groups, Tab subgroups.
3. View hardening at 4 / 4: BUG-0052 (one line), BUG-0053, the mesh-test wall mutant.

## Watch out for

- Merge order sim → view; rerun build / tests after each. Expected conflicts this time:
  `studio/bugs/README.md` (sim changed rows 0030-0050; view changed the 0041 row and appended
  0052-0053: take both), `studio/qa/coverage.md` (both appended), docs/03 (sim appended under
  "Local movement", view under "Rendering and presentation"). STATE, handoff, roadmap, the session
  log and `docs/01-vision.md` were written in the sim worktree only.
- The next sim task touches `Command` and `ReplayFormat`: the golden must stay byte-identical (no
  movement rule changes) or the commit must say why. The view task must not touch `game/data/`.
- Both tracks' QA runs overlap in wall-clock; a Perf failure counts only when it fails again alone
  (QA's first full run this session had 4 Perf failures under load, all green alone).
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; drift noted in STATE for the owner).
- Remote Control is unavailable in unattended sessions; don't retry it.
