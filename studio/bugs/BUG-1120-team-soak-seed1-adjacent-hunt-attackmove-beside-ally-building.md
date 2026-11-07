# BUG-1120: TeamAiSoakStressTests seed 1 adjacent red after M5-4c slice 2: a Normal seat's hunt AttackMoves onto an ally's building edge

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-07-0926, task M5-4c-2 (proposed by the developer as BUG-1120; confirmed by QA) |
| System | AI hunt (M5-4d `MilitaryManager.Hunt` / `HuntTarget`), QA team soak oracle |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeamAiSoakStressTests"`

## Expected
All 8 rows green, as on the diff base 6f6414b (QA ran the same filter on a `git archive 6f6414b` export: Passed 8 / 8,
1 m 8 s). The brief's criterion 1: non-Perf green, only the two known-stall rows skipped.

## Actual
On HEAD 947a273 (QA's `git archive HEAD` export; the same row fails in the worktree's full run):
```
Failed TeamAiSoakStressTests.A2v2OfFourAiSeats_12000Ticks_NeverTargetsAnAlly_AndEverySeatStillWaves(seed: 1, adjacent: True)
  tick 10600: seat 3 AttackMove onto an ally's building at <273.59583, 31.747396>   (repeated per member)
```
Seat 3 is Normal (the row's difficulties turn per seat: `(p + seed) % 3`); seats 2 and 0 of the match are Hard.

## Notes
- The developer's diagnosis, consistent with the code: the Normal seat's M5-4d hunt sends `AttackMove` to its last
  sighting (`Blackboard.EnemyUnitPoint`), an enemy unit attacking the ally's building, which lies within the oracle's
  0.5 m of that building's footprint. The hunt code is unchanged by the slice; the match reaches that tick because the Hard
  seats now play differently. So the hunt behavior is old and latent; the slice uncovers it.
- Needs a ruling: either the hunt should not aim a point on an ally's footprint (snap the sighting off allied buildings,
  a Normal-play change, outside this slice's "nothing but Hard moved"), or the oracle's "AttackMove onto an ally's
  building" should exempt a hunt's sighting point (a QA change to a permanent test, which QA does not do without the
  Producer's say). Until one lands the suite is red.
