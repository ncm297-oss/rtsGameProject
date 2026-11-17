# BUG-1553: `holy_war` against the Malazan `standard`, seed 3, stands at 20:00 without a wave

| Field | Value |
| --- | --- |
| Severity | S3 (balance, the data track's D33) |
| Status | open (the row reaches `main` as a named known case, `AliveWithoutAWave`, by the Producer's ruling 2026-11-16-2036) |
| Found | 2026-11-16-2036, task M6-S12 (the developer) |
| System | AI openings (content) |
| Fixed by | |

## Repro
1. `Content/AiOpeningPairsReportTests` pair (0, 2), seed 3, on M6-S12's tree.

## Expected
Every opening sends its first wave or is beaten before it is due.

## Actual
The Malazan `standard` sends five waves from 5:50; the Whirlwind `holy_war` army peaks at 16 pop (its wave waits for
20; 600 thinks defending) and the side is not defeated. On M6-SH2's tree it waved once, at 14:45.

## Notes
Named in `AliveWithoutAWave` (reported, not failed; the row fails if the side waves or falls, so the entry cannot go
stale). whirlwind.md "The holy war's first wave" prints it. For D33's balance pass: the wave threshold against a
side that is attacked every two minutes.
