# BUG-0580: A mouse release swallowed by the pause leaves a drag live: after Resume the camera follows the cursor (middle-button drag) or a selection box follows it (left-button box) with no button held

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-11-0634, task M6-V1 (QA) |
| System | view: pause menu (`Main`) vs held-button state in `RtsCamera` (`_dragging`) and `SelectionController` (`_pressing` / `_boxing`) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`, then `& $env:GODOT --headless --path game res://tests/QaShellTest.tscn`.
2. The scene prints two probe lines (they print, not fail, until this is fixed):
   ```
   KNOWN BUG-0580 probe (box-select): a visible selection box after resume with no button held: Box (150, 90)
   KNOWN BUG-0580 probe (camera drag): the camera moved 8.43 m on a motion with no button held after resume
   ```
3. By hand in a windowed skirmish: hold the middle mouse button (camera drag), press Esc, release the button over the
   pause menu, press Esc (or Resume). Move the mouse: the map scrolls with it until the next middle click. Same with a
   left-button box drag: the selection rectangle stays on screen and follows the cursor until the next left click.

## Expected
Brief M6-V1: "input swallowed while paused". Swallowing a press is fine, but a button let go while paused should not
leave the match thinking it is still held; after Resume the camera and selection behave as if no button is down.

## Actual
`RtsCamera._UnhandledInput` sets `_dragging` on the `camera_drag` press and clears it only on its release;
`SelectionController` keeps `_pressing` / `_boxing` until the `select` release. Both nodes are `Pausable`, so the release
that happens while the tree is paused never reaches them. After Resume every mouse motion moves the camera
(`MoveFocus(-motion.Relative ...)`) or redraws the box.

## Notes
Possible fixes (the dev's call): clear held-button state when the match pauses or resumes (a `NotificationPaused` /
`NotificationUnpaused` handler in the two nodes, or `Main.PauseMatch` telling them), or check `Input.IsMouseButtonPressed`
on motion. When fixed, change the two `GD.Print` probes in `game/tests/QaShellTest.cs` (`PauseBurstAndLeaks`) into
`Check`s so the loop guards it.
