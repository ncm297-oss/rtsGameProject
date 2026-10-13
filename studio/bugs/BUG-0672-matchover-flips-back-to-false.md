# BUG-0672: Simulation.MatchOver goes back to false when a player that never built places its first building

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | defeat rule (World.MatchOver) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DefeatRuleQaTests.MatchOver_OnceTrue_StaysTrue_WhenAPlayerThatNeverBuiltBuildsLater"`:
three players. Player 0 has a hall, player 1 a site, player 2 only a worker. Player 1 cancels its site: `MatchOver` is
true. Player 2 then places a House site: `MatchOver` is false again.

## Expected
`Simulation.MatchOver`'s own summary says "True once the match has a result". A result that the view puts on a banner
(M6-V4) should not un-happen.

## Actual
`MatchOver went back to false after a result: player 2 built its first building`. `World.MatchOver` is recomputed from
`EverBuilt` / `Defeated` on every read, and a new builder raises the count of players that ever built.

## Notes
Skirmishes start every seat with a hall (all have built from tick 1), so this needs a units-only seat (a scenario, a dev
setup, `--no-bases`). Fix options: latch the result (a sticky flag set by `UpdateDefeats`), or document the non-sticky
meaning. Also worth a docs line: in that 3-player setup the match "ends" while player 2 still has a whole army and no
building.
