# BUG-0076: M3-1 nits (setup timing in docs, redundant flood fill, empty resources list loads clean)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-1255, task M3-1 |
| System | resource placement, data loader, docs |
| Fixed by | |

## Repro / Actual
1. **Setup time in docs.** docs/03 "Implementation (M3-1)" says the worst case (1024 map, 64 forests
   of up to 256, 64 mines) adds "about 1 s on top of the terrain's 1 s". Measured
   (`Stress/ResourcePlacementStressTests.WorstSetup_1024Map_AtTheCaps_UnderFiveSeconds`, Perf):
   terrain 258-301 ms, terrain + placement 2,440-2,627 ms, so placement adds about 2.2 s. The
   developer's report says 2.2 s too, so the docs number is the stale one.
2. **Redundant flood fill.** `Scratch.StaysConnected` runs `MapStaysConnected` (a full-map flood)
   after `RingStaysConnected` passes. The remarks argue that the ring test alone is sufficient, and
   QA's independent oracle agrees over 246 seeds. So every committed node pays a full-map flood
   that can't fail, which is most of the 2.2 s on a 1024 map. Keep it as a debug assertion, or drop
   it and say so (the brief asked for a flood fill, so this is the Producer's call).
3. **Empty resources list.** `common/resources.json` with `{ "resources": [] }` loads with no error
   (`QA/ResourceQaTests.EmptyResourcesList_Report`). `Forests = 12, GoldMines = 8` on that data then
   places nothing, silently (`ResourcePlacement { 0, 0, 0 }`). The brief says the file is required
   and ships `tree` and `gold_mine`. Consider requiring at least one node type per `ResourceKind`.
4. **-0 mine spacing.** `map.mine-spacing 80000000` (-0.0f) passes `Validate` and plays back the
   same as +0. Harmless; mentioned only because every other float field rejects odd encodings.

## Expected
Docs match measurements; no work that can't change the result; required data is actually required.

## Producer triage (2026-10-06-1255 ACCEPT)
S4 agreed. Item 1 fixed by the Producer in docs/03 at this ACCEPT (2.2 s). Items 2-4 go to the next sim
hardening session; for item 2 the Producer's call is: keep the full flood fill as a debug-build assertion
(`Debug.Assert`) and let the ring test decide, with the QA oracle as the proof.
