# BUG-1055: The scout cap counts every own loss, so a seat that lost units early never looks for a forest again

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M6-SH1 round 1, dev; QA to confirm): the first scout's death is always followed (unless `ai.scoutReplacements` is 0); `QA/ScoutCapQaTests.ASeatPastTheCapBeforeItsFirstScout_OnAFarForestStart_StillGetsWood` un-skipped and green (wood at 4,979 / 3,144 / 4,688), `Ai/ScoutReplacementTests.ASeatPastTheCapBeforeItsFirstScout_StillFollowsThatScoutsDeath`. QA's sibling row asserting the old semantics (1 scout sent) was updated to the new rule and renamed `..._ItsFirstScoutIsReplaced_AndGathersNoFartherThanUnharmed`. Seed 1 with every scout killed at the door still gathers 0 wood (no forest ever seen): left for the Producer |
| Found | 2026-11-05-2148, task M6-SH1 (QA) |
| System | AI scout (`ScoutManager.Sweep` death branch, `ai.scoutReplacements` against `PlayerView.OwnUnitLosses`) |
| Fixed by | M6-SH1 round 1: the cap applies to a scout sent again only, marked in its saved waypoints (`ScoutManager.SentAgain`, no new saved byte) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScoutCapQaTests.ASeatPastTheCapBeforeItsFirstScout"`
   (the `..._StillGetsWood` rows are skipped for this bug; remove the `Skip`).
2. The scene: a generated 2-seat Normal v Normal match, seeds 1-3. At tick 5, seat 0's trees within 90 m are taken and
   3 of its workers are killed. Its first scout is then killed 30 ticks after it is sent, before the head ring is seen.
   The match runs to tick 6,000.

## Expected
docs/02 "AI opponent" (M6-SH1) says the cap is there so "a base whose scouts die at the door stops feeding workers to the
role". A seat whose *scouts* have not died yet should still get M6-S7's replacement and M6-SH1's re-scout (BUG-1053) when
it knows no forest.

## Actual
| Seed | Scouts sent | Own losses | Wood by 6,000 | Forest known | With the cap removed (QA mutant) |
| --- | --- | --- | --- | --- | --- |
| 1 | 1 | 4 | 0 | no | 2 sent, wood at tick 5,841 |
| 2 | 1 | 4 | 0 | no | 2 sent, wood at tick 3,144 |
| 3 | 1 | 4 | 0 | no | 2 sent, wood at tick 4,688 |

The rule is "send again only while own unit losses <= `ai.scoutReplacements`". It counts every unit the seat has lost,
not just scouts. So once a seat has lost 3 units to anything, its first scout death ends the sweep for good. The reach
then lifts, but the seat knows no forest, and nothing else ever sends anyone to look. The economy stays at 0 wood.

The same dead end shows on the shipped start, with no trees cleared. In `ScoutCapQaTests.EveryScoutKilledAtTheDoor_*`
seed 1, every scout is killed on its first tick. Seed 1's hall sees no tree from home, so the seat learns of no forest.
It gathers 0 wood through tick 24,000 (20 minutes; QA measured to tick 24,000), stays in Age I, and its gold stops at
2,430 by tick 9,000. Before M6-S7 (ab54ddb) this seed also gathered 0 wood (1 scout lost), so the seed-1 case is not new.
The 3-workers-lost case is new in M6-SH1: on 60dbd57 the scout was replaced.

## Notes
- The developer chose and documented this rule: docs/03 "M6-SH1" says "every unit lost counts, not only scouts ... a seat
  that has lost units in a fight is past the point where a replacement is cheap". Whether a seat with no known forest
  should ever stop looking is the Producer's call.
- Cheap options with no new saved state: compare against the losses at the time the first scout was sent (needs a saved
  int, so a format question); count only losses while `ScoutPhase` is 1; or let the BUG-1053 re-scout ignore the cap
  when no node of a kind is known at all, maybe at a slower cadence.
- M6-SH1 criterion 3's wood clause ("wood > 0 or the hold reported") is not met on seed 1 for the same reason. The
  developer's row asserts "wood > 0 or no forest known" instead and reports this to the Producer.
