# BUG-1920: The limit reader still reads some target counts as a unit limit (an alive word after the noun or later in the clause)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-27-0817, task D-H11 |
| System | content checks (`sim/Rts.Sim.Tests/Content/UnitLimitText.cs`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH11LimitReaderQaTests"` with the `Skip` removed from
   `ATargetCountFollowedByAnAliveWord_ReadsNone` (`sim/Rts.Sim.Tests/QA/Content/DH11LimitReaderQaTests.cs`).
2. Or call `UnitLimitText.Stated(text, "Hound of Shadow")` on these texts.

## Expected
BUG-1880 / D-H11 brief: a count of targets or projectiles is not a unit limit. All of these are target counts and
should read none.

## Actual
| Text | Read |
| --- | --- |
| "Bites up to 3 foes living nearby." | 3 |
| "Heals up to 3 allies living nearby." | 3 |
| "At most 2 enemies remain alive after its bite." | 2 |
| "Up to 3 are hit at once while it is alive." | 3 |

On a unit with no `maxAlive` each one gives `<id> description maxAlive: 'up to 3' reads as the unit limit, data none`,
so a writer gets a false red (it fails loud, it doesn't pass silently). No shipped description hits it today.

## Notes
- Cause 1: in `CountsTheUnit`, "another noun then said to live" accepts any noun followed by an alive word (after
  linking words, and `remain` is a linking word), including when the unit's name is known and the noun isn't it.
- Cause 2: after a linking word ("are", "is", "be") the code checks `aliveAfter` over the whole rest of the clause, not
  for an alive word next to the number, so "Up to 3 are hit ... while it is alive" counts.
- Related wordings, not filed as bugs because they're ambiguous or the brief allows them: "bites up to 3 of them at once"
  reads 3 (the brief lists "of them" as a unit-count marker); "Summons up to 3 wolves alive at once" reads 3 (a summon
  cap); on the Hound, "Bites up to 3 Hounds at once" reads 3.
- Missed limits (fail loud, documented by the developer): "seven"; "Up to 7 at a time can live"; "No more than 7, all
  told, can live at once" (the comma cuts the clause); a "Wolf" written "Wolves" (only `+s` / `+es` plurals match).
