# BUG-0190: M4-V2 nits: corpse discs on a ramp are half buried; ResolveEnemy takes a NaN unit entry as "no unit"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (set by the Producer at the 2026-10-08-2144 PLAN; residual: BUG-0226 item 1) |
| Found | 2026-10-08-0913, task M4-V2 |
| System | combat views (`CombatViews` corpse markers), `ViewApi.UnitPicker.ResolveEnemy` |
| Fixed by | M4-V3 (session 2026-10-08-1814, on `main` 853a60c): item 1 `ViewApi.TerrainHeight.MaxUnder` (the disc sits on the highest ground under its rim joined to the centre; `CombatViewTest` corpse-underside row, QA's `CorpseDiscRimQaTests` / `MaxUnderEdgeWalkQaTests`); item 2 `UnitPicker.ResolveEnemy` counts a NaN entry as nearest (`UnitPickerTests` NaN row). Residual ≤ ~0.5 m near a ramp's foot beside its side wall: BUG-0226 item 1 |

## Repro
1. **Corpse on a ramp.** `& $env:GODOT --path game res://tests/CombatViewTest.tscn -- --seed 1 --shots <dir>`, open
   `combat-seed1-corpses-close.png`: of the six gold (Whirlwind) corpse discs placed along the row, the two that fall
   on the ramp's edge show as half-moons. The other half is under the sloped terrain. The developer noted it and did not
   file it. A disc is a flat cylinder placed at the terrain height of its centre (fill 0.08 m, rim 0.06 m high), so
   wherever the ground rises across its radius it sinks into the slope.
2. **NaN unit entry.** `UnitPickerQaTests.ResolveEnemy_BadSlots_DeadEntities_Ties_AndNaN` prints
   `NaN unit entry with the Tent at 9: resolves True (building True)`. When an own unit is passed with entry NaN and
   the enemy Tent with entry 9, `ResolveEnemy` names the Tent. The comparison `unitT <= buildingT` is false for NaN, so
   the own unit counts as "no unit" and the building behind it wins. `PickRay` never returns NaN for a hit (its entry is
   finite or +infinity), so the shipped path can't reach this today. It is an unguarded public input.

## Expected
1. A corpse disc reads whole on sloped ground. It could sit at the highest terrain sample under its radius, or be
   tilted to the terrain normal. 2. A NaN entry for a live unit counts as nearest (no target) or is rejected, so a
   building can never be picked through a unit the caller passed.

## Actual
As above. Neither breaks an acceptance criterion: corpses are readable by team on flat ground, at zoom 14 and at zoom 30.

## Notes
- A cheap fix for (2) is `!(unitT > buildingT)` style comparisons, or an early `if (float.IsNaN(unitT)) unitT = 0f`.
- Not a bug, for the record: after a Shift-queued triple Attack and then Stop, idle units with an enemy within sight
  take a new target by themselves (`CombatMode.Retaliate`). That is the M4-1 idle scan, not a leftover of the queue
  (`QaV6Test` prints them).
