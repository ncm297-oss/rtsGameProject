# BUG-0275: M4-3b nits: a ghost can't tell a site from a building; a test seam in Rts.Sim; a stale test summary; a one-tick lag the fuzz exemptions miss

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-09-0125, task M4-3b (QA full) |
| System | vision (last-known list), tests |
| Fixed by | items 3 and 4: sim 1ab51c5 (`TowerTests` summary; `Stress/CombatFuzzTests` allows the update-tick lag and asserts the target is gone a tick later). Items 1 and 2 open |

## Repro
1. **A site's ghost.** Player 0 sees player 1's construction site (1 hp, `UnderConstruction`), then looks away.
   `World.Fog.Ghosts(0)[slot]` holds `Generation`, `TypeId`, `Cell`, `Owner` only.
2. **Test seam in production.** `sim/Rts.Sim/Vision/FogStore.cs` `ExploreAllForTests()` (internal; called only from
   `sim/Rts.Sim.Tests/TestSim.cs` `Explored`).
3. **Stale summary.** `sim/Rts.Sim.Tests/Combat/TowerTests.cs` class summary ends "a unit hit by a tower answers it".
4. **One-tick lag.** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GhostAttackHostileFuzz"`, seed 11,
   tick 2065 (a fog update tick): unit 41 still holds gone building (29, gen 1) in an ordered Attack after its owner's
   list dropped the entry in phase 12; the next phase 7 lets it go.

## Expected
1. docs/02 "Vision and fog of war": explored fog shows *last-seen* buildings. A site seen as a site should be remembered
   as one, so the view can draw a foundation rather than a finished building.
2. Test-only code stays out of `Rts.Sim`, or is marked as a seam the way `Fog.Reveal(..., int.MaxValue)` is used.
3. docs/01 M4-3b row (d): a tower's hit starts no retaliation; the victim takes the tower only through its own scans, and
   only within sight (`AUnitHitByATowerOutOfItsSight_DoesNotAnswerIt`).
4. The dev's exemptions in `Stress/CombatFuzzTests` (`live || remembered`) and `Stress/FogHostileFuzzQaTests` assume a
   unit never holds a gone building that its owner no longer lists.

## Actual
1. The entry has no under-construction flag, so the view (M4-V5) can't draw a remembered site as a site.
2. The method ships in the library (harmless, internal).
3. The summary contradicts the behaviour and the test below it.
4. For one tick after a fog update the invariant `live || remembered` is false (benign: nothing swings, and the target is
   dropped at the next phase 7). `Stress/GhostAttackHostileFuzzStressTests` allows it only on an update tick and
   checks it is gone a tick later; the two edited fuzzes would fail on it if a seed ever hit it.

## Notes
None of these change play today. Item 1 matters when the view draws ghosts. A cheap fix is a `bool Site` on
`BuildingGhost`, refreshed with the rest (and hashed). For item 4, the exemptions could allow the update-tick lag the way
the new QA fuzz does.

## QA re-check (2026-10-09-0125, round 1)
Items 3 and 4 fixed in 1ab51c5 and verified. The new `CombatFuzzTests` exemption applies only when the tick just run is a fog
update tick (`World.TickNumber` is bumped at the end of `Tick`, so `IsUpdateTick(ran)` names the right tick), only to an
ordered building target, and the next tick asserts the unit no longer holds that handle; a unit that keeps a forgotten
building is still caught. `CombatFuzzTests` and `GhostAttackHostileFuzzStressTests` pass. Items 1 (no site flag on a
ghost; matters when M4-V5 draws ghosts) and 2 (`ExploreAllForTests` seam in Rts.Sim) are unchanged, so the bug stays open
at S4 for the Producer to schedule or wontfix.
