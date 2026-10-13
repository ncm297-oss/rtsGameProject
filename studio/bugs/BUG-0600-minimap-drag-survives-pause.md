# BUG-0600: BUG-0580's class on the minimap: a left press on the minimap released during the pause leaves the minimap dragging the camera after Resume

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-12-1930, task M6-V2 (QA) |
| System | view: `Minimap` (`_jumping`) vs the pause menu |
| Fixed by | 9ec4a56 (M6-V3, `Minimap._Notification` drops `_jumping` on pause / unpause); regression rows `QaM6V2Test.MinimapHeldIntoPause` (now a Check), `MinimapTest` pause row, `QaM6V3Test.HeldIntoPause` (released during the pause and after Resume: 0 m). QA 2026-10-13-0146 verified. |

## Repro
1. `dotnet build RtsGame.sln`, then `& $env:GODOT --headless --path game res://tests/QaM6V2Test.tscn`.
2. The scene prints (a probe, not a failure, so the loop stays green until the fix):
   ```
   KNOWN BUG-0600 probe (minimap): the camera moved 362.04 m on a motion over the minimap with no button held after resume
   ```
   Its control row (a full click on the minimap, no pause, then the same motion) moves the camera 0 m, so the pause is the cause.
3. By hand (expected from the code; QA did not run this windowed): press and hold the left button on the minimap, press Esc, release the button over the
   pause menu, press Esc (or Resume). Move the mouse over the minimap: the camera follows the cursor with no button held
   until the next left click on the minimap.

## Expected
The same rule BUG-0580 fixed for `RtsCamera` and `SelectionController` (M6-V2 brief item 5, docs/03 "Implementation
(M6-V2)", BUG-0580): a button let go while paused leaves no live drag after Resume.

## Actual
`Minimap._GuiInput` sets `_jumping` on the `select` press and clears it only on the `select` release; the minimap is
`Pausable` (under the match's HUD), so the release during the pause never reaches it, and every motion over the minimap after
Resume calls `JumpTo`. M6-V2 fixed the two nodes the brief named and not this third holder of the same state.

## Notes
Same fix as BUG-0580 (drop `_jumping` on `NotificationPaused` / `NotificationUnpaused`). The regression row is
`QaM6V2Test.MinimapHeldIntoPause`: when fixed, flip its `GD.Print` probe to `Check(moved < 1e-3f, ...)`. Worth a sweep for any other press-to-release flag in `game/scripts`
(grep for fields set on `IsActionPressed` and cleared on `IsActionReleased`); at this commit the three are
`RtsCamera._dragging`, `SelectionController._pressing` / `_boxing` and `Minimap._jumping`.
