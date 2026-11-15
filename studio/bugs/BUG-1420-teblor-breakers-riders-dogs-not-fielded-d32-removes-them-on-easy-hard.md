# BUG-1420: The Teblor AI fields no Breaker, Destrier Rider or War-dog by 15:00 on Normal, and D32 took them away on Easy and Hard too

| Field | Value |
| --- | --- |
| Severity | S3 (AI plays a thinner army than its own mix asks for; outside D32's criteria, which are Normal-only; no crash) |
| Status | open |
| Found | 2026-11-14-1246, task D32 (proposed by the developer for Normal; QA added the Easy / Hard regression) |
| System | data: `game/data/factions/teblor/ai.json` `standard` (build order timing and file order vs the Age II `compositions`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D32OpeningQaStressTests.Opening_Row_Report" --logger "console;verbosity=detailed"`
   (Stress, about 8 min alone). Each row is 64 Teblor seats to 15:00, four maps, seeds 1-8; the last column is the
   matches in which each Age II kind was alive at some second before 15:00: Javelin Throwers / Shamans / Breakers /
   Destrier Riders / War-dogs.
2. Read the rows:

| opening | difficulty | opponents | Breakers | Destrier Riders | War-dogs | Anointing researched | every building kind |
| --- | --- | --- | --- | --- | --- | --- | --- |
| D31 (diff base) | Easy | Malazan / Whirlwind | 8 | 0 | 0 | 37 | 18 |
| D32 (shipped) | Easy | Malazan / Whirlwind | **0** | 0 | 0 | 26 | 9 |
| D31 | Normal | Malazan / Whirlwind (developer's row) | 0 | 0 | 0 | 35 | 18 |
| D32 | Normal | Malazan / Whirlwind | 0 | 0 | 0 | 20 | 18 |
| D31 | Hard | Malazan / Whirlwind | 22 | 18 | 8 | 43 | 34 |
| D32 | Hard | Malazan / Whirlwind | **0** | **5** | **0** | 19 | 14 |
| D32 | Easy / Normal / Hard | Teblor mirror | 0 / 0 / 9 | 0 / 0 / 9 | 0 / 0 / 0 | 23 / 30 / 37 | 19 / 27 / 35 |

## Expected
The Age II `compositions` row weights Breakers 10, Destrier Riders 15 and War-dogs 5 (30 of 100), and docs/02 says the AI
keeps producing its army mix. An opening that lists those kinds should field them in at least some matches by 15:00,
and a pass meant to add Shamans should not remove the kinds that Easy and Hard used to field.

## Actual
On Normal none of the three is fielded in any of 64 matches, with D31's opening or D32's. D32 moved the Breakers Hall
from 9:00 to 10:00 and put the expansion and the Shaman step ahead of it, so on Easy (8 -> 0) and Hard (22 -> 0) the
Breakers are gone too. On Hard the Destrier Riders drop from 18 to 5 and the War-dogs from 8 to 0, the Anointing from 43
to 19 and "every building kind" from 34 to 14. Defeats on Hard improved (18 -> 10), so it is a trade-off, but it is not
in the report.

## Notes
- D32's brief measures Normal only, so this does not fail a criterion. The description was narrowed for the Normal fact
  (no Breakers / Riders / War-dogs), which is now also true on Easy.
- For the balance pass: the Stables and Breakers Hall are both due at 10:00, after the expansion (8:00, holding 275 g /
  275 w), the Circle and the Shaman step; the Carver and the two researches come after. See BUG-1421 item 1 for the
  expand step's reserve gating later steps.
- The named maps give only 1-3 distinct matches across the 8 seeds (known; STATE.md), so the 64 are 22-34 distinct
  matches; the direction of the change is the same on every map.
