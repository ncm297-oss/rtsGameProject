# BUG-0921: QaM6V11Test red since the M5-P2 merge: "no seat reached Age II by tick 17603"

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (QA, 2026-10-17-1624: `QaM6V11Test.MatchTicks` 17,600 -> 21,600; green on M6-VH3 head in 93 s) |
| Found | 2026-10-17-1624, task M6-VH3 (developer report; reproduced by QA on base) |
| System | view QA test `game/tests/QaM6V11Test.cs` (the exact-tick Age II row) on the M5-P2 sim |
| Fixed by | QA test change in the M6-VH3 QA pass (staging length only; every check unchanged) |

## Repro
1. Base f8036fc (before M6-VH3), built: `& $env:GODOT --headless --path game res://tests/QaM6V11Test.tscn`

## Expected
`QA M6-V11 TEST PASS`: the four-seat seed-11 AI match runs long enough for the first Age II (row "Age II at its tick").

## Actual
```
QA M6-V11 TEST FAIL: no seat reached Age II by tick 17603: the exact-tick row did not run (an AI change? raise --ticks or MatchTicks)
```
Run on base with `-- --ticks 36000` (QA): `seat 2 first reached Age II at tick 21001 (17:30)` (was 17,021 = 14:11 at the
M6-V11 QA), seat 0 falls at 11,668 (was ~11,536), seat 3 at 29,594; every oracle row PASS.

## Notes
Not caused by M6-VH3 (same failure on f8036fc). The M5-P2 sim (flank rule, merged at 63e3a31) moved the seed-11 AI
match: its first Age II comes 3,980 ticks (3 min 19 s) later. The test's fixed 17,600-tick match no longer reaches it.
QA raised the match to 21,600 ticks (the test's own failure message names that fix); no check was loosened.
For the sim track / Producer, a note, not a bug: on this seed the first Age II of any of the three AIs (seat 2, Normal; seats 1 and 3 are Hard and never reach it before
tick 21,001) moved from 14:11 to 17:30 with the
flank rule; if the AI's age-up timing matters for balance, it is worth a look in the next AI report.
