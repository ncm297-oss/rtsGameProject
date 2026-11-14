# BUG-1311: `AiLoaderTests.AnOpeningsReserveArmyShare_Loads` goes red (4 rows) once the data track ships docs/03's M7-6 row

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-13-1118, task M7-6 (QA full) |
| System | sim test suite (M7-6 loader rows; cross-track sequencing, as BUG-1280 / BUG-1310) |
| Fixed by | |

## Repro
1. Scratch clone of `studio` at 18be020. Apply docs/03 "For the data track (M7-6)" as written: `"reserveArmyShare": 0.5`
   in the `attack` object of the Teblor `standard` opening (`factions/teblor/ai.json`; the opening D31 is expected to
   pick), plus the M7-4 rows (QA's script: `python -I <scratchpad>/qa1310/tools/rows.py <clone> --m76`).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AnOpeningsReserveArmyShare_Loads"`

## Expected
The brief's goal: "build the second as a schema ... so the data track can land the Teblor rows and the chosen opening
next session". Landing the exact JSON docs/03 hands the data track must not turn a sim-owned default-loop test red
(the data track cannot edit `sim/Rts.Sim.Tests/Data/**`; rule 58).

## Actual
All four rows fail (absent, 0, 0.5, 1):
```
Assert.All() Failure: 1 out of 4 items in the collection did not pass.
[3]: Item: AiOpening { AttackMinCost = 600, AttackMinHalfPop = 16, ... teblor_gatherer ... }
     Error: Assert.Null() Failure ... Expected: null  Actual: 0.5
  at AiLoaderTests.AnOpeningsReserveArmyShare_Loads(String rawJson) in AiLoaderTests.cs:line 299
```
The test asserts that every opening of every other faction loads with no share, and that no shipped opening carries
one (`Assert.All(TestSim.Data.Ai.SelectMany(a => a.Openings), o => Assert.Null(o.ReserveArmyShare))`). Both statements
are about the shipped files, not the edit under test, and are false as soon as D31 lands.

On the same clone, the rest of the M7-4 / M7-6 classes, `ReserveTests`, `ReserveQaTests`, `AllocationTests`,
`DataValidationTests` and `DataContentHashTests` pass (704 / 709; the other failure is BUG-1310).

## Notes
- Fix: compare against the shipped value of each opening (`TestSim.Data`'s) instead of null, as the M7-6 fix did for
  `FrenzyRulesLoaderTests`' "every shipped row" statements; keep "the edited opening carries the value".
- `Ai/OpeningReserveShareTests` (all four of its methods) and QA's `QA/OpeningReserveShareQaTests` pass with the row.
