# BUG-1031: A count typed into Forests / Gold mines is lost when Start is clicked with press and release in one frame; a blanked box shows nothing

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-18-1814, task M6-V15 |
| System | Skirmish setup (view: `SkirmishSetup.MatchArgs`, `ForestsPick` / `MinesPick`) |
| Fixed by | M6-VH4 (2026-10-26-2108): `SkirmishSetup.MatchArgs` applies both boxes first (`Commit`: `SpinBox.Apply` + redraw); Enter and a value change redraw the box; `QaM6V15Test.TypedCounts` checks 11 and the emptied box |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V15Test.tscn -- --only setup`; the log prints
   `QA NOTE: BUG-1031: typed 11 mines then press+release in one frame on Start: the match asks 9 mines`.
2. The steps: Skirmish, click into Gold mines (it shows 9), type 11 without Enter, then click Start with the mouse
   press and release in the same frame (a touchpad tap, or any click shorter than a frame).

## Expected
The match uses the number shown in the box (11). The brief's QA focus: "typed spin-box values applied before Start".

## Actual
The match asks 9 mines, the old value. The SpinBox only takes typed text on Enter or when its line edit loses focus,
and the focus change from a one-frame click lands after Start's `pressed`. A held click (2 frames between press and
release) does apply it: the same scene's typed 7 / 9 row passes. The dev's report lists "spin boxes apply on
Enter/focus-out" as a deviation that wasn't tried by hand.

Second, smaller item (S4): typing nothing (empty text) and pressing Enter keeps the pick (12) but leaves the box showing
an empty field, so the screen doesn't show the count the match will use (`QA NOTE: mines typed '' + Enter: the box
shows '', the pick is 12`).

## Notes
Likely fix: call `ForestsSpin.Apply()` / `MinesSpin.Apply()` (Godot 4.3+ `SpinBox.apply()`) at the top of `MatchArgs`
(or in Start's handler), then read the values. That also redraws the box. When fixed, turn the BUG-1031 note in
`QaM6V15Test.TypedCounts` into a `Check` (want 11).

## Fix (M6-VH4, 2026-10-26-2108, dev)
`MatchArgs` starts with `Commit(ForestsSpin)` / `Commit(MinesSpin)`: `SpinBox.Apply()` (what Enter does) then a redraw of
the box with its value. Each box also redraws on its own `TextSubmitted` (after the SpinBox's handler, so an emptied box or
junk shows the count again) and on `ValueChanged` (the SpinBox redraws its text only later in the frame, so without it a
value set in code, e.g. the floor raised by Add player, was put back by the next `Apply`: `ShellTest.SpeedAndCounts`
caught that). `QaM6V15Test.TypedCounts`: the one-frame click's note is a `Check` (11 mines; failed before: 9), and the
"box shows" note for each Enter row is a `Check` too (failed before for `''`).
