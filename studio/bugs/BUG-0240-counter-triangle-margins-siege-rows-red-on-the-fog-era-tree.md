# BUG-0240: Content/CounterTriangleMarginsTests is red on the fog-era tree: its siege copy has no spotter, so every Attack is dropped

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-2144, task M4-H1 (sim track, while working BUG-0230 item 2) |
| System | content test (data track): `sim/Rts.Sim.Tests/Content/CounterTriangleMarginsTests.cs` `TimeToKill` |
| Fixed by | D7 (data track, 7576d4b): `Content/CounterTriangleMarginsTests.TimeToKill` gained the one `Spotter(...)` line of `Scenario/CounterTriangleTests.cs:111`; green on the 2026-10-08-2144 integration (QA: non-Perf 4041 / 0 failed). D8 switches the copy to `CounterTriangleScene` (BUG-0230 item 2) |

## Repro
1. On the session base `4c1f168` (M4-3a + main), unchanged:
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CounterTriangleMarginsTests"`.
2. Fails: "The doc's winner does not win: Siege beats buildings: 1 malazan_catapult not done is not faster than 5
   malazan_heavy_infantry not done on a whirlwind_tent" (and the same for the ram on a Billet).

## Expected
The row passes, printing the same siege numbers as `Scenario/CounterTriangleTests` (13.8 s ram, 17.0 s catapult).

## Actual
The data track's copy of the siege scene (BUG-0230 item 2's "unpinned copy") predates M4-3a. M4-3a drops an explicit
Attack on a building its owner can't see, and the attackers start out of sight of it; `Scenario/CounterTriangleTests`
gained a spotter beside the building in M4-3a, the copy did not. Every Attack is dropped, nothing is destroyed in
6,000 ticks, and the row fails. It is red on the sim branch's full non-Perf run besides BUG-0219's known row.

## Notes
- Exactly the drift BUG-0230 item 2 warned of. The sim side of the fix landed in M4-H1: the public harness
  `Rts.Sim.Tests.Scenario.CounterTriangleScene` (`Fight`, `TimeToKill(attacker, n, building, max = SiegeMaxTicks)`,
  `Side`, `SameCostCount`, the scene constants), which `Scenario/CounterTriangleTests` now calls, its printed lines
  unchanged. Fix (data track, `Content/` is theirs): call `CounterTriangleScene.Fight` / `TimeToKill` instead of the
  copies.
- S2 because it reds the gate suite on `main` once this tree merges; the data track's D7 may already carry a fix.
