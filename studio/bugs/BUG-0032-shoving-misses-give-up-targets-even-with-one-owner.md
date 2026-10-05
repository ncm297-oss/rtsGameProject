# BUG-0032: Shoving misses every criterion-6 give-up target, also with one owner (no enemies); 2,500 to 4 points is worse than no shoving

| Field | Value |
| --- | --- |
| Severity | S3 (filed S2; Producer re-triage 2026-10-05, see below) |
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

## Re-check round 1 (2026-10-05-1013, fix 16d72e8)
Still open. Re-measured by QA (same tests), 2 players / 1 player:

| Row | Target | Round 1 | Fix round 1 |
| --- | --- | --- | --- |
| 500 x 500 goals, gave up | <= 3% | 46 / 51 | 30 (6.0%) / 28 (5.6%) |
| 128 x 64 goals, gave up | <= 5% | 23 / 21 | 26 (20.3%) / 23 (18.0%) |
| 500 to 4 points, arrived | >= 80% | 103 / 161 | 174 (34.8%) / 219 (43.8%) |
| 2,500 to 4 points, arrived | >= 60% | 264 / 499 | 891 (35.6%) / 996 (39.8%) |

Better than base on every row; every target is still missed. The 128 x 64 row with two players got worse
(23 -> 26), and the developer loosened its own bound 20% -> 22% (`MoreGoalsThanCacheSlots_..._AtMost22PercentGiveUp`).
Crossing a settled blob (`ShoveQaTests.WalkersCrossing...`): mixed owners 2 / 0 of 200 arrive (seeds 73 / 75;
28 / 0 before shoving), same owner 12 / 29 (round 1: 94 / 99). docs/03 now states the causes correctly
(the enemy claim is withdrawn). QA re-tightened its own bounds to the new numbers:
`FieldBuildCapQaTests.BuildCap_500UnitsWith500DistinctGoals_AllStop_AtMost7PercentGiveUp_NoDeadlock`
(<= 7%) and the stress crowd rows (>= 32% / >= 33% arrived). Whether the targets are in scope (the developer
says they need unit-aware routing) is the Producer's call.

## Producer triage (2026-10-05-1013, ACCEPT)
S2 → S3, stays open, task M1-4d-2 accepted. The targets were the Producer's; the causes (flow
field ignores units; units waiting for a field and enemies are walls) were outside the brief's
own scope, so no in-scope change could reach them. Shoving as shipped is correct, safe and
improves every row over both the base and no-shoving. Not debt for a hardening session: it is the
next movement feature, **M1-4d-3 (crowd routing)**: a crowd cost in the flow field for cells held
by standing units (or a per-tick local detour), units that lost their anchor walking back, and
not counting stuck ticks against walkers blocked only by field-waiting units (BUG-0028). Carry
the criterion-6 targets into that task and re-measure the same rows; also re-check the
mixed-owner crossing regression (seed 73: 2/200 vs 28). Fix = targets met or re-set with a reason.
