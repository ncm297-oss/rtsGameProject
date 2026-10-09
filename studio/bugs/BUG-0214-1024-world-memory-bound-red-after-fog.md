# BUG-0214: World_1024Map_CacheStays32_MemoryBounded is red: 230.4 MB against its 228 MB bound (fog arrays)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-3a |
| System | fog of war (memory) / QA memory bound |
| Fixed by | 34931b8 (bound re-baselined 228,000,000 -> 230,500,000 B with the fog's share itemised in the test comment and docs/03 "Vision, detection, fog"); regression: the row itself |

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

## QA re-check (2026-10-08-1435, round 1)
- Arithmetic checked: per player 1,024 x 1,024 = 1,048,576 visibility bytes + 131,072 explored-bit bytes = 1,179,648;
  x 2 players = 2,359,296; reveals 4,096 slots x 2 ints x 2 players x 4 B = 65,536; fog total 2,424,832 B.
  227,976,424 (dd5b5b9) + 2,424,832 + 26,592 (masks, boxes, headers) = 230,427,848, the developer's figure.
  QA measured 230,427,816 B on 34931b8 (32 B run-to-run); margin to 230,500,000 is 72,184 B. The old bound's margin
  over dd5b5b9 was 23,576 B, so the bound moved by exactly the fog plus a margin of the same order: not loosened
  beyond the regression.
- The explanation meets CLAUDE.md ("don't loosen a threshold without explaining the regression"): the test comment
  names the old measurement and commit, itemises the fog arrays by docs/03 section, and docs/03 states the same figure.
- Full non-Perf run on 34931b8: 3,922 passed / 13 skipped / 0 failed of 3,935 in 12 m 42 s. Verified fixed.
