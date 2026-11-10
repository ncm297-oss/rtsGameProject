# BUG-1161: M5-H6 nits: the reach filter reads the start's centre; a start proxy can lie out of sight; a lost house rebuilt twice

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-09-1336, task M5-H6 (QA inspection) |
| System | AI: the far expansion (`FarExpansion.NextStart`), the hunt (`MilitaryManager.StartProxy` / `NextHuntStart`), the rebuild (`BuildOrderExecutor.ReopenLost` with `EconomyManager.House`) |
| Fixed by | |

Three small findings in M5-H6's shipped code. None shows on the shipped data or the three map files today; each has a
skipped QA row in `sim/Rts.Sim.Tests/QA/EndGameH6QaTests.cs` that fails when its `Skip` is removed (checked).

## Repro
1. **The reach filter compares the start's centre, `Candidate` compares the mine's.**
   `EndGameH6QaTests.AStartJustPastTheReach_WithItsMineInside_IsStillWalkedTo` (skip "BUG-1161 item 1"). On the far scene
   (64 flat map, four starts) a gold mine 6 cells from free start 2's centre, toward the hall; `ai.farExpandReach` set halfway
   between the mine and the centre. Output: `hall <20, 64>; start 2 at 66.5 m, its mine at 56.9 m (footprint 2x2), start 3 at
   65.1 m; reach 61.7` / `armed 1, first leg at -1, expansions 0, next start -1`. The mine is a valid `Candidate` once seen,
   but `NextStart` skips both starts, so the leg never walks there and the seat never expands. Before M5-H6 the leg walked
   to start 2 (and would have seen the mine on the way to the centre). A start's mines lie up to
   `MapGenParams.StartMineDistance` (18 m) from its centre, so "no mine there could be a candidate" (the code comment) is not
   true near the reach's edge.
2. **A start proxy can be farther from the start's centre than the army sees.**
   `EndGameH6QaTests.AWorkerAtAFreeStartsCentre_IsSeenWhenTheHuntLooksAtThatStart` (skip "BUG-1161 item 2"). On the
   developer's own 512 m scene (`EndGameHardeningTests.AHunt_SweepsTheFreeStartsFirst_*`) free start 3's centre is
   (81, 401) and its even-pass proxy (76.8, 384), 17.6 m off; Heavy Infantry see 14 m. A lone worker at start 3's centre:
   `seen never; targets 20:<76.8, 384> 2260:<409.6, 409.6> ...` (the hunt "looks at" start 3 first, stands at its proxy, does
   not see the worker, goes on to start 2 and the sweep, and does not see it in 12,000 ticks). On the even pass a proxy can
   be up to half a scout cell (25.6 m) off. The shipped map files are fine
   (`EndGameH6QaTests.TheShippedMapsStartProxies_LieWithinSightOfTheirStarts`: Pale Hills 1.4 m, Vathar 7.6 m; Raraku
   19.4 m but it has only two starts, never free during a match); a new map, or a generated map with spare starts, may not be.
3. **A lost house near the cap comes back twice.** `EndGameH6QaTests.ARazedTent_AtTheCap_IsRebuiltOnce` (skip "BUG-1161
   item 3"). Since the house rule, `ReopenLost` reopens a House step under exactly `EconomyManager.House`'s own test
   (below the hard cap, `HalfPopCap - HalfPop <= HouseHalfPopMargin`), and both send their Build at the same think (the
   economy's "one being built" check sees no site yet): a Whirlwind seat at its cap that loses one of two tents has three
   tents (sites included) six thinks later, two tent Builds sent (`ASeatAtOrNearItsCap_RebuildsTheRazedTent` prints
   `tents (sites too) after 6 thinks 3, tent Builds 2` at 0, 4 and -16 half-pop under the new cap). The double existed
   before for a seat near its cap; M5-H6 makes every reopened House step coincide with the economy's own house. 50 wood
   a time.

## Expected
1. The leg walks to a start whose mines could be inside the reach (centre within reach + `StartMineDistance`), or the docs
   say the reach is measured to the start's centre.
2. When the hunt looks at a free start it sees that start's centre: target the start's centre (clamped) and mark its proxy,
   or pick the proxy on the pass that lies nearest, or check the proxy is within the members' sight.
3. One house for one lost: since the economy's rule already builds a house exactly when the reopened step would, a House
   step need not be reopened at all (or the economy skips a think when a House step was just reopened).

## Notes
- Item 2's measurement is on a flat test map; the M5-H6 match rows are on Pale Hills, where the proxies are 1.4 m off.
- Item 3 does not change the criterion rows' ending (a second tent is a target, not a beacon); it is wood spent twice.
