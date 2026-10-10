# BUG-0420: A zone disc that crosses a cliff is hidden under the higher ground or floats over the lower ground

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1015, task M4-V6c |
| System | ability views: zone discs (`game/scripts/AbilityViews.cs` `SyncZones`) |
| Fixed by | |

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
