# BUG-0363: M4-4b-2 zone nits: far blockers still cost scan time, a 3-tick window after a storm appears, a stale doc line

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | partly fixed (items 1-2 by M4-H2; item 3 a note, documented in docs/03) |
| Found | 2026-10-10-0215, task M4-4b-2 |
| System | vision / combat scans / zones |
| Fixed by | 5c2de85 (M4-H2): item 1 `VisionSystem.BlockerNear` (far-blocker ratio below); item 2 `Vision/ZoneVisionTests.TheMask_IsStampedOnTheVisionCadence` (a unit in a new storm is hidden at once) |

## Repro
1. Run `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ZoneScalePerfTests" --logger "console;verbosity=detailed"`.
   It prints `500-unit brawl: 1.888 ms with no zone, 2.322 ms with 16 blockers, 3.490 ms with 64`. This was Debug, with
   another test run going at the same time, so read the ratios, not the absolute times.
2. The developer's own `Vision/ZoneVisionTests.TheMask_IsStampedOnTheVisionCadence` shows `Fog.CanSeeUnit` is true for a
   Raider in a storm made one tick earlier.

## Expected / Actual
1. **Far blockers still slow every scan.** While any blocking zone lives, `ScanUnits` drops its one-level shortcut. Every
   candidate then goes through `UnitSeesUnit`, whose `ZoneHides` walks every zone slot. Storms more than 100 m from the fight
   still add 23 % to a 500-unit brawl's tick with 16 storms, and 85 % with 64 (the store full). This is within budget. A cheap
   test, such as a bounding box or "is any blocker near the scanner", would remove most of it.
2. **3-tick window after a storm appears.** The `ZoneHides` summary says "live zones, not the last fog update's: the rule
   holds from the tick a zone is made". But when that check fails, `UnitSeesUnit` falls back to `fog.SeesUnit`, whose visible
   bits can be up to 3 ticks old. So for up to 3 ticks after a storm appears, a Crossbowman outside can still acquire a Raider
   inside. The docs/03 cadence sentence covers the fog, not this. Either reword the summary, or accept the window and document it.
3. **A tower's wind-up still fires into a new storm.** A tower that started a wind-up before the storm appeared still fires
   when the wind-up ends. Wind-ups check only the gap, as before M4-4b-2, and units behave the same way. The tower drops the
   target at its next due check. Recorded in
   `QA/ZoneQaTests.ATower_LosesItsTargetInsideAnEnemyStorm_AndShootsABlindedUnitOutsideOne`.

## Notes
None of these affects an acceptance criterion. Item 1 matters only when many storms are live at once.

## QA verification (2026-10-10-0624, M4-H2)
Verified by QA 2026-10-10-0624 (M4-H2). Item 1: `ZoneScalePerfTests` paired runs, base 63efab2 vs 5c2de85 (3 rounds each, no other test host running): 16 storms 1.12-1.23x -> 1.01-1.11x, 64 storms 1.74-1.90x -> 1.24-1.38x of the zone-free tick; 0 bytes a tick. `EmptyTick(2500)` paired medians 482.9 us base / 484.8 us head (8 runs each; both sides cross 500 us now and then on this machine, BUG-0140's thin margin). Item 2: fixed by the fog's per-zone records (a new zone has none, so it hides at once); docs/03 says so. Item 3 stays a documented note.
