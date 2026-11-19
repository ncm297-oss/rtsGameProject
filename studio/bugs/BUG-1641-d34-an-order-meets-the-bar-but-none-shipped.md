# BUG-1641: D34 took the "none ships" fallback, but an order-only variant of the Teblor `standard` meets criterion 2's bar on the full rows

| Field | Value |
| --- | --- |
| Severity | S2 (the brief's goal, BUG-1612, is reachable inside D34's scope; the QA focus asks exactly this: "check the fallback was not taken where an order would have done") |
| Status | fixed (QA re-check 2026-11-18-0926, fix round 1) |
| Found | 2026-11-18-0926, task D34 (QA) |
| System | data: `game/data/factions/teblor/ai.json` `standard` `buildOrder` (order and `at.time` only) |
| Fixed by | D34 fix round 1 (6eae0da): Q2 shipped in `teblor/ai.json`; `QA/Content/D34QaTests.TheShippedData_IsD34s_QasQ2WithTheProposedText`, `Content/TeblorOpeningFixtureTests.TheShippedOpening_IsTheD34Fixture` |

## Repro
1. `$env:D34QA_VARIANTS='M,Q2'` (or leave it unset for M, Q1, Q2, Q3), then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D34OrderQaStressTests.QaOrders_Row_Report" --logger "console;verbosity=detailed"`
   (Stress; QA's own loop in `QA/Content/D33TeblorRowQaStressTests`' shape: the Teblor on seat 0 against the Malazan and
   on seat 1 against the Whirlwind, the same difficulty on both seats, four maps, seeds 1-8, to 15:00: 64 matches a row.
   All four variants x 3 difficulties took 1 h 23 m on a shared machine).
2. Q2 is the shipped opening (M, the D33 fixture) with only these moves: the Bloodwood Carver and the Blood-oil Anointing
   step moved to just after the Breaker's train step and ahead of the Destrier Rider and War-dog train steps, with the
   Anointing due at 10:00 (600 s; it was 11:30). Steps 17-21 then read: train Breaker (10:00), build Carver (10:00),
   research Anointing (10:00), train Rider (10:00), train War-dog (10:00); everything else as shipped.
   `D34OrderQaStressTests.QaVariants_KeepMsSteps_AndLoad` (4 s, in the Stress class) proves it keeps M's steps as a multiset
   and every other field, and loads.

## Expected
Criterion 2: a variant ships if, on the full rows, Easy's Anointing is at least 20 of 64 while on Normal and on Hard each
of Breakers, Riders and War-dogs stays at 8 or more, Normal's Shamans at 14 or more, its second Longhouse at 9 or more
and its "beaten" count at 15 or less.

## Actual
QA's full rows (defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing, of 64). QA's M rows equal the
developer's M rows and QA's D33 rows to the match, so the loop agrees with both earlier harnesses:

| variant | Easy | Normal | Hard |
| --- | --- | --- | --- |
| M (shipped) | 1 / 0 / 34 / 27 / 36 / 51 / 1 | 10 / 23 / 25 / 12 / 22 / 39 / 11 | 11 / 23 / 25 / 35 / 43 / 48 / 18 |
| **Q2** | 1 / 0 / 16 / 30 / 4 / 27 / **28** | **9 / 23 / 15 / 14 / 22 / 37** / 17 | 11 / 23 / 22 / **39 / 33 / 46** / 23 |
| Q1 (D34's "not tried" order: Carver + Anointing at 10:00 just after the three trains) | 1 / 0 / 17 / 29 / 28 / 47 / 18 | 9 / 23 / 22 / 13 / 25 / 38 / 12 | 11 / 23 / 23 / 39 / 42 / 48 / 25 |
| Q3 (Q1, Anointing kept at 11:30) | 1 / 0 / 25 / 28 / 29 / 49 / 17 | 9 / 23 / 26 / 13 / 19 / 38 / 12 | 11 / 18 / 23 / 38 / 42 / 47 / 24 |
| G5 (developer's best, for comparison) | 1 / 0 / 25 / 2 / 2 / 44 / 32 | 9 / 23 / 15 / 7 / 20 / 40 / 19 | 11 / 23 / 21 / 27 / 38 / 46 / 30 |

Q2 meets every figure of the bar: Easy's Anointing 28 (>= 20); Normal Breakers / Riders / War-dogs 14 / 22 / 37, Hard
39 / 33 / 46 (each >= 8); Normal Shamans 15 (>= 14, the tight one), second Longhouse 23 (>= 9), defeated 9 (<= 15).
Q1 (the order the page names as "not tried") misses on Easy's Anointing (18), as does Q3 (17).

## Notes
- Why Q2 works, read against the developer's own cause: G5 starved the Breaker because the Anointing, once ahead of all
  three train steps, holds the reserve and the costliest unit is the one that goes. Keeping the Breaker's train step
  ahead of the Anointing lets it spend first; the Rider and War-dog steps behind it are cheaper. Normal's Breakers rise
  12 -> 14.
- What Q2 trades (not covered by the bar, for the owner's review table if it ships): Easy's Riders 36 -> 4 and War-dogs
  51 -> 27, Easy's Shamans 34 -> 16 (main had 51); Normal Shamans 25 -> 15; Hard Riders 43 -> 33. Easy regains the
  Anointing (1 -> 28), Normal 11 -> 17, Hard 18 -> 23.
- The brief capped the screen at five variants and the developer stayed in it, so this is not a process fault; it is
  that the outcome "BUG-1612 waits for Request 55" is not forced by order alone. If Q2 ships, the hash moves and the
  whole pin list of the brief applies, plus the tooltip / page following Q2's row.
- QA ran Q2 only through QA's loop, not the developer's `D34TeblorStepOrderReportTests` harness; both loops print
  identical M rows, so the counting agrees.

## Re-check (2026-11-18-0926, fix round 1)
- The shipped `standard` build order equals QA's Q2 field for field (`D34QaTests`, green). Data hash 318E8C4256456C05 ->
  A732F387C2BB7E4D.
- QA's loop on the shipped data itself (`D34OrderQaStressTests.QaOrders_Row_Report` with `D34QA_VARIANTS=S`, i.e.
  `TestSim.Data`, 192 matches in 956 s): Easy 1/0/16/30/4/27/28, Normal 9/23/15/14/22/37/17, Hard 11/23/22/39/33/46/23
  (defeated / 2nd Longhouse / Shaman / Breaker / Rider / War-dog / Anointing); Javelin Throwers 64 / 55 / 64. Equal to
  QA's first Q2 run and to the developer's harness to the match. Every figure of criterion 2's bar is met.
