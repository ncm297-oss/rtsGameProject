# BUG-0410: the description pin accepts a stale number that happens to equal another data number

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-10-0624, task D10c |
| System | content tests (`Content/AbilityContentTests.DescriptionProblems`) |
| Fixed by | D-H2 (2026-10-10-1015, data track): `DescriptionProblems` checks each number against the field its words name; `QA/Content/SandstormBlindedPinQaTests.ADescriptionNumber_InTheWrongRole_Fails` un-skipped (4 / 4 fail naming entry, number, role), plus `Content/AbilityContentTests.ADescriptionNumberInTheWrongRole_FailsNamingEntryNumberAndRole` and `TheWordsAroundADuration_ChooseItsField` |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.Content.SandstormBlindedPinQaTests.ADescriptionNumber_InTheWrongRole_Fails`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ADescriptionNumber_InTheWrongRole"`.

The theory edits the `sandstorm` description in memory: "for 12 seconds" -> "for 18 seconds" / "for 45 seconds", and
"Slowed by 30%" -> "Slowed by 45%" / "Slowed by 12%".

## Expected
Each fails naming `sandstorm description`: the duration the text states is not the data duration (12 s), the slow it
states is not the data magnitude (30%). The brief's constraint is "numbers in descriptions must match data".

## Actual
All four pass (4 / 4 fail when un-skipped, i.e. the assertion finds no problem). `DescriptionProblems` collects every
data number of the entry (range, radius, cast, cooldown, duration, amounts, magnitudes, percentages, status durations)
into one list and accepts any description number found in it, so a number in the wrong role passes whenever it
collides with another field. Sandstorm alone has 18, 6, 1.2, 45, 12, 30, 0.3, 1 in its list.

## Notes
Outside the D10c criteria (no criterion asked for role-aware description numbers; the developer documents the pin as
"each number is one of its own data numbers"). Reported because the brief says a text claim not pinned is a gap to
report. Possible fix: bind the common shapes to their field ("for <n> seconds" -> duration, "<n>%" after a status name
-> that status's magnitude, "<n> <type> damage" -> a damage amount, "<n> m" in a status -> sight / reach), and keep the
bag check only for numbers no shape claims.

## Fix (D-H2)
Roles by the words after (and before) each number: "s" / "second(s)" is a duration, refined by "cooldown" / "recharge(s
in)" / "every" (cooldown), "to cast" / "cast time" (cast), "linger" / "after leaving" in the clause (a zone status's
duration), else the ability's duration or an applied status's; "m" / "metre(s)" a distance (range, radius; a status's
sight, reach); "%" a magnitude x 100 or the friendly fire x 100; "<n> [type] damage" an amount, "... damage a second" /
"damage/s" a damage-over-time magnitude. A number no shape claims keeps the old any-field check. A status in
`PendingStatuses` reports its description problems and never fails.
