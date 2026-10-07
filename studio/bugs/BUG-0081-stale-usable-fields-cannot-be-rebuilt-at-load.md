# BUG-0081: Usable-but-stale flow fields can't be rebuilt from their keys at load (docs/03's save/load plan)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-06-1744, task M3-2b |
| System | flow-field cache / save-load design (M6) |
| Fixed by | Design decision recorded in M3-H1 (4abbf37, session 2026-10-07-0800; Producer decision 2026-10-07, owner may revisit at M6): docs/03 "Save/load and replays" now says a save file stores the flow-field cache's contents (every used slot's direction bytes, costs, keys and stamps), so save then load reproduces the unsaved run exactly; replays replay from tick 0 and need nothing. docs/01 change log row. The M6 save/load task implements it; the QA proxy row `RebuildingStaleFieldsAtLoad_Proxy_*` stays as the reason |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RebuildingStaleFieldsAtLoad_Proxy_DivergesFromAnUninterruptedRun_Report" --logger "console;verbosity=detailed"`
   (`QA/GridChangeQaTests.cs`): two twins, 32 walkers with 32 cached fields behind a tree wall; the wall's
   middle tree falls, so all 32 fields are usable but stale. In one twin every stale field is rebuilt
   (what "save the keys, rebuild the fields at load" would do), the other runs on.

## Expected
docs/03 "Flow fields" (and the BUG-0021 decision): the cache's keys, versions and LRU order are sim state,
"and save/load will save the keys and rebuild the fields at load", so save/load mid-run equals an
uninterrupted run (docs/03 testing rules).

## Actual
```
32 stale-but-usable fields at the 'save' point; hash parted at once: True; positions first differ 0 ticks later, largest gap 2.04 m
```
Since M3-2b a cached field's contents depend on the grid *as it was at the field's build* (`FlowField.Version`),
not only on its key and the current grid. Rebuilding at load gives the current route at once; the
uninterrupted run follows the old route until the build pass refreshes it under the cap. The hash also parts
at once (each slot's `Version` tag is hashed).

## Notes
- No save/load exists yet (M6), so nothing breaks today; this is a design question to settle before then:
  save the contents of stale slots (5 bytes x cells each), or save the grid's change history since the
  oldest stale build, or accept that a load refreshes every field (and define the hash so a loaded game
  still matches, e.g. by refreshing every stale field at the save point in both).
- docs/03's sentence was kept unchanged in the paragraph this task edited; it should at least say the plan
  no longer holds for stale slots.
