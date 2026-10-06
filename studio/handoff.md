# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session 2026-10-06-0655 (both tracks ACCEPT, 0 fix
rounds). The next PLAN replaces this with its own "Current session plan" per track.

## Where we are

- `main` after the two merges should be: build 0 warnings; non-Perf suite green (sim branch
  1745 / 22 / 0, view branch 1674 / 23 / 0; the union adds about 65 tests); smoke PASS; scenes
  `OrdersTest`, `QaM23Test` PASS. Conductor re-checks `main`.
- Open bugs 25 (S3 15, S4 10), none block. Bug numbers next session: sim from BUG-0058, view
  from BUG-0069.
- Sessions on 2026-10-06: 1 / 8 done. Feature sessions since last hardening: sim 2 / 4, view 4 / 4.
- **M1: 8 / 8 criteria met.** Not Done yet: the end-of-milestone hardening session and sign-off
  (coverage ✅ for Unit / Invariant fuzz / Determinism on every M1 row in `studio/qa/coverage.md`,
  retro in docs/05, M3 → Next, a "For your review" entry on how to try M1).
- **M2: 6 / 10.** Left: trees / rocks on the terrain mesh (waits for M3 resource entities), M2-5
  debug overlay, M2-6 placeholder audio, M2-7 60 FPS playable check + M2 hardening + sign-off.
- Expected merge overlap this time: `studio/bugs/README.md` (both appended rows at the end of the
  table), `studio/qa/coverage.md` (sim added a row, view edited the view row + appended a note),
  docs/03 (different sections). The sim worktree also carries the Producer's edits: docs/03 (one
  sentence, BUG-0057 item 1), docs/01 change-log row (M2-3 decisions), docs/05 ticks, BUG-0057 note.

## Sim track

### Next task candidate: M1 end-of-milestone hardening (hardening, QA full)

Batch, most valuable first; stop when the size budget (~1,500 lines) or the session is used up,
and say what was left:

1. **BUG-0055 (S3)** holders become hard walls to their own army (Producer decision; likely
   `|| u.Hold[j]` in `IsHardWall`). Measure against `CrowdRoutingTests` and the BUG-0044 perf rows;
   un-skip QA's friendly-plug row. If the crowd rows fall, keep soft and document (reverse the
   decision in docs/01 with the numbers).
2. **BUG-0054 (S3)** `Enqueue` rejects undefined kinds and unknown `Flags` bits (throw, like an
   unknown player); regression test; un-skip the QA row. BUG-0056 item 1 with it.
3. **BUG-0044 (S3)** cache the plug answer per enemy per tick (or only next to blocked ground);
   then make the tight-blob perf row enforced (BUG-0047 remainder). Measure 2,500 two-player rows.
4. **BUG-0045 (S3)** plugs of 5+ (raise `MaxPlugSpan` or span-free test); 5 QA rows skipped are
   the proof. Measure against item 3.
5. **BUG-0049 / BUG-0050 (S3)** re-bound the 64-goal row on seeds 1-80 or make it report-only;
   random-goal give-ups: accept as a known limit in docs/03 or find a terminating rule.
6. **BUG-0046 (S3)** sort `Constrain` clips or document as a known limit.
7. **BUG-0057 (S4)** items 2-4: pre-check the `--record` path; 0-checkpoint `play` wording;
   `DataError.ToString()` `: :` (request 3). `CliTests` temp-file cleanup.
8. BUG-0056 items 2 (test) and the `MovementSystem` blank lines; docs sweep of docs/03 M1 sections
   (known limits list matches the code).
9. Golden replay: if any movement rule changes (items 1, 3, 4, 6 may), regenerate once with the
   reason in the commit and diff the `k` lines.

Then, at ACCEPT, the Producer checks `studio/qa/coverage.md` for every M1 row (✅ Unit, Invariant
fuzz, Determinism), writes the M1 retro, marks M1 Done, sets M3 to Next, and lists the M1 try-out
under For your review.

### QA focus (sim hardening)
- Hash twins and the golden on every movement change; crowd rows before / after; the BUG-0044 perf
  rows alone (not during the view's run); the un-skipped rows; replay fuzz with the new `Enqueue`
  rejections (every hostile command row still never throws through `ReplayPlayer`).

### Watch-outs (sim)
- Keep the public setup API additive (the view compiles against `Command.Move` 3- and 4-arg,
  `SimConfig`, `Simulation.Enqueue`, `PeekCached`).
- Perf rows fail from CPU contention when both tracks' QA run; count a Perf failure only when it
  fails alone.
- After this session the sim's feature counter resets (hardening); M3 starts next.

## View track

### Next task candidate: view hardening session (hardening, QA standard)

1. **BUG-0068 (S3)** minimap right-click while A is armed: cancel targeting and order nothing
   (public `SelectionController.CancelTargeting()`, used by `Minimap`'s `command` branch); docs/03
   M2-3 paragraph updated; the `QaM23Test` print becomes a check.
2. **BUG-0067 (S4)** double-tap compares whole milliseconds (un-skip the QA row); targeting ends
   when the pruned selection is empty.
3. **BUG-0052 (S3)** `TerrainHeight.At` clamp in float before the `(int)` cast; un-skip the 5 QA rows.
4. **BUG-0064 (S4)** minimap dots: 2x2 or outlined dots so single scouts read against ramp / cliff
   tints; min-filter note for wide maps.
5. **BUG-0053 (S4)** `UnitViewsTest` facing on +y / diagonal; stale `SimRunner` remark; docs/03
   picker box range.
6. Dev `TerrainMeshBuilderTests`: wall-count assertion on the default map (the dropped-wall mutant).
7. Docs sweep of docs/03 M2 sections; export-hygiene notes stay notes (M6).

### QA focus (view hardening)
- Re-run every scene (`OrdersTest`, `QaM23Test`, `SelectionTest`, `MinimapTest`, `QaM24Test`,
  `QaM22Test`, `UnitViewsTest`, `CameraClampTest`); hash twins unchanged; the un-skipped rows;
  a screenshot of the new dots at 100 and 1,000 units; A-targeting state machine fuzz
  (A / Esc / minimap left / minimap right / 3D right / S / H / death in every order).

### After that (feature): M2-5 debug overlay
Nav grid cells (passable / cliff / ramp tint), flow arrows for the selection's goal via
`FlowFieldCache.PeekCached` (read each frame, never kept across ticks; null = no arrows), a
tick-time graph; toggled by a key (`debug_overlay`, F3), off by default, `--no-hud`-independent.
Pure geometry in `ViewApi/` (arrow mesh builder), Godot `MultiMesh` or `ImmediateMesh` in `game/`.
Production budget ~500 lines.

### Watch-outs (view)
- `PeekCached` is on `main` after this merge; it is the only sim read the overlay may use for
  arrows. Never call `Get` / `TryGetCached` (internal anyway).
- `select_add`, `order_queue`, `group_add` all sit on Shift; `select_type` and `group_assign` on
  Ctrl. Use the action that matches the purpose in each place.
- Headless scene runs print Godot warning stack traces for the expected "order dropped"
  warnings; they are not errors (the smoke gate greps `ERROR` only).

## Shared watch-outs
- Merge order sim → view. STATE, the session log, the handoff and roadmap ticks are written in the
  sim worktree only.
- `CLAUDE.md` is the owner's file: still "Current milestone: M1", no CLI command line. Noted in
  STATE for the owner.
- Remote Control is unavailable in unattended sessions; don't retry it.
- Both tracks' next sessions are hardening sessions, so neither may take a feature.
