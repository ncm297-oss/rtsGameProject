# BUG-0012: MapGenParams.Validate and the Heightmap ctor overflow on huge ints; Generate then crashes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-1235, task M1-3 |
| System | terrain / map generator |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.MapQaTests.OverflowParams_ValidateThrowsOrMapIsValid` and
   `Rts.Sim.Tests.QA.MapQaTests.Heightmap_SizeOverflow_IsRejected`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~OverflowParams|FullyQualifiedName~Heightmap_SizeOverflow"`

## Expected
The M1-3 brief (QA focus): degenerate params give either a valid map or an
`ArgumentOutOfRangeException` from `Validate()`, never an index error. A `Heightmap` whose
width x height doesn't fit an int is rejected.

## Actual
`Validate()` accepts each of these (default params with one field changed), then `Generate` throws:

| Param | Generate throws | Where |
| --- | --- | --- |
| `RampWidth = int.MaxValue` | `ArgumentOutOfRangeException (maxExclusive)` from `SimRng.NextInt` | `TryPlaceRamp` line 106 |
| `RampLength = int.MaxValue` | `IndexOutOfRangeException` | `TryPlaceRamp` line 138 |
| `EdgeMargin = int.MaxValue` | `IndexOutOfRangeException` | `Raise` line 93 |
| `Level2Inset = int.MaxValue` | `IndexOutOfRangeException` | `Raise` line 93 |

`new Heightmap(65536, 65536, empty, empty)` (and 1048576 x 4096) is accepted: `width * height`
wraps to 0, so the length checks pass; any later query indexes an empty array.

## Notes
Causes: `RampWidth + 2` (minSide), `(RampLength + 1) * CellSize`, `2 * (1 + EdgeMargin)` and
`2 * Level2Inset` all wrap in unchecked int arithmetic, so the range checks see small or negative
numbers. `RampLength`, `RampWidth`, `EdgeMargin` and `Level2Inset` have no upper bound in `Validate`.
Fix: give each an upper bound (e.g. at most the map size) checked before the arithmetic, or compute
in `long`; in `Heightmap`, compute `(long)width * height`. Today only code sets these params; this
becomes S2 if map settings are ever read from data or a lobby.

**Producer triage (2026-10-03-1235):** fix in the first commit of M1-4a: upper bounds on
`RampWidth`, `RampLength`, `EdgeMargin`, `Level2Inset` (at most the map size) checked before any
arithmetic, and `(long)width * height` in `Heightmap`. Un-skip both QA tests. Does not block.
