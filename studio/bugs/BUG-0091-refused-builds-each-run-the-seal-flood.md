# BUG-0091: Refused Builds each run the never-seal flood before the cheap checks; 100 in one tick cost 22 ms

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-2114, task M3-3 |
| System | construction / placement rule (`ConstructionSystem.Check`, `Map/SealCheck`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ConstructionQaTests.UnaffordableBuildsAtALongDetourAnchor_TickCost_Report" --logger "console;verbosity=detailed"`
   (`QA/ConstructionQaTests.cs`): a 128 x 128 flat map with a one-tree wall down x = 64 (gaps at both ends), a House
   anchored in the top gap (its sides meet only round the bottom of the wall), the player at 0 gold / 0 wood, N
   Builds at that anchor applied in one tick.

## Expected
A Build that will be refused for a cheap reason (can't afford, store full, a unit in the way) costs about as much as
the cheap check. docs/03 "Cost": "one `CanPlace` ... 0.05 ms at its slowest" (measured on generated maps only).

## Actual
```
128 map, 1 unaffordable Builds at the long-detour anchor: tick 0.34 ms
128 map, 20 unaffordable Builds at the long-detour anchor: tick 4.51 ms
128 map, 100 unaffordable Builds at the long-detour anchor: tick 22.04 ms
128 map, 4096 unaffordable Builds at the long-detour anchor: tick 907.54 ms
```
`Check` runs `Seal.KeepsConnected` (a flood of up to the whole map) before `UnitInTheWay`, `CannotAfford` and
`StoreFull`, so every refused Build at such an anchor pays a full flood. One Build per selected worker (how the view
will send a group Build) at an unaffordable or unit-blocked spot multiplies it: 20 workers is one 4.5 ms tick, 100 is
22 ms (Debug). A successful placement is cheap for the rest of the group (they join, `SlotAt` only).

## Notes
- `CanPlace` must keep reporting reasons in the documented order, but the apply path (`StartBuild`) only needs
  pass / fail: running the cheap rules first there (or in `Check` when the seal answer isn't needed for the reason)
  removes the amplification. Alternatively refuse further Builds at an anchor already refused this tick.
- Related: BUG-0005 (command flood sort). Same flood on bigger maps (unsupported sizes, a note only): one long-detour
  `CanPlace` takes 0.22 ms at 128, 0.91 ms at 256, 3.6 ms at 512 and 14.6 ms at 1024
  (`ConstructionQaTests.CanPlace_LongDetourYes_ByMapSize_Report`).
