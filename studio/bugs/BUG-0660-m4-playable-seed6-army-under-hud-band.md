# BUG-0660: M4PlayableTest seed 6 step 2: the staged army reaches into the HUD band, so the box-select misses units

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-13-0527, task M6-VH1 (dev; filed by QA) |
| System | test scene `game/tests/M4PlayableTest.cs` (step 2, box-select the army) |
| Fixed by | 650d423 (step 2 looks a little south of the army when its lowest unit would sit under the HUD band's top). QA 2026-10-13-0527: 10/10 seed-6 runs pass beside the sim xUnit suite. |

## Repro
1. At 0155205: `& $env:GODOT --headless --path game res://tests/M4PlayableTest.tscn -- --seed 6`.

## Expected
Step 2 selects the whole army with one box drag on every seed.

## Actual
Since the sim's start locations (M5-4a) seed 6 stages the army on a slope where, centred on screen, its southern units sit under
the bottom HUD band (648 - 200 px); the drag box can't reach them and the selection is short (the developer's finding).

## Notes
Test-side staging, the BUG-0460 class: a camera framing the test assumes. Not re-verified on base by QA (the dev's report);
the fix was verified by the 10 runs above.
