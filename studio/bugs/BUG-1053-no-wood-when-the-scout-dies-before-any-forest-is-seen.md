# BUG-1053: A seat whose scout dies after the head ring is seen but before it has seen a forest never gets wood

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-SH1 round 1, dev; QA confirmed 2026-11-05-2148 re-verify): with BUG-1056's release all three `ReachRuleQaTests.ScoutKilledAfterTheRing_*` rows are un-skipped and green (seed 1 at 2,000: 5,968; seed 3 at 200 / 2,000: 5,541 / 5,619) |
| Found | 2026-11-05-1716, task M6-S7 (QA) |
| System | AI scout / economy (`ScoutManager.Sweep` death branch at phase 2, no re-scout), BUG-1047 / 1048 follow-up |
| Fixed by | M6-SH1: re-scout while a kind has no known node, within `ai.scoutReplacements`; round 1: a scout sent again is released to gather once it knows a forest clear of the enemy (BUG-1056) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReachRuleQaTests.ScoutKilledAfterTheRing"` (remove the
   `Skip`): a generated 2-seat Normal v Normal match, every tree within 90 m of seat 0's hall taken (nearest forest
   90.5-91.9 m), seat 0's scout killed 200 or 2,000 ticks after it is sent, run to tick 6,000.

## Expected
docs/02 "AI opponent": the AI looks near home, then trusts what it knows; a start whose nearest forest is past the reach
"gets its wood once the scout has circled home". A seat that has lost its scout and knows no forest should still find
one (send another worker, or let the sweep's grid be finished by someone).

## Actual
| Seed, killed | Reach lifted at | First wood | Farthest wood target | M6-S7 rows that pass |
| --- | --- | --- | --- | --- |
| 1, 2,000 ticks out | 1,641 | none by 6,000 | none (no forest known) | 1 @ 0 / 200: wood at 5,420 / 5,397 |
| 3, 200 ticks out | 2,361 (by the replacement) | none by 6,000 | 185.6 m (walking) | 3 @ 0: wood at 4,694 |
| 3, 2,000 ticks out | 2,121 | none by 6,000 | none (no forest known) | 2 @ 0 / 200 / 2,000: 3,785 / 3,894 / 3,648 |

Once the head ring is seen the reach lifts, so a scout that dies afterwards is not replaced (phase 2: "periodic
re-scouting is M5-4"). If the ring's 40 m plus a worker's 14 m sight held no forest and the sweep had not reached one, the
seat knows no forest at all and its wood stays 0. Not a regression: on ab54ddb 8 of the 9 kill rows (these 3 included)
gathered 0 wood by 6,000; M6-S7 fixes 5 of them. The decided fallback (a kind with no node in reach takes the nearest
known one) would not help either: there is no known node.

## Notes
Unreachable on a valid map file at tick 0 (the loader's start contract puts a tree within 24 m of every start, and the
generator does too), so it bites once the home forest is cut out or on a start trimmed by a test. A re-scout while a
kind has no known node (bounded, see BUG-1052) is the natural fix.

## M6-SH1 (dev)
- The death branch of `ScoutManager.Sweep` now also sends another scout after the ring is seen while the player knows no
  node of some resource kind at all (`ScoutManager.KnowsEveryKind`, dropped nodes aside), within BUG-1052's bound; the
  new scout's waypoints skip the ring points seen. A seat knowing a node of every kind never re-scouts
  (`Ai/ScoutReplacementTests.ASeatKnowingANodeOfEveryKind_NeverReScouts`, seeds 1-3;
  `TheScoutLostAfterTheRing_WithNoForestKnown_IsSentAgain_WithinTheCap`).
- `QA/ReachRuleQaTests.ScoutKilledAfterTheRing_BeforeAnyForestIsSeen_TheSeatStillGetsWood` un-skipped for seed 1 at 2,000:
  re-scout sent at tick 2,341, first wood at 5,968 (a 32-tick margin under the bound).
- **Still failing, skipped (`ScoutKilledAfterTheRing_Seed3_TheSeatStillGetsWood`):** seed 3 at 200 (the scout dies before
  the ring, is replaced by M6-S7's rule and the replacement lives: nobody dies after the ring, so no re-scout applies) and
  at 2,000 (re-scout sent at 2,341). In both the sweep (head, mirror, grid nearest the scout) first sees a forest at about
  tick 4,250, 179 m off; seven gatherers walk there and none is back by 6,000; the 90.8 m forest is seen only near the end.
  Tried and dropped: a re-scout skipping the mirror (to sweep round home first): seed 3 no sooner, seed 1 at 2,000 lost its
  wood. A fix needs a sweep that looks round home before the far half when no forest is known near it: a sweep-order
  change that touches every first scout (and so the pins), the Producer's call.

## QA (2026-11-05-2148, M6-SH1)
- Confirmed: seed 1 at 2,000 green (first wood 5,968); seed 3 at 200 / 2,000 fail with the `Skip` removed. Filed as
  BUG-1056 (S2: criterion 4).
- Seeds 1-10, 90 m start, one kill (`QA/ScoutCapQaTests.OneScoutKilled_At0_200_2000_*`, printed): wood by 6,000 at 0 on 9
  of 10, at 200 on 6 of 10, at 2,000 on 4 of 10; the unharmed start on 9 of 10 (first wood 3,649-5,782).
- The re-scout is barred by the cap once a seat has lost 3 units to anything: BUG-1055.
- Mutation: re-scout removed / inverted fail 4 / 8 rows; `KnowsEveryKind` ignoring dropped nodes survived the
  developer's rows and is killed by QA's `EveryKnownForestDropped_TheScoutLostAfterTheRing_IsSentAgain`. A save while the
  re-scout is pending (seeds 1, 3) loads equal and plays hash-equal to 6,000.
