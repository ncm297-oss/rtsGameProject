# BUG-0055: Own walkers slip past a friendly holding unit that plugs a 1-cell corridor (10 of 30, overlapping it by up to 1.05 m)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-2330, task M1-7 |
| System | orders / local movement (`UnitStore.Hold`, `MovementSystem.Constrain` soft clip) |
| Fixed by | |

## Repro
1. `QA/OrderQaTests.FriendlyHoldingPlug_InA1CellCorridor_LetsNobodyThrough` (skipped under this bug).
   The measuring twin `FriendlyHoldingPlug_NeverMoves_AndWalkersSettle` passes and prints the numbers.
2. Map: two rooms joined by a 1-cell (2 m) corridor, 20 cells long. One radius-0.9 unit of player 0
   stands mid-corridor with `HoldPosition`. 30 walkers of mixed types (radius 0.4-0.9), player 0,
   are ordered from the west room to the east room.

## Expected
The M1-7 QA focus says "Hold units plugging a ramp against their own army and against enemies
(nobody through)". A 0.9 m unit leaves 0.1 m on each side of a 2 m corridor, so no walker fits past
it without overlapping it.

## Actual
- Player 0 walkers: **10 of 30 end past the holder**. The deepest overlap with it is **1.053 m**
  (the first one is past by tick 164). All walkers are Idle by tick 472. The holder itself never
  moves (correct).
- Player 1 walkers (enemy holder): 0 of 30 pass. Deepest overlap 0.164 m, the same as against a
  merely stopped enemy (that part is pre-existing, cf. BUG-0045).

## Notes
- This is the documented soft-wall rule (docs/03, BUG-0035 section: "The army's own standing units
  ... keep the single clip: a walker pressed between two of them may still slip through"). The
  brief also says a holding unit is "an ordinary Idle friendly (soft clip)". So the code does what
  the brief says. But the brief's QA focus expects nobody through, and Hold is the first friendly
  unit that never yields, so this gap now shows up as walkers passing through a unit told to hold
  a choke. Here the "second wall" is the corridor wall.
- Options for the Producer: accept it and document it (friends can phase through a holder in a 1-cell
  choke), or make holding units hard walls for their own army (the hard-wall fallback already
  exists for enemies; likely just `|| u.Hold[j]` in `MovementSystem.IsHardWall`, untested). That was rejected
  earlier for *all* Idle friendlies because of the crowd rows, but holders are rare, so the crowd
  rows probably won't move.

## Producer triage (2026-10-05-2330)
S3 stands; the code does what the brief said, so the task is accepted. Producer decision (owner may revisit): a unit told to hold a choke should block its own army too, since that is what the player means by H. Fix in the M1 end-of-milestone hardening session: holders count as hard walls for their own player (`IsHardWall` or equivalent), measured against the crowd rows (`CrowdRoutingTests`) and BUG-0044 perf rows; un-skip the QA row. If the crowd rows fall, keep the soft rule and document the gap instead.
