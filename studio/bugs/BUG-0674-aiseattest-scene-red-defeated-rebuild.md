# BUG-0674: AiSeatTest (view scene) goes red with the defeat rule: its match-over row rebuilds after cancelling the only building

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | view tests (game/tests/AiSeatTest.cs), defeat rule |
| Fixed by | |

## Repro
On the M5-4b branch (4edcf26): `powershell -File tools/qa/smoke.ps1`, then
`powershell -File tools/qa/scene-loop.ps1 -Filter AiSeatTest`.

## Expected
The scene loop stays green after the sim change (acceptance 7: the whole suite green). The sim-side twin of this row
(`sim/Rts.Sim.Tests/ViewApi/MatchOverTests.World_ACancelledLastSite_*`) was updated for the new rule. The Godot row was not.

## Actual
```
AiSeatTest               FAIL  exit 1     9.7 s
    | AI SEAT TEST FAIL: round 2: 0 buildings after the Build, 1 lines
    | AI SEAT TEST FAIL: round 2: 0 buildings after the Cancel, 1 'Match over:' lines
```
`AiSeatTest.MatchOverLine` (`--no-bases`): round 1 builds a Billet and cancels it, which now defeats player 0. Round 2's
`Build` is dropped at the command door (the rule working as designed).

## Notes
A test expectation made stale by the rule, in a view-owned file the sim track can't edit. Fix in the view track: keep a
second building of player 0 standing in that row, or make round 2 expect the rebuild to be dropped (one `Match over:`
line). Merging M5-4b without that fix turns `main`'s scene loop red.
