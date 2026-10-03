# BUG-0005: CommandQueue insertion sort is O(n^2); 10k interleaved commands stall ~107 ms

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-03-0907, task M1-1 |
| System | command queue / tick loop |
| Fixed by | |

## Repro
1. Un-skip `Rts.Sim.Tests.Stress.SimCoreStressTests.Flood_10000InterleavedCommands_NoFrameStall`.
2. `dotnet test sim/Rts.Sim.Tests -c Release --filter FullyQualifiedName~Flood_ --logger "console;verbosity=detailed"`

The test enqueues n `Noop`s from 8 players in round-robin, descending player order, then times the
two ticks that sort and apply them (worst of 3).

## Expected
docs/03 perf budget: average tick < 4 ms, p99 < 8 ms. A command burst should not stall a tick
beyond the 50 ms frame.

## Actual (Release, this machine)

| Commands in one tick | Worst two-tick time |
| --- | --- |
| 500 | 0.4 ms |
| 1,000 | 1.2 ms |
| 2,000 | 4.8 ms |
| 10,000 | 107 ms (Debug: 175 ms) |

Clean quadratic growth (x4 per doubling). Allocation stays at 0 bytes, which is good.

## Notes
`CommandQueue.Sort` is an insertion sort over the whole pending buffer. Commands arrive already in
tick order and each player's sequence is already increasing, so the only disorder is the
interleaving of players; insertion sort pays one move per inversion, about n^2/2 for many players.
Not reachable at M1 sizes; becomes relevant if move orders are issued per unit (500 units x a few
players, plus AI). Options: per-player buckets merged in player order (O(n), no sort at all, since
sequence is already monotonic per player), or an in-place heap/merge sort on a preallocated scratch
buffer. Below ~1,000 commands per tick the current code is within budget, so S3 not S2.
