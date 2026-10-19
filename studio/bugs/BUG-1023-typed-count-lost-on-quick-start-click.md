# BUG-1023: A count typed into Forests / Gold mines is lost when Start is clicked with press and release in one frame; a blanked box shows nothing

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-18-1814, task M6-V15 |
| System | Skirmish setup (view: `SkirmishSetup.MatchArgs`, `ForestsPick` / `MinesPick`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V15Test.tscn -- --only setup`; the log prints
   `QA NOTE: BUG-1023: typed 11 mines then press+release in one frame on Start: the match asks 9 mines`.
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
(or in Start's handler), then read the values. That also redraws the box. When fixed, turn the BUG-1023 note in
`QaM6V15Test.TypedCounts` into a `Check` (want 11).
