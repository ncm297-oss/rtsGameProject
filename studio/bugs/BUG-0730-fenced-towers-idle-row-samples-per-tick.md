# BUG-0730: Fenced-hall two-towers QA row checks "busy" every tick; the AI's idle rule samples at thinks (red at 574 vs 580)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-15-2325, task M5-P1 |
| System | AI wave idle rule / QA test model (`QA/SimHardeningM5H1QaTests`) |
| Fixed by | 620ac47 (M5-P1 fix round 1; verified by QA 2026-10-16) |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FencedHall_TwoTowers_NoWaveEndsByTheIdleRuleWhileItFights"`
on `studio/2026-10-15-2325-sim` (ce7e26b). Row `towersInsideTheFence: False` fails:
`ended at 5901, 574 ticks after a busy member, no loss` (limit `cooldown - ThinkTicks` = 600 - 20 = 580). Green on 53e69d7.

## Expected
The row's claim: "the idle rule never cuts a fight short": a wave ends only after `waveCooldown` (30 s) with no member busy.

## Actual
A per-tick trace of the members (QA instrumentation, not committed) shows the last busy ticks before the end at 5901:
```
t5269-5281 u12 Moving mode Retaliate tgt 8/False  (a unit, not a building: no surround claim)
t5321-5327 u12 Moving mode Retaliate tgt 0/False  (7 ticks)
```
Nothing busy from 5328 to 5901. The AI judges "busy" from what it samples at its thinks (every 20 ticks); the 7-tick
retaliation step at 5321-5327 began and ended between two samples, so the AI's idle count runs from its last busy sample
(about 5301) and ends the wave at 5901, 600 ticks later by its own clock. The test counts from 5327 and allows only one
think of slack, which can't cover a blip shorter than a think. The fight was not cut short in any way a player would see:
the last activity was a 0.35 s step 28.7 s earlier.

The surround rule doesn't touch these units (they chase units); it changed the earlier trajectory (the towers fall
sooner), which put a blip in a think gap. The row is trajectory-sensitive: any movement change can flip it.

## Notes
- Not a product bug in the rule's design (the AI reads the world at thinks through its view, by docs/03 rule 3). The
  row's model is too strict. Fix options (Producer's call; QA did not loosen the row): (a) the row records "busy" only on
  the AI's think ticks (the same sample the rule sees), keeping `cooldown - ThinkTicks`; (b) the AI latches "a member was
  non-Idle at any tick since the last think" (would need a per-member tick counter the AI may not read under rule 3).
  Option (a) keeps the row's intent exactly.
- Until resolved the full suite is red on this row on the sim branch.

## Verification (QA, 2026-10-16, round 1)
Option (a) as proposed: the row records "busy" only when `TickNumber % ThinkTicks == 0`, which is the state the next think
reads (`AiPlayer.Think` runs at the start of tick `View.TickNumber`). The limit is unchanged (`cooldown - ThinkTicks` = 580),
the command-based `lastBusy` and the wave-start reset are unchanged. Not a loosening of the threshold: only the sample
points moved to where the rule under test can see. Both rows green: `towersInsideTheFence: False` ends at 5901 with idle
621 (limit 580), `True` ends by loss twice.
