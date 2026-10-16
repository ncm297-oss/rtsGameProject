# BUG-0745: M4PlayableTest seed 1 step 7 failed once under load: the Sapper stood still on a rise through all 8 right-clicks, 45.5 m from the Smithy

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (Producer, 2026-10-16-0847 ACCEPT: the sim half in M5-H2 47ca4a2 (the pinned walker slides on, `WallPinTests` red on 8c39c08), the scene half in M6-VH2 2a822ca (`M4PlayableTest` on `SimRunner.FixedFrameDelta` 1/60 s a frame + a pull-back spot off the Sapper's way: 6 runs alone / 3 at once / 2 loops under load, identical step ticks on both seeds); both verified by QA 2026-10-16-0847) |
| Found | 2026-10-15-2325, task M6-V8 (QA, fix round 1 re-check) |
| System | test scene `game/tests/M4PlayableTest.cs` step 7 (the Cusser on a building); possibly movement (sim) |
| Fixed by | 47ca4a2 (M5-H2) `MovementSystem` flow-step-alone slide, `WallPinTests.AWalkerPinnedOnACliffByItsSidestepRoundIdleFriends_GetsPast` (red on 8c39c08) |

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

## QA verification (2026-10-16-0847, M5-H2)
**Verdict: a sim movement fault, not a scene fault.** The replay was recorded on the view branch b6c33e4 (data hash C6FC120A55AE4A0B, pre-D19). Played on that tree in a bare sim it matches every checkpoint and the Sapper stands at <179.97574, 102.09141> from tick 2325 to 2497 through all 7 later Moves (the scene log's exact coordinates): the sim never moved it. The same tree with only M5-H2's `MovementSystem` hunk applied matches every checkpoint up to 2311, then the Sapper walks 54.65 m toward the Smithy (at <203.2, 92.7> by 2475). On today's data the replay diverges from checkpoint 1099, so it must be played on b6c33e4.
The second data point (seed 6 step 6, "a kill and a loss: not by tick 5500") is not this: on head the scene passed 1 / 4 alone, on base 8c39c08 1 / 2, the same failure; the saved seed 6 replay played on both trees gives the Malazan army 0 losses either way (head: player 1 out at 4266, its Tent and Smithy collapse; base: the Tent razed at 4686). The command stream is frame-timed, so this half stays the view's (M6-VH2 item 4: stage each step from sim state).

## Producer close (2026-10-16-0847 ACCEPT)
Both halves landed in one session: the sim's slide (above) and the view's M6-VH2 scene half (`M4PlayableTest` advances the runner by a fixed 1/60 s a frame at 8x, so every click lands on the same tick whatever the load; the army's pull-back spot is chosen off the Sapper's way; QA: 6 runs, every step tick identical on both seeds, PASS each). Closed as fixed. Known cost: on fixed frame time the scene no longer exercises frame-time variance (the sim's `WallPinTests` is the movement guard now). The view dev's run overwrote `m4playable-seed6-fail-tick4094.replay` under `%APPDATA%`; the seed-6 evidence is this file's text.
