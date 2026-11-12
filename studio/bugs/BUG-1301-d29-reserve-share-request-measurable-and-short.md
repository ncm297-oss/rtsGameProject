# BUG-1301: D29's missing-rule request calls its effect unmeasurable, but the shipped `ai.reserveArmyShare` measures it: at 0.5 on the 8-pop wave, waves miss the target on seed 2

| Field | Value |
| --- | --- |
| Severity | S3 (report accuracy; the request's "measured reason", criterion 4) |
| Status | fixed (re-checked 2026-11-12-0546, fix round 1) |
| Found | 2026-11-12-0546, task D29 (QA) |
| System | D29 report / sim request (`AiPlayer.MayReserve`, `rules.json` `ai.reserveArmyShare`) |
| Fixed by | D29 fix round 1 (48206a4): request restated with proxy figures, `TeblorOpeningHorizonReportTests.ReserveShareProxy_ShippedAndHandOver_To15Minutes_Report` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D29OpeningQaStressTests.ReserveShareHalf" --logger "console;verbosity=detailed"`
   (QA's eight pairings, seeds 1-3, 12,000 ticks, on data copies whose `common/rules.json` `ai.reserveArmyShare` is 1 or 0.5;
   the Teblor opening is the D29 fixture with the shipped 8 / 600 wave put back, or the shipped opening).

## Expected
The request (`openings[i].attack.reserveArmyShare` overriding `rules.json` `ai.reserveArmyShare` in `AiPlayer.MayReserve`)
comes with its measured reason; the developer wrote its effect as "an inference, unmeasurable until the sim has the field".

## Actual
`MayReserve` reads `Rules.ReserveArmyShare * Knobs.WaveSizeMultiplier` (AiPlayer.cs 348), and `ai.reserveArmyShare` is
already a data field (`rules.json` line 20, value 1). Setting it to 0.5 on a data copy gives the requested gate (half the
wave) for the Teblor today; it moves the opponent's gate too, so it is an upper-bound proxy, not the per-opening field:

| config (Teblor opening, share) | Age II by 10:00, seeds 1 / 2 / 3 | a wave by 10:00 |
| --- | --- | --- |
| fixture with 8 / 600 wave, share 1 (control; the developer's 5 / 4 / 5) | 5 / 4 / 5 | 7 / 7 / 7 |
| fixture with 8 / 600 wave, share 0.5 | **7 / 7 / 7** | 6 / **5** / 7 |
| shipped opening, share 0.5 | 3 / 3 / 4 | 4 / 3 / 3 |

So the inference is right about Age II (6 of 8 met on every seed) and wrong about the target as a whole: with an 8-pop
wave and half the reserve gate, waves reach 6 of 8 on seeds 1 and 3 only (seed 2: no wave on the generated map in either
pairing nor on Raraku v Malazan; with share 1 the same opening waves 7 / 7 / 7). And on the shipped order the share alone barely helps.

## Notes
- The request is still a reasonable one (a per-opening share avoids moving every faction's AI), but its reason should
  carry these figures, and the Producer may weigh the global field (a data-only change, every faction's gate) against a
  new sim field.
- The rest of the developer's code reading checks out: the gate is closed while `Board.Defending` or the army is under
  share x wave in both half-pop and worth; only a non-train step can be reserved; Age II is reservable only once its two
  hall kinds are finished (`ResearchableSomewhere`); train steps earlier in the file than the reserved step and every
  build step are not held. One small inaccuracy: the reserve is not the only brake on army spending; the production
  manager trains only into an empty queue unless spending down (`ProductionManager` line 33).

## Re-check (2026-11-12-0546, fix round 1)
Fixed: the request now carries measured figures from the global field, labelled as a proxy that moves the opponent's gate
too. QA re-ran `TeblorOpeningHorizonReportTests.ReserveShareProxy_ShippedAndHandOver_To15Minutes_Report`; the table
reproduces exactly (hand-over, of 40: share 1: 1 of 5 seeds, Age II 21, wave 34, every kind 0, Anointing 2, defeated 5;
0.75: 2 of 5, 26, 37, 2, 10, 11; 0.5: 3 of 5, 29, 38, 8, 13, 6; at 0.5 seeds 2 and 5 miss with Age II 5 of 8; V0 at 0.5:
0 of 5 seeds). The ProductionManager correction is in the report.
- Correction to this file's own wording: "an upper-bound proxy" (Actual, above) is not proven. Lowering the opponent's
  gate also changes the opponent's tech and army timing, and the direction of that effect on the Teblor is unknown
  (hand-over share 0.5 vs share 1: 3 pairings newly defeated, 2 newly saved). The developer repeated the phrase; read it
  as "a proxy" only.
- Of note for the Producer: on the shipped opening the global share does more for the 15:00 horizon than for 10:00
  (V0 share 0.5: every kind 13 vs 3, Breakers Hall 15 vs 3, Anointing 10 vs 5, defeated 13 vs 17; Age II by 10:00 17 vs 7).
