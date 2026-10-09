# BUG-0230: D6 nits: an appended contradicting Ages sentence passes G, the margins harness is an unpinned copy, the Whirlwind siege row misuses a column

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1 and 3 in D7; item 2 in D8) |
| Found | 2026-10-08-1435, task D6 |
| System | content tests (data track), docs/factions pages |
| Fixed by | item 1: D7 anchors `TechContentTests.AgeClause` to the end of its bullet (all eight mutants of Repro 1 fail G). item 3: D7 gives `docs/factions/whirlwind.md` "Balance baseline" a separate siege table like the Malazan page's (numbers unchanged). item 2 (sim half): M4-H1, `sim/Rts.Sim.Tests/Scenario/CounterTriangleScene.cs`; `Scenario/CounterTriangleTests` uses it; the data half: D8, `Content/CounterTriangleMarginsTests` has no harness of its own and calls `CounterTriangleScene.Fight` / `TimeToKill` / `SameCostCount` |

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

## D7 update (2026-10-08)
- **Item 1 fixed.** `TechContentTests.AgeClause` now requires the clause to end its bullet (only the next "- " bullet or
  the end of the "### Ages" section may follow). Mutant results against `TechContentTests.G` (and QA's
  `AgesRuleQaTests`): appended "Or the Forge alone." fails G (QA's row still passes it; QA's oracle was not touched);
  "three production halls, or two halls and the Forge", "two production halls, or the Forge alone", "two production
  halls, or two halls and the Forge", "two Barracks, or one Barracks and the Forge", "one production hall, or one hall
  and the Forge", "..., or one hall and the Forge, or the Forge alone." and "three production halls, or the Forge alone."
  all fail both.
- **Item 3 fixed.** The Whirlwind page's Battering Ram row is in its own "Siege beats buildings" table with the Malazan
  page's columns (Siege unit | Same cost of line infantry | Building | Siege time | Line time | Siege / line); numbers
  unchanged (13.8 s, 254.5 s, 5 %).
- **Item 2 happened.** On the D7 base (the M4-3a fog branch with main merged), the copy went red: M4-3a added a spotter
  to `Scenario/CounterTriangleTests.TimeToKill` (the fog drops an Attack on an unseen building) and the copy did not
  follow, so both siege rows printed "not done" and `CounterTriangleMarginsTests` failed. D7 mirrors the one spotter
  line into the copy (public `CombatScenes.Spotter` / `At`); every printed row, group and siege, is again identical to
  the pages. The item stays open until D8 switches the copy to the sim's shared helper.

## D8 update (2026-10-09)
- **Item 2 fixed (data half).** `Content/CounterTriangleMarginsTests` lost its private `Fight` / `TimeToKill` / constants and
  calls `Rts.Sim.Tests.Scenario.CounterTriangleScene` (`Fight`, `TimeToKill`, `SameCostCount`, `Side`). It also prints the
  scenario's own 14 lines (same format as `Scenario/CounterTriangleTests`) for a byte diff, and pins every printed table
  row to the faction pages cell for cell (`EveryPrintedRow_EqualsItsPageRow`), so a scene change can no longer leave the
  pages describing a different scene silently.
- Item 1's residual (" - Or the Forge alone." on the same line) is BUG-0260, also fixed in D8.
