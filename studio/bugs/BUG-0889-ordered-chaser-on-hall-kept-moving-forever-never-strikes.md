# BUG-0889: An ordered chaser on a hall can be kept Moving for the whole fight without ever striking (queued ranged unit; melee 0.02 m out of reach)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 (QA re-check after fix round 1); pre-existing |
| System | combat chase keep (M4-H3 ordered keep, slice-1 surround), movement queueing |
| Fixed by | |

## Repro
1. `QA/FlankFixRound1QaTests.HallRingedByFriends_ReachableChasersNeverGiveItUp(Ordered, 30, 12, 0)` (skipped with this
   id). 30 Heavy Infantry and 12 Crossbowmen, all given an ordered Attack on a Whirlwind hall on flat ground. The hall's
   hp is topped up every tick. Run 2,000 ticks.
2. Results at cbeb944, 3ec22ee and 321d7d6 (before slice 2) are identical:
   - Unit 42, a Crossbowman, is Moving at (78.7, 43.6) at tick 2,000 and never struck. It is about 16.5 m from the
     footprint against its 15 m range, its chase stall is held at 9, and two own Crossbowmen beside it are planted and
     shooting the hall.
   - Unit 25, a Heavy Infantry, is Moving at (79.8, 68.9) by the hall's south-west corner and never struck. Its gap is
     0.92 - 0.4 = 0.52 m against its 0.5 m reach, and four own strikers stand beside it.
3. The same Crossbowman case on a hall ringed by trees: `HallRingedByTrees_MeleeGivesUp_RangedAndDoorKeepOn(Ordered, xb, 8, 2, false)`,
   7 of 8 strike and 1 is Moving at the end.

## Expected
An ordered chaser either reaches a standing point and strikes, or (unreachable) gives up. A ranged unit 1.5 m short of
its range behind its own planted ranged units should step past or round them.

## Actual
Both are held by M4-H3's ordered keep, since their walk "reaches its goal", for 100 s. They neither strike nor give up.
The flank rule leaves them alone by design: `StrikesOurTarget` drops members striking the walker's own target building
from within its reach, so the Crossbowman's own line is never a block for it.

## Notes
- Not caused by M5-P2 or its fix round; logged because the re-check's keep-alive attacks surfaced it.
- The Heavy Infantry is a form of BUG-0461's limit cycle (docs/03 "Known limits (M1)"). The Crossbowman is the
  ranged-behind-ranged case on a building, which slice 2 leaves out.
- In a real fight the hall falls, so the cost is one idle unit per fight. Severity S3.
