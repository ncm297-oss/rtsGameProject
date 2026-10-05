# Handoff: next session brief

Written by the Producer at the end of each session for the next one. The next session's
Producer starts from this, verifies it against the repo, and then plans. One section per track.

## Where we are (both tracks)

Session 2026-10-05-1446 (first two-track session) accepted both tracks with 0 fix rounds. Sim:
M1-6 replays + golden + BUG-0014 (1214 / 14 skipped / 0 failed, 2 m 24 s). View: M2-1 match scene
+ terrain mesh + camera + `--screenshot` (1203 / 14 / 0, 2 m 3 s; smoke PASS). M1 is 6 / 8, M2 is
1 / 10 with three criteria half-done. Open bugs: 12 S3 + 4 S4, none block. Sessions today: 4 / 8.

**First things at the next PLAN:**
- Confirm both branches merged (sim first, then view) and `main` is green *after* the view merge:
  `dotnet build` 0 warnings, non-Perf suite, `tools/qa/smoke.ps1` PASS. Expected merged counts: about
  1,300 tests (1,228 sim-branch + ~90 view-branch QA/dev additions), 15 skips (BUG-0021/0022,
  BUG-0023 x2, BUG-0025, BUG-0037, BUG-0038, BUG-0039, BUG-0040, BUG-0041 and the older ones).
- Verify one claim per track: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` exists (15,526
  bytes, ASCII) and `ReplayGoldenTests` passes on `main`; `game/scenes/Match.tscn` is instanced by
  `Main.tscn` and the smoke log shows "Match stopped at tick N" with N > 30.
- Check the merge resolutions: `studio/qa/coverage.md` has both the Replays row (✅ x5) and the two
  view rows plus both appended paragraphs; `studio/bugs/README.md` indexes BUG-0039 (sim), BUG-0040
  and BUG-0041 (view, renamed from the view branch's 0039); docs/03 has both tracks' sections
  (phase 14 + "Presentation timing" are adjacent; "Testing strategy" got bullets from both).
- Give each track its bug-number range in the brief (new rule in `studio/bugs/README.md`): next
  free is BUG-0042 for sim, BUG-0052 for view.

## Sim track

What the sim does now: `MovementSystem` (docs/03 "Local movement"): flow fields with an LRU cache
and a build cap of 2 per tick; per-unit `Plan`/`Constrain`/`Apply`/`ApplyShoves`/`RecheckAnchors`;
friendly Idle units are soft and shovable, enemies holding ground are hard walls; arrival within
1 m or touching an arrived groupmate; queued walkers don't count as stuck; stuck 20 ticks → give up.
`Rts.Sim.Replays`: `ReplayRecorder` attached to a fresh sim records stamped commands and
`StateHash()` every 100 ticks (phase 14); `ReplayFormat` text files; `ReplayPlayer.Run(replay, data)`
refuses on format version or `GameData.ContentHash()` mismatch. Golden: `cross_map_seed1.replay`.
Seeds are mixed (`SimRng.MixSeed`); six old tests use `TestSeeds.PreMix(oldSeed)` to stay on their
calibrated maps.

### Next sim task: hardening session (feature sessions since last hardening: 4 / 4)

Type hardening · QA full (movement is core) · budget up to `max_task_lines`, batch what fits.
Candidates in value order (plan the first as the task, fold in the small ones):

1. **M1-4d-3 crowd routing** (S3; closes BUG-0028/0032/0033; ~800 lines): flow-field crowd cost or
   a local detour so walkers go round other groups' blobs; un-anchored units walk back; no stuck
   ticks against walkers blocked only by field-waiting units; parked groups yield sideways in chokes.
   Re-measure the M1-4d-2 criterion-6 rows and the mixed-owner crossing (seed `PreMix(73)`: 2/200).
2. **BUG-0037 + BUG-0038** (S3, same code): owner-aware groupmate checks; the hard-wall fallback keeps
   friendly clips. Un-skip the `QA/HardWallQaTests` repros.
3. **BUG-0034 + BUG-0039** (S3): the flaky `Perf_500Units_DistinctTargetsInterleavedBySlot(32)`
   (median or more ticks) and the single-map bounds (`MoreGoalsThanCacheSlots_..._AtMost22PercentGiveUp`,
   `Crowd_ToOneOrFourClosePoints(2500, 4, ...)`): sweep many maps, set bounds that hold everywhere or
   make them report-only, retire `TestSeeds.PreMix` from them; un-skip the `SeedSweepQaTests` twin.
4. **BUG-0040 part 1** (S4, a few lines): tick-count / interval format limit in `Replay.Validate`;
   un-skip `QA/ReplayQaTests.AbsurdTickCount_IsRefusedAtRead`; docs/03 format limits line.
5. BUG-0030 (S3): Move inside an arrived unit's own goal cell.

**Golden rule for this session:** any movement change changes the golden's checkpoints. The dev runs
once with `RTS_REGEN_GOLDEN=1`, once without, and the commit message says which rule change moved
them. QA must confirm the regen happened exactly once and the new golden still has 15 checkpoints,
1,500 ticks, 400 commands. The 15 `MapGeneratorTests` pins must NOT move (no generator change).

After hardening: M1-7 perf test (500 moving units < 4 ms avg; BUG-0023 cap note), M1-8 CLI in
`tools/` (reuse `ReplayPlayer`: record / play `.replay` files, print hashes and timings), then the
M1 sign-off check: all 8 criteria, coverage ✅ for Unit/Invariant/Determinism on M1 rows, no S1/S2.

## View track

What exists in `game/`: `Main.tscn` → `Match.tscn` (`Match.cs` wires `SimRunner`, `World3D/Sun`,
`WorldEnvironment`, `TerrainView`, `RtsCamera`, `DebugOverlay/Label`, `Screenshotter`).
`SimRunner` ticks a `Simulation` (seed 1, 2 players, 2,000 units, 4,096 commands) through
`ViewApi.FixedStepClock`; exposes `Simulation`, `Alpha`, `GameSpeed`, `LastTickMs`. `RtsCamera`
focus/zoom with `ViewApi.CameraLimits`. `LaunchOptions`: `--seed`, `--speed`, `--screenshot <path>`,
`--screenshot-after <s>`. Test scene `game/tests/CameraClampTest.tscn`. Public sim surface the view
may use read-only: `World.Units` arrays (`Alive/Position/PrevPosition/Facing/Owner/TypeId/Radius/
State`), `World.Heightmap`, `World.NavGrid` (`WorldToCell/CellCenter/IsPassable`), `World.Spatial`
(check its public query surface for a rectangle query), `World.Data` (`Units[typeId].Radius`,
`Factions[owner].PrimaryColor`), `Simulation.Enqueue(Command)` with `CommandKind.SpawnUnit` /
`Move` (see `sim/Rts.Sim/Commands/`).

### Next view task: M2-2 unit views + selection + right-click move (feature, QA standard)

Scope sketch (the Producer refines at PLAN): `UnitViews` node pooling one `MeshInstance3D` per
alive unit (capsule/cylinder scaled by `Radius`, `StandardMaterial3D` tinted by the owner's faction
`PrimaryColor` from data, no physics bodies); each frame position = lerp(`PrevPosition`, `Position`,
`SimRunner.Alpha`) mapped to (x, elevation at the cell, y), facing from `Facing`; views created /
freed by scanning `Alive` against the pool each frame (events come later); 100-200 units spawned at
match start through `SpawnUnit` commands (both players, around the map center); selection: click
and drag-box on screen through a ground-plane ray (`Camera3D.ProjectRayOrigin/Normal` against the
unit's elevation plane or the heightmap), shift-add, selection ring/highlight; right-click enqueues
`Command.Move` for every selected unit of player 0. Debug label adds the selected count. OUT:
attack-move/stop/hold/shift-queue (M2-3), control groups, minimap, audio, HUD. Fold in BUG-0041 if
`LaunchOptions`/`FixedStepClock` are touched (few lines). Criteria to tick: `SimRunner` (now
interpolating) and "placeholder unit views", and the selection/right-click halves of their rows.
QA focus: the view never writes sim state except `Enqueue`/`Tick` (grep + hash twin); pool reuse
across free/respawn of the same slot; alpha interpolation never extrapolates; selection ray
accuracy at both zoom limits and on high ground; 2,000 views at 60 FPS report; screenshot inspected.

## Watch out for (both tracks)

- Merge order sim → view; the conductor reruns build/tests after each merge. Shared files likely to
  conflict every session: `studio/qa/coverage.md` (adjacent rows + appended paragraphs), `studio/bugs/README.md`
  (both append), docs/03 (phase 14 / "Presentation timing", "Testing strategy"). Resolution is
  "take both" unless the Producer says otherwise.
- Budgets: sim M1-6 ran ~3,450 lines (860 production); view M2-1 ~2,140 (560 production). Fine
  for feature slices with clear design; keep production small and plain.
- Movement is chaos-sensitive and now pinned by the golden: expect a regen in the hardening session
  and verify it is deliberate. `MoreGoalsThanCacheSlots` and the 33% crowd row are single-map guards
  until BUG-0039 is worked.
- Determinism rules: all unit-unit interaction reads start-of-tick state; neighbor order ascending
  slot; no `HashCode`, no `Dictionary` iteration; `SimRng` is a mutable struct (always `ref`).
- `TestSim.Config(...)` is the one way tests build a `SimConfig`. `ArchitectureTests` and
  `SpatialHashTests.Source_UsesNoHashCollectionsOrLinq` grep sim source (ViewApi included).
- The view reads `game/data` through `ProjectSettings.GlobalizePath("res://data")`: fine from the
  editor/worktree, not from an exported `.pck` (M6 debt).
- Full sim suite ~2-2.5 min per worktree; both tracks' QA runs overlap, so a Perf failure counts
  only when it fails again alone.
