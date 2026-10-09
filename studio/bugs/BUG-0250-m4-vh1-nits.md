# BUG-0250: M4-VH1 nits: a skipping tracker still misses a collinear same-target lob reuse (docs/03 says "from elsewhere starts over"); `Straddle` copies QA's 512-step oracle; the double-Cancel guard outlives a match

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-2144, task M4-VH1 (QA) |
| System | view: `sim/Rts.Sim/ViewApi/ProjectileTracker.cs`, `TerrainHeight.cs`; `game/scripts/SelectionController.cs`; docs/03 |
| Fixed by | |

## Item 1: collinear same-target lob reuse, skipping observer
Repro: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~LobReuseHostileQaTests.CollinearReuse" --logger
"console;verbosity=detailed"`. Three slots, sharpers of one owner into one impact point, every launch on one line due
west of it, an observer that looks on 30 % of ticks. 5 of about 3,300 lob observations per seed keep the earlier
shot's launch:
- seed 5, tick 268: true launch (47.97, 60), tracked (61.71, 60), position (48.57, 60). The tracked launch is ahead of
  the stone, so the arc is drawn from a point the stone has not reached.
- seed 6, tick 577: true launch (64.69, 60), tracked (68.72, 60), position (67.69, 60).

The new off-line test (BUG-0222 item 1) can't see a shot on the old line, and the "nearer its launch than before" test
compares with the old shot's last distance. Off the line it holds: 0 stale in about 3,400 observations per seed on four
seeds (`RandomLaunches_...`). An every-tick observer never restarts a real flight, including at (1020, 1010) on a
1024 m map (`FarCoordinates_...`). **The Match observes every tick, so the game can't reach this.** docs/03 now says
"a same-target lob fired from elsewhere starts over". Add "unless it is on the old shot's line" to that sentence, or add a
check that the position lies between the recorded launch and the impact point (along in [0, 1]).

## Item 2: `TerrainHeight.Straddle` copies the QA oracle
`Straddle(radius) = 0.05 + min(r, 2) / 512 x 2 x (LevelHeight / CellSize)`. The second term is the per-step slope
allowance of the 512-step reference walk in `MaxUnderEdgeWalkQaTests`, and the comment says so ("so the two agree on
which side-wall feet are walls"). `Joined` compares exact heights at the crossing point, so a step count means nothing
there. The term adds at most 8.6 mm and changes no result QA measured: 16 unseen seeds, 0 sinks, worst hang 0.225 m,
under the oracle's 0.25 m (`MaxUnderBandsQaTests`). It ties production code to a test's discretisation, though. A
plain constant with its own reason would read better. The 0.05 m uncapped band lifts a disc whose centre is within about
0.1 m of a ramp's foot line up to 0.46 m over strict "every step is a wall" ground (seed 404, (77.997, 144.075), r
1.08: the rim lies over the ramp beside its foot). That is the designed BUG-0190 float, joined through the foot, and is
noted here only.

## Item 3: the double-Cancel guard survives a new match
`SelectionController.CancelSelectedSite` drops a press when `(TickNumber, slot, generation)` equals the last Cancel's.
Those fields are not reset when `_runner.Simulation` is replaced. On a new match, a site in the same slot with the same
generation, cancelled at the same tick number as the last Cancel of the previous match, would have its first press
swallowed. This is theoretical: it needs `SimRunner.Start` to run again under the same `SelectionController`, and no
game path does that today (`Main` starts one Match). It becomes reachable once a "restart match" keeps the scene.
Keying on the `Simulation` instance as well, or resetting the fields when the instance changes, closes it.

## Expected
Docs describe what the code does. Production constants have their own reasons. A view-only guard is cleared with the
match it belongs to.

## Notes
None of these is reachable in normal play, and none blocks M4-VH1.
