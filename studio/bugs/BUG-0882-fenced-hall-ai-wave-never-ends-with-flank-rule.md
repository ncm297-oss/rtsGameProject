# BUG-0882: The fenced-hall AI wave never ends with the flank rule on (BUG-0596's guard rows red)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 |
| System | AI waves / combat chase / movement (BUG-0461 limit cycle) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AWaveWhoseMembersAllStandByATargetNoneCanStrike|FullyQualifiedName~FencedHall_EachWaveEndsAndTheNextStarts"`
2. Red at 3ec22ee: `AiMilitaryQaTests.AWaveWhoseMembersAllStandByATargetNoneCanStrike_DoesNotStayForever(n: 10)` and
   `SimHardeningM5H1QaTests.FencedHall_EachWaveEndsAndTheNextStarts_NoneEndsWhileAMemberWasBusyWithinACooldown(n: 10)`.
3. The same build with `World.FlankRuleOn` defaulted to `false` (QA's scratch clone): all 15 rows of these classes'
   theories plus `KeptChase_MeanBound_*` and `Brawl_IsFoughtToAFinish_*` pass, so the change is what turns them red.

## Expected
BUG-0596's rule: a wave whose members all stand by a target none can strike ends within 3,000 ticks, so the AI starts
another wave.

## Actual
`10 members; ... hp 2400 / 2400; wave active True, waves 1, still members 9; last order at tick 801; ... Idle d=8.6 ...
Moving d=9.1`: one Heavy Infantry stays `Moving` forever, so the wave never ends and the AI never attacks again in the
game. The dev's trace (docs/03 slice-2 "Moved elsewhere"): that unit ends on its ordered chase's goal by the tree ring in
BUG-0461's two-tick limit cycle, gives its walk up every 20 ticks, and M4-H3 / M5-H1's ordered-chase rule re-walks it.

## Notes
- The root (the limit cycle, and an ordered chase re-walked with no end) predates the change, but the change is what makes
  the guard rows red, and the effect is an AI that stops attacking for the rest of a match: a regression, not a re-baseline.
- The dev reports "no unit is planted then: the flank rule isn't running" at the end state; it moved where the unit stands
  earlier in the run.
