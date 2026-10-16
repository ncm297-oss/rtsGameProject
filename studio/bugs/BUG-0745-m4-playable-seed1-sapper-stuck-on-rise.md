# BUG-0745: M4PlayableTest seed 1 step 7 failed once under load: the Sapper stood still on a rise through all 8 right-clicks, 45.5 m from the Smithy

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-15-2325, task M6-V8 (QA, fix round 1 re-check) |
| System | test scene `game/tests/M4PlayableTest.cs` step 7 (the Cusser on a building); possibly movement (sim) |
| Fixed by | |

## Repro
1. Not reproducible alone: it failed once in the full scene loop (`powershell -File tools/qa/scene-loop.ps1`, 58 scenes, after
   CombatViewTest took 215 s, so the machine was loaded) and passed when rerun alone (`-Filter M4PlayableTest`, 45.7 s).
2. The failing run saved its replay: `%APPDATA%/Godot/app_userdata/RtsGame/m4playable-seed1-fail-tick2497.replay`.

## Expected
`M4PlayableTest` passes on both seeds whatever the frame timing.

## Actual
```
seed 1 step 6. kills and losses move: done at tick 1849 (531 ticks; kills 14, losses 1; ...)
  1, right click: the army pulled back to <176.811, 103.06635> on tick 2124
  right-clicked <211, 77>: the Sapper stopped at <179.97574, 102.09141> (level 1), 45.5 m from the Smithy (level 0), on tick 2320
  right-clicked <211, 73>: the Sapper stopped at <179.97574, 102.03219> (level 1), 45.5 m from the Smithy (level 0), on tick 2345
  ... (6 more spots round the Smithy, the Sapper never moves more than 0.07 m)
  right-clicked <219, 69>: the Sapper stopped at <179.97574, 102.02431> (level 1), 45.5 m from the Smithy (level 0), on tick 2497
M4 PLAYABLE TEST FAIL 7. Cusser on a building, rubble shown (seed 1, tick 2497): the Sapper never got within 6 m of the Smithy: 45.5 m
```
The Sapper stood among the pulled-back army (mean <176.8, 103.1>) on level 1 and did not move for any of the 8 Moves. The
scene's comment names this class (the army's units standing as walls on a rise, seen on seed 6); here it is seed 1, after
the army was pulled back on top of the Sapper.

## Notes
Not caused by M6-V8: the fix round changed only `KeyMap` / the settings screen, the step's input (1, right clicks) uses
default keys, and `user://settings.cfg` had no `[keys]` section. Worth replaying the saved replay in a bare-sim probe (as for
BUG-0463 / BUG-0603) to tell a scene timing fault (the Sapper boxed in by idle units it can't push past) from a sim
movement fault (a unit that never leaves its cell on a valid Move).
