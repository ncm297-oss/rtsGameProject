# BUG-0301: A Burning refreshed more often than once a second never deals damage

| Field | Value |
| --- | --- |
| Severity | S3 (no shipped path reaches it today short of ~27 staggered Cadre Mages; every slice-2 zone that reapplies a DoT each tick will) |
| Status | open |
| Found | 2026-10-09-0724, task M4-4a |
| System | statuses (`StatusSystem.Run`, `StatusStore.Apply`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~BurningRefreshedEvery10Ticks_StillBurns10ASecond` after
   removing its `Skip` (`sim/Rts.Sim.Tests/QA/AbilityQaTests.cs`).
2. The test reapplies Burning (10/s, 4 s) to a Heavy Infantry every 10 ticks for 200 ticks.

## Expected
docs/02 "Status effects": Burning is "damage over time"; Telas Fire's is 10 magic damage a second, and "reapplying the same
status refreshes its duration". Ten seconds under constant Burning should deal about 10 pulses (13 each on Heavy).

## Actual
```
took 0 in 10 s of constant Burning
```
A pulse lands only when `TicksRemaining` reaches a multiple of 20 (`left % SimConstants.TicksPerSecond == 0`), and a refresh
sets `TicksRemaining` back to 80. A refresh every < 20 ticks therefore never lets the count reach 60, and the unit burns
forever for nothing. Slower refreshes also lose damage: a refresh every 30 ticks drops the rate to about one pulse per 1.5 s.

## Notes
The pulse clock and the duration are the same counter. A separate per-entry pulse phase (ticks until the next pulse, kept
across a refresh), or pulsing on `(applyTick - now) % 20` from the first application, would keep 10/s under any refresh
cadence. Slice 2's zones ("apply statuses to units inside every tick", docs/02 "Zones") would hit this on every tick, so fix
it before or with zones. Related nit (BUG-0302 c): a duration that isn't a whole number of seconds pulses first after the
fraction, not after a second.
