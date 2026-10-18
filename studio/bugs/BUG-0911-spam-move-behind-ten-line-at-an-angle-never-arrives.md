# BUG-0911: A Move re-clicked every 2-3 ticks behind an own 10-Crossbowman line at 30-45 degrees swings and never arrives

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-1624, task M5-H3 (QA, full tier); pre-existing (base bf8026e gives the same numbers) |
| System | movement, flank rule (lines under `FlankKeepMinMembers`, so no kept side) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H3QaTests.SpamGrid_LineAtAnyAngle"`. The
   `(10, 2)` and `(10, 3)` rows are skipped with this id; remove the `Skip` to run them.
2. The scene is QA's BUG-0887 line scene. One Heavy Infantry stands 3 m behind an own line of 10 Crossbowmen, 1 m apart, at
   the given angle. The Crossbowmen are planted on holding Raiders 14.5 m in front. The walker gets a Move to 5 m beyond the
   line's middle, re-issued every 2 or 3 ticks and alternating with a point 2 m along the line. 900 ticks.
3. Results (identical on head d048a21 and base bf8026e; arrived tick or -, turns along the line):

| angle | offset -4 | -0.3 | 0.2 | 1.5 |
| --- | --- | --- | --- | --- |
| 0, every 2 | 107, 1 | **841, 376** | 86, 3 | 98, 6 |
| 0, every 3 | **635, 177** | 102, 6 | 86, 3 | 99, 6 |
| 30, every 2 | **-, 432** | **-, 444** | **-, 154** | 91, 9 |
| 30, every 3 | **-, 288** | 125, 11 | **-, 294** | 94, 8 |
| 45, every 2 | **-, 29** | **-, 34** | **-, 34** | **-, 30** |
| 45, every 3 | **-, 23** | **-, 35** | **-, 32** | 341, 8 |
| 60, every 2/3 | arrives 108-375, 1-15 turns | | | |

## Expected
BUG-0887's bound: the walker arrives within 900 ticks with at most 8 turns along the line. Lines of 16-32 now meet it at every
angle and offset (`SpamGrid_*` rows 16-32, green on head, red on base).

## Actual
Behind a 10-line, which is the size of the owner's criterion-1 scene, the click spam at 30-45 degrees never gets round.
The walker ends about 1.5 m behind the line after 23-444 turns. A line this short is under `FlankKeepMinMembers` (11), so it
gets no kept side and no memory, and BUG-0887's `AgainstKeptSide` replacement never applies to it.

## Notes
- Not a regression, and outside M5-H3's scope (BUG-0887 named lines of 24-32). It is logged because the hammer swept lines
  of 10-48.
- Spam this fast (7-10 clicks a second) is a player's panic-click. A single Move behind the same line arrives
  (`FlankFixRound1QaTests.LineAtAnAngle_*`, 10 at 0 degrees).
- Lines of 40 and 48 are bigger than `MaxPlugCluster` and get no flank at all (docs/03 "Not yet"). They are reported by
  `OverPlugClusterLines_Report_NoCorruption` and bounded only against non-finite positions and blocked cells.
