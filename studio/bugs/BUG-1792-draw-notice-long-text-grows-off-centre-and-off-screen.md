# BUG-1792: The draw notice grows to the right, off centre and off screen, when its text is wider than 800 px; its doc comment gives a stale width

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-11-26-0207, task M6-V29 (QA) |
| System | HUD: `DrawNotice` |
| Fixed by | M6-VH10 (2026-11-26-1847, view track) |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6V29Test.tscn`: the scene adds a second `DrawNotice` on the match's
   HUD with a `ui.json` whose `hud.draw_countdown` is 180 characters plus `{0}`, at 1152 x 648. It prints
   `note: long text at 1152 x 648: label (176, 86), (3521, 30) (min width 3521), on screen False`.
2. `DrawNotice.cs`, the `Width` summary: "the English line at its font size is about 640 px"; the scene and docs/03 measure 594 px.

## Expected
A longer line (a translation, the pre-release rename pass, which must be data-only per CLAUDE.md rule 8) stays centred and
on screen: wrap (autowrap with a taller rect), shrink, or at least grow both ways (`GrowHorizontal = Both`).

## Actual
The label keeps its left edge at 176 px and grows to the right only (Godot's default grow direction), so any text over
800 px is off centre, and over 976 px runs off the right edge at 1152 x 648. The shipped English line (594 px) is fine.

## Notes
Out of the documented range today (one English line), hence S4. Fix the stale 640 px in the same edit.

## Fix (M6-VH10)
The label wraps (`AutowrapMode.WordSmart`: a word wider than the line is split) inside its 800 px, grows down from 86 px
and both ways if ever wider (`GrowHorizontal.Both`). The `Width` comment now gives the measured 594 px. New scene
`ViewHardenVh10Test` (N): at 1152 x 648 and 1920 x 1080 the shipped line is drawn where it was (800 x 30 from 86 px down,
centred: (176, 86) and (560, 86)); a 120-character sentence (800 x 59, two lines) and 120 letters with no space (800 x 90)
are centred within 0 px and wholly on screen. On the base the two long texts grew right from the left edge (1154 and 2233
px wide, 177 and 716.5 px off centre, off screen at 1152 x 648). `QaM6V29Test`'s 200-character note now reads on screen
True (800 x 152).
