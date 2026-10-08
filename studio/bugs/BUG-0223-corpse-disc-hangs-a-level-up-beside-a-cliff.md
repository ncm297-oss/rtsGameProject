# BUG-0223: The BUG-0190 corpse fix (`TerrainHeight.MaxUnder`) lifts a corpse disc 4 m into the air when the unit dies within ~0.6 m of a blocked cliff cell

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-08-1435, task M4-V3 |
| System | view: `game/scripts/CombatViews.cs` (`ShowMarker`), `sim/Rts.Sim/ViewApi/TerrainHeight.cs` (`MaxUnder`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~CorpseDisc_MaxUnderTheRim"` with the `Skip` removed from
   the seed 17 and seed 5 rows (`QA/ViewApi/ProjectileViewQaTests.cs`): `CombatScenes.MapBrawl(seed, 200)`, 1,500 ticks, every live
   unit sampled every 5 ticks for `MaxUnder(pos, radius x CorpseRimScale) - At(pos)`.
2. Output:
   `seed 17: 80492 unit samples, worst disc lift 4.00 m at <123.18489, 127.7788>, 3344 over 1 m; 282 deaths, worst death-spot lift 4.00 m`
   (a real death: that corpse is drawn 4 m in the air) and
   `seed 5: 76763 unit samples, worst disc lift 4.00 m at <117.90936, 115.84763>, 24 over 1 m`. Around the seed-5 point:
   `(58,57) e0`, `(59,57) e4#` (a blocked cliff cell at level 1): the east rim sample at x + 0.6 m lands on the cliff top.

## Expected
BUG-0190 item 1: a corpse disc on a ramp is not half buried. A disc on flat ground next to a cliff stays on the ground.

## Actual
`MaxUnder` takes the highest of nine samples within the rim radius (1.2 x unit radius, about 0.6 m), including samples
in blocked cells. The cliff's blocked cells are drawn at the upper level, so a unit that dies hugging a cliff gets a
corpse disc floating `LevelHeight` (4 m) above the ground. Seed 23 never had a unit that close (0.00 m worst). Seed 5
did 24 times in 76,763 samples. Seed 17 did 3,344 times, and at least one unit died there (a corpse drawn 4 m up).
`CombatViewTest`'s new row doesn't catch it: it checks only that the underside is at or above `MaxUnder`, not that the
disc isn't floating. Before this change, the same corpse was drawn on the ground (`TerrainHeight.At`), so this is a
regression from the BUG-0190 item 1 fix, and a worse look than the half-buried ramp disc it fixed.

## Notes
Possible fixes: only take samples from the centre's own cell and ramp cells, skip blocked cells, or cap the lift at what
a ramp can give over the rim radius (the ramp case needs only a few tenths of a meter).
