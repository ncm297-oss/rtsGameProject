# BUG-1534: Stress row `StarvedPickOracleQaStressTests.AStampedThink_SendsNoTrain(raraku, 5)` red, not named in the M6-SH2 report: the retrain sends a worker Train on a starved think

| Field | Value |
| --- | --- |
| Severity | S2 (Stress red outside the rows M6-SH2's criterion 11 allows; two documented AI rules now disagree and neither the docs nor the row says which wins) |
| Status | closed (ruled: the retrain wins; oracle exemption verified narrow by QA re-check 2) |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI: `BuildOrderExecutor.Retrain` (BUG-1465 (a)) against the M5-9a starved rule's oracle |
| Fixed by | 039d6eb (M6-SH2 fix round 2: test + docs, per the Producer's ruling) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StarvedPickOracleQaStressTests"`
2. Row `(raraku, 5, Normal, 2, 72000)` fails: `seat 0 tick 27960: stamped starved (since 26900) but sent 1 Train; gold 4356,
   wood 20, pick 0`. The match ends at 31,420, winner p1 (the same end as `MatchRowsM6S10Report`'s Raraku seed 5 on this tree).

## Expected
The row's oracle (QA, M5-9a round 2): a think that ends with the seat stamped starved sends no Train, because a starved
seat cannot pay for what production waits for.

## Actual
Seat 0 has no worker left; the new retrain (docs/02 "AI opponent": "trains one first thing ... whatever its build order
says") sends a worker Train on a think where the seat is stamped starved. The row is not among the base's red rows
(M6-S10: seed 5 ends at 29,569). QA ran it on base + item 0 only (no retrain): **green**, the match ends at 31,089, seat 0
stamped on 322 thinks with no Train on any of them; the retrain is the only new Train source. The Train itself looks right (a worker is how a starved seat gets the kind it lacks), so this is the
oracle meeting a new rule, not a wrong Train.

## Notes
- For the Producer: rule which wins. If the retrain may run on a starved think (QA's reading of docs/02), the row's oracle
  should exempt a worker Train sent while the seat has no worker (`ai.WorkerCount == 0`), with the reason in the row, and
  docs/02's starved paragraph should say so. QA did not edit the row (no loosening without a ruling).
- Not in the developer's "for ruling" list; the developer's Stress run must have missed it or run a filter that did not
  include `StarvedPickOracleQaStressTests`.

## M6-SH2 fix round 1 (the developer): for ruling, left red
(a) Retrain wins: the oracle exempts a worker Train sent while the seat has no worker; docs/02's starved paragraph says
so; test and docs only, no pin moves. (b) Starved wins: `Retrain` sends nothing while `StarvedSince >= 0`; a behaviour
change: Raraku seed 5 diverges from 27,960 (now 31,420; with no retrain 31,089), any match whose worker-less seat is stamped
re-rolls, every pin re-measured again, and a stamped seat with gold and no worker never retrains.

## QA re-check 1
Still red in QA's Stress run (`AStampedThink_SendsNoTrain (raraku, 5)`). Judgement: a real conflict between two documented
rules (docs/02: a seat with no worker "trains one first thing ... whatever its build order says"; the starved rule: "it
waits for that one rather than buy a cheaper one"), so a ruling is needed, not a fix. QA leans to (a): the worker retrain is
the newer and narrower rule, and a seat with no worker cannot otherwise recover; (a) is test and docs only.

## QA re-check 2 (fix round 2, 039d6eb)
- Ruling (a) applied: `StarvedPickOracleQaStressTests` exempts a worker-type Train sent while the seat has no live worker;
  docs/02's starved paragraph has the sentence. The class whole: 6 / 6 green; across all six rows exactly one exempt Train
  (Raraku 5 seat 0), every other count 0.
- Narrowness proved with a throw-away probe driving the oracle's hooks on a stamped think (deleted after): a worker Train
  while a worker lives fails; a non-worker Train with no worker fails; a worker plus a soldier Train on one think with no
  worker fails; a lone worker Train with no worker is exempt. Closed.
