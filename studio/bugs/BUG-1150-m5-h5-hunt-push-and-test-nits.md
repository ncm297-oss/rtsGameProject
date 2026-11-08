# BUG-1150: M5-H5 nits: the sighting push chains through a row of touching allied buildings; stale seed-2 comments

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-07-2306, task M5-H5 (QA full) |
| System | AI hunt (`MilitaryManager.SightingPoint`), test comments |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H5QaTests.ASightingAtTheEndOfARowOfAlliedBuildings"`
   (`sim/Rts.Sim.Tests/QA/SimHardeningM5H5QaTests.cs`): a row of N touching allied Billets (2 x 2 cells) west to east, the
   sighting 0.3 m off the row's west wall, the hunters far east on the row's axis.

## Expected
docs/02: a sighting by an ally's building "is aimed just off that building, on the army's side"; docs/03: moved "until it is
0.5 m clear of the footprint, then half a map cell (1 m) more"; QA's soak oracle: more than 0.5 m from every allied footprint.

## Actual
Each push leaves one footprint's slack box and lands inside the next (touching footprints), so the point walks the whole row:
```
1 billets: seen <27.7, 82> -> <33.5, 82>, 1.50 m clear,  5.8 m moved
3 billets: seen <27.7, 82> -> <41.5, 82>, 1.50 m clear, 13.8 m moved
4 billets: seen <27.7, 82> -> <45.5, 82>, 1.50 m clear, 17.8 m moved
5 billets: seen <27.7, 82> -> <45.5, 82>, 0.00 m clear, 17.8 m moved   (red: MaxPushes 4 stops on the fifth wall)
6 billets: the same                                                     (red)
```
Rows 5 and 6 are red; 1, 3 and 4 pass the oracle but move the point the row's whole length, not "just off".

## Notes
- Needs touching allied footprints (or gaps under 1 cell): the AI's own `ai.buildingSpacing` (2 cells) keeps an AI ally's
  base clear of it (QA's seeded fuzz round a Keep and a Billet 2 cells apart: 4,000 sightings, 0 within 0.5 m, none moved
  over 15 m), so only a human ally's packed row reaches it. Hence S4.
- A possible rule: of the four faces, the exit nearest the army's side that is clear of every allied box, else the nearest clear exit.
- Also, while there: `Stress/HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall`'s summary still names BUG-1119
  ("p3 keeps the defeated p2's buildings as known, BUG-1119, so it neither waves nor hunts") as a live cause and says "the
  hardening re-runs it with BUG-1119 and the lone-idle-worker hunt gap fixed"; after M5-H5 the stall is BUG-1068's gold
  alone (docs/03's own M5-H5 note). The skip reason is still right. **Fixed in fix round 1 (6c3639a).**
- **Added by the Producer at the ACCEPT (QA's round-1 note, S4):** the BUG-1148 retry flag lives only in the pending order
  (`PendingOrder.Slot = 1`). If the excused think cannot send the retry (no free builder), the order is dropped and the next
  Build at the old anchor is excused again: one wasted Build per such cycle, never a hang (a loop needs builders unavailable
  on exactly every excused think). Fix if wanted: remember the excuse in a saved fact, or mark the anchor when the retry
  cannot be sent. Open with this id.
