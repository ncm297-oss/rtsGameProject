# BUG-1163: M5-H6 criterion 3: the Hard v Normal tally is level on Vathar Crossing (7 / 7), not "Hard ahead on every map"

| Field | Value |
| --- | --- |
| Severity | S3 (re-triaged by the Producer at the 2026-11-09-1336 ACCEPT: filed S2 as "an acceptance criterion not met as written"; the criterion clause is waived for M5-H6 because the levers in scope act after a Vathar match is decided and the 7 / 7 is the same at M5-9a, M5-9b and M5-H6; what remains is a tuning item) |
| Status | open (Producer ruling 2026-11-09-1336: criterion 3 read as "not inverted, no-results fewer than 5", as the previous ACCEPT read it; **the Hard-level-on-Vathar case is the data track's Hard tuning item** (`ai.json` Hard knobs / openings, a larger sample, rule 43 on its own branch), not a sim bug) |
| Found | 2026-11-09-1336, task M5-H6 (developer's finding, QA confirmed) |
| System | AI difficulty (Hard v Normal), measured by `Stress/HardVersusNormalTallyQaStressTests` |
| Fixed by | |

## Repro
1. On a clone of 8d1a9e3: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HardVersusNormalTallyQaStressTests" --logger trx`
   and read the `TALLY-SUM` lines from the trx (QA ran it inside the full Stress + Soak run, 1 h 12 m).

## Expected
Brief M5-H6, acceptance criterion 3: "The 42-match Hard v Normal tally re-run once: no-results fewer than 5, Hard ahead on
every map."

## Actual
Hard 29 / Normal 11 / no result 2 (exactly the developer's figure):

| Map | Hard seat 0 | Hard seat 1 | Total (Hard / Normal / none) |
| --- | --- | --- | --- |
| Raraku | 5 / 2 / 0 | 5 / 2 / 0 | 10 / 4 / 0 |
| Pale Hills | 5 / 0 / 2 | 7 / 0 / 0 | 12 / 0 / 2 |
| Vathar Crossing | 3 / 4 / 0 | 4 / 3 / 0 | **7 / 7 / 0** |

No-results 2 (< 5: met). Hard is level, not ahead, on Vathar Crossing.

## Notes
- Not caused by M5-H6: Vathar read 7 / 7 / 0 at M5-9b (QA 2026-11-09-0426) and M5-9a too; none of the shipped levers
  touches a Vathar match much (the house rule moves the Vathar Normal pin by 279 ticks; no free start is used in a 1v1 there).
  The criterion asked for more than the levers in scope (all AI end-game rules, no `ai.json`) can deliver.
- The two no-results are one match (Pale Hills Hard seat 0 seeds 3 / 6, the same map file and openings): a long war Normal
  wins at 116,335 (QA's probe `EndGameH6ProbeQaStressTests` reproduces it, hunt fog oracle clean). It is a real fight to the
  end, not a decided match finishing late.
- The Producer's call: waive "Hard ahead on every map" for this task (criterion 3 then reads "not inverted, no-results < 5",
  as at M5-9b), or carry it as Hard's tuning item (STATE "Rough edges" (1): Hard not measurably stronger than Normal on 42
  matches; a data-track `ai.json` task with a larger sample).

## M5-H6 fix round 1
Re-run once on the fix-round code (the hunt now also looks at a live enemy's start after the free ones): **29 / 11 / 2**
again, Vathar 3 / 4 / 0 and 4 / 3 / 0, the same winners as round 1 (Normal's Vathar wins at 27,368-29,387 ticks). The
end-game levers in scope (hunt order, house rule, leg cap) finish decided matches sooner; none of them has changed a
Vathar winner across M5-9a, M5-9b, M5-H6 and this round, so "Hard ahead on Vathar" looks like Hard's tuning
(`ai.json`, out of scope by rule 41). Left for the Producer.
