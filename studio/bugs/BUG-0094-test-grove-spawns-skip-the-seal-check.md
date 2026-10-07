# BUG-0094: Hand-built test groves (`ResourceStore.Spawn`) skip the never-seal check and wall cells in before tick 1

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-07-0800, task M3-H1 |
| System | test tooling (`ResourceStore.Spawn` via `ResourceMaps.Spawn`, dev-only) |
| Fixed by | QA harness half: `Stress/PocketRuleFuzzStressTests.Grove` (session 2026-10-07-0800) now spawns a grove tree only where `World.Seal.KeepsConnected(x, y, 1, 1)` allows |

## Repro
1. Before the harness fix (as committed in 76d2ec2): `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PocketRuleFuzzStressTests"`.
   Maps 0, 3 and 4 failed at the oracle check on the initial map, before any random step:
   `flood from (3, 1) reaches 271 of 286 passable cells` (map 4), `1452 of 1503` (map 0), `835 of 865` (map 3).
2. A probe printed the unreachable cells right after the groves were spawned (no tick run, no building): each is walled
   in by `Blocked | Resource` neighbours, the border ring, or the map 3 cliff column, e.g. map 0 `(21, 13)`: all four
   neighbours `Blocked, Resource`.

## Expected
Test scenes that claim "every passable cell reaches every other" (the pocket rule's invariant) start in that state.
`ResourcePlacer` and every building placement (`Build`, `SpawnBuilding`) refuse a spot that seals ground.

## Actual
`ResourceStore.Spawn` (internal: load-time placer and tests only) only checks `Fits`: passable, not a ramp, one level.
A dense random grove spawned through it, or the `ResourceMaps.Spawn` helper, can enclose open cells. That is a
load-time-style pocket the pocket rule never sees (it's plain open ground nobody can reach), so any reachability
assertion fails on the setup, not on the code under test.

## Notes
- Not reachable in play: no player or dev command spawns resource nodes. Hence S4.
- Options: a `ResourceMaps.SpawnSealSafe` helper (or a seal check inside `ResourceMaps.Spawn` with an opt-out for the
  tests that build hollows on purpose, e.g. `ResourceQaTests`' hollow grove), so the next fuzz author doesn't repeat it.
