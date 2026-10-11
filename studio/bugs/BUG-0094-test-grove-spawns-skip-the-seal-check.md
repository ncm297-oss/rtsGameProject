# BUG-0094: Hand-built test groves (`ResourceStore.Spawn`) skip the never-seal check and wall cells in before tick 1

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-07-0800, task M3-H1 |
| System | test tooling (`ResourceStore.Spawn` via `ResourceMaps.Spawn`, dev-only) |
| Fixed by | QA harness half (2026-10-07-0800); helper half: M4-H3 `ResourceMaps.SpawnSealSafe` |

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

## Fix (M4-H3, 2026-10-11)
`ResourceMaps.SpawnSealSafe(world, type, x, y, amount)` spawns a node only where it fits and `World.Seal.KeepsConnected`
allows (the never-seal rule placements follow), returning whether it did; `ResourceMaps.Spawn`'s summary now warns that it
runs no seal check and points at the safe one. Proof: `ResourceStoreTests.SpawnSealSafe_NeverWallsACellIn_ThePlainHelperDoes`
(a ring of 8 trees: the safe helper refuses the closing tree and the centre stays reachable; the plain one walls it in).
