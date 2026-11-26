# BUG-1771: Test helpers that replace an opening's `buildOrder` leave `buildOrders` in place; `WorkerCarryTests` goes red on D38-shaped Teblor data

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-26-0207, task M7-9 |
| System | sim tests (AI scene helpers), rule 58 |
| Fixed by | |

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
