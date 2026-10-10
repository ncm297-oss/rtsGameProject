# BUG-0340: Selection panel allocates its "+N" overflow text whenever the overflow count changes

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-09-1155, task M4-V6a (reported by game-dev, confirmed by QA) |
| System | HUD (selection panel) |
| Fixed by | ed18d2f (M4-V6b), verified 2026-10-10-0215 |

## Repro
1. Select more units than the portrait grid shows (`PortraitGrid.MaxPortraits`), e.g. 100 soldiers in a battle.
2. Let selected units die while measuring `GC.GetAllocatedBytesForCurrentThread()` around `SelectionPanel` sync.

## Expected
The view rule (docs/03, "Per-tick code must not allocate" and the 0 B steady-frame rows): text a frame can change is
pre-built or cached, like the command card's `_secondsText` cache (M4-V6a).

## Actual
`game/scripts/SelectionPanel.cs` ~line 389:
`if (over > 0) _overflow.Text = string.Create(CultureInfo.InvariantCulture, $"+{over}");`
builds a new string (~56 B) on every frame the overflow count changes, i.e. on each death in a large selection.
`AbilityViewTest.SyncAll` leaves the panel out of its 0 B measurement because of this.

## Notes
Event-driven, not per steady frame, so small. Fix: a cached `"+N"` string array sized by the unit capacity (or by
`capacity - MaxPortraits`), then put the panel back into `AbilityViewTest`'s 0 B span.
