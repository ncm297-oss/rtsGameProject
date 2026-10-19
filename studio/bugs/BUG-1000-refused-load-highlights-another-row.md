# BUG-1000: A refused Load highlights row 0 under the refusal note, so Delete next removes a different (good) file

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-18-1236, task M6-V14 |
| System | Load Game screen (view: `Main.ShowLoadNote`, `LoadGameScreen.Refresh`) |
| Fixed by | M6-V15: `Main.ShowLoadNote` picks `RowOf(refused path)` after the refresh (row 0 when it is gone). Regression rows: `SaveLoadTest.Corrupted` (each refused damaged save keeps its row) and `SaveLoadTest.RefusedRowKept` (refused Load and refused Watch, then Delete removes exactly that file; refused again with it gone, row 0). |

## Repro
1. Run `SaveLoadTest` windowed with shots: `& $env:GODOT --path game res://tests/SaveLoadTest.tscn -- --shots <dir>`, open `save-load-refused.png`.
   Or by hand: have a good save and a damaged one (a flipped body byte) in `user://saves`, open Load Game, pick the damaged
   row, press Load.

## Expected
The note explains why the picked file was refused, and the picked row stays highlighted, so the player's next action
(Delete) acts on the file the note talks about (docs/02 "Game flow" 1: "a save that won't load says why";
BUG-0921's rules: refused files stay listed for Delete).

## Actual
`Main.LoadSavedGame` -> `ShowLoadNote` -> `LoadGameScreen.Refresh` re-reads the folder and calls `Select(0)`. The note
reads "This save is damaged and can't be read." while the highlight sits on row 0, which in the screenshot is the newest,
**good** save (Length 0:25, tag Save). Pressing Delete now (the obvious next step after "this save is damaged") deletes
the good save, not the damaged one; pressing Load loads the good one, contradicting the note. Same path for replays
(`Refused` -> `ShowLoadNote`), so a refused Watch has the same jump (inherited from M6-V9, now more likely with saves'
seven refusal codes).

## Notes
Fix idea: in `ShowLoadNote`, remember the refused path and after the refresh `Select(RowOf(path))` (falling back to 0
when it is gone). A regression row: refuse a damaged save that is not row 0, assert `RowPath(SelectedIndex)` is it.
