# BUG-0311: An Attack on a gone building's ghost ends ~1 m before the fog shows its ground; the ghost stays and can no longer be attacked

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (sim track) |
| Found | 2026-10-09-0724, task M4-V5 (view track) |
| System | combat / vision: `VisionSystem.UnitSeesFootprint` vs `FogStore.SeesFootprint` (M4-3b) |
| Fixed by | |

## Repro
1. `dotnet build RtsGame.sln`
2. `& $env:GODOT --headless --path game res://tests/QaGhostViewTest.tscn` (row "gone ghost"; add `-- --strict` to make it fail).
3. Log, seed 1: player 1's House site seen, cancelled unseen; 4 selected units right-click its ghost (4 Attacks, taken).
   ```
   orders ended at 361: footprint level 0, 0 cells visible [0 d 15.89 sight 16 lvl 0 ...] [13 d 15.96 ...] [14 d 15.92 ...] [15 d 15.69 ... mode None state Idle]
   ghost gone at 738: footprint level 0, 1 cells visible ...
   KNOWN BUG-0311: orders ended at 361, ghost gone at 738
   ```
   Seed 6: orders end at 339, the ghost goes at 862.

## Expected
docs/03 "Implementation (M4-V5)" / M4-3b: an Attack on a remembered building that is gone walks there and ends "when
its ground comes into sight", the same moment the player's last-known list drops the entry (and the view its ghost).

## Actual
`VisionSystem.UnitSeesFootprint` (the rule that ends the order and refuses a new Attack) measures sight from the unit to
the footprint rectangle's nearest point (`d <= sight`), while the fog marks cells visible by their centres, so at
15.7-15.96 m from the rectangle (sight 16) the order ends with 0 footprint cells visible. The units stop (or drift into
other fights), the ghost stays drawn for another 19-26 s, and a fresh right-click on it sends Attacks that `MayAttack`
refuses (it thinks the unit sees the footprint). On screen: a darkened building next to your army that nothing will
attack and that doesn't go away.

## Notes
Fix options (sim track): end the order on the fog's own rule (`fog.SeesFootprint`, the one that drops the entry), or
walk on until a footprint cell centre is within sight. The view mirrors the sim correctly (0 ghost mismatches over
1,408 frames with ghosts in the same run), so no view change is needed. Related: BUG-0310 (the opposite lag, a ghost
drawn over visible ground for up to 3 ticks).
