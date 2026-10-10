# BUG-0281: M4-V4 fog view nits: explored fog shows live changes, and minimap dots lag the click rule

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (items 1 and 3 fixed; item 2 deferred to the M6 fog-look pass) |
| Found | 2026-10-09-0125, task M4-V4 |
| System | fog of war view (PropsView, CombatViews markers, Minimap) |
| Fixed by | items 1, 3: 956eab3 (M4-VH2): `SeenResources` + `FogOfWar.Resources` for the props and the minimap resource layer; `MinimapRaster.DrawnEnemyDotAt` + `Minimap.CommandAt` over the dots as drawn; regression `ViewApi/SeenResourcesTests`, `ViewApi/FogViewTests.MinimapDrawnDotPick_*`, `FogViewTest` last-seen tree row |

## Repro
Code reading, plus the QA scene `res://tests/QaFogViewTest.tscn` for item 3's numbers.

1. **Trees vanish live in explored fog.** `PropsView` relists every tree when `NavGrid.Version` moves, and the minimap
   resource layer redraws on the same trigger. A tree an enemy worker cuts down in explored (not visible) fog disappears
   from the 3D view (darkened) and the minimap at once.
2. **New corpses and rubble appear in explored fog.** `marker_fog` / `prop_fog` draw every marker on explored ground,
   darkened. That follows the brief ("props and corpse/rubble markers under the same rule"), but a marker added while
   its cell is explored and not visible is information the player didn't see. With two players this is rare: an enemy
   has to die out of our sight, e.g. one of our own units dies and its corpse stays after the cell goes dark.
3. **Minimap dots vs the right-click rule.** Dots are redrawn at 5 Hz (`Minimap.RefreshTicks` = 4) from `UnitShown`
   *at that refresh*, but `Minimap.CommandAt` asks `EnemyDotAt` over `UnitShown` and positions *now*. For up to 3 ticks:
   a dot of an enemy that just went hidden is still drawn and right-clicking it gives a Move; an enemy that just came into
   sight has no dot yet, and right-clicking its empty spot gives an Attack. The pick also uses positions now, not the
   drawn dot's. Small at 150 ms, but the click doesn't always match what's drawn.

## Expected
docs/02 "Vision and fog of war": explored = "terrain and last-seen buildings shown, darkened". Last-seen state, not live
changes. The minimap click should act on what the minimap shows.

## Actual
As above. None of it breaks a criterion: the hide rule, texture and click tests all pass.

## Notes
Items 1-2 overlap with M4-3b's ghosts (last-known state). The M6 fog-look pass could keep a "last seen" copy for props and
markers per explored cell. For item 3, refreshing the dot layer when `Fog.View.RefreshedTick` moves, or picking over
the shown list captured at the last dot refresh, would line the two up.

## Verification (QA 2026-10-10-0624, M4-VH2)
- Item 1 fixed: QA `QA/ViewApi/ViewHardening2QaTests.LastSeenCopy_*` (3 seeds x 2,000 ticks, a scout walking a 64 x 64
  forest, about 110 fells per seed, most out of sight): the copy changes a slot only when the old or new footprint is
  visible, is never stale over a visible footprint, and the props relist exactly once per copy version. `FogViewTest`
  last-seen row (seed 1): a tree felled in explored fog stays drawn and on the minimap with no relist; gone in one relist
  once seen. Read-only twin `NewViewApiHelpers_VH2_*` green.
- Item 3 fixed: the click picks among the dots as drawn and attacks only a unit still live and shown (code read, the
  `FogViewTests` row, `FogViewTest` minimap clicks).
- Item 2 not done: docs/03 "Implementation (M4-VH2)" leaves corpses / rubble in explored fog to the M6 fog-look pass.
