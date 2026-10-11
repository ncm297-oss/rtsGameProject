# BUG-0520: Cast-point rings and resolve flashes are still flat at their centre's height across a cliff

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-2334, task M4-VH3 (proposed by the developer, confirmed by QA) |
| System | ability views: `game/scripts/AbilityViews.cs` cast rings (`_circles`) and resolve flashes (`_flashes`) |
| Fixed by | |

## Repro
1. Read `AbilityViews.cs`: the cast-point ring (an own unit with `CastAbility` set gets an orange ring of the ability's
   radius at its cast point) is a `MultiMesh` torus at `TerrainHeight.At(centre) + Lift` (line ~465), and the resolve
   flash is a flattened disc at the same height (line ~532).
2. Cast Telas Fire (radius 3 m) or a Sapper's Cusser at a point 1 m from a 4 m cliff (the cliff found as in
   `AbilityViewTest.CliffRun`). While the mage walks in, the cast ring's part over the other level is drawn 4 m off the
   ground (hidden under the plateau, or floating over the low ground); the resolve flash does the same for its 0.5 s.

## Expected
The same as BUG-0430 / BUG-0462 item 1, which M4-VH3 fixed for the zone discs, the targeting rings and the hover ring:
a ring that marks ground lies on the ground on both levels.

## Actual
Only these two are still flat. The cast ring shows only during a walk-in, and the flash only for 10 ticks, so this is
polish.

## Notes
`GroundDisc` (M4-VH3) covers it: one node per cast ring would need a pool (up to every selected mage), so the developer
left it out of scope and noted it in docs/03 "Implementation (M4-VH3)", "Not yet". The cast ring could reuse a small
`GroundDisc` pool, rebuilt only when a cast point changes.
