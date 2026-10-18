# BUG-0960: M6-S2 save nits: a forged header makes the load allocate ~1.4 GB before refusing; forged saves that load can throw on the next tick; the save Perf row trips under heavy contention

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-18-0516, task M6-S2 |
| System | sim: `Save/SaveHeader`, `Simulation.TryLoad`, `Save/SavePerfTests` |
| Fixed by | |

## Repro / items
1. **The header's capacities are trusted before any body byte is checked.** `SaveForgeryQaTests.AForgedHeader_WithEveryCapacityAtTheFormatMaximum_IsRefused_WithoutThrowing`:
   the small save (every capacity 1, 16 x 16 map) with its six capacities forged to 1,000,000 (`SaveFormat.MaxCapacity`)
   and the checksum recomputed. `TryLoad` builds the whole `World` for those capacities (`World.ForLoad`) and only then
   refuses on the first array length: `Malformed` in ~420 ms with **~1,400 MB allocated** (`GC.GetTotalAllocatedBytes`).
   No throw on this 64-bit dev machine, but a smaller one could hit `OutOfMemoryException`, which `TryLoad` doesn't catch
   (its doc says bad bytes never throw). A cheap pre-check before `ForLoad` (the declared capacities and map size imply a
   minimum file length: units x bytes-per-unit + cells x bytes-per-cell...; refuse `Malformed` when the file is shorter)
   would refuse it in microseconds. Only a hand-forged file (checksum recomputed) reaches this.
2. **A forged save that loads can throw on its first ticks** (docs/03 already says "a forged file can load into a sim
   whose later ticks misbehave", so this is a count, not a new rule). `SaveForgeryQaTests.TheSmallSave_AnInterestingInt32AtEveryOffset_NeverThrowsOnLoad`:
   13 int32 values at every offset of the small save, checksum recomputed: 47,714 load, 81 of them throw within 3 ticks
   (`ArgumentOutOfRangeException` in `FlowFieldCache.Get` from a unit's forged goal cell; `IndexOutOfRangeException` in
   `FogStore.Combine` from a forged `_visibleBox`). The menu-match sweep (250 random multi-byte edits of a 2.0 MB save):
   102 load, 0 throw in 5 ticks. If the view's Load screen ever reads saves from outside the game's own folder, checking
   the unit goal cells and the fog's visible boxes in `ReadFrom` would close the two paths seen.
3. **`SavePerfTests.MenuMatch_AtTick12000_SavesAndLoadsUnder100Ms_InUnder8MB` failed once** in a fresh clone while the
   full suite ran in the worktree at the same time (two test hosts on the shared machine). Alone: save 25 ms, load 46 ms;
   under one concurrent suite: 23-26 ms / 41-42 ms (two reruns green). Same as the developer's "fails only under load";
   recorded so a second sighting under normal load is recognized.

## Notes
None blocks: items 1 and 2 need a file whose checksum was recomputed by hand, item 3 is wall-clock contention.
