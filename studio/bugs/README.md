# Bugs

One file per bug, filed by the QA inspector (or anyone). The Producer triages them; open S1/S2
bugs outrank new features.

- Name: `BUG-<nnnn>-<short-slug>.md`, numbered in order (look at the highest existing number).
- Status moves `open` → `fixed` (with proof) or `wontfix` (with the Producer's reason).

## Template

```markdown
# BUG-<nnnn>: <title>

| Field | Value |
| --- | --- |
| Severity | S1 / S2 / S3 / S4 |
| Status | open |
| Found | <SESSION_ID>, task <TASK_ID> |
| System | <e.g. pathfinding, economy, data loader, HUD> |
| Fixed by | <commit and regression test, when fixed> |

## Repro
1. <exact steps or the failing test name and command>

## Expected
<what the docs or criteria say should happen>

## Actual
<what happens, with output excerpts>

## Notes
<suspected cause, related bugs>
```

## Index

| Id | Sev | Status | Title |
| --- | --- | --- | --- |
| [BUG-0001](BUG-0001-smoke-exit-code-hides-script-load-failure.md) | S3 | fixed | Headless smoke run exits 0 when the C# script fails to load |
| [BUG-0002](BUG-0002-sln-release-builds-game-debug.md) | S4 | open | RtsGame.sln Release configuration builds RtsGame in Debug |
| [BUG-0003](BUG-0003-simmath-sin-out-of-range-for-huge-angles.md) | S3 | fixed | SimMath.Sin/Cos return values far outside [-1, 1] for huge angles |
| [BUG-0004](BUG-0004-rejected-enqueue-advances-sequence.md) | S3 | fixed | A rejected Enqueue (queue full) still advances the player's sequence counter |
| [BUG-0005](BUG-0005-command-sort-quadratic-under-flood.md) | S3 | open | CommandQueue insertion sort is O(n^2); 10k interleaved commands stall ~107 ms |
| [BUG-0006](BUG-0006-spawn-accepts-non-finite-position.md) | S4 | fixed | SpawnUnit accepts NaN/Infinity positions into sim state |
| [BUG-0007](BUG-0007-loader-accepts-huge-numbers-as-infinity-or-overflow.md) | S3 | fixed | Data loader turns huge numbers into float Infinity or a negative int instead of rejecting them |
| [BUG-0008](BUG-0008-loader-duplicate-json-keys-last-wins.md) | S3 | open | Duplicate JSON keys are silently resolved last-wins |
| [BUG-0009](BUG-0009-loader-null-entries-in-requires-tags.md) | S4 | fixed | Null or blank entries in `requires` / `tags` are copied into GameData |
| [BUG-0010](BUG-0010-loader-does-not-check-faction-slots.md) | S4 | open | A faction with no units, or a missing or doubled template slot, loads clean |
| [BUG-0011](BUG-0011-ramp-sides-walkable-steeper-than-30-degrees.md) | S3 | fixed | Ramp sides are walkable: a unit can step 1.6-3.2 m sideways off a ramp (39-58 degrees) |
| [BUG-0012](BUG-0012-mapgen-int-overflow-passes-validate-then-crashes.md) | S3 | fixed | MapGenParams.Validate and the Heightmap ctor overflow on huge ints; Generate then crashes |
| [BUG-0013](BUG-0013-mapgen-worst-case-params-take-tens-of-seconds.md) | S4 | fixed | Map generation with params Validate allows can take ~35 s |
| [BUG-0014](BUG-0014-seed-max-gives-same-map-as-seed-zero.md) | S4 | open | Seed ulong.MaxValue generates exactly the same map as seed 0 |
| [BUG-0015](BUG-0015-mapgen-worst-case-still-slow-with-big-ramps.md) | S2 | fixed | Worst valid map params still take ~45 s (Debug) / ~7 s (Release): ramp size not in the time bound |
| [BUG-0016](BUG-0016-nearest-enemy-returns-nan-positioned-unit.md) | S3 | fixed | SpatialHash.NearestEnemy returns a NaN-positioned unit that QueryRadius excludes |
| [BUG-0017](BUG-0017-flaky-flood-allocation-test.md) | S3 | open | `Flood_10000Commands_OneTick_AllocatesNothing` failed once in 11 full suite runs, not reproduced |
| [BUG-0018](BUG-0018-flow-field-cache-thrash-rebuilds-per-unit.md) | S2 | open | More than 32 live move goals rebuilds a flow field for every unit every tick (~320 ms/tick) |
| [BUG-0019](BUG-0019-move-to-blocked-cell-full-map-scan.md) | S3 | open | Every Move to a blocked cell runs a full-map nearest-passable scan (500 Moves = 63 ms) |
| [BUG-0020](BUG-0020-arrival-across-blocked-corner.md) | S3 | open | A unit "arrives" across a blocked corner: arrival is a straight-line check |
