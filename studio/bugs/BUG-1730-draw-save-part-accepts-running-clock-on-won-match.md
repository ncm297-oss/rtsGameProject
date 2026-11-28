# BUG-1730: A save's `DRAW` part with a running clock on a won (latched, not drawn) match is accepted

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-11-25-1437, task M6-S13 (QA) |
| System | save/load (draw rule, `World.ReadDraw`) |
| Fixed by | M6-SH3 D (`World.ReadDraw`: the not-drawn branch needs `!_matchOverLatched`; `QA/DrawRuleQaTests.AForgedClock_OnAWonMatch_IsRefused` un-skipped) |

## Repro
1. Un-skip `QA/DrawRuleQaTests.AForgedClock_OnAWonMatch_IsRefused`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DrawRuleQaTests.AForgedClock_OnAWonMatch_IsRefused"`
3. The test saves the staged spent match at clock 10, sets the result-latch byte just before the `DRAW` tag to 1 (a won
   match), re-checksums, and loads.

## Expected
docs/03 "Implementation (M6-S13)" and the developer's report promise a strict reader ("strict refusal of forged values").
The sim never writes this state: on the tick a match is won, phase 11c sees a match that is over, so the clock goes back to 0.
A part with clock > 0, not drawn, on a latched match should be refused as `Malformed`, like the other forged parts in
`Draw/DrawSaveReplayTests.AForgedDrawPart_IsRefused`.

## Actual
`SaveError.None`. The world loads with `MatchOver` true, `MatchDrawn` false and `DrawClock` 10. Its state hash differs from
any honest world (bit 37 and the clock are hashed). The next tick puts the clock back to 0, so nothing worse follows.

## Notes
`World.ReadDraw` checks `drawn ? clock == wait && _matchOverLatched : clock < wait`. The not-drawn branch does not require
`!_matchOverLatched`. The fix is one more condition, `drawn || !_matchOverLatched`, plus this test un-skipped.
