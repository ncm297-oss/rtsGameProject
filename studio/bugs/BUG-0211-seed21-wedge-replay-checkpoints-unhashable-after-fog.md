# BUG-0211: GatherWedgeQaTests' checkpoint prefix is off (19 -> 0) until the seed-21 replay is re-recorded

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-08-1435, task M4-3a |
| System | QA regression rows / replays |
| Fixed by | M4-H1 (sim track): `studio/bugs/BUG-0146-seed21-wood-wedge.replay` re-recorded from `M3PlayableTest -- --seed 21 --break 19` (format 4, 11,541 ticks, data hash 1437FEB446E68586); `GatherWedgeQaTests` checks all 11,541 checkpoints, `SameGameDataHashes` pruned to the recording's own; docs/03 "Hash format" note |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherWedgeQaTests"` on M4-3a (977db1d) with
   `CheckpointPrefixTicks` set back to 19: fails, "the replay no longer plays the recorded game: checkpoint 1 differs".
2. The same with `World.Fog.AddToHash` and the projectile level byte left out of `Simulation.StateHash` (a scratch clone,
   not committed): passes.

## Expected
`QA/GatherWedgeQaTests.Seed21PlayableReplay_NoGathererStandsOutOfReachForever` proves, through its checkpoint prefix,
that the substituted data hash still plays the recorded match.

## Actual
M4-3a hashes the fog's explored bits, which `studio/bugs/BUG-0146-seed21-wood-wedge.replay` (recorded before M4-3a)
can't contain, so no checkpoint can match. The developer set `CheckpointPrefixTicks` to 0 (the check is off) and added
`M4_3aDataHash` to `SameGameDataHashes`. QA verified the claim above: with the fog out of the hash the 19-tick prefix
still passes, so the row still tests the same game today; but nothing now guards that for the next data change.

## Notes
Fix: re-record the match from `M3PlayableTest -- --seed 21` (view scene) on a post-M4-3a build, update `RecordedDataHash`
and restore `CheckpointPrefixTicks` (19 or more). Any later hashed-state addition will do the same to every pre-recorded
replay; a short "hash format version" note in docs/03 Replays would make that expected.
