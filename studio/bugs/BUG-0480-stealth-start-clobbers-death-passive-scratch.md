# BUG-0480: A stealthed status applied from the death path reaches only the first listed friend (OnStealthStarted overwrites the death passive's neighbour scratch)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1954, task M4-5 |
| System | stealth / passives (`StealthSystem.OnStealthStarted`, `PassiveSystem.OnDeath`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~QA.StealthQaTests.AnOnDeathPassiveApplyingStealthed_ReachesEveryListedFriend"`
   with the `Skip` removed from the `true` row.
2. The scene: the Zealot's `onDeath` passive edited to apply `stealthed` (no magnitude; the loader accepts it), a dying
   Zealot with five own Zealots within 6 m and eight enemy Laborers (10 m detectors in `StealthTests.Data`) 7 m away.
   `PassiveSystem.OnDeath` is called once.

## Expected
All five Zealots take the status, as all five take the shipped Frenzied in the `false` (control) row.

## Actual
`Assert.Equal() Failure: Expected: 5, Actual: 1`. Only the first Zealot the neighbour query lists gets it.

## Notes
`PassiveSystem.OnDeath` iterates its query results in `World.ShoveNeighbors` and calls `StatusSystem.Apply` per friend.
The first friend's new stealth runs `StealthSystem.OnStealthStarted`, which is documented to use "the second movement
scratch (phase 6's resolve and phase 11's splash hold the first)" and runs `SpatialHash.QueryEnemies` into that same
`ShoveNeighbors` array. The rest of the death passive's loop then reads enemy slots (skipped by its owner check). Deterministic
(twins agree), so no hash break; it is a wrong game rule. Not reachable with shipped data (no shipped status source applies
`stealthed` before M8), but the loader accepts the shape, and Shadow's M8 content builds on these hooks. Any future
`StatusSystem.Apply` caller that holds `ShoveNeighbors` has the same hazard. Fix idea: give detection its own scratch (one
more int[UnitCapacity], itemise the memory) or defer the "detect at once" to the end of the caller's loop.

## Producer triage (2026-10-10-1954 ACCEPT)
S3 confirmed; not reachable with shipped data (no stealth source before M8) but a wrong rule on a shipped hook, so it goes to the
**end-of-M4 sim hardening** (M4-H3), right after BUG-0461. Direction: give detection its own `int[UnitCapacity]` scratch (about
16 KB: itemise it in the memory row; the margin is 29,560 B, so this is the last per-slot array before a re-baseline) rather than
deferring the "detect at once" step; un-skip the QA row.
