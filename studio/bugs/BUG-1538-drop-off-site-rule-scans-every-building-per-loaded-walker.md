# BUG-1538: While any drop-off site stands, every loaded walker scans every building slot every tick (+1 ms a tick for 400 walkers)

| Field | Value |
| --- | --- |
| Severity | S3 (a real per-tick cost of the BUG-1520 fix; within the 4 ms budget in QA's scene but about 6x that scene's tick, and it applies to every player whenever any player has a drop-off site) |
| Status | fixed (M6-SH2 fix round 1, verified by QA re-check 1) |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | sim: economy (`EconomySystem.Step` -> `WalksToSite` -> `InReachOfDropOff(..., finished)`) |
| Fixed by | M6-SH2 fix round 1 (`World.DropOffSites`) |

## Repro
1. Un-skip `sim/Rts.Sim.Tests/Stress/DropOffSiteScanQaStressTests.FourHundredLoadedWalkers_ASiteStanding_CostsLittle`
   and run it alone: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DropOffSiteScanQaStressTests"`.
2. Measured on the M6-SH2 tree, in QA's Perf pass with nothing else of QA's running: `400 loaded walkers, 61 buildings:
   mean tick 0.175 ms with no site, 1.167 ms with one drop-off site standing (+0.991 ms)`. The same scene on QA's scratch
   copies: base `fdc8710` 0.184 -> 0.185 ms (+0.001 ms); base + item 0 only 0.187 -> 1.149 ms (+0.962 ms).

## Expected
The walking re-pick is for the rare walker that was heading to a site when the rule landed (a save from before it). It
should cost next to nothing in a normal tick. The developer's comment in `EconomySystem.Run` says "one scan a tick, so the
walking re-pick below costs nothing while no drop-off is being built".

## Actual
`Run` gates the re-pick on `AnyDropOffSite` (any player's site), and the AI builds Camps and halls all match long, so the
gate is often open. While it is open, every loaded walker (`Moving` and `full`) calls `WalksToSite` on every tick. That
call runs `InReachOfDropOff(owner, goal, finished: false)`, a scan of all `BuildingCapacity` slots (256 by default) with a
`BuildingDef` lookup and an `InReach` test for each. A walker heading to a finished drop-off that is not beside a site
pays the full scan, and `&&` skips the second scan only when the first finds nothing. 400 walkers x 256 slots is about
100k slot checks a tick.

## Notes
- Cheaper shapes that keep the rule: run the check once, when a loaded walker's walk starts or when it is loaded
  (the only case is a save from the base), or gate it per owner and only for walkers whose goal cell lies next to a site.
  Any of these fits rule 5 (no allocation, bounded scans).
- No Perf threshold covers this: the existing gather Perf rows have no drop-off site standing.

## M6-SH2 fix round 1 (the developer): fixed
`EconomySystem.Run`'s one scan a tick lists the drop-off sites in `World.DropOffSites` (scratch, not saved); a loaded
walker tests only those. `Stress/DropOffSiteScanQaStressTests` un-skipped: +0.021 ms a tick (was +0.99 ms). No behaviour change.

## QA re-check 1
Verified: the row passed inside QA's Stress run beside the default loop (+0.096 ms; 6.1 ms ticks), and alone in the Perf
run (see the re-check report). QA moved its own row to the Perf category (a wall-clock row runs alone, rule 42) and made it
take the least of five interleaved samples (was two); the 0.25 ms bound is unchanged.
