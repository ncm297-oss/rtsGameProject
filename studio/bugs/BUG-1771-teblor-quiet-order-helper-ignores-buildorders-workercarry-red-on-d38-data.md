# BUG-1771: Test helpers that replace an opening's `buildOrder` leave `buildOrders` in place; `WorkerCarryTests` goes red on D38-shaped Teblor data

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M8-4 part A, 2026-11-26-0717; QA verified on the real D38 file, on a second D38 shape and on the merged tree: `WorkerCarryTests` green, no sim-owned helper red) |
| Found | 2026-11-26-0207, task M7-9 |
| System | sim tests (AI scene helpers), rule 58 |
| Fixed by | M8-4 part A (6352a58): `AiScenes.SetBuildOrder`; `Ai/BuildOrderHelperSourceTests.OnlyTheHelper_WritesBuildOrder`, `NoTest_ReadsASeatsStepsAsTheOpeningsBuildOrder`. The data folders' sites: D38 (`D19QaTests`, `AiHouseRuleReportTests`, 14 files read `ai.Steps`); what is left there is BUG-1840 |

## Repro
1. Scratch clone of 756bc1e. In `game/data/factions/teblor/ai.json`, add to the `standard` opening what docs/03
   "For the data track (M7-9)" shows: `"buildOrders": { "easy": <D36 K9KC10S buildOrder>, "hard": <D36 QRD1 buildOrder> }`
   (from `Content/D36TeblorOrderSearchReportTests.AllVariants`).
2. `dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`.

## Expected
Only the rows that pin the shipped data hash or the shipped Teblor opening go red (those are D38's to move). A sim
helper that sets up a "quiet" opening keeps it quiet whatever the shipped file carries (rule 58).

## Actual
8 failed of 10,509. Seven are pins (the data hash or the D34 fixture): `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
`QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`, `StillStealthLoaderTests.TheHash_IsUnchangedWithoutTheField_MovesWithIt_AndWithEachField`,
`Content/D34HashMoveTests.TheShippedHash_IsD34s_AndNotD33s`, `QA/Content/D33QaTests.TheRebuiltMainData_HasTheDiffBasesHash_AndTheShippedDataD33s`,
`QA/Content/D34QaTests.TheShippedData_IsD34s_QasQ2WithTheProposedText`, `Content/TeblorOpeningFixtureTests.TheShippedOpening_IsTheD34Fixture`.

One is a broken helper:

```
WorkerCarryTests.TheAisFullLoadTest_ReadsTheWorkersOwnCarry(ownFullLoad: False)
Assert.Equal() Failure: Expected: 1  Actual: 0   (WorkerCarryTests.cs:163)
```

`s_teblorQuiet` (WorkerCarryTests.cs ~line 113) sets every Teblor opening's `buildOrder` to one train step at 3,600 s,
but the row's seat is Hard, so it now plays the `hard` entry (QRD1), not the quiet order, and the scene is no longer
the one the row describes.

## Notes
About 100 sites in `sim/Rts.Sim.Tests` write `o["buildOrder"] = ...` without removing `buildOrders` (grep
`\["buildOrder"\]\s*=`). Only this one went red on the rehearsal, because the rehearsal has no `normal` entry and most of
those scenes use Normal or Malazan seats. Any of them can break silently once a shipped opening carries an entry for
the difficulty the scene uses. Suggested fix (sim track, before or with D38): one shared helper (for example in
`AiScenes`) that sets an opening's build order and removes `buildOrders`, used by these sites; or at the least fix
`WorkerCarryTests.s_teblorQuiet` and add this list to D38's brief.

## Fix (M8-4 part A, 2026-11-26-0717)
- `AiScenes.SetBuildOrder(opening, steps)` writes `buildOrder` and removes `buildOrders`; all 104 sim-owned sites that
  wrote `["buildOrder"] =` use it. Source rows: `Ai/BuildOrderHelperSourceTests.OnlyTheHelper_WritesBuildOrder` (one hit:
  the helper's line) and `NoTest_ReadsASeatsStepsAsTheOpeningsBuildOrder` (11 files read `ai.Opening.BuildOrder` for a
  seat's steps; now `ai.Steps`).
- `TeblorSwitchOrders` none / A / B and `PerDifficultyOrders` A-C are built on D34's order
  (`Content/Fixtures/teblor_standard_d34.json`, read only), written through the helper, so their figures did not move
  when D38 landed.
- Proof (rule 59): the developer's copy `m84-d38copy`, QA on the real D38 `ai.json` (21 red = the brief's 20 + the new
  golden-header row `MaxAliveLoaderTests.TheHash_*`), QA's second shape (`buildOrder` = Easy's, entries for normal and
  hard: no sim-owned helper red), and the Producer's merged tree at the ACCEPT (default loop 0 failed).
