# BUG-0104: The 10 s bench moves seed 21's army centre only 19.1 m (QaM27's 20 m bound fits seed 1, not the map family)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-0800, task M2-H2 (QA) |
| System | view: `game/scripts/BenchRunner.cs`, `Rts.Sim.ViewApi.BenchScript` / `BenchTarget` (the bench's "order across" march) |
| Fixed by | 9998824 (M3-V3b: `QaH2Test` holds seed 1 to 20 m and other seeds to 15 m; docs/03 "Implementation (M2-7)" says the march length is the seed's). Verified by QA 2026-10-07-1715: `QaH2Test -- --seeds 1,6,21` PASS (22.7 / 20.7 / 19.8 m), twice (shipped data, D3 buildings) |

## Repro
1. `dotnet build RtsGame.sln`
2. `& $env:GODOT --headless --path game res://tests/QaH2Test.tscn -- --seeds 21`
3. Read the `QA M2-H2 NOTE: seed 21` line and the `TEST FAIL` line.

## Expected
The M2-H2 brief (item 1) and the Producer's QA focus: on seed 1 and on other seeds, the 10 s bench sends
the local army at least 100 m across the map and its centre moves at least 20 m (`QaM27Test`'s
`MinCentreShift`, the Producer's replacement for the brief's 25 m). docs/03 "Implementation (M2-7)"
says the army marches from 0.75 s until H at 9.5 s.

## Actual
Deterministic (same numbers on two runs alone and one run under suite load):

```
seed 21: far cell (108, 64) flags Blocked, Cliff; 1 across orders; target <217, 127> 101.5 m from <115.5, 128.8>; centre moved at most 19.1 m
seed  6: far cell (108, 64) flags Blocked, Resource; target 116.4 m away; centre moved at most 20.3 m
seed  1: far cell (108, 64) flags None;            target 108.9 m away; centre moved at most 22.1 m
seed  7: 102.6 m, 23.3 m   seed 23: 102.3 m, 25.6 m   seed 31: 106.6 m, 26.2 m   seed 43: 105.4 m, 24.5 m
```

The target is right on every seed (passable, on the other side, reachable, >= 100 m for the west army);
only the 10 s displacement varies from seed to seed (19.1-26.2 m over the seven seeds run). The cause on
seed 21 was not isolated; the start position alone doesn't explain it (seeds 7 and 23 start at about the same
x and move 23-26 m), so the terrain and the idle east block in the march's path are the likely factors
(docs/03's "halves the army's pace" remark).

Related, from `ViewH2QaTests.BenchTarget_200MatchSeeds_BothSides_*` (seeds 1-200, both start blocks):
the target is >= 100 m away for every west block, but for the *east* block on seeds 42 (99.97 m) and 133
(99.5 m) it is just under 100 m. The bench only orders from the east side after the army has crossed the
centre line (never in a 10 s run), so this is a note on the docs' implied guarantee, not a failure.

## Notes
The 20 m bound was fitted to seed 1 (22.1 m) with ~10% margin; seed 6 sits at 20.3 m. Options: hold the
bound on seed 1 only and say so in docs/03 (the march length is a property of the seed's terrain), or give
the march room (e.g. order across before A + click so the move is not behind the enemy block, or start it
at 0.5 s). Not a perf problem: the bench still measures a marching army. `QaH2Test`'s default seeds are
1, 6, 31; seed 21 reproduces this.
