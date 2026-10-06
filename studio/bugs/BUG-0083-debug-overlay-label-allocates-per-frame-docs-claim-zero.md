# BUG-0083: Overlay-on label line allocates ~600 B per frame; docs/03 claims "0 bytes per frame on and off"

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-06-1255, task M2-5 |
| System | debug overlay (view: `game/scripts/DebugOverlay.cs`), docs/03 "Debug tooling" |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaM25Test.tscn` (QA scene, 2,000 units, 200 selected, a cached field, steady camera).
2. Read the line `allocation / 60 frames: ...`.

## Expected
Brief M2-5 criterion 5 and the docs/03 "Debug tooling" bullet: "0 bytes per frame on and off".

## Actual
```
allocation / 60 frames: layers on 0 B, whole frame on 56640 B, whole frame off 20640 B; off frame 0.0006 ms (label only)
```
`DebugOverlay.SyncLayers()` (nav check, goal, peek, relist, counts) allocates 0 bytes, as the developer
measured. But the whole `DebugOverlay._Process` frame with the overlay on allocates about 944 B per frame,
compared with about 344 B per frame off. The extra ~600 B per frame comes from the second label line: its
interpolated string has 7 formatted numbers, appended to the first line with `+=`. The off figure is the M2-1
label, which predates this task.

The docs disagree with each other. "Implementation (M2-5)" says "The label's strings allocate per frame, as
the M2-1 label always has", but the "Debug tooling" bullet says "0 bytes per frame on and off".

## Notes
- Not S2: the "off" half of the criterion can only hold if the pre-existing label is excluded, so the
  criterion evidently means the overlay's own work, and that work is at 0 bytes.
- Cheap fix if wanted: rebuild the label text only when a shown value changes, or at most a few times
  per second. At minimum, correct the "Debug tooling" bullet to say the overlay layers allocate 0 bytes
  and the label text does not.
- ~56 KB per second at 60 FPS feeds Gen0 GC. That matters for M2-7's "100 units at 60 FPS" check
  only if GC pauses show up.
