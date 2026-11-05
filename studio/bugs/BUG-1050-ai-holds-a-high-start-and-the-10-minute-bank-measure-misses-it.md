# BUG-1050: at the "high" starting resources two openings hold the start for minutes, and M6-S6's 10:00 bank measure misses them (it fails five other sides on a fight's ebb instead)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-05-1206, task M6-S6 (game-dev) |
| System | AI (production throughput, build-order timing), `Stress/StartingResourcesAiStressTests` |
| Fixed by | |

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
