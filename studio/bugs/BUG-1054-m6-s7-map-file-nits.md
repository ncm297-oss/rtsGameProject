# BUG-1054: M6-S7 nits (docs/02's "first minute out", two clauses no test covered, a save's map id is a label)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 fixed by the Producer at the 1716 ACCEPT: docs/02 says "its first minute or two out: measured 60-103 s"; item 2 covered by QA's rows; left: item 3, one sentence on what a save's map id does, M6-SH1) |
| Found | 2026-11-05-1716, task M6-S7 (QA) |
| System | docs/02 "AI opponent", `DataLoader.Maps` connectivity, `ScoutManager.Waypoints` replacement skip, save format 5 |
| Fixed by | |

## Repro
1. **docs/02 "AI opponent" says a far-forest start gets its wood "once the scout has circled home (its first minute
   out)".** Measured (`QA/ReachRuleQaTests.NearestForestFarPastTheReach_*`, unharmed scouts): the reach lifts at tick
   1,501 / 1,641 / 2,121 on seeds 1-3, the scout leaving at about tick 300: 60-91 s out, and 103 s when a replacement had
   to finish the ring (seed 3, scout killed at 200). "Its first minute or two out" would be true.
2. **Two clauses no test covered** (QA's mutation run on a scratch copy, 13 mutants on the hash, loader, save, replay
   and reach rule; 11 killed by the developer's tests): dropping the loader's connectivity flood (`Connected`) and
   dropping the replacement's "skip the ring points already seen" both passed every test. QA's
   `QA/MapFileQaTests.AForestRingWallingInAPocket_IsOneError` and
   `QA/ReachRuleQaTests.AReplacementScout_SkipsTheRingPointsAlreadySeen` now kill both (no product change needed).
3. **A format-5 save's map id is a label, not a source.** A load builds the world on the save's own HMAP section (M6-S2:
   "stored, not re-generated"), never on `MapDef.Terrain`; nothing checks the two agree. A Raraku save with the id cut
   out and relabelled format 4 loads as a generated-map match and plays on hash-equal
   (`QA/MapFileQaTests.AFormat5SaveRelabelled4_IsRefused_AndWithTheIdCut_LoadsItsStoredTerrain`). Harmless today (the
   data hash in the header pins the map file, and a forger can already edit any stored field), but docs/02 "Map and terrain"'s "a save
   or a recording of a match on one names the map and rebuilds it exactly" reads as if the id rebuilt the terrain; one sentence would say the id only names it.

## Expected
1. docs/02 matches the measured lift. 2. Covered now. 3. docs/02 or docs/03 says what the id does on load (a recording rebuilds from it; a save names it).

## Actual
As above.

## Notes
None blocks M6-S7.
