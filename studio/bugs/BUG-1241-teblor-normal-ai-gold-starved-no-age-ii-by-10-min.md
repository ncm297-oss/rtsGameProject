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
