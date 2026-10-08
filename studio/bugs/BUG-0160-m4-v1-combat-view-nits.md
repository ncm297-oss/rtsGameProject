# BUG-0160: M4-V1 nits: F12 line covers "K / L", a unit hit before its first frame never flashes, corpses read black for both teams, F12 fallback labels in C#

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-0313, task M4-V1 |
| System | combat views / HUD (`UnitViews` + `ViewApi.HitFlash`, `CombatViews`, `ResourceBar`, `DebugOverlay`) |
| Fixed by | |

## Repro
1. **F12 overlap.** `& $env:GODOT --path game res://tests/DebugOverlayShot.tscn`, open
   `%APPDATA%/Godot/app_userdata/RtsGame/debug-overlay.png`: the overlay's second line ("... tick avg 0.480 ms
   worst 4.063 ms (64) arrows ...") runs through the resource bar's new "K 0 / L 0" label at the top right; both are
   unreadable where they cross. (The developer noted it; dev overlay only.)
2. **First-sight hit.** `QaV5Test` phase 2 (headless): a Heavy Infantry spawned next to Raiders and ticked without a
   frame until its hp fell, then one frame: the bar shows (correct) but `UnitViews.Flash.IsLit` is false. `HitFlash`
   takes a new generation's hp as its baseline, so a unit that appears and is hit between two frames (at 8x, up to 5
   ticks a frame: a unit trained or rallied into a fight) never flashes for that hit. Criterion 3 says "every unit whose
   hp fell this tick flashes".
3. **Corpse tint.** `& $env:GODOT --path game res://tests/CombatViewTest.tscn -- --seed 1 --shots <dir>`,
   `combat-seed1-corpses.png` / `combat-seed6-corpses.png`: the corpse discs (owner colour x 0.35, unshaded) are
   near-black for both the grey-blue and the gold player at zoom 30; the brief asks for "faction-tinted".
4. **Fallback literals.** `DebugOverlay` initialises `_killsName = "K", _lossesName = "L"` in C# (used if `UiText.Shared`
   is null). Dev overlay only, and the overlay has other English literals, but rule 8 says player-facing text never
   comes from C#; `ResourceBar` falls back to an empty label instead.

## Expected
1. The F12 line and the HUD labels don't overlap. 2. A unit whose hp is below its type's `hp` the first time the view
sees it could count as hit when its hp is also below what it spawned with (or compare a new unit against the type's max).
3. A corpse's team is readable (e.g. a lighter shade, or a team-coloured rim). 4. No label literal in C#.

## Actual
As above. None of these breaks a criterion's main path; the developer's and QA's scenes are green.

## Notes
- Also noted, not a bug (documented by the developer): a replay recorded from a `--no-combat` match needs
  `ReplayPlayer.Run(..., combat: false)` until replay format 4 carries the switch.
- The K / L label and the F12 line allocate a string when a count changes (by design: rebuilt on change only).
