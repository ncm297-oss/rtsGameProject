# BUG-1312: Three sim-owned loader tests go red with docs/03's M7-4 rows (they pin the shipped status list's length)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-13-1118, task M7-6 (QA full) |
| System | sim test suite (status loader rows; cross-track sequencing, as BUG-1280) |
| Fixed by | |

## Repro
1. Scratch clone of `studio` at 18be020 with docs/03 "For the data track (M7-4)" applied as written (QA's
   `qa1310/tools/rows.py`; four files).
2. The whole default loop: `dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"`
   gives `Failed: 32, Passed: 8991, Skipped: 21, Total: 9044`.

## Expected
Landing the rows docs/03 hands the data track leaves the sim-owned default loop green, or the brief names each sim test
that the data session must expect red (the data track may edit only `Content/**` and `QA/Content/**`).

## Actual
Of the 32, the ones the data track can neither edit nor is told to expect:
- `Data/StatusLoaderTests.Shipped_BurningIsMagicDamageOverTime_SlowedIsASlow`: `Assert.Equal(7, d.Statuses.Length)`
  (line 32), actual 8.
- `Data/StatusLoaderTests.Shipped_Regenerating_IsARegeneratingStatus_ItsTextInData`: the exact shipped id list, actual
  ends `..., "stealthed", "teblor_frenzied"`.
- `Data/StealthLoaderTests.ASecondRevealedStatus_IsRefused_AtItsKind`: expects the appended row's error at the literal
  `statuses[7].kind` (line 115), the loader reports `statuses[8].kind`.
- `BloodOilScenesTests.Edit_OnACopyCarryingTheDataTracksRows_*`: filed separately as BUG-1310 (S2).

The rest are expected or someone else's: 14 in `Content/**` / `QA/Content/**` (the data track's own pins of pages and
descriptions; the rows' `"description": "..."` placeholder trips them); 2 golden-header data-hash pins
(`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`:
the data hash moves on purpose, docs/03 says so); 9 on QA's `TechBonusOracle` (a `NullReferenceException` on an effect
with no `appliesTo`): QA fixed the oracle this session (`QA/TechBonusOracle.cs`), and `ResearchQaTests` and
`ResearchHostileFuzzStressTests` are 28 / 28 on the rows.

## Notes
- Fix: read the shipped count / index from the shipped file (as `BloodOilScenes.RowIndex` now does) and compare the
  id list as "contains these, in this order" rather than "equals"; or list these three in the data brief as the sim
  track's to re-pin in the same merge.
- BUG-1280's brief scope was "M7-4 helpers that append blindly"; these are older pins, so S3, not a criterion miss.
