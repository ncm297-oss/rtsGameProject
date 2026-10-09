# BUG-0134: A building requiring a tech researched only at its own slot (Armory requires Melee Weapons) loads clean and can never be built

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-1715, task M3-H2 |
| System | data loader (requires reachability, `CheckAnyOfReachable`) |
| Fixed by | M4-H1 (sim track): the reachability fixpoint needs a tech's `researchedAt` building and a unit's `trainedAt` building; anything a faction can still never have is one error at the first entry; `QA/RequirementReachQaTests.ABuildingRequiringATechResearchedOnlyAtItself_IsAnError` un-skipped (pin removed) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RequirementReachQaTests.Bug0134Pin"` (passes: it pins
   today's behaviour).
2. By hand: in a copy of the shipped data set `malazan_armory.requires = ["melee_weapons_1"]` (or
   `["moranth_supply"]`). Both techs have `researchedAt: "forge"`, and the Armory is Malazan's only forge.
3. `DataLoader.LoadAll` → 0 errors.

## Expected
docs/03 "Unmeetable requirements (M3-H2, BUG-0100)" and BUG-0100's intent: a requirement that can never be met is a
load error. A Malazan player can't place an Armory until Melee Weapons is researched, and can't research it without a
finished Armory, so the Armory, every forge tech, and everything behind them are locked for the whole match.

## Actual
Loads clean. The reachability fixpoint makes a tech reachable once its `requires` and any-of are, without asking
whether a building of its `researchedAt` slot is reachable for the faction (and a unit's `trainedAt` likewise is never
checked), so this loop is invisible to it. `FindCycles` doesn't follow `researchedAt` either.

## Notes
- None of this ships (D3's requires don't do it). A content author hits it the first time a building is gated behind
  an upgrade of its own slot.
- Fix: in `CheckAnyOfReachable`'s fixpoint, a tech is reachable for faction f only if f's building of its
  `researchedAt` slot is reachable; afterwards report any building / tech / unit left unreachable that no other error
  explains (one error each, or at the first entry on the loop). Then flip the pin to the skipped
  `ABuildingRequiringATechResearchedOnlyAtItself_IsAnError`.
- Not in M3-H2's listed cases (cross-faction, common tech naming a faction building, any-of): S3, not a criterion miss.
