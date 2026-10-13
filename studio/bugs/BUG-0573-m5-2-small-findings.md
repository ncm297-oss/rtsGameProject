# BUG-0573: M5-2 small findings: CanPlace answers two world-wide facts; production divides by a weight that can be 0f

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11-0634, task M5-2 |
| System | AI `PlayerView.CanPlace`, `ProductionManager` |
| Fixed by | |

## Repro / Actual
1. **`PlayerView.CanPlace` and the building store's free count.** `CheckPlace` ends with
   `if (_buildings.FreeCount == 0) return PlacementError.StoreFull;`. The store is shared by every player, so with one slot
   left an enemy building the player never saw turns the answer from `None` into `StoreFull` (a hidden-state bit). Same for
   a `NavFlags.Pocket` cell left by a building or node freed out of sight: it reads as terrain (`Blocked`). `World.CanPlace`
   (the human's ghost) does the same, so the AI knows no more than the player's own build ghost; noted because the method's
   summary says it is judged "on what the player knows". QA's twin test (`QA/AiSeatQaTests.TwinWorlds_...`) passes:
   neither case arises with a store that has room and no pocket.
2. **`ProductionManager` divides by a composition weight.** `share = (live + queued) / mix.Weights[k]`. BUG-0541 found a
   weight of `1e-300` loads as `0f`; until that's fixed the share is `+Infinity` (or `NaN` for 0 / 0), and a `NaN`
   `pickShare` makes every later unit win the comparison. Deterministic, only reachable with hostile data.

## Expected
1. A docs line ("`StoreFull` and pockets read world-wide state, as the human's ghost does") or the check dropped (the Build
   refuses a full store anyway). 2. Goes away with BUG-0541's fix (refuse a weight that rounds to 0f).

## Notes
Both cheap; neither blocks M5-2.

## Update (2026-10-12-1930, QA)
Item 2 **fixed** in M5-3 `238f261` with BUG-0541 item 1 (no weight loads as 0f; production now divides by a weight above 0).
Item 1 is still open, so the bug stays open.
