# BUG-0099: M3-5 tech data nits: requires cycles, filters matching no unit, a tech id equal to a building id

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1 and 3 in 8928418, M3-6; item 2 in 3f494c0, M3-H2) |
| Found | 2026-10-07-1131, task M3-5 |
| System | data loader (techs.json, requires) |
| Fixed by | items 1 + 3: 8928418 (M3-6), `QA/TechDataQaTests.ARequiresCycle_IsOneError` un-skipped, `ATechIdEqualToABuildingId_IsOneError`; QA `RequirementQaTests` cycles of length 1-4. Item 2 (an effect matching no unit) not in M3-6 scope; item 2: 3f494c0 (M3-H2), an effect matching no unit (any faction for a common tech, own faction for a faction upgrade) is one error at `appliesTo`: `QA/TechDataQaTests.AFilterThatMatchesNoUnit_IsOneErrorAtAppliesTo`, `AFactionUpgradeFilterMatchingOnlyAnotherFactionsUnits_IsOneError` (verified QA 2026-10-07-1715) |

## Repro
Each one loads with 0 errors (pinned in `sim/Rts.Sim.Tests/QA/TechDataQaTests.cs`):
1. **`requires` cycles.** `melee_weapons_1.requires = ["melee_weapons_2"]` (and `melee_weapons_2` already requires
   `melee_weapons_1`), or a tech requiring itself. `BUG0099_Today_ARequiresCycle_Loads`. The wanted behaviour is
   `ARequiresCycle_IsOneError` (skipped, BUG-0099).
2. **An effect that matches no unit.** `appliesTo: { "attackType": "magic", "siege": true }`: the tech can be
   researched and paid for and changes nothing. `AFilterThatMatchesNoUnit_Loads_AndTheTechChangesNothing`.
3. **A tech id equal to a building id.** A tech `malazan_armory` loads next to the building `malazan_armory`, so a
   `requires: ["malazan_armory"]` entry is ambiguous. `BUG0099_Today_ATechIdEqualToABuildingId_Loads`.

## Expected
1. With M3-6 gating, a cycle makes every tech in it unresearchable forever, and so does any unit or building that
   needs one of them. The loader should report a cycle once, at the `requires` entry that closes it.
2. An effect whose filters match no unit type is almost certainly a typo. It could be one error, or a warning if the
   loader ever gets warnings.
3. `requires` names "a tech or building id", so the two id spaces should not overlap: a tech id that equals a
   building id should be one error.

## Actual
All three load clean. M3-5 only checks that each `requires` id exists (that was the brief), so 1 and 3 belong with
the M3-6 `requires` resolution work.

## Notes
- Fold 1 and 3 into M3-6, where `requires` gets resolved to ints anyway.
- Note for M4 (not a bug yet): docs/02 says Ranged Weapons gives "+1 / +2 attack for pierce units **and towers**",
  but `World.TechBonus(player, unitType, stat)` only covers unit types. Towers (buildings) will need a building-type
  query or a tower path when M4 gives buildings an attack.
