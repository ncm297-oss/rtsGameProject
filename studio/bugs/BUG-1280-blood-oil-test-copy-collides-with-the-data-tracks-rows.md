# BUG-1280: The M7-4 Blood-oil test copy breaks 84 sim-owned tests as soon as the data track ships the docs/03 rows

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-11-12-0546, task M7-4 (QA full) |
| System | sim test suite (M7-4 fixtures; cross-track sequencing, as BUG-1222) |
| Fixed by | M7-6 (sim, 2026-11-13-1118; 18be020 + fix round 1 198c5d4): `BloodOilScenes.Edit` removes any shipped `teblor_frenzied` row and `statusDuration` effect, then appends its own; `BloodOilScenesTests` (2), QA `OpeningReserveShareQaTests.DataTrackRows_OnACopyThatAlreadyCarriesThem_StillLoads` |

## Repro
1. On a scratch clone of `studio/2026-11-12-0546-sim` (1422a33), apply docs/03 "For the data track (M7-4)" to
   `game/data` exactly as written: append the `teblor_frenzied` row to `common/statuses.json`, point
   `blood_oil_frenzy`'s `applyStatus` at it, append `{ "stat": "statusDuration", "amount": 4, "statuses":
   ["teblor_frenzied"] }` to `teblor_blood_oil_anointing`, and `"scale": 1.25` on every Teblor building. (QA's script:
   `datarows.py` in the session scratchpad; four files change, nothing else.)
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StatusOnEndTests|FullyQualifiedName~FrenzyDamageTests|FullyQualifiedName~StatusDurationTechTests|FullyQualifiedName~FrenzyRulesQaTests|FullyQualifiedName~Hash_TheAftermath|FullyQualifiedName~FrenzyRulesLoaderTests|FullyQualifiedName~FrenzyAftermathFuzz|FullyQualifiedName~AnM74Field|FullyQualifiedName~AllocationTests.BrawlTicks_WithFrenzy|FullyQualifiedName~DataValidationTests"`

## Expected
The data track's next session (the Teblor rows, which the brief names as the follow-up) lands the rows docs/03 tells it
to write, and the sim track's M7-4 tests keep passing: the data track owns only `Content/**` / `QA/Content/**` and
cannot edit `sim/Rts.Sim.Tests/Abilities/BloodOilScenes.cs` or the classes built on it.

## Actual
`Failed: 84, Passed: 342 of 426`. Every class on the copy fails at load:
`common/statuses.json: statuses[8].id: duplicate id 'teblor_frenzied'` (from `AiScenes.DataWith`).
`BloodOilScenes.Edit` appends the row unconditionally, and also appends a second `statusDuration` effect to the
Anointing (a +8 s copy once the shipped one is there). Red: `StatusOnEndTests` (8), `FrenzyDamageTests` (4 +),
`StatusDurationTechTests` (5), `FrenzyRulesLoaderTests` (about 20 rows), `StateHashTests.Hash_TheAftermath_*`,
`DataContentHashTests.AnM74FieldInTheFiles_*` (5, its own copy also appends `teblor_frenzied`),
`Stress/FrenzyAftermathFuzzStressTests` (3 seeds, a Stress pin class the data track runs), and (before QA made its
copy idempotent) QA's own `QA/FrenzyRulesQaTests`.

## Notes
- Same class of hazard as BUG-1222 (sim-owned fixtures that a data-track change turns red).
- Fix options: make `BloodOilScenes.Edit` (and `DataContentHashTests.AnM74FieldInTheFiles_*`'s copy) idempotent:
  replace an existing `teblor_frenzied` row and an existing `statusDuration` effect instead of appending, as
  `QA/FrenzyRulesQaTests.BloodOil` now does (it passes 28 / 28 on both trees); or have the fixtures use a test-only id
  (`zz_test_frenzied`) that the data track will never ship. Either way the data brief should name the order: this
  fix lands on `main` before the data track's Teblor rows, or the data session will see 84 red tests it may not touch.
- `DataValidationTests` stays green with the rows (the rows themselves load).

## Fix (M7-6)
Remove-and-append, not edit in place, so every M7-4 pin keeps its ids. QA's first pass found the fix's own tests still
broke on the rows (BUG-1310 / 1311 S2, BUG-1312 S3); fix round 1 closed them. Re-check on a clone carrying docs/03's
M7-4 + M7-6 rows: the only reds are the data track's `Content/**` / `QA/Content/**` pins (D31 moves them) and the two
golden data-hash pins that move once at the landing (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
`QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`). Producer at ACCEPT: default loop green on the three-way tree.
