# BUG-1613: D33's Teblor "was" figures are not `main`'s, and the new tooltip names three kinds the Normal AI fields together in 7 of 64 matches

| Field | Value |
| --- | --- |
| Severity | S4 (docs accuracy for the owner's review, and tooltip wording; no crash, criteria met) |
| Status | open |
| Found | 2026-11-17-0816, task D33 (QA) |
| System | data docs: `docs/factions/teblor.md` "The Age II army (BUG-1420, D33)" and the AI section's D33 paragraph; `game/data/factions/teblor/ai.json` `standard` `description` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D33TeblorRowQaStressTests" --logger "console;verbosity=detailed"`
   (Stress, about 35 min alone; QA's own loop, the 64-match row of `D32OpeningQaStressTests`' shape). The "main" rows play
   `main`'s data rebuilt on a copy (`D33QaTests.Main`, asserted to hash to the diff base's B74DC7AE61B4862D); the
   "shipped" rows play D33's (318E8C4256456C05).
2. Read the rows (defeated / 2nd Longhouse / Shamans / Breakers / Riders / War-dogs / Anointing / all three new kinds):

| data | difficulty | defeated | 2nd Longhouse | Shaman | Breaker / Rider / War-dog | Anointing | all three |
| --- | --- | --- | --- | --- | --- | --- | --- |
| `main` | Normal | 12 | 25 | 26 | 0 / 4 / 0 | 8 | 0 |
| D33 | Normal | 10 | 23 | 25 | 12 / 22 / 39 | 11 | 7 |
| `main` | Hard | 11 | 28 | 35 | 6 / 12 / 5 | 27 | 5 |
| D33 | Hard | 11 | 23 | 25 | 35 / 43 / 48 | 18 | 35 |
| `main` | Easy | 1 | 0 | 51 | 0 / 0 / 0 | 35 | 0 |
| D33 | Easy | 1 | 0 | 34 | 27 / 36 / 51 | 1 | 17 |

## Expected
The page's "(was N)" figures describe the change D33 made, that is `main` against D33, so the owner reads the real
effect. A description that names a kind as "joining" holds in most matches (the rule D32 used to drop these three kinds
when they were fielded in 0 of 64).

## Actual
1. **The "was" figures are a baseline that never shipped.** teblor.md measures "before" as D32's opening *with D33's
   Malazan / Whirlwind waves* (it says so in a parenthesis), so it reads "10 defeats (was 5)" on Normal, which suggests
   D33 doubled the Teblor's defeats. Against `main` the Normal defeats went **12 -> 10**. The same applies to the
   other cells: Normal 2nd Longhouse "was 28" (main 25), Shamans "was 30" (main 26); Hard Breakers / Riders / War-dogs "was
   11 / 18 / 8" (main 6 / 12 / 5), Anointing "was 32" (main 27), Shamans "was 36" (main 35). The developer's report
   discloses this baseline. The page's short paragraph in the AI section ("and is defeated in 10 (was 5)") does not.
2. **The tooltip names five kinds, but the Normal AI rarely fields them together.** "...then Javelin Throwers, Shamans,
   Breakers, Destrier Riders and War-dogs join them..." By 15:00 on Normal, a Breaker is alive in 12 of 64 matches
   (none on Raraku), and all three new kinds in 7 of 64. Every check in criterion 3 passes (the target was 8 of 64 each);
   this is wording, not a missed number. Easy fields them more often (17 of 64 with all three) and Hard much more (35).

## Notes
- Both D33's numbers and the developer's F3 row are reproduced exactly in QA's loop (Normal 10 / 23 / 25 / 12 / 22 / 39;
  Hard 11 / 23 / 25 / 35 / 43 / 48; Easy Anointing 1, Shamans 34), so the measurement is right. Only the baseline label is off.
- Suggested fix (data track, docs only): print `main`'s row beside D33's in the page's table, or relabel the column
  "D32's opening on D33's waves".
- Related: BUG-1420 (cured on Normal and Hard by these numbers), the developer's provisional BUG-1612 (Easy loses the
  Anointing 35 -> 1 and Shamans 51 -> 34, confirmed above; Hard also loses Anointing 27 -> 18 and Shamans 35 -> 25 against `main`).
