# BUG-1330: The description-number rule can't tell the Frenzy row's two 20%s apart; a moved damage bonus or aftermath leaves the tooltip stale with every pin green

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-13-1118, task D30 |
| System | content pins (`Content/AbilityContentTests` description-number rule, D-H2) |
| Fixed by | |

## Repro
1. In `sim/Rts.Sim.Tests/QA/Content/D30LandingQaTests.cs`, remove the `Skip` from
   `Mutant_OneOfTheTwo20s_Moved_TheStatusTooltipIsNamed`.
2. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~D30LandingQaTests.Mutant_OneOfTheTwo20s`

## Expected
D30's goal is that the content pins hold the Frenzy row's player-facing words to its numbers both ways (docs/03 "For
the data track (M7-4)": "a text needs a pin taught about them"). On the landing copy the `teblor_frenzied` description
reads "Attacks faster and deals 20% more damage. When it ends, the unit is Slowed 20% for 4 seconds." Moving either
number alone in the data (a balance pass setting `onEnd.magnitude` 0.3, `damageBonus` 0.3, or dropping `damageBonus`)
should make `DescriptionProblems` name `teblor_frenzied description`.

## Actual
All three cases pass the description rule (3 / 3 failures with the skip removed: "no problem containing
'teblor_frenzied description:'"). `AddRowNumbers` puts `damageBonus x 100` and the `onEnd` magnitude into the same
`Role.Percent` list, so the "20%" in the text is accepted as long as either field is still 0.2, and nothing requires each
data number to be stated. The page pins (`RuleRowClaims`, the Abilities Effect cell, docs/02's row) do catch the
data change, so the page is safe; the in-game tooltip is the part nothing holds.

The developer reported half of this (the `damageBonus` 0.25 case) as a known gap. QA confirms it covers both fields and
the removal case too, and that a balance pass changing either number will leave the player-facing tooltip wrong.

## Notes
- A fix in the pins: read the phrase around the number (e.g. "N% more damage" for `damageBonus`, "<slow name> N%" or
  "Slowed N%" for an `onEnd` slow), or require every row number to be stated in the row's own description.
- Only in test code (`Content/**`); no shipped file. Best before D31 writes the description into `statuses.json`.
