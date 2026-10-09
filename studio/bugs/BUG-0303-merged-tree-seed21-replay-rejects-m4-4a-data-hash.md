# BUG-0303: On the sim + view merge, the seed-21 replay row fails: M4-4a's data hash is not whitelisted

| Field | Value |
| --- | --- |
| Severity | S2 (the merged tree's test suite is red; neither branch is red alone) |
| Status | fixed (verified by QA 2026-10-09-0724, re-check round 2) |
| Found | 2026-10-09-0724, task M4-4a QA re-check round 1 |
| System | `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests` (QA row) vs M4-4a's shipped data |
| Fixed by | 37c482d (M4-4a round 2): `0x7E04011FC88881F3` added to `SameGameDataHashes` |

## Repro
1. Scratch clone; check out `studio/2026-10-09-0724-sim` (c62d7b5), merge `studio/2026-10-09-0724-view` (0dd8952).
   The only conflicts are `studio/bugs/README.md` and `studio/qa/coverage.md` (records).
2. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~GatherWedgeQaTests.Seed21`

## Actual
```
shipped data hash 7E04011FC88881F3 is not one known to play the recorded match (C22FBFEA0197CF3E): re-record it from
M3PlayableTest -- --seed 21, or add the hash for a change that cannot touch this game
```
The view track (M4-V5, BUG-0273) re-recorded the replay and un-skipped the row on the pre-M4-4a data; the sim track's
M4-4a data (abilities, statuses, the Cadre Mage's ability) moves the shipped data hash. On the sim branch alone the row is
still skipped, so neither branch shows it.

## Expected
The merged suite is green.

## Notes
QA probed the fix in the scratch merge only: with `0x7E04011FC88881F3` added to `SameGameDataHashes`, the row passes and
replays all 12,131 recorded checkpoints tick for tick (1 s), so M4-4a's data and hash additions do not change this match.
The fix is that one array entry in the merge (with a comment: "M4-4a: abilities / statuses data, nobody casts in this
match"), not a re-recording.

## Verification (2026-10-09-0724, re-check round 2)
Fixed in 37c482d (one line, test code only). QA scratch merge of 37c482d + `studio/2026-10-09-0724-view` (0dd8952):
`GatherWedgeQaTests.cs` auto-merges cleanly (only `studio/bugs/README.md` and `studio/qa/coverage.md` conflict).
`GatherWedgeQaTests` 3/3 passes. The non-Perf suite is 4,258 passed / 0 failed / 11 skipped (12 m 57 s), and smoke prints PASS.
