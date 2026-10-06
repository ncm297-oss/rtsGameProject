# BUG-0086: M2-3b nits: every relist uploads every type's full 4,096-instance buffer; tight minimap perf margin

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-1503, task M2-3b |
| System | PropsView, MinimapRaster perf row |
| Fixed by | |

## Repro
1. Read `game/scripts/PropsView.cs` `Sync`: on any `NavGrid.Version` change it loops over every type, copies that
   type's transforms into one shared `float[capacity * 12]` and assigns the whole array to `MultiMesh.Buffer`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PropsMeasureTests" --logger "console;verbosity=detailed"`.

## Expected
A felled tree re-uploads only what changed, sized to what is drawn; perf rows keep a margin under their limits.

## Actual
- One felled tree re-uploads both the tree and the mine MultiMesh, each at full store capacity (4,096 x 12 floats =
  192 KiB per type, marshalled to a native PackedFloat32Array each time) even when 8 mines are live. Harmless today
  (nothing fells in the live game until M3-2), but with gathering every felled tree pays for it.
- The shared buffer is not cleared between types, so the mine MultiMesh's hidden instances (past
  `VisibleInstanceCount`) hold the tree transforms just copied; if Godot sizes the MultiMesh AABB over all instances,
  the mine layer's bounds span the whole forest set (culling never helps; no visual effect).
- `MinimapRefresh_2000Units_4096Nodes_ResourceRedraw_Under0_3Ms` measured avg 0.255 ms against its 0.3 ms limit
  (worst 0.886 ms) on an idle machine: about 15% headroom, a likely flake under the other track's load.

## Notes
Possible fixes: upload only the types whose count or slots changed; set `InstanceCount` to the live count (or keep a
per-type buffer); the perf note is a watch item, not a request to loosen the threshold.
