# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session 2026-10-05-2330 (both tracks ACCEPT, 0 fix
rounds). The next PLAN replaces this with its own "Current session plan" per track.

## Where we are

- `main` after the conductor merges: sim M1-7 (orders + perf criterion) then view M2-4 (minimap).
  Both branches: build 0 warnings; non-Perf suite green; Perf criterion alone avg ~0.7 ms; smoke
  PASS. Expected merge overlap: `studio/bugs/README.md` (sim added 0054-0056, view 0064),
  `studio/qa/coverage.md` (both appended a row and a note), docs/03 (different hunks).
- Open bugs 22 (S3 14, S4 8), none block. Sessions on 2026-10-05: 6 / 8; a new day resets the cap.
- Feature sessions since last hardening: sim 1 / 4 (M1-7), view 3 / 4 (M2-1, M2-2, M2-4).
- Bug numbers next session: sim from BUG-0057, view from BUG-0067.
- M1: 7 / 8 (M1-8 CLI left, then the end-of-milestone hardening and sign-off). M2: 4 / 10.

## Sim track

### Next task candidates (in order)

1. **M1-8: headless CLI in `tools/`** (feature, QA light or standard, small: ~300-500 lines). A
   console project (e.g. `tools/Rts.Cli`, added to `RtsGame.sln`) that runs a scenario headless
   from seed + unit count + ticks and prints the per-checkpoint `StateHash` and tick timings
   (avg / p99 / worst), records a `.replay`, and plays one back through `ReplayPlayer` (exit code
   non-zero on mismatch). Reuse `MoveScenario` / `CrossMapScenario` logic without referencing the
   test project (copy the small scenario builder or move it into a shared `tools` helper; don't make
   the CLI depend on xUnit). docs/03 "Testing strategy" / README command line. Fold in request 2 if
   trivial: a public read-only accessor for a cached flow field's directions (for the M2-5 overlay).
2. **M1 end-of-milestone hardening session** (hardening, QA full): BUG-0044 (two-player perf at
   2,500), BUG-0045 (plugs of 5+), BUG-0055 (holders block their own army; Producer decision in
   the bug file, measured against the crowd rows), BUG-0054 + BUG-0056 item 1 (`Enqueue` refuses
   undefined kinds / unknown flags; `Flags` on Spawn/Noop), BUG-0046 (sort clips or document),
   BUG-0049 / BUG-0050 (re-bound or accept), BUG-0047 nit, the `MovementSystem` blank lines, docs
   drift sweep. Then the M1 sign-off: every M1 criterion verified, coverage ✅ for Unit, Invariant
   fuzz and Determinism on every M1 row, retro, M3 set to Next for the sim track.
3. M3 sim side (resources, gather loop, placement/construction, production queues, Age II, full
   data), with any new **Requests for the sim track** first.

### Watch-outs (sim)
- M1-8 adds a project: keep `Rts.Sim` free of new NuGet packages; the CLI may use only the BCL.
  `.sln` Release config mapping (BUG-0002) is still wrong for `game/`; don't fix it in passing
  unless it's one line.
- Unknown `Flags` bits or an undefined kind still get through `Enqueue` and break
  `ReplayFormat.Write` (BUG-0054): the CLI must only build commands through the factories.

## View track

### Next task candidates (in order)

1. **M2-3: A attack-move, S stop, H hold, Shift-queue; double-click type select, control groups
   (Ctrl+1-9 set, 1-9 recall), Tab subgroups** (feature, QA standard, production budget 500 lines
   as in M2-4, total under 1,200). The sim side is on `main` once M1-7 merges:
   `Command.Move(player, unit, target, queued)`, `Command.Stop(player, unit, queued)`,
   `Command.HoldPosition(...)`, `Command.AttackMove(player, unit, target, queued)`; `Flags` bit 0 =
   queued; queue of 8 per unit (a 9th is dropped silently, so the view may warn). A-then-click
   targets ground (AttackMove = Move until M4); S / H apply at once (unqueued) or queued with
   Shift. Note BUG-0056 item 3: a Hold followed by a shift-queued order is released as soon as the
   queued order starts, so don't show Hold as a persistent stance in the HUD. Keybinds in
   `project.godot` input map (docs/02 "Controls and camera"). Extend `SelectionTest`/a new
   `OrdersTest.tscn` with injected keys; QA hash twin with the new kinds from the view.
2. **View hardening session** (4 / 4 after M2-3): BUG-0052, BUG-0053, BUG-0064 (dot outline or
   2x2 dots; min-filter or screen-space dots for maps over 220 cells), the dev mesh-test wall
   mutant, export hygiene notes.
3. M2-5 debug overlay (nav grid, flow arrows need sim request 2, tick-time graph); M2-6 placeholder
   audio (no downloads); M2-7 playable check at 60 FPS → M2 sign-off with the M2 hardening.

### Watch-outs (view)
- Three view slices ran over before M2-4; M2-4 held its budget (365 production lines). Keep the
  explicit production-line budget in every view brief.
- `SelectionController.OrderMoveTo(Vector2)` is the single order path (3D right-click and minimap);
  M2-3 should generalise it (kind + queued flag) rather than add a second path.
- The minimap consumes mouse events inside its rect (including the wheel); any new HUD control
  must do the same and must not swallow events outside its rect.

## Shared watch-outs
- Merge order sim → view. STATE, the session log, the handoff and roadmap ticks are written in the
  sim worktree only.
- Perf failures count only when they fail again alone (both tracks' QA run at the same time).
- Remote Control is unavailable in unattended sessions; don't retry it.
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; drift noted for the owner in STATE).
