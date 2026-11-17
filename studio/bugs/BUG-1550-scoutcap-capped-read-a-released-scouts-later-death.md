# BUG-1550: `ScoutCapQaTests`' "capped" read a released scout's later death as the sweep ending on a death

| Field | Value |
| --- | --- |
| Severity | S4 (test) |
| Status | fixed (M6-S12, 2026-11-16-2036; the Producer accepts the re-read) |
| Found | 2026-11-16-2036, task M6-S12 (the developer) |
| System | AI scouting (QA test) |
| Fixed by | M6-S12 (efa7b31): `ScoutCapQaTests.Play` counts only a phase 1 -> 2 change with the scout dead |

## Repro
1. On M6-S12's tree with the old read: `ScoutCapQaTests.ASeatPastTheCapBeforeItsFirstScout_*` seed 3.

## Expected
"Capped" means the sweep ended because the scout died and the cap allowed no other.

## Actual
`Play` set `cappedAt` on the first tick with phase 2 and the tracked scout dead, so a scout sent again, released to
gather and killed later as a gatherer (seed 3: at its far forest, tick 4,259) read as "the sweep ended on a death".

## Notes
QA: the re-read is plausible; not traced further. The scout counts are still asserted.
