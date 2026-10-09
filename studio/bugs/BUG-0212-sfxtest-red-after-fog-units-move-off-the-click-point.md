# BUG-0212: SfxTest is red on the M4-3a sim: the clicked own unit walks off its screen point (fights start differently)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-08-1435, task M4-3a |
| System | view test scene (`game/tests/SfxTest.cs`, view-owned) vs sim combat under fog |
| Fixed by | |

## Repro
1. Sim branch 977db1d, after `tools/qa/smoke.ps1`: `powershell -File tools/qa/scene-loop.ps1 -Filter SfxTest -SkipPlayable`
   (the script fetched from `origin/studio/2026-10-08-0913-view`): FAIL, every run (2 of 2):
   `double-click a type: Select played 0 times, expected 1`, `click own unit again`, `group recall`, `click own unit`.
2. The same on dd5b5b9 (scratch clone): PASS.
3. The same on 977db1d with `"--no-combat"` added to the two `--units 100` argument lists in `SfxTest.StartMatch`
   (scratch clone, not committed): PASS.

## Expected
Brief criterion 8: scene loop with only `MinimapTest` red; "any other red scene is this task's".

## Actual
The scene clicks an own unit's screen point several times over a few seconds of the default seed-1 match. Under the
fog, player 1's raiders on level 0 no longer see (or charge) player 0's army standing ~16 m away on a level-1
plateau (the developer's diagnosis; QA confirmed only the outcome), so the fight starts differently and the unit
under the cursor is no longer at that point when the later clicks land: they select nothing. The sim behaviour is what docs/02 "High ground" asks (low ground can't see up,
high ground sees down, a hit from above reveals the shooter), so the defect is the scene's assumption that the match
stands still, the BUG-0147 / BUG-0210 class.

## Notes
Owner: the view (the sim can't edit `game/tests/`). Fix: launch arguments only, `--no-combat` on both
`LaunchOptions.Parse` lists in `SfxTest.StartMatch`, as BUG-0147's five scenes and the view's BUG-0210 fix for
`MinimapTest` do; every `Check` stays. Must land with or before the sim merge, or `main`'s scene loop goes red.
