# BUG-0881: A walker behind an own line of 16-32 swings between its two ends (or runs out of flank budget) and gives up

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 |
| System | movement (flank rule), `MovementSystem.FlankTurn` / `DetourTurn`, `MovementConstants.MaxFlankTurn` / `FlankCountStride` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~FlankQaTests.LongOwnRow_UpToTheBlockBound_WalkerFindsAnEnd"`
2. Flat 80 map. N Crossbowmen 1 m apart along y at x 70 (centred on y 80), each `Attacking` its own holding Raider 14.5 m in
   front (out of the walker's 14 m sight, so no scan of its own turns the Move into a chase). One Heavy Infantry at
   (67, 80.2), `Move` to (70 + d, 80). 600 ticks, hp topped up. The block is N members: inside `MaxPlugCluster` (32).

## Expected
docs/03 "Implementation (pathing slice 2)": the walker walks round the nearer end of an own planted block of up to 32
members and arrives; the brief's QA focus (b): "does the walker find an end, or loop between two ends?"

## Actual
| N | d = 5 m | d = 8 m | d = 13 m |
| --- | --- | --- | --- |
| 10 | arrives tick 57 | | |
| 16 | **gives up tick 153** at (68.9, 83.1) | arrives tick 77 | **gives up tick 72** at (70.2, 88.7) |
| 20 | **gives up tick 149** | | |
| 24 | **gives up tick 153** | | |
| 32 | **gives up tick 89** | **gives up tick 93** | **gives up tick 93** |

With the rule off every row gives up at tick 31. A per-tick trace of N = 16, d = 5 (scratch test): the walker flanks up the
row from y 80.2 to 85.7 (tick 48; the row's end is at 87.5), **reverses** and walks down to y 76.8 (tick 111), **reverses
again**, and gives up at y 83.1 on tick 153: it loops between the two ends and never clears either.

## Notes
- Suspected cause of the swing: `FlankTurn` measures both ends' turns from the *current* aim (the walker → goal line). As the
  walker walks toward one end, the aim at a near goal swings back behind it, so the near end's tangent needs more than
  `MaxFlankTurn` (135°) and `DetourTurn` falls through to the other side: the side flips even though the walker is 2 m from
  the end it was walking to. The kept-side preference (start-of-tick velocity) only orders the sides; it doesn't stop the
  bound from rejecting the kept one.
- Second cause, far goals (d = 13 m): the walk round a 16-32 m line takes longer than the flank budget
  (`FlankCountStride` 4: about 4 s of flanking), so the walker gives up as it reaches the end.
- Rows of 10 (the acceptance scenes) pass; the owner's playtest note ("infantry behind crossbowmen get stuck instead of
  going around them") is about armies, whose ranged lines are often wider than 10.
