# BUG-0157: Attack-move spam to a new point every few ticks still costs damage: chasers out of reach lose their fight on each click

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-0313, task M4-2a re-check round 2 (present since round 1, `fea7963`; not caused by the BUG-0154 fix) |
| System | sim: orders / combat (`Orders/OrderSystem.Apply` AttackMove branch), sim track |
| Fixed by | 5c2de85 (M4-H2): un-skipped `QA/AttackMoveRepickQaTests.BrawlSpam_JitteredAttackMove_StillDealsTheDamage(3, true)`, `...MeanOverIntervals1To20_AtLeast90Percent`; QA `QA/SimHardeningH2QaTests.KeptChase_MeanBound_HoldsOnOtherBrawls` |

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

## M4-H1 attempt (sim track, 2026-10-08-2144): not fixed, measured
The brief's fix ("an AttackMove to a new point keeps a chase whose target is in sight") was built and measured, then
taken out: it closes this row but turns the green `(10, true)` row red, and the bound is at the scene's noise floor.
Damage dealt over 400 ticks as a share of one order's (40 v 40 unless noted; with BUG-0149's fix in, one order deals 1,827):

| Re-issued every (jittered) | 1 | 2 | 3 | 4 | 5 | 6 | 7 | 8 | 10 | 12 | 15 | 20 | mean |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| today (chasers dropped) | 72 | 88 | 74 | 83 | 99 | 98 | 93 | 94 | 103 | 96 | 98 | 100 | 92 |
| kept chase + re-pick | 100 | 97 | 100 | 88 | 95 | 97 | 93 | 93 | 88 | 94 | 89 | 99 | 94 |
| kept chase, no forced re-pick | 92 | 89 | 90 | 93 | 92 | 93 | 92 | 99 | 89 | 99 | 94 | 97 | 93 |

At 30, 50 and 60 a side the kept-chase rule is better on average (95 / 91 / 95 % against 92 / 86 / 87 %), but single
intervals swing by 10 points either way in every variant, so a per-interval 90 % bound on one 400-tick brawl is noise
for both rules. For the Producer: either take the kept-chase rule and bound the mean over intervals (or several brawl
sizes), or keep the row skipped. Row unchanged (skipped).

## Producer decision (2026-10-08-2144 ACCEPT, owner may revisit)
Take the **kept-chase rule** ("an AttackMove to a new point keeps a chase whose target is still in sight", with the re-pick)
in the **next sim hardening session**, not now: it is better on average at every brawl size measured (94-95 % against 86-92 %)
and never worse by more than noise, but a per-interval 90 % bound on one 400-tick brawl is noise for either rule. Bound it
as the dev proposed: the **mean over intervals 1-20 at 40 v 40 at least 90 %** of one order's damage, and no single interval
under 80 %; keep the `(10, true)` row green under that bound (re-state it the same way). Until then the row stays skipped
and the docs/03 "Redirecting" sentence is true only for units fighting in reach (a known limit; say so there when the fix
lands). Rationale: the game ships with same-point spam lossless and 200 ms clicks at most 4 % off; the rule is a small
average gain that is not worth a fix round inside a hardening session already at its budget.

## QA verification (2026-10-10-0624, M4-H2)
Verified by QA 2026-10-10-0624 (M4-H2). The jittered rows' per-row bound went 90 -> 80 %, and the 90 % now applies to the mean over the intervals. Both are the Producer's 2026-10-08-2144 bound; same-point spam keeps 90 %. Builder detail: the chase is kept only for a unit already on an attack-move leg (not for a `Retaliate` chase on the first attack-move); recorded in docs/01 (b) and docs/03 with the 200 v 200 reason. QA measured the bound on 9 brawl variants (jitter phase 0-3, fronts 6 / 8 / 10 m apart, 4 / 5 / 8 ranks, seed 1 / 7). Head: means 93.7-98.8 %, worst interval 84-94 %. Base: means 75.7-93.7 %, worst 60-85 %. The seed changes nothing in this flat scene. Five of the variants are now a permanent row.
