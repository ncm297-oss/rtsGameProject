# BUG-0620: MapGenParams.Validate accepts start-location params the flat fallback can't hold; Generate (and a replay's playback) then throws

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-13-0146, task M5-4a |
| System | map generator (`MapGenParams.Validate`, `MapGenerator.Generate` / `FindStarts`), replays |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StartLocationQaTests.Bug0620"`: both pins pass, which shows the bug.
2. Minimal: `MapGenParams.Default with { Width = 40, Height = 40, EdgeMargin = 1, Level1MaxSize = 30, StartEdgeMargin = 1 }`:
   `Validate()` passes, and `MapGenerator.Generate` throws `InvalidOperationException: no room for 2 start locations on a flat 40 x 40 map`.
3. The sweep `StartLocationQaTests.EveryValidatedStartParamSet_Generates` (skipped against this bug; un-skip to see it): **180 of
   444** validated parameter sets (maps 16-80 cells, `StartEdgeMargin` 1-3, `StartClearRadius` 2-8, mine distance at its minimum
   and maximum) throw.
4. Through a replay: a format 6 file recorded on that 40 x 40 map with `map.start-locations 0`, edited to 2 and re-signed, reads
   with `ReplayError.None`, and `ReplayPlayer.Run` throws instead of returning a refusal
   (`StartLocationQaTests.Bug0620_ReplayPath_Pinned`; the wanted behaviour is `Bug0620_ReplayHeaderThatValidatesButCannotFit_IsRefusedNotThrown`, skipped).

## Expected
docs/03 "Implementation (M5-4a)": "`Validate` makes the pair fit a flat map (player 0's band non-empty ...), so the flat fallback
always has them; `Generate` still throws `InvalidOperationException` if it doesn't." Every parameter set `Validate` accepts should
generate. Before M5-4a that held, so a replay header that validated always played back or was refused with a `ReplayError`.

## Actual
`Validate` checks only `lo = StartEdgeMargin + StartClearRadius <= Width / 2 - 1 - StartSiteRadius` and `lo <= Height - 1 - lo`.
The search, though, needs the whole **site disc** (radius `StartSiteRadius` = max(clear, ceil(mine / 2 m)) + 1, larger than the
clear disc) on open ground, and open ground starts two cells in from the border (the border ring and its neighbours are not open).
So a centre needs `cx, cy >= StartSiteRadius + 2` and `cy <= Height - 3 - StartSiteRadius`, which `Validate` doesn't check. With
the default margin 10 it holds for the default radii, but not with a small `StartEdgeMargin`, or with the default margin, a
maximal `StartMineDistance` (clear + 16 m) and a minimal height.

## Notes
- Not reachable from the game today: the launcher offers only 128 / 192 / 256 maps with the default start params. It is reachable from
  any `SimConfig` and from a replay file (external input that used to be refused cleanly).
- Fix options: make `Validate` require `max(lo, StartSiteRadius + 2) <= min(hiX, Height - 3 - StartSiteRadius)` (by name, as
  `StartClearRadius` or `StartEdgeMargin`); or catch the throw in `ReplayPlayer` as `InvalidHeader`. The first keeps the docs true.
