# BUG-0572: With the building store full, every AI think searches all 1,089 site candidates to the end: 1.6-2.2 ms a think (Debug)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-11-0634, task M5-2 |
| System | AI placement (`AiPlayer.FindSite`, `PlayerView.CanPlace`) |
| Fixed by | M5-2 fix round 1 (3cd37a1): `PlayerView.CanPlaceAnywhere` checked once in `AiPlayer.FindSite`; `QA/AiThinkCostQaTests` rows un-skipped, `Ai/PlayerViewMemoryTests.CanPlaceAnywhere_AnswersAsCanPlace_OnOpenGround` |

## Repro
1. Remove the `Skip` on `QA/AiThinkCostQaTests.EightAiSeats_FullBuildingStore_AThinkUnder1Ms`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiThinkCostQaTests|FullyQualifiedName~EightAiSeats_Over2500"`
   (run alone, nothing else on the machine).
2. The scene is the M5-2 gate's (`Ai/AiPerfTests`: 8 Hard AI seats, 2,500 units, a generated 128 map, population over the
   cap so the house rule searches every think) with one change: `BuildingCapacity` = the 32 buildings spawned, so the shared
   building store is full.

## Expected
Brief M5-2 criterion 5 / docs/03 "Implementation (M5-2)", Cost: a think under 1 ms in the Debug build. "A think never
stalls: every loop is bounded by the match's slots and the search radius."

## Actual
Alone on the machine (the gate measured in the same run for comparison):
```
gate (store with room)   8 AI seats, 2500 units: 400 thinks, avg 0.064 ms, p95 0.149 ms, worst 0.263 ms
store full, 6 forests    200 nodes; 280 thinks, avg 1.649 ms, worst 2.745 ms
store full, 12 forests   374 nodes; 280 thinks, avg 2.215 ms, worst 4.724 ms
```
`FindSite` walks Chebyshev rings out to 16 cells (33 x 33 = 1,089 candidates). For each candidate that passes the cheap
rules, `CanPlace` walks the footprint, the ghost list, and every resource slot up to the store's high-water mark (the
remembered-node loop) before it reaches `if (_buildings.FreeCount == 0) return PlacementError.StoreFull;`. With the store
full no candidate passes, so the whole search runs at every think, for the house rule and for every due `build` step. The
cost grows with the map's node count (12 forests: +34 %), so bigger maps are worse. Eight seats: about 13-18 ms of AI per
think tick in Debug.

## Notes
Reachable late in an 8-player match: the default `BuildingCapacity` 256 is shared, about 32 buildings a player. Cheap fixes:
check the store-wide and the cost rules once before the search (they don't depend on the cell), and test the
remembered-node overlap only for nodes near the candidate (or only after the footprint passes everything else, as now, but
with the store check hoisted). The gate scene finds a site in the first rings, so it never measures a search that fails.

## Verified (QA re-check round 1, 2026-10-11-0634)
Status set by QA. Perf rows alone, Debug: full store avg 0.058 ms (200 nodes) / 0.057 ms (374 nodes), worst 0.20 / 0.17 ms
(was 1.65 / 2.22 ms); gate 8 seats x 2,500 units avg 0.063 ms, p95 0.139, worst 0.279. `CheckPlace` keeps its reason order
(cell rules first, then cost and store), so `CanPlace` answers are unchanged; the whole Perf category alone 163 / 166, 3
skipped, 0 failed.
