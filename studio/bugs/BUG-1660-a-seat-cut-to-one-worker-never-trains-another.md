# BUG-1660: an AI seat cut down to one worker never trains another while that one lives

| Field | Value |
| --- | --- |
| Severity | S3 (a beaten seat never recovers or acts; it is the silent seat of BUG-1621) |
| Status | fixed (M6-S14 item B, session 2026-11-26-1847; QA verified, accepted 2026-11-26-1847) |
| Found | 2026-11-24-1431, task M7-8 (the developer's trace of BUG-1621; QA re-ran the trace and filed it) |
| System | AI economy (`BuildOrderExecutor.Retrain`, the build order's worker `train` steps) |
| Fixed by | M6-S14 (B) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~VatharStallTraceTests"` (Stress; 8-9 minutes under load).
2. Seat 2: one worker from 30,000 to the end (88,998), 876 gold / 10 wood at 60,000, no Train after 37,581.

## Expected
A seat with gold, a hall and too few workers to rebuild trains workers (docs/02 "AI opponent": the AI keeps an economy).

## Actual
Workers are trained only by the opening's worker `train` steps (done for good once their count of commands is accepted)
and by the M6-SH2 retrain, which fires only at zero live and queued workers. One worker left = no worker ever again.
QA's re-run (2026-11-24-1431) printed the same table as docs/03 "Implementation (M7-8)": seat 2 at 32,000-36,000 has
1 worker, 0 army, 1 hall, 0 producers, 66-96 gold, last command 30,781; one Laborer trained at 37,581 after the worker
died; 876 / 10 at 60,000 and still one worker. The lone worker gathers gold only (the gold share rounds one gatherer to
gold), so the bank never holds the wood the reopened build steps need either.

## Notes
- Cure (recommended, not built): the retrain becomes a floor, `rules.json` `ai.minWorkers` (the Producer's value; the
  trace suggests 4): fewer live + queued workers than that and the bank pays one, one Train at a hall a think. About 15
  lines and a hashed rules field. Moves every AI pin where a seat falls under the floor (docs/03 "Implementation (M7-8)").

## Fix (M6-S14)
The retrain is a floor: `rules.json` `ai.minWorkers` (shipped 4, 1-50; 1 is the M6-SH2 rule and hashes as the old data). Test: `Ai/RetrainTests.OneWorker_AKeepAndGold_TrainsUpToTheFloor_OneATrainAThink` (red on the base). The session's one data-hash move.
