# BUG-0273: The seed 21 Playable replay (BUG-0146's wedge row) builds on unexplored ground and must be re-recorded

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M4-V5, session 2026-10-09-0724: re-recorded from `M3PlayableTest -- --seed 21 --break 19` on the M4-3b view tree, 12,131 checkpoints, `RecordedDataHash` C22FBFEA0197CF3E; `GatherWedgeQaTests.Seed21PlayableReplay_NoGathererStandsOutOfReachForever` un-skipped and green; the sim's M4-4a hash 7E04011FC88881F3 whitelisted in `SameGameDataHashes` (BUG-0303)) |
| Found | 2026-10-09-0125, task M4-3b |
| System | replays / QA rows (placement on explored ground) |
| Fixed by | view 215838b (re-record + un-skip), sim 37c482d (the M4-4a hash row); QA verified all 12,131 checkpoints on the sim + view scratch merge |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~GatherWedgeQaTests"` with the skip removed and the shipped
   data hash (C22FBFEA0197CF3E since M4-3b's tower data) added to `SameGameDataHashes`.

## Expected
The recorded match plays all 11,541 checkpoints (`CheckpointPrefixTicks`).

## Actual
"the replay no longer plays the recorded game: checkpoint 617 differs" (checkpoint 26 with the last-known list hashed: the
list's entries are new hashed state once a player sees an enemy building). With the list's hash words left out locally the
game still diverges at checkpoint 617: the replay's only command near it, `c 616 0 18 8 1 ...`, is player 0's Build of a
House (type 1) at (113, 139) m, ground player 0 had not explored by then. M4-3b's placement rule (docs/02 "Buildings":
the footprint must be explored by the player) refuses it, so the recorded game is no longer the one the rule allows.

## Notes
Not a sim bug: the input is invalid under the new rule. Fix: re-record `studio/bugs/BUG-0146-seed21-wood-wedge.replay`
from `M3PlayableTest -- --seed 21` on the merged M4-3b tree (the scripted build must pick explored ground, or the
recording just reflects the refusal), update `RecordedDataHash`, and un-skip the row. The sim track may not write
`studio/**`; left for the view track / QA. Skipped in M4-3b with this id.

## QA (2026-10-09-0125, M4-3b inspection)
Confirmed the row is skipped (`[Fact(Skip = "BUG-0273 ...")]`); the non-Perf run reports it among 11 skips. The skip is
a loosening of a QA row, but the recorded input is invalid under the new docs/02 rule, so it can't pass unchanged. A
re-record depends on BUG-0274: on the merged tree `M3PlayableTest -- --seed 21` must play to the end, and today
`M3PlayableTest` stops at step 16 (seed 6) on the explored rule. Until then BUG-0146's wedge (a gatherer out of reach
forever) has no long-replay guard; the dev's `GatherWedgeTests` rows still run. Fix order: BUG-0274's
`M3PlayableTest` spot search first, then re-record, update `RecordedDataHash`, and un-skip.
