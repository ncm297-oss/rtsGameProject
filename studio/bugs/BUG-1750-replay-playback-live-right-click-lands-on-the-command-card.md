# BUG-1750: ReplayPlaybackTest's live right click can land on the command card

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-11-25-1437, task M6-V28 (view developer, the scene loop); filed by QA from the developer's draft |
| System | `game/tests/ReplayPlaybackTest` (a test row, not the game) |
| Fixed by | M6-V28 (`ReplayPlaybackTest`: the open-ground search skips offsets inside the command card's global rect) |

## Repro
1. `& $env:GODOT --headless --path game res://tests/ReplayPlaybackTest.tscn` on the base 2336d58 code (developer: the four
   M6-V28 model folders moved aside and the three M6-V28 scripts restored to the base give the same result).

## Expected
`REPLAY PLAYBACK TEST PASS` (124 of 124 at the 1437 PLAN).

## Actual
Developer's account: `REPLAY PLAYBACK TEST FAIL: two: the live control right click sent nothing`, three runs of three on
2026-11-25 afternoon. The row looks for "open ground" round the selected worker (16 offsets, M6-VH9) by asking the sim's
pickers only; the offset it took, (-23, +55) px from a worker at about (811-825, 480-491), was under the HUD's
`Hud/CommandCard/Cell6` (`GuiGetHoveredControl` printed it), so the GUI took the right click and `SelectionController`
never saw it. The worker's spot moves by some pixels run to run, so the row passed when the chosen offset fell outside the
card.

## Notes
- Fixed in the test only; no game code changed. A HUD control taking a click over it is the intended game behaviour, so
  skipping card pixels does not weaken what the row checks (the right click still has to reach the ground and send a move).
- QA (2026-11-25-1437) did not re-run the base to confirm the "red 3 of 3"; on the head the scene passes in QA's scene loop.
- Other scenes that push a right click at a computed screen point could meet the same HUD; none other failed in this loop.
