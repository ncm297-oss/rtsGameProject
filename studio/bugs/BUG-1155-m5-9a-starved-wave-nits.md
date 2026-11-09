# BUG-1155: M5-9a round 2 nits: a sub-tick starvedWaveAfter loads as "off", the perf gate never runs the starved check, a pin that stopped ending

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-08-1556, task M5-9a (round 2, QA full) |
| System | data loader (`ai.starvedWaveAfter`), AI perf tests, QA pins |
| Fixed by | |

## Repro
1. **A positive `ai.starvedWaveAfter` that rounds to 0 ticks loads as off, without an error.**
   `QA/StarvedWaveQaTests.StarvedWaveAfter_APositiveValueThatRoundsToZeroTicks_IsRefused` (skipped under this id; fails
   unskipped: `ok True, ticks 0, errors` empty). `starvedWaveAfter: 0.01` -> `TicksUpTo` -> `Ticks(min 0)` = 0 ticks, which
   is the knob's "off". Every other 0-allowed seconds knob rounds the same way, but on this one 0 has a special meaning, so
   a value meant as "almost at once" silently disables the rule. Expected: "0.01 s rounds to 0 ticks" (0 itself stays valid).
2. **The perf gate never measures the starved check's walk.** docs/03 "Implementation (M5-9a)" round 2 says it: "Every
   seat of the perf scene is defending, so the starved check returns at its first test there." `AiPerfTests` therefore
   never pays for `Starved()`'s worst path (the composition, the army's `Free`, `Gathering`'s worker loop and an
   uncached `WalkForNode` over the node memory). QA measured it allocation-free
   (`StarvedWaveQaTests.AStarvedSeatsThink_AllocatesNothing`), but there is no timed row with starved, undefended seats.
3. **A pinned Easy-v-Normal match that used to end no longer ends inside 36,000, re-pinned without a finding (rule 43).**
   `NonHardMatchPinQaTests` Pale Hills seed 3 Easy v Normal: `4FF13AE84024D0BB 33524 3075` before M5-9a, now
   `8FBFA3820C590F5A 36000 2606` (round 1: `63DE1922B755D390 36000 2708`). docs/03's round-2 table lists the digest move
   but not that the match stopped ending. (Vathar seed 1 Easy v Normal went the other way: 36,000 -> ends at 27,344.)
   It is an Easy pin with no bound of its own, so this is a note for the Producer, not a criterion miss.

## Expected
1. The loader refuses a positive value that rounds to 0 ticks for a knob whose 0 means off.
2. A perf row with undefended starved seats (e.g. the 8-Hard-seat scene with mines emptied) under the 1 ms gate.
3. A row that stops ending is named as a finding (rule 43).

## Actual
As above.

## Notes
Grouped per the QA rule for tiny related findings.
