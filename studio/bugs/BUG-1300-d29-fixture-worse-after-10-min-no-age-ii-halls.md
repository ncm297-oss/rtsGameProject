# BUG-1300: D29's hand-over opening (V4) wins the 10:00 count but plays worse to 15:00: no expansion, no Age II halls, more Teblor defeats; its new description does not hold

| Field | Value |
| --- | --- |
| Severity | S2 (criterion 4: the opening named "to land" and what the player reads; the Producer's QA focus "worse anywhere / expand, every hall, Anointing by 15:00") |
| Status | fixed (re-checked 2026-11-12-0546, fix round 1) |
| Found | 2026-11-12-0546, task D29 (QA) |
| System | data: the D29 fixture `sim/Rts.Sim.Tests/Content/Fixtures/teblor_standard_d29.json` (the proposed Teblor `standard` opening); AI opening play |
| Fixed by | D29 fix round 1 (48206a4): the hand-over is now V24, measured to 15:00 (`Content/TeblorOpeningHorizonReportTests`); QA regression `QA/Content/D29FixtureQaTests` (repointed) and `D29OpeningQaStressTests.HandOverShippedAndV14_Seeds1To5_To15Minutes_Report` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D29OpeningQaStressTests.ShippedAndFixture_Seeds1_4_5_To15Minutes_Report|FullyQualifiedName~D29OpeningQaStressTests.Fixture_Seed1_VatharVMalazan" --logger "console;verbosity=detailed"`
   (QA's eight Teblor pairings, the shipped opening V0 and the fixture V4 on data copies, seeds 1, 4 and 5, played to 18,000 ticks).

## Expected
The opening D29 hands over is the best one "ready to land" (brief), and the player-facing description it brings ("War Lodge
and Javelin Ground first, then Age II; two Warriors raid at 4 army pop, then Javelin Throwers, Shamans, Breakers, Destrier
Riders and War-dogs join in Age II") describes what it does. Getting to Age II earlier should not cost the rest of the
plan (expansion, the Age II halls, the Anointing) or more lost matches.

## Actual
By 10:00 V4 is better, as the developer measured (Age II 6 / 6 / 5 of 8 on seeds 1 / 4 / 5, a wave 8 / 8 / 8; V0 1 / 2 / 1
and 4 / 3 / 2). No pairing that reached Age II or waved on V0 loses it on V4. But to 15:00 (24 pairings per variant):

| by 15:00 | V0 shipped | V4 fixture |
| --- | --- | --- |
| expanded (a second finished Longhouse) | 2 / 24 | **0 / 24** |
| every building kind of the opening finished at least once | 2 / 24 | 1 / 24 |
| Breakers Hall ever finished | 2 / 24 | 1 / 24 |
| Shamans' Circle ever finished | 5 / 24 | 6 / 24 |
| Blood-oil Anointing researched | 3 / 24 | 1 / 24 |
| Teblor defeated | 10 / 24 | **14 / 24** |

Five pairings are lost by 15:00 on V4 and not on V0 (seed 1 Raraku v Malazan 14:29, Pale Hills v Malazan 14:59; seed 4
Raraku v Malazan 14:42, Vathar v Malazan 14:49; seed 5 Raraku v Whirlwind 11:36); one the other way (seed 5 Raraku v
Malazan, V0 14:48).

Seed 1, Vathar Crossing v Malazan on V4 (Age II at 7:42, the best kind of row), minute by minute:

| time | gold / wood | Gatherers | army pop | age | waves | gate open / thinks | defending / thinks |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 8:00 | 35 / 165 | 14 | 6 | 2 | 2 | 60 / 60 | 0 / 60 |
| 9:00 | 15 / 35 | 14 | 10 | 2 | 2 | 60 / 60 | 0 / 60 |
| 10:00 | 35 / 65 | 14 | 4 | 2 | 2 | 7 / 60 | 53 / 60 |
| 11:00 | 30 / 145 | 7 | 4 | 2 | 2 | 0 / 60 | 60 / 60 |
| 12:00 | 5 / 15 | 4 | 4 | 2 | 3 | 2 / 60 | 58 / 60 |
| 15:00 | 30 / 75 | 1 | 0 | 2 | 3 | 0 / 60 | 60 / 60 |

The 14-worker economy (9 trained, not 11; the eight more only at 10:00) pays for Age II and a trickle of two-Warrior waves,
then the purse is empty (under 40 gold every minute from 8:00), the army is 4-10 pop when the opponent's attack lands at
10:00, and the Gatherers go 14, 7, 4, 1. The Breakers Hall, Stables, the eight Gatherers, the expansion and the Anointing
are still waiting at 15:00. So "Shamans, Breakers, Destrier Riders and War-dogs join in Age II" is what the player would
read and almost never see (Breakers need the Breakers Hall: 1 of 24; Destrier Riders and War-dogs the Stables, due only
at 10:00).

## Notes
- The developer's report stops at 10:00 (the brief's 12,000 ticks), so its "the file is enough" holds for the count it
  measured; the cost lands after it. The Producer's QA focus asked for 15:00.
- Likely cause: V4 buys Age II by cutting the economy (9 starting Gatherers instead of 11, the eight more moved from 7:00
  to 10:00) and spends the rest on 4-pop waves; nothing in the file makes the extra Gatherers or the Age II halls come
  sooner once Age II is in. A variant that brings the eight Gatherers back right after Age II completes (a `time` step
  just after the Age II step, e.g. 420) was not measured by D29.
- Not a sim defect: the opening is data; nothing shipped changed this session.

## Re-check (2026-11-12-0546, fix round 1)
Fixed as a report: the hand-over is now honest on both horizons. QA re-ran the developer's three reports and its own
harness (`D29OpeningQaStressTests.HandOverShippedAndV14_Seeds1To5_To15Minutes_Report`); every tally reproduces exactly.
- The fixture is V24 (V4 with Age II due at 6:00, the shipped 8 / 600 wave, the expansion at 8:00); asserted step for step
  by `TeblorOpeningFixtureTests.D29Fixture_IsTheMeasuredVariant`.
- QA's seats, seeds 1-5 (of 40), V24 vs V0: Age II by 10:00 21 vs 7, a wave 34 vs 15, target met on 1 of 5 seeds vs 0;
  by 15:00 expanded 3 vs 4, every kind 0 vs 3, Breakers Hall 0 vs 3, Stables 14 vs 22, Anointing 2 vs 5, defeated 5 vs 17
  (3 pairings worse, 15 better). Seats swapped: Age II 19 vs 5, wave 38 vs 24, defeated 9 vs 25 (2 worse, 18 better). No
  pairing loses Age II or a wave by 10:00 against V0 on either seat order.
- The report now says where V24 is worse (halls, Stables, Anointing, army pop) and concludes "the file alone is not enough",
  which the 960-match table supports: no variant meets the target on all 5 seeds (best 4 of 5: V4, V13, V14, V18, V21, all
  defeated 18-22 of 40).
- The description ("... four Warriors raid at 8 army pop, with Javelin Throwers beside them") holds: the wave is four
  Warriors' pop and the Age I composition is Warriors only (`D29FixtureQaTests.HandOverDescription_FourWarriorsAtEightPop_MatchesTheData`);
  Javelin Throwers are fielded in 21 of 40, i.e. in every match that reaches Age II by 15:00. It no longer names Breakers,
  Destrier Riders or War-dogs (fielded 0 of 40).
- Residual, not this bug: one figure in the conclusion is off (V25 finishes 4 Breakers Halls, not 0): BUG-1302 (S4).
