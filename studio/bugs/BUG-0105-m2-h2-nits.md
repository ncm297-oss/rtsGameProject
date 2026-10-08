# BUG-0105: M2-H2 nits: minimap refresh row now at 92-94% of its 0.3 ms limit (docs say 0.25 ms), Sfx exit-wait range in docs/03

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-07-0800, task M2-H2 (QA) |
| System | view: `Rts.Sim.ViewApi.MinimapRaster` (perf row), docs/03 "Implementation (M2-3b)" and "Implementation (M2-6)" |
| Fixed by | 9998824 (M3-V3b: docs/03 minimap and Sfx figures refreshed; `PropsMeasureTests` times the dots and the forced redraw apart). Verified by QA 2026-10-07-1715 alone: dots 0.166 ms (limit 0.25), redraw 0.125 ms (limit 0.2), sum 0.291 ms; docs say about 0.17 / 0.13 ms. See BUG-0126 item 5 on the unguarded sum |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "Category=Perf&FullyQualifiedName~PropsMeasureTests" --logger "console;verbosity=detailed"`,
   alone, four times.
2. `& $env:GODOT --headless --path game res://tests/SfxTest.tscn`, ten times, while `dotnet test sim/Rts.Sim.Tests` runs.

## Expected / Actual
1. **Minimap refresh margin.** `MinimapRefresh_2000Units_4096Nodes_ResourceRedraw_Under0_3Ms` measured avg
   0.277 / 0.283 / 0.281 / 0.277 ms (worst 0.91-0.94 ms) against its 0.3 ms limit, alone on the dev PC. BUG-0086
   (fixed here for its upload item) recorded 0.255 ms and called 15% headroom "a likely flake under the other
   track's load"; the 4 x 4 dot (BUG-0069) cut it to 6-8%. docs/03 (M2-3b "Minimap" cost line) still says
   "about 0.25 ms (Debug; limit 0.3 ms)". The dots-only rows are fine (0.14-0.16 ms; the edge-heavy layout
   0.28-0.31 ms against its 0.5 ms limit). A watch item, not a request to loosen the threshold: refresh the
   docs figure, and if the row flakes, the resource redraw (the forced 4,096-node half) is where the time is.
2. **Sfx exit wait.** docs/03 "Quitting with a sound playing" says the wait is "about 5-60 ms headless".
   Ten headless `SfxTest` runs under suite load printed 22-93 ms (one alone: 28 ms), always under the 200 ms
   limit and always clean (no ObjectDB warning). The sentence could say "up to ~100 ms under load".

## Notes
Neither affects play. Both are doc figures plus one thin perf margin.
