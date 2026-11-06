# BUG-1050: at the "high" starting resources two openings hold the start for minutes, and M6-S6's 10:00 bank measure misses them (it fails five other sides on a fight's ebb instead)

| Field | Value |
| --- | --- |
| Severity | S2 (QA 2026-11-05-1206: was S3 as filed by the dev; M6-S6 acceptance criterion 5 is not met) |
| Status | open (behaviour fixed and verified in round 1; criterion 5's 10:00 measure still fails 4 of 36 unseen sides: Producer to restate the measure) |
| Found | 2026-11-05-1206, task M6-S6 (game-dev) |
| System | AI (production throughput, build-order timing), `Stress/StartingResourcesAiStressTests` |
| Fixed by | 0ad9d0c (spend-down rule, `aiSpendAbove` 200 on `high`; `Ai/SpendDownTests`, `QA/SpendDownQaTests`) for the behaviour; the measure is the Producer's call |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StartingResourcesAiStressTests" --logger "console;verbosity=detailed"`:
   the 9 Malazan x Whirlwind opening pairings on their smallest seeds, Normal v Normal, menu map, 10 minutes, each run at
   `standard` (200 / 200) and at `high` (1000 / 1000). The `ROW` lines give, per side, the bank (gold + wood) at 10:00, the
   mean bank from 2:00 to 10:00 (every 5 s) and the bank at 4:00, standard / high.

## Expected
M6-S6 criterion 5 (the brief): each of the six openings spends a `high` start; its measure: "the bank at 10:00 no more than
2x the `standard` run's 10-minute bank". If an opening idles, the lever is the executor's reserve rule (not `ai.json`), and
the 27 `standard` pairing runs must not move (rule 31).

## Actual
Two different things, neither caught by the measure:

1. **Two openings hold much of the start for minutes.** Malazan `cadre_rush` (Fast Age II) and Whirlwind `holy_war` (Holy
   war) at `high`:

   | Side, seed | bank at 4:00 standard / high | mean bank 2:00-10:00 standard / high | bank at 10:00 standard / high |
   | --- | --- | --- | --- |
   | `cadre_rush` v `standard` (Swarm), 13 | 369 / 1,515 | 272 / 855 | 41 / 143 |
   | `cadre_rush` v `raider_rush`, 7 | 443 / 1,415 | 625 / 1,091 | 948 / 921 |
   | `cadre_rush` v `holy_war`, 20 | 203 / 1,325 | 315 / 832 | 409 / 255 |
   | `holy_war` v `standard` (Combined arms), 3 | 431 / 951 | 343 / 785 | 307 / 239 |
   | `holy_war` v `crossbow_push`, 12 | 275 / 941 | 279 / 800 | 393 / 351 |
   | `holy_war` v `cadre_rush`, 20 | 245 / 841 | 303 / 574 | 519 / 169 |

   Both reach Age II at the same tick at `high` as their build order's time trigger allows (`cadre_rush` 6:05 on every
   seed at `high`), with 1,000+ in hand from the first minute; the probe columns of the timeline show producers idle with
   the bank full (`cadre_rush` seed 7 at 4:00: 540 gold / 875 wood, 1 of 2 producers busy; at 6:00 pop 32 / 34). The
   extra money waits for time-triggered steps and for production capacity (one queue item per producer, houses), not for
   the reserve: at those moments no step is due (`steps ... due 0`), so M5-4g's reserve holds nothing. The same float shows
   at `standard` later in a match (`cadre_rush` seed 7 holds 948 at 10:00 at `standard` too), so this is the AI's spending
   capacity, which a big start exposes early.
2. **The 10:00 measure fails five sides, four of which spent the start by 4:00** (bank at 4:00 at most 350 of 2,000 at
   `high`): `standard` (Combined arms) v `raider_rush` seed 2 (221 -> 481, x2.18), its `raider_rush` side (372 -> 794,
   x2.13), `crossbow_push` v `raider_rush` seed 5 (72 -> 306, x4.25), Swarm v Combined arms seed 1 (170 -> 432, x2.54: 13
   gold / 419 wood with its army lost). One sample at 10:00 is a fight's ebb (an army just lost, wood piling while gold is
   short). The fifth, `cadre_rush` v Swarm seed 13 (41 -> 143, x3.49), does hold the start (1,515 at 4:00) but fails only
   because its `standard` bank at 10:00 is near empty: doubling a near-empty bank is noise. The test lists these five in `KnownOverTwice` (a listed side that comes under prints "take it out", a new side
   over fails), so it asserts the brief's measure everywhere else.

No lever was pulled: the brief's named lever (the executor's reserve rule) holds nothing in the moments that bank, and any
spend-faster rule (a time-triggered step coming due early when the bank covers it, an extra producer when the bank floats)
fires at `standard` too once a bank is large, which would move the 27 `standard` pairing runs (rule 31) unless gated on the
preset. The 27 `standard` runs print identical to 5368cfe on this commit.

## Notes
- Producer's call: (a) accept `high` as "more money, same pacing" (the AI is no worse than at `standard`, it just floats
  more early on) and restate criterion 5 with a measure that reads the start (the bank at 4:00, or the mean bank) rather
  than one 10:00 sample; or (b) a spending rule for large banks, gated so `standard` cannot reach it (a rules.json knob, a
  bank threshold above every `standard` run's peak, measured), with the data track re-printing nothing.
- Related: BUG-0981 (long gold / wood waits at `standard`), M5-4g's reserve (docs/03 "Implementation (M5-4g)").

## QA (2026-11-05-1206, M6-S6 inspection)

Re-graded **S2**: criterion 5 ("AI at high: no opening banks over 2x standard's 10-minute bank") is not met, and the
behaviour behind it is real (an AI that sits on most of a 2,000 start through 5:00 is not "spending its start", the brief's
goal). The dev's analysis holds; QA's independent numbers, per opening rather than per pairing
(`QA/StartingResourcesAiQaTests`, each opening on the 3 smallest seeds its seat picks it on, Normal v Normal, menu map,
10 min, standard / high; identical in two separate processes):

| Opening (seeds) | bank at 5:00 standard / high | bank at 10:00 over 2x standard |
| --- | --- | --- |
| Malazan `cadre_rush` (13, 7, 8) | 703 / 1,539; 687 / 1,769; 443 / 1,679 | seed 13 (41 / 143) |
| Malazan `crossbow_push` (11, 5, 9) | 308 / 149; 264 / 316; 118 / 74 | seed 11 (68 / 397, x5.8, a side the dev's test doesn't run), seed 5 |
| Malazan `standard` (1, 2, 3) | 120 / 740; 42 / 210; 76 / 570 | seed 2 |
| Whirlwind `holy_war` (3, 4, 6) | 449 / 1,125; 237 / 1,225; 479 / 1,025 | none |
| Whirlwind `raider_rush` (2, 5, 7) | 301 / 358; 320 / 384; 603 / 224 | seed 2 |
| Whirlwind `standard` (13, 1, 9) | 419 / 92; 343 / 212; 349 / 92 | seed 1 |

6 of 18 sides over the brief's measure; the two openings that really hold the start (`cadre_rush`, `holy_war`: 1,025-1,769
at 5:00) are not among the ones the measure flags, except `cadre_rush` seed 13 by accident. No opening is idle in the other
sense: at high every side has done at least as many build steps by 5:00 as at standard, no side is defeated at high that
stands at standard, and waves at high are at most one fewer (Whirlwind `standard` seed 1: 3 -> 2). The 27 standard pairing
runs are identical to 5368cfe (QA re-ran both commits). Either way the Producer picks, criterion 5 needs the restated
measure or the gated rule before M6-S6 can be accepted as written.

## QA re-check round 1 (2026-11-05-1206, fix 0ad9d0c)

**The behaviour is fixed, robustly.** With `aiSpendAbove` 200 at `high` every opening spends the start: on 36 sides no
tuning saw (`QA/SpendDownQaTests.SixUnseenSeedsPerOpening_AtHigh_SpendTheStart`: each opening on the first six seeds from
101 its seat picks it on, Normal v Normal, menu map, 10 minutes) the mean bank 2:00-10:00 at `high` is 0.32-1.44 x
`standard`'s (30 of 36 at or below 1.0) and the bank at 5:00 at `high` is 28-654 (`standard` 68-587). `cadre_rush` at
5:00 holds 70-466 (was 1,539-1,769 on QA's round-0 seeds), `holy_war` 85-545. Rows identical in two processes.

**The brief's measure is met only on the tuned seeds.** The same 36 sides, bank at 10:00 `high` over twice `standard`'s:

| Side, seed | bank at 10:00 standard / high | mean bank 2:00-10:00 standard / high |
| --- | --- | --- |
| Malazan `standard`, 114 | 61 / 230 (x3.77) | 145 / 199 |
| Malazan `crossbow_push`, 105 | 87 / 209 (x2.40) | 229 / 213 |
| Malazan `crossbow_push`, 118 | 235 / 484 (x2.06) | 269 / 207 |
| Whirlwind `holy_war`, 115 | 75 / 157 (x2.09) | 254 / 209 |

4 of 36 (11 %), against 0 of the 36 the threshold was chosen on (QA's 18 + the dev's 18). Each is one 10:00 sample over a
small `standard` bank (61-235) with the mean bank at or below 1.37 x. More evidence it is the sample, not the rule: the
`>`-to-`>=` mutant on the threshold (a difference only when a bank sits exactly on 200) flips `crossbow_push` v
`raider_rush` seed 5 in `Stress/StartingResourcesAiStressTests` to 359 v 72 (x4.99). Picking another threshold to pass these
36 would be the same over-fit. QA's recommendation: restate criterion 5 as "the mean bank 2:00-10:00 at `high` at most 1.5 x
`standard`'s on every side" (met on all 72 measured sides, 0.32-1.44; the dev's stress test already asserts it) and close
this bug; no further code change is asked for.

Also verified: `standard` bit for bit (StandardPresetPinQaTests 20 / 20, SharedVisionFfaPinTests 5 / 5, the 27 pairing
runs identical to 5368cfe), a `high` AI match saved at 0:10 / 1:00 / 3:00 while spending down plays to 8:00 hash-equal to
the unsaved run every tick, a `high` recording plays to every checkpoint (and diverges played as `standard`), 0 B per tick
on the spend-down path, extreme thresholds (1, 1,000,000) x starts (0, 1,000,000) twin-equal with no negative bank.
