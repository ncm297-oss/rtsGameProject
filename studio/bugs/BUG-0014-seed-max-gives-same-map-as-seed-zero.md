# BUG-0014: Seed ulong.MaxValue generates exactly the same map as seed 0

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-03-1235, task M1-3 |
| System | determinism / SimRng seeding |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.QA.MapQaTests.SeedMaxValue_AndSeedZero_GiveDifferentMaps`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SeedMaxValue_AndSeedZero"`

## Expected
M1-3 criterion 5: different seeds give different `ContentHash()`.

## Actual
Both hash to `8511056977482715277`; the MapGen RNG ends in the same state too.

## Notes
`SimRng(seed, stream)` follows PCG's `pcg32_srandom_r`: state = 0, step, state += seed, step. For
stream 0 (`RngStream.MapGen`, increment 1) the first step gives state 1, so seed `ulong.MaxValue`
wraps the state back to 0 and its user-visible stream is seed 0's stream with one extra leading
output, and that output is 0. `NextInt` (Lemire) always rejects a 0 draw and redraws, so the first
`NextInt` swallows the extra value and every later draw matches seed 0. Other streams (Combat, AI)
for these two seeds differ. This is inherent to the PCG reference seeding (every seed's stream is a
shift of another's); only this pair collides exactly. The developer noticed it but did not file it.
Possible fix: mix the seed first (e.g. SplitMix64) before it's added to the state. That changes
every stream, so do it before golden replays exist (M1-6).

**Producer triage (2026-10-03-1235):** fix in the first commit of M1-6 (replay + golden), before
the first golden hash is recorded: mix the seed (SplitMix64) in the `SimRng` constructor, update
`SimRngTests` expectations in the same commit, un-skip the QA test. Does not block.
