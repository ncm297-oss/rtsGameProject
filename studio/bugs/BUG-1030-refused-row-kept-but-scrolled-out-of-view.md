# BUG-1030: A refused row far down Load Game stays picked but the list jumps to the top, so the picked row is off-screen

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-18-1814, task M6-V15 |
| System | Load Game screen (view: `Main.ShowLoadNote`, `LoadGameScreen.Select` / `Refresh`) |
| Fixed by | M6-VH4 (2026-10-26-2108): `LoadGameScreen.Select` scrolls the picked row into view (`Tree.ScrollToItem`, again deferred after layout); `QaM6V15Test.RefusedRows` checks the bottom and middle rows |

## Repro
1. `& $env:GODOT --path game --resolution 1280x720 res://tests/QaM6V15Test.tscn -- --only rows --shots <dir>`, open
   `qa-v15-refused-bottom.png`; the log prints `QA NOTE: BUG-1030: ... item y 6204-6235, scroll 0, tree height 340`.
2. By hand: a save folder with ~20 rows and a damaged save (header intact, a byte changed in the body) as the oldest row.
   Scroll to the bottom of Load Game, pick the damaged save, press Load.

## Expected
BUG-1000's fix: the refused file's row "stays picked under its note, so Delete removes that file" (docs/02 "Game flow" 6).
The player should be able to see which row is picked before pressing Delete.

## Actual
The refused row is picked (`SelectedIndex` 199 of 200, correct), but `ShowLoadNote` runs `Refresh`, which rebuilds the
Tree and leaves the scroll at 0, and `Select(row)` doesn't scroll. The screen shows the top ten rows with nothing
highlighted, the note "This save is damaged: ..." and an enabled Delete. Pressing Delete removes a file the player can't
see. It is the right file (the refused one), but the screen doesn't show that. Any refused row past the first screenful
(about 9 rows at 1280 x 720) is affected.

## Notes
Likely fix: `List.ScrollToItem(item)` (or `EnsureCursorIsVisible()`) in `LoadGameScreen.Select`, or in `ShowLoadNote`
after the select. When fixed, turn the `QA NOTE: BUG-1030` line in `QaM6V15Test.RefusedRows` into a `Check` (the
visibility test is already computed there).

## Fix (M6-VH4, 2026-10-26-2108, dev)
`LoadGameScreen.Select` calls `List.ScrollToItem(item)` and once more deferred (`ScrollToPicked`), so the refused row that
`Main.ShowLoadNote` re-picks after the rebuild is on screen. The `QA NOTE: BUG-1030` in `QaM6V15Test.RefusedRows` is a
`Check` now (failed before the fix: item y 6204-6235 in a 340 px tree), plus a second `Check` for the refused middle row
(`PickedOnScreen`). Note for QA: `Tree.GetItemAreaRect` is already in the tree's scrolled coordinates, so the note's
visibility test subtracted the scroll twice (it only showed once the list scrolled); the `Check` compares the rect with
the tree's height directly. Windowed: `qa-v15-refused-bottom.png` shows the picked row at the bottom of the list.
