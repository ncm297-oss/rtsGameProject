# BUG-0661: Four view test scenes fail on main since the M5-4a merge (FogViewTest, QaH2Test, QaM27Test, QaV3Test)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-13-0527, task M6-VH1 (QA; the dev reported it) |
| System | view test scenes staged on the generated map: `game/tests/FogViewTest.cs`, `QaH2Test.cs`, `QaM27Test.cs`, `QaV3Test.cs` |
| Fixed by | |

## Repro
1. `powershell -File tools/qa/scene-loop.ps1 -SkipPlayable` at 0155205 (base of M6-VH1) or at 650d423 (M6-VH1 head).
2. Both commits print the same four failures, line for line:
```
FOG VIEW TEST FAIL: seed 1: no clean visible (-1) or hidden (45) enemy for the minimap
QA M2-H2 TEST FAIL: seed 1: centre moved only 19.6 m (want >= 20 m)
QA M2-H2 TEST FAIL: seed 6: centre moved only 11.3 m (want >= 15 m)
QA M2-7 TEST FAIL: the army centre moved at most 19.6 m in 10 s (want >= 20 m)
QA M3-V3 TEST FAIL: BUG-0274: 9636 not explored
QA M3-V3 TEST FAIL: minimap: no site
```

## Expected
The scene loop is green on main (definition of done; every view brief's acceptance says "scene loop green").

## Actual
SCENE LOOP FAIL: 4 of 46 on both commits. M6-VH1 changed none of the four scenes or the code they test.

## Notes
The failures look like staging assumptions the new map geometry broke (start locations, M5-4a: mines and a forest by each
base): march distances 0.4 m short of a 20 m line, a fixed cell (9636) or site that is now different ground, and a seed-1 enemy
that is never cleanly visible or hidden. Probably test-side retunes (the view track owns `game/tests/**`), as BUG-0660 was
for M4PlayableTest; each threshold must be re-derived, not loosened, and the reason written in the test. Until then every
view task's "scene loop green" criterion can't be met.
