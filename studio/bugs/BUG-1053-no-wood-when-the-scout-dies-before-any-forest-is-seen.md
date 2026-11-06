# BUG-1053: A seat whose scout dies after the head ring is seen but before it has seen a forest never gets wood

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-1716, task M6-S7 (QA) |
| System | AI scout / economy (`ScoutManager.Sweep` death branch at phase 2, no re-scout), BUG-1047 / 1048 follow-up |
| Fixed by | |

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
