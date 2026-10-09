# BUG-0310: A building destroyed in plain sight is drawn as a ghost over visible ground until the next fog update

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-09-0724, task M4-V5 (view track) |
| System | fog view: `FogView.CollectGhosts`, `BuildingViews` ghost pool, `TargetRing` |
| Fixed by | |

## Repro
1. Un-skip `sim/Rts.Sim.Tests/QA/ViewApi/GhostQaTests.cs` `ABuildingDestroyedInSight_IsNeverAGhostOverVisibleGround`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~IsNeverAGhostOverVisibleGround"`
   -> `a ghost drawn over visible ground for 3 ticks after the building died in sight`.

Scene: an enemy Tent in a spotter's sight (entry known, not a ghost). The Tent dies between fog updates (tick % 4 != 1).

## Expected
docs/02 "Vision and fog of war": ghosts are last-known buildings "in explored fog". A building the player watches die
should disappear (the live box goes) and never come back as a darkened ghost over visible ground.

## Actual
Combat deaths happen in phase 11 and the fog (with the last-known list) updates in phase 12 only every 4th tick, so on
3 ticks out of 4 the sim's entry outlives the building until the next update. `CollectGhosts` marks every known entry
that is not drawn as a ghost (`g.Known && !drawn`), so for 1-3 ticks (50-150 ms) `BuildingViews` draws the darkened
box exactly where the building just died, in full view. Same effect when:
- a site is cancelled in sight;
- a slot freed in sight is reused before the next update (old ghost box overlapping the new live box);
- the `TargetRing` on a building that dies in sight: `ghost` is true for those ticks, so `Mark.Clear()` is skipped and the
  ring stays on the dead building's footprint until the update;
- a right-click in that window is an Attack on the dead handle, which the sim drops (the unit sees the footprint).

This matches the acceptance wording ("ghosts drawn == sim unseen entries") but not the design rule.

## Notes
Suggested fix (view only): in `CollectGhosts`, also skip an entry whose remembered footprint has any cell in the
visible state now (`fog.IsVisible(Player, cell)` over the footprint; the same rule the sim uses to drop the entry at
the next update, `SeesFootprint`). Cost: a footprint scan per known entry per tick. The fuzz row
`Ghosts_EqualTheSimsUnseenEntries_EveryTick_...` then needs its oracle updated to the same rule.
