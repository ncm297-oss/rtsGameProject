# BUG-0521: M4-VH3 nits: a sub-band ring rebuilds every frame; a seen new node drops the old remembered node unseen; a re-bound CombatViews keeps an old max-hp array

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-2334, task M4-VH3 (QA) |
| System | view: `game/scripts/GroundDisc.cs`, `sim/Rts.Sim/ViewApi/SeenResources.cs`, `game/scripts/CombatViews.cs` |
| Fixed by | Item 1: 36248fa (M5-V1), `GroundDisc.Place` clamps the hole before comparing; test `AbilityViewTest.Bug0521Row` (a 0.1 m ring placed 5 times: 1 rebuild; 5 before). Items 2 and 3 open. Verified by QA 2026-10-11-0314 |

None of these is reachable with the shipped data or the current game paths.

## Item 1 (fixed, M5-V1): `GroundDisc.Place` never takes its early return when `hole` < 0
`Place` stores `Hole = Math.Max(0f, hole)` but compares the caller's raw `hole == Hole`. `AbilityViews.PlaceRing` passes
`radius - RingWidth`, which is negative for a radius under 0.22 m, so such a ring would rebuild its mesh (and bump
`Rebuilds`) every frame even when it doesn't move. No shipped ability has a radius between 0 and 0.22 m (a radius-0
summon draws no ring). Compare the clamped value, or clamp before comparing.

## Item 2: a slot whose new node is seen drops the old remembered node unseen
BUG-0400 item 3 is fixed: seeing only the old footprint now takes the removal and not the unseen new node (QA
`ViewHardening3QaTests.ReusedSlots_*`, 4 seeds x 250 reuses, green). The opposite case is left: when only the new
node's footprint is visible, `SeenResources.Update` copies the new node, so the old remembered node elsewhere disappears
from the copy (and the props) without its footprint being seen. A one-slot copy can't hold both. Resource nodes spawn
only at map setup today, so no slot is reused in play.

## Item 3: `CombatViews.MaxHp ??=` keeps an array from an earlier `Bind`
`CombatViews.Bind` makes its own `SlotMaxHp(unitCapacity)` only when `MaxHp` is null. A second `Bind` on the same node
with a larger capacity (and no `MaxHp` set by `Match` first) would keep the first, shorter array, so the bars would
read a max-hp array shorter than the unit store. `Match` binds once per instance and always sets `MaxHp` first, so it can't happen today.

## Expected
1. A ring that hasn't moved isn't rebuilt. 2. The copy shows only what was seen in both directions. 3. A re-bind sizes
the shared array to the new capacity.

## Update (2026-10-13-0527 ACCEPT, Producer)
Item 3 **fixed** in M6-VH1 (650d423): `CombatViews.Bind` replaces a max-hp array shorter than the unit store (`SlotMaxHp.Capacity`). Item 2 (`ViewApi/SeenResources`, a sim file) stays open for a sim hardening, so the bug stays open.
