# BUG-0981: in Normal AI-v-AI play, four of the six openings never reach Age II in 20 minutes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M5-4g, accepted 2026-10-19 with a residue: 4 of 49 live sides still miss Age II on the 27 pairing runs, none of which had it before; see BUG-1024's waiver) |
| Found | 2026-10-18-0516, task D21 |
| System | AI economy / production manager (sim: no reserve for a due step; gather split), seen through D21's openings |
| Fixed by | M5-4g (0650492 + fix rounds 57dfa22 / c76bc5f): `BuildOrderExecutor.Reservable` / `Fillable`, `AiPlayer.MayReserve` / `SetReserve` / `CanAffordAboveReserve` / `WantedGoldShare`, `rules.json` `ai.reserveShift` 0.25 + `ai.reserveArmyShare` 1. Proof: `Ai/ReserveTests.Bug0981_AnAgeTwoStepStarvedByProduction_IsResearched` (Age II never queued in 6:00 before, queued at 2:37 after); the 27 pairing runs: sides alive at 20:00 without Age II 23 / 41 → 4 / 49 (QA's `Stress/ReservePairingsStressTests`) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiOpeningPairsReportTests"` (smallest seed per pairing) and
   `--filter "FullyQualifiedName~D21OpeningsQaTests.EveryPairing_SecondSeed"` (second seed per pairing), Normal v Normal,
   the menu's map, 24,000 ticks. Read the "Age II at" in the output.

## Expected
docs/factions/malazan.md and whirlwind.md give each opening an Age II mix and Age II steps (Cadre Tower, Engineers' Yard,
Shrine, Ram Yard, the faction tech). docs/02 "AI opponent": the AI plays its build order. A side still standing at 20:00 should
have reached Age II. Its step comes due at 5:00-8:00.

## Actual
On the 9 smallest-seed pairings, Age II came in for `cadre_rush` (6:05, 8:24, 8:24) and `holy_war` (9:14, 10:35, 11:39)
only. Malazan `standard` and `crossbow_push` and Whirlwind `standard` and `raider_rush` show "Age II at never" in every
pairing, including the sides that survived to 20:00 at full pop:
- `malazan standard v whirlwind standard`, seed 1: Malazan pop 100 / 100, gold 525, wood 65, Age II never.
- `crossbow_push v holy_war`, seed 12: Malazan pop 90, **gold 1,547, wood 25**, Age II never.
- `cadre_rush v whirlwind standard`, seed 13: Whirlwind (defeated 18:24) held **gold 1,653, wood 9**; Malazan held wood 3,835, gold 17.
- `crossbow_push v raider_rush`, seed 5: Whirlwind pop 76, gold 25, wood 1,855; its `age_ii` step "can't pay 400 gold / 200 wood".

So the Age II half of four openings (their Age II compositions, 5-6 Age II steps each) never runs against an active
opponent. The pairs report counts these steps as "legitimately waiting" (gold or wood not in hand), which is true at the
moment it looks and hides the pattern.

## Notes
- The D21 developer says the same in docs/03 "Spending per opening (D21)": "the AI keeps no reserve for a due step and the
  production manager spends first", reported to the Producer as a sim-side lever. The big one-sided banks (one resource in
  the thousands, the other near zero) suggest the gather split doesn't follow what the due step needs either.
- Data levers exist but are weak (an earlier `age_ii` time, fewer queued workers). The fix belongs in the sim: a reserve for
  the earliest due research or build step, and/or a gather split weighted by the due step's cost.
- Not a D21 acceptance criterion (criterion 2 asks only for waves and no dead-end step), so S3.
