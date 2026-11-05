# BUG-1051: M6-S6 starting-resources nits: a stale SaveSummary doc line, the AI stress test's exemption list hides the opening it is about, docs/01 has no row

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-1206, task M6-S6 (QA) |
| System | save format docs (`ViewApi/SaveSummary`), `Stress/StartingResourcesAiStressTests`, docs/01 |
| Fixed by | |

## Repro
1. `sim/Rts.Sim/ViewApi/SaveSummary.cs` line 19: `<param name="FormatVersion">The header's format version (any; only <see cref="SaveFormat.CurrentVersion"/> loads).</param>`.
2. `sim/Rts.Sim.Tests/Stress/StartingResourcesAiStressTests.cs` `KnownOverTwice` row `(2, 0, 0)`.
3. `docs/01-vision.md` decisions table: no row for the starting-resources presets.

## Expected
1. Since M6-S6 a format-3 save loads too (`SaveFormat.OldestReadVersion`); the Load Game list's reader doc should say "3 or the current one load".
2. The exemption list holds sides that over-shoot the measure on noise; the one side that over-shoots because it really holds
   the start (`cadre_rush` v Swarm seed 13: 1,515 at 4:00) should not be silenced as noise.
3. The Producer default (standard 200 / 200, high 1,000 / 1,000, 5 workers both, owner may revisit) is a decision a later
   session needs to find: docs/02 records it inline; docs/01's table is where dated decisions live (the dev listed it as
   not done).

## Actual
1. The doc line says only the current version loads. (The view owns `ViewApi/`; the sim track must not edit it, so this
   rides with the view's setup task.)
2. `(2, 0, 0)` is listed beside the four fight's-ebb sides with the same treatment, so the test passes on the one side
   BUG-1050 is about.
3. No row.

## Notes
- Item 2 is moot once BUG-1050's measure is restated; item 1 belongs to the view's M6-S6 follow-up (`SimRunner` start and
  `StartPlayback` must also pass the preset, the dev's request).
