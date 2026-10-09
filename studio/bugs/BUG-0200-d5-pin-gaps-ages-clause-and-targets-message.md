# BUG-0200: D5 pin gaps: docs/02 Ages trailing clause is unpinned; explicit `"targets": "all"` fails with a "page" message

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-08-0913, task D5 |
| System | content tests (`sim/Rts.Sim.Tests/Content/TechContentTests.cs` G, `UnitContentTests.cs` C) |
| Fixed by | D6 9e73f49 (data track, session 2026-10-08-1814): `TechContentTests.G` pins the trailing clause (`AgeClause`: n halls, or n - 1 halls and the Forge, against `age_ii.requiresAnyOf`; QA's `AgesRuleQaTests` row kept as the independent oracle); `UnitContentTests.C` reads "written out although it is the default (style rule ...)" for an explicit `"targets": "all"`. Residual: an appended contradicting sentence after the clause still passes (BUG-0230 item 1, S4) |

## Repro
Scratch clone of 3f70d8c, one edit at a time, then `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Content"`:
1. `docs/02-game-design.md` "Ages": "two production halls, or one hall and the Forge." ->
   "three production halls, or the Forge alone." All 123 content tests pass (before QA's new row).
2. `game/data/factions/malazan/units.json` `malazan_laborer.attack`: add `"targets": "all"` (same behavior as absent).
   C fails with `malazan_laborer file attack.targets: page (absent) vs data all`.

## Expected
1. D5 added the clause to docs/02 as the plain-words form of the any-two-of rule; like the rest of the paragraph it
   should be pinned to `age_ii.requiresAnyOf`, so a wrong rule sentence fails a test.
2. The "file attack.targets" pin is a style rule (write the field only when it narrows), not a page value; its
   message should say so, e.g. "write targets only when it is not 'all'".

## Actual
1. G's regex stops at the "):" after the slot list; the clause after it is free text. Now covered by
   `QA/Content/AgesRuleQaTests.AgesTrailingClause_MatchesTheAnyOfRule` (fails on edit 1 naming the page text, and
   on "three production halls, or one hall ..." with `age_ii Ages clause halls: page three vs data two`).
2. The message says "page (absent)" although no page says anything about the file field.

## Notes
- Neither is shipped-content wrong today: the clause matches data (count 2 of the three halls + Forge), and no
  file writes an explicit `"all"`.
- Optional fold-in for the data track: move the clause check into G and reword C's file-field message.
