# BUG-1120: TeamAiSoakStressTests seed 1 adjacent red after M5-4c slice 2: a Normal seat's hunt AttackMoves onto an ally's building edge

| Field | Value |
| --- | --- |
| Severity | S3 (was S2; QA re-triage in round 1, **confirmed by the Producer at the 2026-11-07-0926 ACCEPT**) |
| Status | open (the red row is green through a narrow oracle exemption; **the hunt-point question is an item of the sim's hardening M5-H5**: the Producer's default is that a hunt's sighting point is pushed off any allied building footprint by half a cell toward the hunter, a Normal-play change that re-measures the pins; the exemption is then removed) |
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

## Re-verify (2026-11-07-0926, round 1, HEAD 9f7423b)
- The row is green. QA's full non-Perf run on a `git archive HEAD` export includes all 8 `TeamAiSoakStressTests` rows. The
  developer's exemption `HuntToASighting` excuses 14 AttackMoves on seed 1 adjacent and 0 on the other 7 rows.
- The exemption is narrow. `QA/HardKnobsRecheckQaTests.TheSoakExemption_ExcusesOnlyAHuntToItsSighting` calls the private
  predicate through reflection. It excuses only a hunt in progress whose target point is the order's point and the seat's
  last enemy-unit sighting (still remembered). Six forged near-misses are still reported: no wave; a wave on a building;
  the sighting forgotten (`EnemyUnitTick` -1); a hunt to a sweep point; an order to the sighting while the hunt walks
  elsewhere; a point 1 cm off the hunt's.
- Why S3 now: the suite is no longer red, and an AttackMove to a point beside an ally's building targets no ally (it walks
  to where an enemy unit was seen). What is left is the open question for the hardening session: should a hunt snap such a
  point off allied footprints? That would change Normal play.

## Re-verify (2026-11-07-0926, round 2, HEAD f823ec4)
- Unchanged and still open as S3. All 8 `TeamAiSoakStressTests` rows pass in QA's full non-Perf run. The exemption
  excuses 14 AttackMoves on seed 1 adjacent, the same count as round 1, and 0 on the other 7 rows. The round-2 fix doesn't
  change the hunt-point rule.
