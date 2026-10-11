# BUG-0430: A zone disc that crosses a cliff is hidden under the higher ground or floats over the lower ground

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-1015, task M4-V6c (filed as BUG-0420 by QA; renumbered to the view track's id range at ACCEPT) |
| System | ability views: zone discs (`game/scripts/AbilityViews.cs` `SyncZones`) |
| Fixed by | afa2661 (M4-VH3): zone discs and the targeting / hover rings are `GroundDisc`s, one quad per terrain cell on that cell's own surface (`ViewApi/GroundPatch`); tests `ViewApi/GroundPatchTests`, `AbilityViewTest.CliffRun` (seeds 1, 6; failed first: 119 of 208 points off the ground), QA `QA/ViewApi/ViewHardening3QaTests`, `game/tests/QaVh3Test.tscn` |

## Repro
1. `dotnet build RtsGame.sln`
2. `& $env:GODOT --path game res://tests/QaV6cTest.tscn -- --shots <abs dir>` (windowed)
3. Look at `qa-v6c-cliff-zoom30.png` / `qa-v6c-cliff-zoom50.png`. Seed 1: storm A is centred on a level-1 plateau's
   east edge at cell (89, 36) (disc drawn at y 4.10), storm B on the low ground at (92, 44), 3 cells off the cliff
   (disc at y 0.10). A Laborer stands on the low ground at each storm's east edge.

## Expected
The disc shows the zone's footprint on the ground (docs/03 "Implementation (M4-V6c)": "a disc on the ground per zone"),
so a player can see which units stand in the storm (Blinded + Slowed) on either side of a cliff. Sandstorm (radius 6 m)
is a ground-target ability with no level rule, so it lands across cliff edges in normal play.

## Actual
The disc is one flat plane at the terrain height under its centre + 0.1 m. In the shots storm B's west part, over the
4 m higher plateau, is cut off at the cliff (drawn under the plateau's top, so invisible): units standing on the plateau
inside the storm (and Blinded by it) show no disc under them. Storm A's east part floats 4 m above the low ground, so
at the RTS camera's pitch its rim is drawn shifted from where the zone actually reaches on the low ground.

## Notes
docs/03 notes only the slope case ("on a slope it may dip into the ground"); a cliff is 4 m (`MapConstants.LevelHeight`),
much worse than a slope. Possible fixes: project the disc onto the terrain (a decal, or a shader that samples the
heightmap / the terrain's depth), or draw it with depth test off over a terrain-hugging mesh. Not a leak: the fog rule
(`ZoneDiscs`) is right on both maps (QA `ZoneDiscsQaTests` two-level fuzz is oracle-equal).

## Verification (QA 2026-10-10-2334, M4-VH3)
- Fixed. `QaVh3Test.tscn` (seeds 1, 6) puts Sandstorm discs exactly on a cliff lip, one cell onto the plateau, one cell
  off on the low ground and on a ramp: 9 circles x 180 points each (1,578-1,596 points per disc, interior and rim) are
  drawn on `TerrainHeight.At` + 0.1 m with 0 misses, spanning 0-4 m (seed 6) and 0-8 m (seed 1, two levels inside one
  disc). Every mesh triangle read back from Godot lies inside one cell and on its surface, so no face spans or paints a
  cliff wall. The radius and range rings with the cursor on each centre pass the same checks.
- The fog rule is unchanged: `ZoneDiscs.cs` is not in the diff, `QaV6cTest` (leaks, freed, scale) and `ZoneDiscsQaTests`
  are green, and QA's new per-node oracle (32 zones of both players under the fog, 600 ticks with fighting armies, slots
  reused at new centres) found 0 nodes visible against the rule and 0 at a wrong centre or radius.
- Cost: 32 zones + ~1,960 units at 8x, `AbilityViews._Process` 0.028-0.035 ms (0.017-0.025 with no zones), 0 B, no zone
  mesh rebuilt in a steady window. 64 zones (`QaV6cTest`): 0.048 ms, inside M4-V6c's 0.045-0.30 ms. A rebuild costs
  12-17 us for a 6 m disc and 21-26 us for a 16 m range ring's band, 0 B managed.
- Looked at windowed `build/shots/qa-vh3/qa-vh3-seedN-cliff-zoom20/30/50/60.png` and `-cliff-rings-zoom30.png`: the discs
  lie on the plateau top and on the low ground, the vertical walls carry no fill or rim, and a wall in front hides the
  part behind it. The cast-point ring and the resolve flash are still flat (BUG-0520).
