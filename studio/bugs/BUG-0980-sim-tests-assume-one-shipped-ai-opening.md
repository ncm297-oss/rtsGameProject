# BUG-0980: 29 sim-owned tests assume each shipped `ai.json` holds one opening; the suite is red with D21's three

| Field | Value |
| --- | --- |
| Severity | S1 (red build: `dotnet test` fails 29 rows once D21 merges; the brief's criterion 1 "`DataValidationTests` green" can't be met inside the data track's files) |
| Status | open |
| Found | 2026-10-18-0516, task D21 (data track; the fix is the sim track's, its test files) |
| System | sim tests: AI opening fixtures (`Ai/`, `QA/`, `DataValidationTests`) |
| Fixed by | |

## Repro
1. On the D21 branch (`b5710be`): `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf`
   -> `Failed: 29, Passed: 7080, Skipped: 13, Total: 7122` (37 m 53 s, machine shared with two other test runs).
2. A fresh clone of the branch fails the same 10 `DataValidationTests` rows (filtered run: 10 / 1,407 failed).

## Expected
The sim's tests that build hostile or multi-opening fixtures stay green whatever count of openings the shipped files hold
(docs/03 "Implementation (M5-8a)": 1-8 openings), as the brief's criterion 1 asks.

## Actual
Every failure traced to a fixture that copies or edits `openings[0]` of the shipped file and assumes it's the only one. No
row shows wrong data: each message is an index, count or pick offset that the two extra shipped openings explain.

| File | Rows | Message | Cause |
| --- | --- | --- | --- |
| `Ai/AiOpeningPickTests.cs` | `BothShippedFiles_HoldOneOpening_Standard_EqualToThePreM58aOrder` (2) | `Assert.Single() Failure: The collection contained 3 items` | Pins one opening and the old names / Malazan wave 40 half-pop / 1,544 (D21 moved it to 32 / 1,235 on purpose, said on the page) |
| `Ai/AiOpeningPickTests.cs` | `NormalAndHard_PickEveryOpening_OverSeeds1To200` (2) | `Expected: 0 Actual: 1` | `WithOpenings(3)` appends 3 copies to the 3 shipped openings (5 in all); the row checks `NextInt(0, 3)` |
| `QA/AiOpeningsQaTests.cs` | `AThirdOpeningsBadId_IsOneErrorAtItsId` (8), `EightOpenings_Load_InFileOrder`, `FaultsInTwoOpenings_AreBothReported_AtTheirPaths`, `EachOpeningLength_RunsWithItsOwnBlackboard` (2), `FourSeatsOnThreeOpenings_TwinsEqualAtEveryCheckpoint_AndTheReplayPlaysClean` | `Expected "openings[2].id" Actual "openings[4].id"`; `10 openings is above the maximum 8`; pick 1 vs 0 / 2 vs 1 | Appends to the shipped list (indices shift by 2; 8 + 2 = 10 > 8) |
| `DataValidationTests.cs` | `BrokenAiOpenings_YieldExactlyOneErrorAtTheField` (9), `AnOpeningDescription_IsOptional_AndTheLimitsAreInclusive` | `Expected "openings[1].…" Actual "openings[3].…"`; `11 openings is above the maximum 8`; `Expected 2 Actual 4` | Same |
| `QA/TechDataQaTests.cs` | `AnEmptyFactionTechsList_Loads_TheFactionSimplyHasNoUpgrade` | `openings[1].buildOrder[25].id: unknown tech 'moranth_supply'`, same for `openings[2].buildOrder[24]` | Strips the faction tech's step from `AiScenes.Opening(root)` (opening 0) only |
| `Ai/AiDeterminismTests.cs` (via `Ai/AiScenes.EarlyWaves`) | `TwoAiSeats_Waves_TwoRuns_SameHashEveryTick(seed: 7)` | `seed 7: AI 0 never attacked` | `EarlyWaves` lowers the attack of opening 0 only; on seed 7 the Hard Malazan seat picks opening 2 (`cadre_rush`, QA probe: `PROBE seed 7: p0 2 p1 0`; seed 1: `p0 0`), which keeps its shipped 18 pop / 1,332 and waits past 6,000 ticks. The per-tick hash equality held for the whole run; only the "waved" assert after the loop fails |

## Notes
- Mechanical fixes, all in sim-owned test files: trim `openings` to `[0]` before appending in `WithOpenings`,
  `AiOpeningsQaTests`' builders and the `DataValidationTests` fixtures (keeps every index and count the rows pin); edit every
  opening in `AiScenes.EarlyWaves` and in `TechDataQaTests`' tech-strip; rewrite the "one shipped opening" pin as "opening 0
  equals the pre-M5-8a order, step for step" plus a count of 3 (or `>= 1`), and take the names and the Malazan wave from the
  file or the faction page rather than literals.
- Planning gap: the D21 brief required `DataValidationTests` green but listed only the data track's test folders, so the data
  developer couldn't touch the fixtures. Merge D21 with (or after) the sim track's fix, or `main` goes red.
- The developer's report proposed this bug and named the same 29 rows; QA reproduced them and checked each message.
