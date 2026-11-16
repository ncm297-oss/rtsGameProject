# BUG-1500: M6-VH8 nits: a Stop ring and a Hold ring from two units at one point are drawn at one radius; two docs numbers off

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-15-1536, task M6-VH8 |
| System | queued-orders drawing (`OrderPathView`), docs/03 |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM6VH8Test.tscn` (row R), or windowed with
   `-- --shots <dir>` for `rings-stop-and-hold-shared-point.png`.
2. Two Heavy Infantry each walk a Move, then a queued Move to one point q; unit a then has a queued Stop, unit b a
   queued Hold. Select both.

## Expected
docs/03 "Implementation (M6-VH8)": "A second no-place entry at the same point (a Stop, then a Hold) is the next ring
out ... the rings touch and never overlap". Each order the selection holds at a point can be seen.

## Actual
Item 1. The ring level is counted per unit (`level` in `OrderPathView.Sync`), and the marker key is (point, level, kind).
So unit a's Stop and unit b's Hold at one point are both level 0, both drawn at radius 1.20 m (default zoom). One ring
covers the other and only one colour shows (the picture shows one cream ring). Row R prints:
`(R) a Stop (unit a) and a Hold (unit b) at one point: [Stop level 0 radius 1.20 m; HoldPosition level 0 radius 1.20 m]`.
The same happens past `MaxRingLevel` for one unit (Stop, Stop, Hold, Stop: Hold and Stop both at level 2), which is
by design (docs say "at most three rings deep").

Item 2. `OrderPathView.MarkerFullZoom`'s summary says "at the farthest zoom (60 m) a marker is 1 m wide". The marker is
1 m in **radius** at 60 m, so 2 m wide (the scene row prints `zoom 60: marker 19.1 px wide (2.00 m)`; docs/03's own
table says 2.0 m).

Item 3. docs/03 "Implementation (M6-VH8)" says a soldier's line is "about 0.16 m higher than its own clips need".
Measured by row O over the clips each type plays (idle / walk / attack, plus the three work clips for a worker): a
soldier's highest bone is `Head` in `walk` at 1.568 m, so the line (1.961 m) is 0.39 m over it, 0.21 m higher than its
own clips need. For a Teblor Warrior / Breaker / Shaman: `Head` in `walk` at 2.352 m, line 2.941 m, 0.59 m over it
(0.32 m higher). The Javelin Thrower's own `attack` reaches 2.632 m (0.31 m under the line).

## Notes
Item 1: the level could be counted per point across units (a second pass, or the key without the kind for no-place
entries at a point) so different no-place kinds at one point nest too. Rare in play: it needs two units with different
end orders at one shared point. Items 2-3 are doc wording only. The pictures show the overlays read well just above the
heads, so item 3 is input for the Producer's Question 1, not a request to move the line.
