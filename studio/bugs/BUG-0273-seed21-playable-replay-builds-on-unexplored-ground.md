# BUG-0273: The seed 21 Playable replay (BUG-0146's wedge row) builds on unexplored ground and must be re-recorded

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-09-0125, task M4-3b |
| System | replays / QA rows (placement on explored ground) |
| Fixed by | |

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
