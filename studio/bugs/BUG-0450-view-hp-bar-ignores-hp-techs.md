# BUG-0450: Hp bars and the first-sight hit flash still use the type's hp as the maximum, so an upgraded hurt unit shows no bar

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-1314, task M4-4b-3b (QA, sim track; the code is the view track's) |
| System | view: `ViewApi.UnitHpBars`, `ViewApi.HitFlash`, `game/scripts/CombatViews.cs`, `game/scripts/UnitViews.cs` |
| Fixed by | 273d56b (M4-V7b, view branch): per-slot `World.MaxHp` via `UnitViews.FillMaxHp`, `UnitHpBars.Collect(shown, hp, maxHp, slots)`, `HitFlash.UpdateWithSlotMax`; regression `QA/ViewApi/UnitHpBarsMaxHpQaTests.AProphecyZealotAt70Of83_ShowsABar` (un-skipped) and `QA/ViewApi/MaxHpExpiryQaTests` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~UnitHpBarsMaxHpQaTests` and un-skip
   `AProphecyZealotAt70Of83_ShowsABar`: a Whirlwind player researches Dryjhna's Prophecy (Zealot +20 hp, real since
   M4-4b-3b / BUG-0421), a Zealot is at 70 / 83 (`World.MaxHp` 83), and `UnitHpBars.Collect` returns 0 units.
2. In a match: research Prophecy, let a Zealot take up to 20 damage. It shows no hp bar until it drops below 63, and then
   the bar reads hp / 63 (e.g. 62 / 83 draws as 98 % full, green).

## Expected
docs/03 "For the view (M4-V7)" (added by M4-4b-3b): a unit's maximum is `World.MaxHp(slot)` (type hp + the owner's hp
techs). A hurt unit shows a bar of hp / that maximum.

## Actual
- `UnitHpBars.Collect` skips a unit when `hp >= defs[t].Hp` and `CombatViews.SyncBars` calls `Shows(u.Hp[i], _data.Units[type].Hp, ...)`.
- `HitFlash.Update`'s first-sight rule takes `UnitViews._maxHp[t] = def.Hp`: an upgraded unit first seen hurt but at or
  above its type hp (63-82 of 83) does not flash; with a negative hp tech (none shipped) a unit spawned at full would flash.
- `UnitHpBars`' remarks still say "Hp tech bonuses are not applied by combat yet".

## Notes
Not a sim defect: the sim brief named this as a view concern and docs/03 tells the view to switch to `World.MaxHp`. Filed
so it is not lost: the M4-V7 sandbox can research Prophecy. Only shipped hp tech today: Prophecy (+20 Zealot).

## Re-check (QA, 2026-10-10-1954, M4-V7b)
Fixed and verified. `AProphecyZealotAt70Of83_ShowsABar` passes un-skipped; the headless `AbilityViewTest` Prophecy row
(seeds 1 and 6) shows a bar of 0.843 at 70 / 83, none at 83 / 83, and a first-sight flash on the 70 / 83 one. QA added
`QA/ViewApi/MaxHpExpiryQaTests`: exactly 63 / 83 has a bar and flashes; Prophecy researched mid-fight changes the bars on
the next read (41 / 63 becomes 61 / 83, 63 / 63 becomes 83 / 83, no bar) with no flash; a fresh unit in a freed slot reads
its own maximum. `CombatViewTest` (oracle on `World.MaxHp`) passes in the scene loop.

