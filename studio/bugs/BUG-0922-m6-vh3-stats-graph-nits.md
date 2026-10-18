# BUG-0922: M6-VH3 stats graph nits: the zero-line band covers the time axis, the contrast row can't fail, 13 labels past 16 h

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-17-1624, task M6-VH3 (QA) |
| System | post-match stats screen (`game/scripts/ArmyValueGraph.cs`), `game/tests/StatsScreenCheck.cs` |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. `& $env:GODOT --path game --resolution 1280x720 res://tests/QaM6VH3Test.tscn -- --shots <dir>`; open
   `qa-vh3-stats-20s-1280x720.png` (a 20 s four-seat match, every seat's army value 0).
3. Headless, the same scene prints the label notes and the contrast note.

## Expected
docs/03 "Implementation (M6-V11)" / M6-VH3: lines at one value "show side by side"; the time axis has "at most nine
labels"; BUG-0890's check guards the grey seat's readability.

## Actual
Three small things, filed together:
1. **The zero band hides the x axis.** Four seats at 0 are drawn at -4.5 / -1.5 / +1.5 / +4.5 px, each over a 5 px light
   outline, on top of the axis: from the first sample (0:10) to the end the 2 px axis line disappears under a ~16 px
   striped band (pixel rows 530-545 at x 900; the axis is at 538), and the lower half sits under the plot, toward the
   time labels. The four colours are told apart (the fix works), but the baseline itself is gone where it matters.
   Drawing the axis after the lines, or offsetting only upwards, would keep it.
2. **The contrast row in `StatsScreenCheck.Readable` can't fail.** It passes when the line *or* the outline is 3:1 on
   the backdrop; the outline is white at 40 % over the fixed backdrop, 3.80:1 for every seat, so any line colour (even the
   backdrop's own) passes. For the record: Iron `#4B4F55` is 2.35:1 on the backdrop and 1.62:1 on its own outline (it reads
   as a darker core in a light halo).
3. **Past 16 hours the time axis has more than ten labels.** The largest step is 2 h, so a 24 h span (the replay cap)
   gets 13 labels (0:00 ... 1440:00, 56 px apart, still readable); `StatsScreenCheck.TimeAxis` wants at most 10 and
   docs/03 says at most nine. The old minute steps ended at 120 min too, so this is not new; out of normal play.

## Notes
Found while verifying BUG-0890. None of it hides a seat's line or misreads a value. `QaM6VH3Test` records item 3 as a
note (spans over 16 h) and prints the contrast numbers.
