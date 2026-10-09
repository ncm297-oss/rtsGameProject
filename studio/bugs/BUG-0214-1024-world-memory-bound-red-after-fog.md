# BUG-0214: World_1024Map_CacheStays32_MemoryBounded is red: 230.4 MB against its 228 MB bound (fog arrays)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-08-1435, task M4-3a |
| System | fog of war (memory) / QA memory bound |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~World_1024Map_CacheStays32_MemoryBounded"`
   on 977db1d: `Failed ... 230.4 MB` ("1024 x 1024 world, 4096 unit slots: 230.4 MB allocated, cache 32").
2. Full non-Perf run: 3,909 passed / 11 skipped / **1 failed** (this row) in 14 m 46 s.

## Expected
Brief criterion 9: non-Perf green, no threshold loosened. The row bounds a 1024 x 1024 world at 228 MB
(docs/03: M4-2a already reworked the queued-Attack storage to stay under it, 228.2 MB measured then).

## Actual
The fog allocates per player one byte a cell (1 MB on a 1024 map) plus the packed explored bits (128 KB), and 2 ints
a unit slot a player for the reveals (64 KB at 4,096 slots, 2 players): about 2.3 MB, taking the world to 230.4 MB.
The developer did not loosen the bound and proposed a re-baseline.

## Notes
The growth is real and expected (maps above 256 cells are unsupported for gameplay, docs/03), so this is a Producer
call: either re-baseline the bound with the fog's share written in the test comment and docs/03 (QA recommends this:
about 2.3 MB on a 1024 map, linear in cells x players), or allocate the fog lazily / smaller. Until one happens the
non-Perf suite is red, which blocks ACCEPT.
