# BUG-1465: A Normal AI seat left with a finished Keep and 4,058 gold but no units never trains a 50-gold Laborer (Raraku seed 1, 42,000 ticks)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed ((a) the retrain, M6-SH2, session 2026-11-15-2136: `Ai/RetrainTests`, red on the base; (b) the hall rebuild was not a defect: it already worked, the seat was 18 wood short when the report stopped) |
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

## Note from D-R39 (2026-11-15-1536, BUG-1510 item 2)
A second shape of the same pattern: `AiOpeningPairsReportTests` (malazan 2, whirlwind 0) seed 13: the Whirlwind seat's Town Hall is razed, it keeps 4 workers, 1,072 gold and 257 wood to 20:00 and never rebuilds a hall. The report now reads that seat as "beaten: hall lost" (by the brief), so the row no longer flags it: the hardening's fix should cover "workers and gold, no hall" as well as "a hall and gold, no worker", with this seed as a row.

## M6-SH2
(a) Cause: workers were trained only by the opening's `train` steps, so a seat whose workers all died after those
steps never trained another. Fixed by `BuildOrderExecutor.Retrain`. QA: the retrained worker gathers; a camping enemy
drains one worker every 67 s or so, bounded by gold (18 trained, 17 lost in 20 minutes). Ruling (BUG-1534): the retrain
also runs on a starved think. (b) Not a defect; `RetrainTests.AWorker_NoHall_*` guards the rule. The brief's criterion 2
asked for a failing-first scene of a rule that already worked: the brief's error (rule 66), corrected in the session log.
