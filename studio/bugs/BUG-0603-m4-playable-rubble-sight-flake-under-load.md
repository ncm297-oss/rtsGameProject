# BUG-0603: M4PlayableTest seed 6 step 7 failed once under load: the Raider Camp fell at the Sapper's first blast and the rubble was out of sight

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-12-1930, task M6-V2 (QA) |
| System | test scene `game/tests/M4PlayableTest.cs` (step 7, the Cusser on a building) |
| Fixed by | 650d423 (M6-VH1): step 7 reads the camp's cell and generation before the cast; step 2 frames the army above the HUD band (BUG-0660). QA 2026-10-13-0527: 10 / 10 seed-6 runs under load; the root cause confirmed by replaying `m4playable-seed6-fail-tick4094.replay` on e41be72 |

## Repro
1. Not reproducible alone: 1 failure in 3 runs at this commit. It failed in the full scene loop
   (`powershell -File tools/qa/scene-loop.ps1`) while another track's xUnit suite ran on the same machine, and passed twice when
   run alone (`-Filter M4PlayableTest`).
2. The failing run saved its replay: `%APPDATA%/Godot/app_userdata/RtsGame/m4playable-seed6-fail-tick4094.replay`.

## Expected
`M4PlayableTest` passes on both seeds whatever the frame timing (BUG-0460 made the rubble check read any footprint cell).

## Actual
```
seed 6 step 6. kills and losses move: done at tick 3079 (2268 ticks; kills 21, losses 1 ...)
  right-clicked <193, 139>: the Sapper stopped at <192.99995, 138.12703> (level 0), 4.1 m from the Raider Camp (level 0), on tick 4068
Match over: player 1 has no buildings left (tick 4094)
  Cusser on the Raider Camp (6) from 4.1 m: hp 338 -> 0
M4 PLAYABLE TEST FAIL 7. Cusser on a building, rubble shown (seed 6, tick 4094): the rubble is not in sight (no footprint cell visible)
```
In the passing runs step 6 takes about 740 ticks and the Cusser takes the camp from 1,200 to 844 hp. In the failing run step 6
took 2,268 ticks: the army fought on into the enemy base and had already brought the camp down to 338 hp (the enemy's last
building: the M6-V2 dev line printed `Match over:`). The Sapper's blast then destroyed the camp, and the Sapper died with it,
so there was no sight left on the footprint.

## Notes
This is BUG-0460's class again: a frame-timed staging step. The M6-V2 change is not the cause, because without `--ai` /
`--faction` the seats equal the old defaults (`QA/ViewApi/ExplicitSeatsQaTests`: equal hash every tick, byte-equal replays).
Possible test-side fixes: keep the army out of the enemy base in step 6 (pull it back once the enemy army is gone), or have
step 7 check sight from a unit kept at the camp.

## Producer (2026-10-13-0527 ACCEPT)
**Fixed** in M6-VH1 (650d423, verified by QA: 10 / 10 seed-6 runs beside the sim suite; the replay probe confirms the cell -1 read). Status set by the Producer.
