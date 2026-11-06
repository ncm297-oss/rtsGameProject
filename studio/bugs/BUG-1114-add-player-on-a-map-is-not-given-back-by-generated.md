# BUG-1114: an Add player on a map file is forgotten: Generated gives back the count from before the first map, not the player's last

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-06-0926, task M6-VH5 (QA, standard) |
| System | view: skirmish setup, `SkirmishSetup.ApplyMap` / `AddSlot` (`_slotsBeforeMap`) |
| Fixed by | |

## Repro
1. `& $env:GODOT --headless --path game res://tests/QaVh5Test.tscn`: the log prints
   `KNOWN BUG-1114: 3 slots, Raraku, Vathar, Add player (4), Raraku, Generated: 3 slots (want 4)`.
2. By hand: Skirmish, Add player once (3 slots), Map → Raraku (2 slots), Map → Vathar Crossing (3 slots, M6-VH5's give-back),
   Add player (4 slots), Map → Raraku (2 slots), Map → Generated: **3** slots.

## Expected
docs/03 (M6-V18 / M6-VH5): "Generated again gives back ... the slot count the cap took away (unless the player removed a
slot meanwhile)". The second Raraku pick took two slots (4 → 2), so Generated should show 4, the player's last count.

## Actual
3 slots. `_slotsBeforeMap` is recorded only the first time a cap bites (`if (_slotsBeforeMap < 0)`), `RemoveSlot` clears it
(the player's choice wins), but `AddSlot` leaves the old, smaller count in place, so the next cap and Generated restore
the count from before the first map. Same shape before M6-VH5 (Add twice on Vathar Crossing after Raraku), but the new
give-back makes the path natural: the player sees 3, adds the 4th, and loses it.

## Notes
- Likely fix: in `AddSlot`, when `_slotsBeforeMap >= 0`, raise it to `SlotCount` (or clear it, as `RemoveSlot` does).
- `game/tests/QaVh5Test.cs` `SlotGiveBack` has the row as a print-only line; turn it into a `Check(afterAdd == 4, ...)`
  with the fix.
