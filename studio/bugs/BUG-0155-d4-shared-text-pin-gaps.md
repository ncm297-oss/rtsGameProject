# BUG-0155: D4 shared-text pin gaps: H's name check is case-sensitive and skips faction tech names; age_ii sits at 158 of 160 chars

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-07-2014, task D4 |
| System | content tests (`sim/Rts.Sim.Tests/Content/TechContentTests.cs` H) |
| Fixed by | 3f70d8c (D5): `TechContentTests.H` compares OrdinalIgnoreCase and forbids faction tech names; QA 2026-10-08-0913 verified "moranth supply" and "caster hall" in a short shared description now fail H naming the name |

## Repro
Scratch clone of 338da1e, one edit at a time to `game/data/common/techs.json` `age_ii.description`, then
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~TechContentTests"`:
1. "Unlocks your caster, siege ..." -> "Unlocks your caster hall, siege ...": H fails only with `age_ii: 163 chars`.
   Its name check is `StringComparison.Ordinal`, so a lower-case "caster hall" passes it.
2. "level II and faction upgrades" -> "level II upgrades and Moranth Supply": H fails only with `age_ii: 165 chars`
   (G fails because "faction upgrade" is gone). H's forbidden list has building, slot, unit and faction names but not
   the faction techs' names, so "Moranth Supply" in a shared tech passes the name check.

## Expected
H's comment: "neither a building's name nor docs/02's slot name ... nor any unit or faction name" may appear in a
shared tech's text. A faction's own tech name is just as faction-specific, and the check should not depend on case.

## Actual
Both edits are caught only because the shipped `age_ii` description is 158 characters long, 2 below H's 160 limit. A
shorter description with the same leak would pass H. The leaks are caught now by the QA rows in
`QA/Content/SharedTechTextQaTests.SharedTechText_NamesNoFactionThing_InAnyCase` (case-insensitive, faction tech names
included), so this is about H's message and coverage, not about shipped text.

## Notes
- Also in the group: with 158 / 160, any later addition to Age II's text (e.g. once towers get their M4-3 attack) hits
  the card limit at once. That's a heads-up for the next writer, not a defect.
- Pre-existing and outside BUG-0132's scope: `TechLoaderTests.ForgeUpgrades_MatchDocs02` (the sim's test) still fails
  a data-only cost edit with a bare `Assert.Equal() Failure: Values differ`. The content pin F beside it names both
  values, so the owner still gets a readable message.
- Fix (data track, test-only): add `Data.Techs.Where(t => t.Faction >= 0).Select(t => t.DisplayName)` to H's list and
  compare with `OrdinalIgnoreCase`.
