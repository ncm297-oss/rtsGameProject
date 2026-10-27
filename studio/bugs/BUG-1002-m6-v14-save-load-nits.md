# BUG-1002: M6-V14 save / load nits (a lone .replay in the save folder, the tick-0 camera, pause lines over the card)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-18-1236, task M6-V14 |
| System | Save / load (view: `SaveStore`, `LoadGameScreen`, `Match.LoadedFocus`, `PauseMenu`) |
| Fixed by | M6-VH4 (2026-10-26-2108): item 1 `SaveStore.LoneReplays` + `LoadGameScreen.AddLoneReplays` (`QaM6V14Test.ManyFiles`); item 2 `Match.LoadedFocus` start-location fallback (`QaM6V14Test.TickZero`); item 3 `PauseMenu.Panel` (`SaveLoadTest.SaveAndResume`) |

## Repro
`& $env:GODOT --headless --path game res://tests/QaM6V14Test.tscn` prints `QA NOTE` lines for items 1 and 2; item 3 is in
`SaveLoadTest`'s windowed shot `save-pause-loaded.png` (`-- --shots <dir>`).

1. **A lone `.replay` in the save folder can't be removed from the game.** Delete a `.sav` outside the game (or have
   `SaveStore.Delete` fail on a locked `.sav` after the player removes it by hand): its `<stem>.replay` stays in
   `user://saves`, is never listed (Load Game skips `.replay` files there by design) and so no Delete reaches it. Log:
   `QA NOTE: a .replay in the save folder whose .sav is gone is not listed, so Load Game can't delete it`. Harmless
   (it only holds a name `NewPath` then skips), but the folder can collect files the player can't see.
2. **A save made at tick 0 loads with the camera on the map centre**, not the player's base: at tick 0 the start bases
   are still queued commands, so `Match.LoadedFocus` finds no building or unit. Log: `camera at <125, 125> (the
   original started at its Town Hall, <36, 120>)`. Only reachable when the pause lands before the first tick.
3. **The pause menu's `SaveStatus` / `RecordNote` lines draw over the HUD's command card** (the two-line "A loaded match is
   not recorded ..." sits on the card's top row at 1152 x 648); readable, but the menu has no backdrop at that height.

## Expected
1. Either list orphan `.replay` files in the save folder as unreadable rows (for Delete), or have `SaveStore.List` /
   Delete sweep them. 2. The camera on the local player's start location (the map's start spot, or the queued hall's
   cell). 3. The lines inside the menu's own panel, or the HUD dimmed under the menu.

## Notes
None affects the sim or a resume's correctness (hash-equal runs verified by `QaM6V14Test`).

## Fix (M6-VH4, 2026-10-26-2108, dev)
1. `SaveStore.LoneReplays(dir)` lists the `.replay` files in the save folder with no `.sav` of their stem;
   `LoadGameScreen.Refresh` adds them to the replays (header reads: a Replay row, Watch works) or the unreadable replays (a
   row for Delete, counted in the skipped note). A save folder that is the replay folder adds nothing. `QaM6V14Test.ManyFiles`:
   the note is a `Check` (the junk lone `.replay` is row 200, an unreadable replay row; failed before: not listed), and its
   Delete removes it and nothing else.
2. `Match.LoadedFocus` falls back, before the map centre, to the footprint centre of the local player's Town Hall centred
   on its start location (where `StartBase.Plan` puts the queued hall). `QaM6V14Test.TickZero` checks the loaded match's
   `StartFocus` against the original's (failed before: the map centre, ~100 m off; equal now). The camera itself was
   compared before; it edge-pans toward the headless cursor's corner over the frames waited, so the row reads `StartFocus`.
3. The pause menu's column sits in its own opaque panel (`PauseMenu.Panel`, `PanelColor` alpha 1, a thin border), so the
   save lines no longer draw over the HUD's command card through the dim backdrop. `SaveLoadTest`: the panel holds the
   note, the status line and Quit (rects enclosed); windowed `save-pause-loaded.png`.
