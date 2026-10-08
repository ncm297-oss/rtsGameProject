# BUG-0157: Attack-move spam to a new point every few ticks still costs damage: chasers out of reach lose their fight on each click

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-0313, task M4-2a re-check round 2 (present since round 1, `fea7963`; not caused by the BUG-0154 fix) |
| System | sim: orders / combat (`Orders/OrderSystem.Apply` AttackMove branch), sim track |
| Fixed by | |

## Repro
1. `sim/Rts.Sim.Tests/QA/AttackMoveRepickQaTests.cs` `BrawlSpam_JitteredAttackMove_StillDealsTheDamage(every: 3, jitter: true)`
   (skipped for this bug): remove the row's `Skip` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AttackMoveRepickQaTests.BrawlSpam"`.
2. Scene: 40 Heavy Infantry v 40 Raiders on a 64 x 64 flat map (`FlatBrawl`, 5 ranks). Player 0's whole army is
   attack-moved toward the enemy block every N ticks, each time to the same goal moved 2-3 m (a different cell),
   the way a human's A-click spam lands. Damage dealt by player 0 over 400 ticks is compared with one order.

## Expected
BUG-0152's bound: re-issuing a fight order must not throw the fight away; at least 90 % of one order's damage.
docs/03 (M4-2a, "Redirecting") says "spam to the same or to a new point still lands every hit".

## Actual
Damage in 400 ticks (one order: 1,854):

| Re-issued every | Same point (head) | New point: base `007262d` | round 1 `a38967b` | round 2 head `3c09765` |
| --- | --- | --- | --- | --- |
| 1 tick | 1,854 | 0 | 1,557 (84 %) | 1,521 (82 %) |
| 2 ticks | | 0 | 1,656 (89 %) | 1,674 (90 %) |
| 3 ticks | 1,854 | 0 | 1,503 (81 %) | 1,368 (74 %) |
| 4 ticks | | 0 | 1,728 (93 %) | 1,773 (96 %) |
| 5 ticks | | 0 | 1,503 (81 %) | 1,854 (100 %) |
| 10 ticks | | 1,908 | 1,854 (100 %) | 1,800 (97 %) |

Same-point spam is lossless on both fixes. New-point spam is far better than the base (which dealt nothing), but at a
click every 50-150 ms it still loses 10-26 %. Round 2 is not worse overall than round 1 (worse at 3 ticks, better at 5).

## Notes
- Suspected cause: only a unit that is fighting in reach / mid-swing, or that is on the same leg, keeps its fight. A unit
  chasing a target out of reach (the back ranks pressing in) gets a new cell on every click, so `ClearForOrder` drops
  its target and chase, and it walks to the click until its next scan (every `ScanInterval` ticks) takes a target
  again; the next click drops it again. The docs line above is true only for units fighting in reach.
- S3, not S2: same-point spam (BUG-0152's case, and an AI refreshing its orders) is lossless, and at 4+ ticks between
  clicks (200 ms, still very fast for a human) the loss is at most 4 % (except round 1's 5-tick row). For the
  Producer to decide whether an attack-move's new point should also keep a chase whose target is in sight.
