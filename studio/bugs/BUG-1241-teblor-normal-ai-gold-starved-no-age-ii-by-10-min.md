# BUG-1241: A Normal Teblor AI stays in Age I at 10:00 in 7 of 8 map / opponent pairings: gold-starved while wood idles

| Field | Value |
| --- | --- |
| Severity | S3 (balance / opening data; the Teblor balance pass after M7-4 owns it; nothing crashes or refuses) |
| Status | open |
| Found | 2026-11-11-1317, task D28 (QA) |
| System | data: `game/data/factions/teblor/ai.json` (`standard`) and the Teblor costs; AI opening play |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TeblorAiMatchQaStressTests.ATeblorAiSeat" --logger "console;verbosity=detailed"`
   on D28's branch (QA's Stress rows: Normal Teblor v Normal Malazan with the Teblor on seat 0, v Normal Whirlwind with the
   Teblor on seat 1; seed 1; generated map, Raraku, Pale Hills, Vathar Crossing; 12,000 ticks).
2. Read each row's line ("teblor age N, waves started N").

## Expected
teblor.md "Opening 1: `standard`": step 18 researches Age II at 7:00 (time 420), Age II is in by about 8:00 and the Age II
halls follow at 8:00-9:30; teblor.md "Spending on the menu's map" measures Age II in at 513 s against an idle seat.

## Actual
At tick 12,000 (10:00) the Teblor are in Age I in 7 of 8 rows (only Pale Hills v Malazan reached Age II); 4 of 8 sent no
wave. A per-minute probe (generated v Whirlwind, Raraku v Malazan, seed 1) shows why: step 18 (`age_ii`, 400 gold / 200
wood) is never done, because gold in hand never reaches 400 while wood piles up unspent:

| Time | Raraku v Malazan: Teblor gold / wood in hand | gathered gold / wood (Teblor) | (Malazan) |
| --- | --- | --- | --- |
| 7:00 | 80 / 645 | 1290 / 1060 | 1860 / 1390 |
| 8:00 | 60 / 655 | 1570 / 1270 | 2290 / 1620 |
| 9:00 | 60 / 785 | 1870 / 1550 | 2750 / 1900 |
| 10:00 | 20 / 615 | 2220 / 1790 | 3310 / 2180 |

The gold goes to step 19's 8 Gatherers (75 gold each, due at the same 7:00), the Warriors (120 gold) and armor_1 (125
gold); nothing holds gold back for the due research. In the generated v Whirlwind run the Teblor also lose 11 of 15
Gatherers between 8:00 and 9:00 (Whirlwind raid) while holding 395-515 wood. The steps placed by time still go down
(Javelin Ground at 9:00 on 150 wood), so the base grows while the age never comes.

## Notes
- Same family as the developer's BUG-1240 (seed 1, menu map: no wave, no Age II, beaten by both Normal AIs), with a wider
  sample (4 maps, both seat orders) and a diagnosis: the opening's gold demand (Gatherers at 75, Warriors at 120, Age II's
  400) against the Teblor's 16-worker economy, not the wave's defense pull-back. Candidates for the balance pass: step 19
  after step 18 in time, a wood-heavier mix, cheaper Gatherers; or a sim-side reserve for a due research (a request).
- Not in D28's scope (the numbers are the page's).
- **Producer's probe at the 2026-11-12-0546 PLAN** (a scratch clone of `main` 9a9555b, the repro's eight rows, seed 1,
  only `ai.json` edited): the eight extra Gatherers (step 19) moved to time 480: Age II in 1 of 8 (Pale Hills v Malazan,
  as shipped), a wave in 5 of 8. Armor I moved to time 600 and the eight Gatherers to time 600, both after every other
  step: **Age II in 3 of 8** (Vathar v Malazan, Pale Hills v Whirlwind, Pale Hills v Malazan), **a wave in 6 of 8** (none
  on Raraku in either pairing). The order helps and is not enough alone. A step's cost is already held back
  (`AiPlayer.MayReserve`, M5-4g), but only while the army is at least one wave and the base is not defended, and only
  from train steps after it in file order: the likely missing rule. D29 (this session) measures the variants and lands
  no shipped file.

## D29 (2026-11-12-0546): measured, not fixed
- 25 openings over 40 pairings (four maps, v Malazan on seat 0 and v Whirlwind on seat 1, seeds 1-5) to 15:00
  (`Content/TeblorOpeningHorizonReportTests`, 960 matches). **`ai.json` alone is not enough:** no opening gives "Age II
  and a wave by 10:00 in 6 of 8" on every seed without being worse than the shipped one by 15:00.
- Of 40 (QA's count): shipped V0 Age II by 10:00 in 7, a wave in 15, defeated 17; the hand-over V24
  (`Content/Fixtures/teblor_standard_d29.json`) 21 / 34 / 5; V25 21 / 34 / 5 (every building kind in 4, no expansion);
  V14 (a two-Warrior wave) 28 / 40 / 18. The table is in `studio/sessions/2026-11-12-0546.md`.
- The missing rule: a per-opening reserve share (Request 52). Through the global `rules.json` `ai.reserveArmyShare` as a
  stand-in at 0.5 the hand-over meets the target on 3 of 5 seeds with 6 defeats.
- Nothing shipped moved. The opening lands with the Frenzy rows (D31) after Request 52; this bug closes there or goes to
  the balance pass with what is left.
