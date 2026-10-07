# BUG-0101: `--bench` "order across the map" sends the army about 15 m, to the enemy block next door

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-2114, task M2-7 (QA) |
| System | view: `game/scripts/BenchRunner.cs` (`BenchStep.OrderAcross`) |
| Fixed by | M2-H2 (f4e1b8d): `ViewApi.BenchTarget.TryAcross`, `BenchRunner.AcrossFrom/AcrossTarget/AcrossOrders`; `BenchTargetTests`, `QaM27Test` asserts reach >= 100 m and centre >= 20 m |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM27Test.tscn`
2. Read the `QA M2-7 NOTE` line (a measurement, not a failing check).

## Expected
The M2-7 plan (studio/handoff.md, "View track") asks for a scripted minute of "select, order across
the map, minimap jumps, zoom extremes", and docs/01's change-log row says the bench orders the army
"across". On the default 128 x 128 map (256 m), an order across should send the army a large part of
the map, which exercises long flow fields and a marching army on screen.

## Actual
```
QA M2-7 NOTE: 10 s bench moved the local army centre at most 17.4 m and any unit at most 30.4 m on a 256 m map
```
`OrderAcross` targets `StartLayout.Block(grid, 1, west: false, 1f)[0]`, the innermost cell of the
**east start block**. The two debug start blocks sit `HalfGapCells` (3) cells either side of the
centre line, so that target is about 15 m from the west army, inside the enemy army. The other
orders are screen points at the start zoom (A + click at 0.8 / 0.35 of the viewport, the three
Shift-queue points), so they also stay within one screen of the army. docs/03 describes the step
honestly ("Move to the far start block"); docs/01 and the plan say "across".

## Notes
- The frame-time verdict probably still holds: 100 units per player use under 1 ms of a 16.7 ms frame,
  and the 1,000-unit 60 s run reaches 3 ms ticks. But the bench as written never measures an army
  crossing the map (long field builds, units spread over many screens, the camera following
  nothing), which is what the owner's playtest will do.
- A fix could target a fixed far point instead, e.g. the passable cell nearest (0.85 W, 0.5 H) and
  then (0.15 W, 0.5 H), or the map corner opposite the army. `QaM27Test.OrderReach` prints the
  distance; once the fix lands it could assert at least about 100 m.

## Re-check (2026-10-07-0800, M2-H2 commit f4e1b8d): fixed
Verified: `QaM27Test` PASS (seed 1: 108.9 m, 22.1 m); `ViewH2QaTests.BenchTarget_200MatchSeeds_*` (30 blocked far cells: 18 cliff, 12 forest/mine; every target passable, opposite, reachable by flow field). Seed 21 moves the centre only 19.1 m: BUG-0104.
