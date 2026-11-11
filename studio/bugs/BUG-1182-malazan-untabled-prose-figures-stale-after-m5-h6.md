# BUG-1182: malazan.md's untabled raider-rush figures (seeds 18, 23, 25, 19) are stale on today's sim, stated as current

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D-R36, 2026-11-10-1236: the four figures re-worded with their dates (10:50; three waves from 12:29, falls 18:49; 12:22 / tick 14,843; 502 gold), the old ones kept dated; QA verified against BUG-1182's printout) |
| Found | 2026-11-09-2226, task D-R35 |
| System | Docs: `docs/factions/malazan.md` (`cadre_rush` BUG-0982 paragraph, "Fast Age II against the raider rush") |
| Fixed by | `D-R36` (b7082a8) |

## Repro
1. On D-R35's HEAD (`780d73c`, the sim is M5-H6), run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH6ThresholdQaTests|FullyQualifiedName~D21OpeningsQaTests" --logger "console;verbosity=detailed"`.
2. Compare the printout with the prose that quotes these seeds (no table pins them, so no test fails).

## Expected
The page quotes the figures the sim plays today, or says they are dated and no longer current. The brief asked for every
prose figure to agree with the measurement it quotes.

## Actual
`DH6ThresholdQaTests.Bug0982_KeepEighteen_HoldsOnTwoMoreSeeds` today:

```
| 18 / 1332 | 18 | never (0) | never | 10:50 | 6 |
| 18 / 1332 | 23 | 12:29 (3) | 8:11 | 18:49 | 21 |
| 16 / 1184 | 23 | 12:28 (2) | 8:11 | never | 21 |
| 14 / 1036 | 23 | 12:10 (2) | 8:11 | never | 17 |
```
`CrossbowPushVersusRaiderRush_FourthAndFifthSeeds`: `seed 25: ... defeated 12:22 (tick 14843)`.
`D21OpeningsQaTests` seed 19: `at 20:00 pop 1 / cap 0, gold 502, wood 270`.

The page says:
- line 411-412 (present tense): "18 at 10:44); seed 23 sends one wave (13:53) and still falls, at 18:27". Today seed 18
  falls at 10:50 and seed 23 sends 3 waves, the first at 12:29, and falls at 18:49.
- lines 580-583: seed 18 "destroyed at 10:44"; seed 23 "sends one wave at each (13:53, 13:34, 13:31), peaks at 20 to 21
  pop ... destroyed at 18:27 at 18 pop and 18:39 at 14, but stands at 20:00 at 16". Today: 3 / 2 / 2 waves at 12:29 /
  12:28 / 12:10, peaks 21 / 21 / 17, destroyed at 18:49 at 18 pop and standing at 20:00 at both 16 and 14.
- line 588-589: "Today ... seed 25's is defeated at 12:33 (tick 15,068)". Today 12:22, tick 14,843.
- line 591: seed 19 "at 20:00 the side has 1 pop, no building and 522 gold". Today 502 gold.

The conclusions the paragraphs draw still hold: the rule over five seeds picks 18 (the run prints "rule over 5 seeds
picks 18"), seeds 8, 14, 18 send no wave, 19 and 25 send none, and "5 of 10 seeds" is unchanged.

## Notes
The developer's report names these figures (seeds 18 / 23 / 25 / 19, dated 2026-11-07) as having "no table to check, left
as is". They have no table, but the QA tests above print them, so they can be checked. Probably moved by M5-9a or M5-H6,
neither of which re-printed them. A fix is a docs re-word with the numbers above (or the "measured 2026-11-07" form kept
and "Today" dropped). Pinning them would need these Stress-category tests to assert the page, which is a Producer call.
