# BUG-0225: docs/03 still lists the `--no-combat` scenes without SfxTest and QaM24Test, and says "QaM24 passes without it"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-1814, task M4-V3 (QA re-check of fix round 1, e1f3333) |
| System | docs: `docs/03-technical-design.md` ("Debug tooling" no-combat flag; "Implementation (M4-V1)" BUG-0147) |
| Fixed by | |

## Repro
1. `git -C .claude/worktrees/studio-view grep -n "no-combat" e1f3333 -- game/tests` lists DebugOverlayTest, MinimapTest,
   OrdersTest, QaH1Test, QaH2Test, QaM24Test, QaM27Test and SfxTest as launching with it (8 scenes; CombatViewTest
   also matches, but it only tests the flag's parsing).
2. docs/03 line ~3741 ("No-combat flag"): "The scenes that pass it: DebugOverlayTest, OrdersTest, QaH1Test, QaH2Test,
   QaM27Test (M4-V1) and MinimapTest (M4-V3, BUG-0210 ...)". SfxTest and QaM24Test are missing.
3. docs/03 line ~3341 (BUG-0147): "No expectation changed. QaM24 passes without it." Now false: e1f3333 adds
   `--no-combat` to QaM24Test.

## Expected
CLAUDE.md "Docs updated in the same commit when behavior or a decision changed": both scenes, with their reason
(BUG-0212 / BUG-0213: under the sim's fog, M4-3a, the seed-1 fight starts differently, so SfxTest's clicked unit walks
off its screen point and QaM24Test's freed slot is taken by a death before the enemy spawn).

## Actual
The recovered fix commit (e1f3333) changed the two scenes' launch arguments and no docs line about them.

## Notes
Known gap named in the 1814 resumption note. Docs-only; no behaviour impact.
