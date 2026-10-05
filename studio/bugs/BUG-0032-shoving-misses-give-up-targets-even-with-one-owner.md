# BUG-0032: Shoving misses every criterion-6 give-up target, also with one owner (no enemies); 2,500 to 4 points is worse than no shoving

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-05-1013, task M1-4d-2 |
| System | movement (shoving, give-up rule) |
| Fixed by | |

## Repro
1. Criterion-6 rows as shipped (two players alternating by slot):
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~BuildCap_500UnitsWith500DistinctGoals|FullyQualifiedName~MoreGoalsThanCacheSlots_BuildsAtMost|FullyQualifiedName~Crowd_ToOneOrFourClosePoints" --logger "console;verbosity=detailed"`
2. The same rows with every unit owned by player 0, so every Idle unit in a walker's way can be shoved:
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ShoveQaTests.BuildCap|FullyQualifiedName~ShoveQaTests.MoreGoals|FullyQualifiedName~ShoveQaTests.Crowd" --logger "console;verbosity=detailed"`
   (`QA/ShoveQaTests.cs`; same seeds and draws as the shipped rows.)
3. Base numbers: the same tests on `0fca776` (before shoving).

## Expected
Criterion 6 of M1-4d-2: 500 units x 500 goals at most 3% give up; 128 units x 64 goals at most 5%;
500 units to 4 points at least 80% arrive; 2,500 to 4 points at least 60%. BUG-0028: groups sent
to nearby points should mostly arrive.

## Actual
| Row | Target | Base 0fca776 (2 owners / 1 owner) | M1-4d-2, 2 owners (shipped test) | M1-4d-2, 1 owner |
| --- | --- | --- | --- | --- |
| 500 x 500 goals, gave up | <= 15 (3%) | 59 / 52 | 46 (9.2%) | 51 (10.2%) |
| 128 x 64 goals, gave up | <= 6 (5%) | 41 / 41 | 23 (18%) | 21 (16.4%) |
| 500 to 4 points, arrived | >= 400 (80%) | 78 / 78 | 103 (20.6%) | 161 (32.2%) |
| 2,500 to 4 points, arrived | >= 1,500 (60%) | 329 / 329 | 264 (10.6%) | 499 (20.0%) |

- All four rows miss by a wide margin. The shipped assertions were set to the measured numbers
  (11%, 20%, 18%, 9%) and the tests renamed, not to the criterion.
- The developer's explanation ("mostly enemy units") does not hold: with no enemies at all the
  500 x 500 row is unchanged (51 vs 46 give-ups) and the other rows stay at a quarter to half of
  their targets. docs/03 repeats the claim ("most give-ups in the random-goal tests are next to
  an enemy Idle unit").
- Regression: 2,500 units to 4 points with two owners arrives 264 vs 329 before shoving. Also
  200 walkers crossing a settled 300-unit mixed-owner blob (`ShoveQaTests.WalkersCrossing...`,
  seed 73): 7 arrive vs 28 before shoving.
- Walkers crossing their own player's blob: 94 / 99 of 200 arrive (seeds 73 / 74), half give up,
  with every blob unit shovable (0 / 9 before shoving).

## Notes
- Likely contributors (reading the code, not proven): arrived units within `ArrivalDistance` of
  their point are never shoved, and the rest only toward their own point, so a walker whose flow
  field runs through another group's blob center still meets an immovable wall; units waiting for
  a field under the build cap stay walls (1.4 M standing-Moving unit-ticks in the 500 x 500 row);
  the flow field ignores units, so walkers aim straight through blobs.
- Whether the targets are reachable inside the brief's scope (no shoving Moving units or enemies,
  no flow-field changes) is the Producer's call; the numbers above are the evidence.
