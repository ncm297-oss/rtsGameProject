# BUG-0272: A last-known building is replaced, not kept, when its slot is reused by a building seen elsewhere

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | wontfix |
| Found | 2026-10-09-0125, task M4-3b (builder's known limit) |
| System | vision (last-known buildings list) |
| Fixed by | M4-H3: kept as a documented limit |

## Repro
1. Player 0 sees player 1's Tent in building slot 3, then looks away (the ghost is listed).
2. Unseen, the Tent is destroyed, and another building of player 1 takes slot 3 (slots recycle lowest-free-first)
   somewhere player 0 can see.
3. Next fog update.

## Expected
docs/02: the old Tent stays a ghost until player 0 sees its ground again; the new building is listed too.

## Actual
`FogStore.Ghosts(player)` holds one entry per building slot, so the new building's entry replaces the old ghost although
player 0 never looked at the old ground (it learns the Tent is gone).

## Notes
The brief's store is "per enemy building slot" with fixed capacity per player (no per-tick allocation); keeping both
needs more entries than slots (a ghost per remembered building, bounded by the map's cells). Rare: needs a destroyed
building's slot reused while unseen. Pinned in docs/03 "Implementation (M4-3b)".

## Decision (M4-H3, 2026-10-11)
Wontfix, kept as the documented limit in docs/03 "Implementation (M4-3b)" (the last-known list): keeping both ghosts needs
entries beyond the building slots (a ghost per remembered building, bounded only by the map), against the fixed-size,
allocation-free store, and the case needs a ghosted building's slot reused unseen. Revisit with the AI's memory (M6) if it matters.
