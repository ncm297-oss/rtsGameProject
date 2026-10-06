# BUG-0049: The re-bounded 64-goal row breaks on new maps: 49 give-ups on seed 61, a stray anchor on seed 64

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1609, task M1-4d-3 |
| System | movement (anchor rule) / tests (BUG-0039 re-bound) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CrowdRowSweepStressTests.MoreGoalsThanCacheSlots_Seeds41To80"`
   (remove the `Skip`). It applies the dev test's own rules (`MovementSystemTests.MoreGoalsThanCacheSlots_SweptOver40Maps_*`:
   termination, every Idle unit arrived or gave up, at most 48 of 128 give up) to seeds 41-80.

## Expected
BUG-0039 asked for bounds "that hold on every swept map"; a bound fitted to seeds 1-40 should hold on
the next 40 maps too, or be report-only.

## Actual
```
seed 51: 94 still moving (BUG-0048)
seed 61: 49 of 128 gave up (bound 48)
seed 64: 1 neither arrived nor gave up: unit 11 Idle at (120.05, 108.00), goal cell 6843, 1.45 m from
         its goal (119, 107), keeping its goal cell without being linked to an arrived groupmate
seeds 41-80 overall: gave up min 6, median 28, max 49; pack rule broken on 20/40
```
Base 7f741f1 on seeds 41-80 (same rules): median 32, max 92 gave up; seeds 61 and 64 stop cleanly
(no unit left neither arrived nor gave up).

## Notes
- Seed 64 is a stray anchor (an Idle unit keeps a goal cell it is not linked to), the state the
  anchor re-check exists to prevent. Possibly the "backing off at the limit keeps the goal" rule now
  that groupmates must share the owner, or the new deep-overlap push-out moving a settled unit; not
  traced further.
- Seed 61 alone is a fitted bound one unit short; with seed 64 it suggests the "holds on every map"
  claim of the BUG-0039 fix rests on the swept sample only.
