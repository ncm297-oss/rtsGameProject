# BUG-0122: M3-V2 nits: mid-word wrap on "Quartermaster's Depot", Shift-click floods duplicate Builds, ghost lags a panning camera, small reason text

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-07-1131, task M3-V2 |
| System | command card, build ghost (view) |
| Fixed by | 9998824 (M3-V3b: `CommandCard.FitNameSize`, Shift-click on the last-placed anchor skipped, `BuildGhost.ProcessPriority` 1, zoom-scaled deeper-red reason). Verified by QA 2026-10-07-1715: `CommandCardTest` prints `43 names, 1 shrunk below 12 px, 0 break mid-word; 'Quartermaster's Depot' at 10 px`, `25 Shift-clicks -> 3 Builds`, `pan: 36 frames ... 0 lagging`, reason text 25.8-35 px/em at 20-60 m. The double-Cancel aside from item 2 is not addressed (moved to BUG-0126 item 4) |

## Repro
1. Windowed: `& $env:GODOT --path game res://tests/CommandCardTest.tscn -- --shots <dir>` (`card-menu-basic.png`,
   `card-ghost-red.png`).
2. Headless: `res://tests/QaV2Test.tscn` prints the flood and double-Cancel figures.

## Expected / Actual
1. **Label wrap.** The Malazan basic menu's W entry reads "Quartermaster" / "'s Depot" on two lines: the 12 px name
   label (88 px wide) breaks the word at the apostrophe. Every other name wraps at a space.
2. **Shift-click flood.** 25 Shift-clicks on one green anchor in one frame enqueue 75 Builds (3 unqueued, then 72
   queued for the same anchor, which join the workers' own new site). Each click also plays the Command sound (after
   the Sfx gap). It does no harm in the sim, but it fills each worker's order queue with copies. One Build per anchor
   per ghost would be enough. Likewise, two Cancel presses before a tick enqueue two Cancels; the second finds nothing.
3. **Ghost under a panning camera** (found by reading the code, not measured). `BuildGhost` runs in World3D, before
   `RtsCamera` in tree order. It projects the cursor with the camera's transform from before this frame's pan, so
   while edge- or key-panning the box trails the cursor by one frame of pan.
4. **Reason text size.** At 30 m zoom on 1152 x 648, "Can't afford" over a red ghost is about 9 px tall. You can read
   it, but it's small next to the 3 m box. The red at 55% alpha over grass also reads as orange.

## Notes
1: shrink the font for long names, or wrap with `AutowrapMode.Word`, or allow three lines. 2: in `GhostClick`, skip a
placement whose anchor equals the last one placed by this ghost. 3: have the ghost read the camera after it moves
(process priority, or move the ghost after `RtsCamera`), keeping it after `SimRunner`. 4: scale `PixelSize` with zoom,
or use a deeper red.
