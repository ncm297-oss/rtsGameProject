# BUG-0016: SpatialHash.NearestEnemy returns a NaN-positioned unit that QueryRadius excludes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-2220, task M1-4a |
| System | spatial hash |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.SpatialHashQaTests.NearestEnemy_IgnoresUnitWithNaNPosition_LikeQueryRadius`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~NearestEnemy_IgnoresUnitWithNaN"`

Setup: put slot 0 (owner 1) at (NaN, NaN) and slot 1 (owner 1) at (1, 1), then rebuild. Call
`NearestEnemy((0,0), 10, player 0)`.

## Expected
The XML doc and docs/03 both say NearestEnemy "uses the QueryRadius match rule".
`QueryRadius((0,0), 10)` returns only slot 1, because a NaN distance fails `d2 <= r2`. So
NearestEnemy should return slot 1.

## Actual
```
Assert.Equal() Failure: Values differ
Expected: 1
Actual:   0
```
It returns slot 0, the NaN unit. Because `d2 = NaN`, `if (d2 > best) continue;` doesn't skip it, and
the `slot < 0` branch accepts it. `best` then becomes NaN, and `d2 < NaN` is always false, so no
real enemy found later can replace it unless it has a lower slot.

## Notes
This can't happen today: SpawnUnit rejects non-finite positions (BUG-0006), and nothing moves units
yet. Once movement exists, though, a single NaN unit would land in bucket 0 (the map corner) and
become "the nearest enemy" for every query whose range touches bucket 0. Fix: write the test as
`if (!(d2 <= best)) continue;`, which mirrors QueryRadius.
