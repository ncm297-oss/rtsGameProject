# BUG-1067: A seat whose scouts all die at the door can go 13+ minutes without wood, though its start woodlot is 22 m from the hall

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-06-0326, task M6-S8 (QA); not caused by M6-S8 (the generated map and Raraku show it too) |
| System | AI scout / economy (the reach after `ai.scoutReplacements` is spent, `ScoutManager.Sweep` death branch, known forests) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MapSlice2StressTests.EveryScoutKilledAtTheDoor_FirstWood_ByMap" --logger "console;verbosity=detailed"`
   (a measurement row: two Normal AI seats, every scout of both seats killed the tick it is seen out, to 16,000 ticks).
2. Read the `ROW` lines (first wood tick, wood at 8,000 / 16,000).

## Expected
docs/02 "AI opponent": the AI looks near home, then trusts what it knows. The start contract puts a tree within 24 m of every
start (Pale Hills and Vathar Crossing: a 14 x 3 woodlot 22 m north of the hall). Once the cap has ended the sweep (3 scouts
lost, the reach lifted) the seat should find that woodlot and gather wood within a minute or two, not after many minutes.

## Actual
Each seat's three scouts die by tick 401-441 (all before the head ring is seen). Phase 2 and the reach lifted on every row, then:

| Map, seed | Seat 0 first wood | Seat 1 first wood |
| --- | --- | --- |
| Pale Hills 5 | **none by 16,000** (13:20) | 11,108 |
| Vathar Crossing 5 | **none by 16,000** | 11,108 |
| Pale Hills 2 | 8,110 | 10,410 |
| Vathar Crossing 1 | 4,357 | 13,209 |
| generated 1 | **none by 16,000** | 1,922 |
| Raraku 1 / 2 / 5 | 765 | 10,201-10,228 |
| generated 2 / 5 | 742-869 | 744-841 |

In `MapSlice2StressTests.EveryScoutKilledAtTheDoor_OnANewMap_LosesAtMostThreeASeat` (seeds 1-10, to 8,000) seat 1 has 0 wood
at 8,000 on all 20 rows, seat 0 on 5 of the 10 Pale Hills seeds. Unharmed, the same pairings have 530-770 wood at 5:00. The
cap itself works (3 scouts a seat on every row). A building sees `rules.json` `buildingSight` 12 m, short of the 22 m woodlot, so presumably no forest is
known until a gatherer happens to walk near one.

## Notes
- This is a harsh scene (every scout killed within 20 s of leaving), but it is what an enemy camping the door does. A
  seat with no wood can't build its Billets, so its army stalls.
- It looks like the BUG-1053 / BUG-1047 family: the reach lifts but the seat knows no forest, and the cap (rightly) stops
  any more scouts. Possible fixes are the Producer's call: the hall sees its start woodlot (a longer hall sight, data),
  or with no forest known after the cap, gatherers search near home.
