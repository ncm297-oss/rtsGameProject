# BUG-0147: The view branch on top of the sim's M4-1: smoke fails (`ui.json` has no `states.attacking`), and 7 of 27 test scenes fail, the M3 Playable proof among them (player 1's idle start laborers kill player 0's)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-2014, task M3-V4 (QA; a merge check, not a defect of M3-V4 itself) |
| System | view integration with M4-1 combat: `game/data/common/ui.json`, `game/tests/**` scenes |
| Fixed by | 20c05a8 (M4-V1: `--no-combat` launch flag -> `SimRunner.Combat` -> `SimConfig.Combat`; DebugOverlay, Orders, QaH1, QaH2, QaM27 run with it; regression rows in `CombatViewTest` LaunchRows) |

## Repro
1. In a scratch clone of `studio/2026-10-07-2014-view` (df37a8c), merge `studio/2026-10-07-2014-sim` at f2879b9
   ("M4-1: implement Combat slice 1"). The merge is clean. `dotnet build RtsGame.sln`: 0 warnings, 0 errors.
2. `powershell -File tools/qa/smoke.ps1`: `FAIL: log: ERROR: ui.json: missing states.attacking (a non-empty string)`.
3. With `"attacking": "Attacking"` added to `ui.json` `states` (scratch only), smoke PASSes. Then run every
   `game/tests` scene headless.

## Expected
The view runs on the merged sim: smoke PASS, every scene PASS (the brief's QA focus: "Smoke and the whole scene loop with
the merged sim branch (M4-1) before the final run").

## Actual
- `ui.json` needs the new `states.attacking` key (the view owns this file). Without it, smoke fails.
- With the key, 20 of 27 scenes pass and 7 fail:
  - **M3PlayableTest**: `FAIL 9. B, A: Armory (seed 1, tick 16001): a green ghost on the spot: not by tick 16000 ...
    ghost active False`. The replay shows why. Player 1's five start laborers stand `Idle` at x ≈ 135 m, about
    26 m east of player 0's Town Hall (109, 129). The script's forest Depot (anchor (123, 123)) brings player 0's
    laborers within their reach. Player 1's laborers acquire and kill all 9 of player 0's laborers, one by one,
    between ticks 1,584 and 6,182 (`killer owner 1` in `World.Deaths`, every victim type 4 = Laborer). The selected
    builder dies, so the menu and the ghost close.
  - **DebugOverlayTest** (63 FAIL lines, `evict tick 0: overlay goal -1, want 8381`), **OrdersTest** (`S: walkers not
    all Idle with GoalCell -1 after 2 ticks`), **QaH1Test** (`a plain click on empty ground left 1 selected`;
    `Move x0, expected 2`), **QaH2Test** and **QaM27Test** (army centre moved 17.8 / 16.8 m in 10 s, want >= 20 m),
    **QaM24Test** (`respawned enemy in slot 0: pixel ... want ...`). These are M2 scenes with start armies (100 a side
    by default). Their units now fight and die, so selections, goals, slots and march distances change under them.
  - Passing on the merge: Bench, CameraClamp, CommandCard, EconomyView, Minimap, ProductionHud, PropsView, QaM22,
    QaM23, QaM23b, QaM25, QaM26, QaV1, QaV2, QaV3, QaV3b, QaV4, Selection, Sfx, UnitViews.

## Notes
- The conductor's note expected this (the sim branch is PARTIAL, and 187 two-player sim rows fail there for the same
  reason). Whoever merges M4-1 into the view needs: the `states.attacking` key (and a `ui.json` test row for it), and
  a decision per scene. Peaceful setups (`--units 0`, or an ignore-enemies / truce option if the sim offers one), or
  updated expectations. For the M3 Playable proof: player 1's start workers sit 26 m away, so either the scene removes
  or freezes player 1, or M4-1's acquisition should not let idle *workers* hunt (a design question for the sim track /
  docs/02, not decided here).
- **Re-check round 1 (QA 2026-10-07-2315): still open, S3.** Plan, as given by the developer / Producer: "The view ships
  `states.attacking` (M3-V4 fix round 1). The six M2 scenes that fight on the merge (DebugOverlay, Orders, QaH1, QaH2,
  QaM27, QaM24) are the first item of the view's next task, once M4-1 (`SimConfig.Combat`) is on `main`. The Playable
  scene's worker case is fixed sim-side: workers never fight on their own (Producer decision). Stays open at S3."
  QA verified the first part on ec28ee4. `ui.json` has `"attacking": "Attacking"`, and `ProductionHudTest` has a row for
  it. Mutation check in a scratch clone (not the branch): with `UnitState.Attacking = 5` added to the sim enum,
  `ProductionHudTest` PASSes. Removing the key then gives `ERROR: ui.json: missing states.attacking`, a scene FAIL and a
  smoke FAIL. Without the enum member, removing the key fails the row's unconditional presence check
  (`ui.json: states.attacking is missing or empty`). So the key can't go missing silently before or after M4-1.
  The advisory scratch merge of the sim branch was deferred (the sim branch is mid-fix in another worktree). Do it in the
  view's next task, and confirm the red set is exactly the six M2 scenes and that the Playable scene passes with the
  sim's worker rule.
- **Re-check (QA 2026-10-08-0313, task M4-V1): fixed.** On 20c05a8 the five scenes' diffs change only their launch
  arguments (`--no-combat` appended; no `Check`, threshold or expected value touched; diffed file by file). The flag
  reaches the sim before the match builds: `Match.Start` sets `SimRunner.Combat` before `SimRunner.Start` builds the
  `SimConfig`, and `CombatViewTest` asserts `World.Config.Combat == false` / `CombatEnabled == false` on a real Match
  with the flag and true without it; the DebugOverlay hash twin is built from `World.Config`, so it is off too.
  QaM24 passes without the flag. Scene loop on the branch: 28 / 28 headless `*Test` scenes PASS (27 + QA's new
  `QaV5Test`), 5 / 5 windowed `*Shot` scenes exit 0 with no ERROR line; smoke PASS.
