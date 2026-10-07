# BUG-0100: Requirements that can never be met load clean (another faction's building, an any-of only its own tech opens)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-07-1415, task M3-6 |
| System | data loader (requires, requiresAnyOf) |
| Fixed by | |

## Repro
Each loads with 0 errors (pinned in `sim/Rts.Sim.Tests/QA/RequirementQaTests.cs`, `BUG0100_Today_*`; the wanted
behaviour is `ARequirementThatCanNeverBeMet_IsOneError`, skipped):
1. `malazan_heavy_infantry.requires = ["whirlwind_raider_camp"]`. A building requirement is met only by an own
   finished building of that type, and a Malazan player can't place a Raider Camp (`WrongFaction`), so the Heavy
   Infantry is `LockedByRequirement` forever. Same for a faction tech or building naming the other faction's building.
2. `armor_1.requires = ["malazan_barracks"]` (a common tech naming one faction's building): Whirlwind can never research
   Armor 1, nor Armor 2 behind it.
3. `age_ii.requiresAnyOf` = any two of the four halls, and `malazan_barracks`, `malazan_crossbow_range`,
   `malazan_wickan_corral` each `requires: ["age_ii"]`: only the Armory is reachable, so Malazan can never reach Age II.
   The cycle search doesn't follow `requiresAnyOf` edges (the dev's report and docs/03 "Not yet" say so).

## Expected
docs/03 "Implementation (M3-6)": a building requirement is an own finished building of the type. An entry that names
another faction's building (or a common tech naming any faction's building) can never be met, the same failure as the
cycles BUG-0099 made errors. One error at the entry, like the cycle error. For (3), an any-of whose reachable entries
(those not themselves requiring the tech, directly or through others) number fewer than `count` is one error at the
field.

## Actual
All three load; the gates then lock the content for the whole match with no hint in the data errors.

## Notes
- None of this ships (D3's requires are own-faction and reachable; checked with D3's buildings.json in a scratch copy).
  Content authors (the data track) are the ones who'd hit it.
- (1) and (2) are a few lines in `TechReader.Resolve` (the def's faction vs the building's). (3) needs any-of edges in
  `FindCycles` with a count rule, or a reachability pass (a fixpoint from "nothing built, nothing researched").
