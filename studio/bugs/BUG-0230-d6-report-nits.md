# BUG-0230: D6 nits: an appended contradicting Ages sentence passes G, the margins harness is an unpinned copy, the Whirlwind siege row misuses a column

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open: item 2 done on the sim side in M4-H1 (`Scenario/CounterTriangleScene` public: `Fight`, `TimeToKill`, `Side`, the scene constants); the data track's `Content/CounterTriangleMarginsTests` should call it (items 1, 3 and the switch are data-track work) |
| Found | 2026-10-08-1435, task D6 |
| System | content tests (data track), docs/factions pages |
| Fixed by | M4-H1 item 2 (sim half): `sim/Rts.Sim.Tests/Scenario/CounterTriangleScene.cs`; `Scenario/CounterTriangleTests` uses it, its 14 printed lines unchanged |

## Repro
1. **Ages clause, surviving mutant.** In a scratch clone, change docs/02 line 115 to
   `Shock Hall, Forge): two production halls, or one hall and the Forge. Or the Forge alone.` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TechContentTests|FullyQualifiedName~AgesRuleQaTests"`:
   10 / 10 pass. Seven other mutants of the clause (three halls, Forge alone, two halls and the Forge, Barracks,
   "one production hall", ", or the Forge alone." before the period) all fail both G and QA's row.
2. **Harness copy.** `Content/CounterTriangleMarginsTests.Fight` / `TimeToKill` are line-for-line copies of the private
   helpers in `Scenario/CounterTriangleTests`. Today the twelve group rows and both siege rows print the same numbers
   (checked in QA). Nothing fails if the sim track later changes the scenario's scene (spacing, seed, budget, MaxTicks)
   and the copy does not follow: the faction pages then claim "on the scene of `Scenario/CounterTriangleTests`" for
   numbers from a different scene.
3. **Whirlwind page table.** `docs/factions/whirlwind.md` "Balance baseline": the siege row puts
   `13.8 s v 254.5 s (5 %)` in the "Winner keeps (cost)" column and dashes in the others. The Malazan page uses a
   separate siege table with proper columns.

## Expected
1. The clause pin rejects a page that states the rule correctly and then contradicts it (for example: the regex
   anchored to the end of the bullet, i.e. the clause followed only by the next bullet or the paragraph end).
2. Either the sim exposes the scene as a public helper both tests call, or a test pins that the copy and the scenario
   give the same rows (the sim's rows are printed, not returned, so this needs the sim track's help: a request).
3. A separate siege table on the Whirlwind page, as on the Malazan page.

## Actual
As in Repro.

## Notes
None of these change a number or a shipped rule. Item 2 is a request for the sim track (make `Fight` / `TimeToKill`
public or move them to `CombatScenes`), not a data-track fix.
