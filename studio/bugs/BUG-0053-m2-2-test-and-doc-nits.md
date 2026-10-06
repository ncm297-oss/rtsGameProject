# BUG-0053: M2-2 nits: facing test can't see a yaw sign error, a stale SimRunner remark, picker box height in docs

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-05-1609, task M2-2 |
| System | view: UnitViewsTest, SimRunner docs, docs/03 |
| Fixed by | M2-H1 (3dc0568): `UnitViewsTest` checks facings +y, -y, pi/4, -3pi/4; SimRunner remark reworded; docs/03 says -1 to 9 m |

## Repro
1. `game/tests/UnitViewsTest.cs` checks facing with one unit walking +x only (sim facing 0). At
   theta = 0 the correct yaw `-theta - pi/2` and a sign-flipped `theta - pi/2` both give `-pi/2`, so
   the test passes for either sign. (The code is correct: QA's `game/tests/QaM22Test.tscn` checks 64
   angles, worst forward error 2.75e-7.)
2. `game/scripts/SimRunner.cs` class remark: "The view changes sim state only through `Simulation.Tick`
   here and, later, `Enqueue`." Since M2-2, `Enqueue` is called from `Match` and `SelectionController`,
   not from SimRunner.
3. docs/03 "Implementation (M2-2)", right-click move: "the ray is clipped to the map's box (0 to 9 m
   high)". `GroundPicker.TryPick` clips Y to [-1, top + 1] = [-1, 9].

## Expected
Tests that can fail on the property they name; comments and docs that match the code.

## Actual
As above.

## Notes
Suggested: walk the facing unit along +y or diagonally too (or keep QA's angle sweep as the guard);
reword the SimRunner remark; say "-1 to 9 m" in docs/03.

## Re-check (2026-10-06-0905, M2-H1 commit 3dc0568): fixed
Verified: `UnitViewsTest.tscn` prints UNITVIEWS TEST PASS with the four new facing checks (a sign-flipped yaw would turn +y into -y, which the check would see). The SimRunner remark now names Match and SelectionController. docs/03 "Right-click move" reads "-1 to 9 m", which matches `GroundPicker.TryPick`.
