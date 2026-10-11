# BUG-0490: M4-V7b nits: the stated tick range is already exceeded on both seeds, and the per-slot max is filled twice a frame

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-10-1954, task M4-V7b (QA, view track) |
| System | `game/tests/M4PlayableTest.cs` (`TickBudget` comment), docs/03 "Implementation (M4-V7)"; `game/scripts/UnitViews.cs`, `CombatViews.cs` |
| Fixed by | |

## Repro
1. **The measured range.** Run `res://tests/M4PlayableTest.tscn` headless several times beside a `dotnet test` (QA: the scene
   loop plus 6 more scene runs, 14 seed-runs, all PASS). Seed 1 took 2,927-3,081 ticks (3,081 in run 2), seed 6
   3,692-4,488 (4,488 in run 6, whose step 6 ran 1,553 ticks, the known long fight).
2. **Two copies a frame.** Read `UnitViews.Sync` and `CombatViews.Sync`: each calls `UnitViews.FillMaxHp(world, ...)` into
   its own array every frame.

## Expected
1. The `TickBudget` comment and docs/03 give the measured range (BUG-0462 item 5). They say seed 1 2,869-3,028 and seed 6
   3,710-4,372.
2. One per-slot max read a frame would do.

## Actual
1. Seed 1 reached 3,081 (above the stated 3,028) and seed 6 4,488 (above the stated 4,372, though matching the comment's
   "about 4,490" estimate for the long-fight case). The margin is still 1,012 ticks (18 %) on the worst run, so the budget
   holds. Only the stated range is low.
2. Two passes of 2,500 `World.MaxHp` calls a frame at full scale. That's cheap (bars and flash measured 0 B), but the
   two arrays are identical.

## Notes
For item 1, widen the range to what this QA round measured (seed 1 up to 3,081, seed 6 up to 4,488), or state it as "about
3,100 / 4,500 worst seen". For item 2, `CombatViews` could read the array `UnitViews` already filled, or the reverse. Low
priority; fold into the end-of-M4 view hardening.
