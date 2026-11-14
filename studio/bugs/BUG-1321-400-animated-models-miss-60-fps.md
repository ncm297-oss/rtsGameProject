# BUG-1321: 400 animated unit models miss the 60 FPS budget (average 47-64 FPS, worst 1 % 27-135 ms); 1,000 run at 16 FPS

| Field | Value |
| --- | --- |
| Severity | S3 (the brief scoped optimisation out of M6-V20: "measure only". By the table this is "perf budget blown" (S2); the Producer decides) |
| Status | open |
| Found | 2026-11-13-1118, task M6-V20 (QA) |
| System | unit views (models, animation) |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game --resolution 1600x900 res://tests/ModelShots.tscn -- --bench 30 --vsync off` (400 units,
   200 a side, Laborers and Crossbowmen, moving then fighting, default zoom).
2. The same with `--placeholder-art` for the capsule baseline.

## Expected
docs/03 "Target hardware": 60 FPS with ~400 animated units; docs/04 "Style guide": ~400 animated units must hold 60 FPS.

## Actual
QA's runs, windowed at 1600 x 900 with vsync off, on a fresh clone of fbe19b5. Five of the other tracks' testhosts were
running each time. No other process of QA's was running.

| Run | Average | Worst 1 % | Draw calls / frame |
| --- | --- | --- | --- |
| 400 models | 18.03 ms (55 FPS) | 33.67 ms | 3,257 |
| 400 models, rerun | 21.34 ms (47 FPS) | 135.45 ms | 3,405 |
| 400 capsules (`--placeholder-art`) | 2.09 ms | 5.04 ms | 56 |
| 1,000 models (`--per-side 500`, a scratch edit of `ModelShots`) | 63.43 ms (16 FPS) | 83.73 ms | 10,443 |
| 1,000 capsules | 3.26 ms | 7.74 ms | 68 |

The developer measured 14.5-15.7 ms average and 26.6-30.1 ms worst 1 % with fewer test hosts running. With every
`AnimationPlayer` stopped the frame is 6.5 ms, so about 9 ms of it is animation (developer's split, docs/03
"Implementation (M6-V20)"). Cost grows about linearly with model count: 2.5x the units take 3.5x the frame time here.

## Notes
- Every model is in the game by default: the menu's match and `--skirmish` draw Laborers and Crossbowmen as models. A
  400-unit army of these two types is below 60 FPS today. 30 more model types are planned, so this needs to land before them.
- Next steps the developer proposed and QA agrees with: drop the 40 finger bone tracks from the library at import; update
  crowd animation at a lower, staggered rate (`AnimationPlayer` callback mode manual, advanced N slots per frame); and
  consider shadow casting off for models at far zoom. Re-measure alone (rule 42) once no other track's suite is running.
- Every bench run QA made here was under other tracks' load (5 testhosts), so the absolute numbers are pessimistic. The
  capsule-to-model ratio (about 9x at 400 units, 19x at 1,000) is the robust number.
