# BUG-0593: M5-3 criterion 9 not met: in AI-vs-AI the Whirlwind seat never finds an enemy building on seeds 1-2, and neither seat attacks at the shipped thresholds

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-12-1930, task M5-3 |
| System | AI `ScoutManager` (sweep coverage), `MilitaryManager` (wave thresholds in play), `Ai/AiVersusAiTests` |
| Fixed by | 786c9e4 (M5-3 fix round 1); verified by QA re-check 2026-10-12-1930 |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiVersusAiTests" --logger "console;verbosity=detailed"`.

## Expected
Brief item 7 / criterion 9: Malazan v Whirlwind, both Normal, generated 128 map, seeds 1-3, 6,000 ticks: no exception; both
seats' wood gathered > 0 by tick 3,000; both have at least 1 army unit; **each has sent at least 1 `AttackMove`**; recorded
and played back to equal checkpoints.

## Actual
Shipped data (`ScoutAndGatherWood` rows): no seat starts a wave on any seed (waves 0, AttackMoves 0; armies 3-8 at pop
24-27). With the test's 1-pop threshold (`TestThresholds_BothAttack` rows):

| Seed | Malazan first wood | Whirlwind first wood | Malazan AttackMoves | Whirlwind AttackMoves | Whirlwind known enemy buildings at 6,000 |
| --- | --- | --- | --- | --- | --- |
| 1 | 3,220 | 1,356 | 1 | **0** | **0** |
| 2 | 3,825 | 1,327 | 1 | **0** | **0** |
| 3 | 1,361 | 436 | 1 | 1 | 5 |

On seeds 1 and 2 the Whirlwind scout finishes its sweep (`ScoutPhase` 2) without seeing any enemy building, and nothing
looks again (re-scouting is M5-4), so that seat can never attack in the match. The test asserts only "at least one seat
attacked" and "wood by 6,000"; it doesn't assert the brief's "each" and "by 3,000".

## Notes
- Cause (the developer's report agrees): the sweep's 9th point is the hall's point-mirror through the map centre, and
  `AiScenes.Generated` puts each base beside the first gold mine of its half, not mirrored. On a 256 m map, 13 points with
  14 m of sight don't cover the ground between them. `Match.cs` places bases either side of the centre line, so the mirror
  is roughly right in the game; the criterion's scene isn't.
- The shipped thresholds (Malazan 20 pop / 1,544, Whirlwind 14 pop / 861) are never reached in 5 minutes because the seats are
  wood-starved (BUG-0592).
- Options: a sweep that ends with a pass over the enemy half (or keeps going until an enemy building is known); a
  criterion scene with mirrored bases; or the Producer re-scoping the criterion. The test should assert what is agreed.

## Re-check (round 1, 786c9e4)
- The finding part is fixed. The map grid after the mirror finds the enemy: every seat of seeds 1-3 knows an enemy building at
  tick 6,000 (now asserted), and at the test's one-unit threshold each seat sends AttackMoves. Turning the grid off fails 8 rows
  (`AiVersusAiTests` x 6, two `ScoutManagerTests`).
- Criterion 9 is still not met, on wood: "both seats' wood gathered > 0 by tick 3,000". The Malazan seat's first wood is
  3,220 / 3,823 on seeds 1 / 2. That has the same cause as BUG-0592, so it is tracked there.
- At the shipped thresholds no seat waves within 6,000 ticks. Brief item 7 says "each has sent >= 1 AttackMove" without naming
  the thresholds, so the Producer decides whether the test threshold satisfies it.
