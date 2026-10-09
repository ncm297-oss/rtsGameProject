# BUG-0213: QaM24Test fails about 1 run in 3 on the M4-3a sim: "slot N not reused by the enemy" (a death mid-check)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-3a |
| System | view test scene (`game/tests/QaM24Test.cs`, view-owned) vs sim combat under fog |
| Fixed by | M4-V3 fix round 1, e1f3333 (view track): `--no-combat` in `QaM24Test`'s `--units 1000` launch list, every `Check` unchanged. Verified by the view QA on a sim + view scratch merge (2026-10-08-1814: 8 / 8 quiet, 6 / 6 under load) and by the 33 / 33 merge loop |

## Repro
1. Sim branch 977db1d: `powershell -File tools/qa/scene-loop.ps1 -Filter QaM24Test -SkipPlayable`, 12 runs alone:
   4 FAIL (`QA M2-4 TEST FAIL: slot 1 not reused by the enemy`), 8 PASS. (Full loop run: PASS; the developer saw it red.)
2. dd5b5b9 (scratch clone): 6 / 6 PASS.
3. 977db1d with `"--no-combat"` added to `QaM24Test`'s `--units 1000` arguments (scratch clone, not committed): 8 / 8 PASS.

## Expected
Brief criterion 8: only `MinimapTest` red.

## Actual
`RespawnAsEnemy` frees the first own unit's slot and expects the enemy spawn two ticks later to reuse it. With the
fight now starting differently in the seed-1 match (see BUG-0212: the low side no longer charges the plateau), a unit
sometimes dies in between, so the spawn takes another freed slot. Wall-clock frame / tick alignment makes it flaky.
The sim behaviour is per docs/02; the scene assumes no deaths.

## Notes
Owner: the view. Fix: `--no-combat` in `QaM24Test`'s launch arguments (its subject is minimap routing and dots, not
combat), every `Check` unchanged; land it with or before the sim merge.

## QA re-check (2026-10-08-1435, round 1)
Still open by design: the scene file is view-owned and not changed on the sim branch (34931b8 touches no `game/` file).
The local view branch `studio/2026-10-08-1435-view` (e1f3333, "recovered work from interrupted session") already passes
`--no-combat` in this scene's `LaunchOptions.Parse` list(s); QA did not run it there. Owned by the view; it gates the
integration: after the sim and view merge, the scene loop must show this scene green (QaM24Test: repeated runs).
