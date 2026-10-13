# BUG-0601: A mirror match picked from the setup draws both sides in the same colour: own and enemy units, buildings and minimap dots look alike

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-12-1930, task M6-V2 (QA) |
| System | view: player colours (`Match` `playerRgb`, `UnitViews` materials per faction) vs the new live faction lists |
| Fixed by | 9ec4a56 (M6-V3 per-player colours from `ui.json` `setup.colour_*`); `QaM6V2Test.SwappedHud` mirror row (a Check), `QaM6V3Test.MirrorMatchColours` (bodies, buildings and both players' minimap dots in colours 1 / 2). QA 2026-10-13-0146 verified. Picking one colour for both slots is still allowed: BUG-0631 item 3. |

## Repro
1. `& $env:GODOT --path game`, Skirmish, set both faction lists to the same faction (e.g. "The Whirlwind" / "The Whirlwind"), Start.
2. Or headless: `res://tests/QaM6V2Test.tscn` prints
   `swapped HUD: ... colours p0 (0.78431374, 0.5372549, 0.18039216, 1) p1 (0.78431374, 0.5372549, 0.18039216, 1)` for
   `--faction 1 whirlwind --ai hard`.

## Expected
docs/02 "Game flow" 2 lets each slot pick any faction; a player must be able to tell their army from the enemy's.

## Actual
Every colour is the seat faction's `PrimaryColor` (`Match.Start` `playerRgb`, and `UnitViews.Bind` makes one material per
faction, indexed by the unit type's faction). In a mirror both sides, their buildings and their minimap dots are identical.
Before M6-V2 a mirror could not be picked from the menu; now it is two clicks away.

## Notes
Known and flagged by the dev (deviation: "per-faction PrimaryColor; M6-V3"); the brief puts colour in M6-V3. Filed so the
Producer can tie it to M6-V3's per-player colours (or, until then, have the setup refuse a mirror). Not a criterion miss.
