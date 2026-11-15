# BUG-1465: A Normal AI seat left with a finished Keep and 4,058 gold but no units never trains a 50-gold Laborer (Raraku seed 1, 42,000 ticks)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-15-0506, task M6-S10 (QA full, probing BUG-1460) |
| System | sim: AI (economy / production executor in the end game) |
| Fixed by | |

## Repro
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RarakuStallProbeQaStressTests" --logger "console;verbosity=detailed"`
(Raraku, Normal v Normal, seed 1, the shipped data; a line every 6,000 ticks per seat).

## Expected
A seat that still has a finished worker-training building and the gold for a worker trains one and goes back to gathering:
`malazan_laborer` costs 50 gold and 0 wood, and the seat uses 0 population.

## Actual
Seat 0 (Malazan), every line from 30,000 to 72,000: gold 4,058, wood 5, **workers 0, army 0**, buildings 18 falling to 11,
worker-trainers 1 (a finished Keep). No Laborer is trained in 42,000 ticks; the match never ends (BUG-1460). The knob-off
copy (the cure alone) shows the same shape on the other seat before its end: p1 at 30,000 with 1,281 gold, 0 workers, 0
army, 12 buildings, 1 worker-trainer, defeated at 35,736.

## Notes
Cause not traced (candidates: the seat defends every think while two enemy soldiers raze its buildings and defence blocks
production; the opening's worker step is long done and nothing re-arms it). It is half of why BUG-1460's matches don't end:
the loser cannot rebuild, and the winner (17 idle workers, the nearest wood 41.8 m from its drop-offs, 2 soldiers) cannot
finish. Not a held worker: neither seat has a worker on a loop.
