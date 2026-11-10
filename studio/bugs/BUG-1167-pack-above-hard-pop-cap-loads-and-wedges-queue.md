# BUG-1167: A queue item whose whole population exceeds the rules' hard cap loads clean, is accepted and paid, and wedges its queue for the match

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-10-0146, task M7-2 |
| System | data loader (`DataLoader` unit rows, `rules.popCap`) / production (`ProductionSystem.TryStart`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PackQaTests.APackAboveTheHardCap"`.
   - `APackAboveTheHardCap_LoadsAndWedgesTheQueue_Bug1167Symptom` (passes, documents the symptom): a test copy where Malazan
     Heavy Infantry has `spawnCount` 8 and `pop` 13 (104 pop an item) loads with no error; with every Billet built the cap is
     the hard 100 (200 half), the Train is accepted and paid, and after 2,000 ticks the item is still at the head with
     progress 0. Output: `cap 200 half (rules 200); item 208 half: wedged`.
   - `APackAboveTheHardCap_IsALoadError` (skipped, `Skip = "BUG-1167 ..."`): asserts the load fails.

## Expected
A unit row the rules can never start is a load error, like the other "loads but can't play" rows the loader already refuses
(e.g. `ai.houseWhenPopWithin` above the pop cap). `units[i]`: `spawnCount x pop` at most `rules.popCap`.

## Actual
The loader checks `pop` only as non-negative and `spawnCount` only as 1-8. `TryStart` waits while
`HalfPop + ItemHalfPop > HalfPopCap`, which is forever once `ItemHalfPop > rules.HalfPopCap`. Everything queued behind it at
that building waits too; the player paid for it and gets it back only by cancelling. The AI, which never cancels, would
lose that building for the match.

## Notes
- The hole is older than M7-2: a single unit with `pop` above 100 has it too. M7-2 makes it reachable with ordinary per-unit
  pops (8 x 13, or 8 x 12.5). The developer flagged it as "possible BUG-1167" in the M7-2 report.
- No shipped unit is affected (largest shipped `pop` 3, no `spawnCount`); the planned war-dog row (3 x pop 1) is far below.
- Fix sketch: one check in the unit pass once `rules.json` is read: `HalfPop * SpawnCount <= rules.HalfPopCap`, error at
  `units[i].spawnCount` (or `.pop` when `spawnCount` is absent) naming both numbers.
