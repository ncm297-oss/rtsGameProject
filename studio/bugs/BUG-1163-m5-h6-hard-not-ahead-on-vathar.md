# BUG-1163: M5-H6 criterion 3: the Hard v Normal tally is level on Vathar Crossing (7 / 7), not "Hard ahead on every map"

| Field | Value |
| --- | --- |
| Severity | S3 (re-triaged by the Producer at the 2026-11-09-1336 ACCEPT: filed S2 as "an acceptance criterion not met as written"; the criterion clause is waived for M5-H6 because the levers in scope act after a Vathar match is decided and the 7 / 7 is the same at M5-9a, M5-9b and M5-H6; what remains is a tuning item) |
| Status | **fixed** (D27, 2026-11-10-0146, 1 fix round: Hard `gatherBonus` 0.25 -> 0.4 in both `ai.json`; the tally 32 / 8 / 2, Hard ahead on every map, Vathar 11 / 3 / 0; proven by `Stress/HardVersusNormalTallyQaStressTests` and `QA/Content/D27HardMirrorQaStressTests`; before it: open, Producer ruling 2026-11-09-1336: criterion 3 read as "not inverted, no-results fewer than 5"; the Hard-level-on-Vathar case was the data track's Hard tuning item) |
| Found | 2026-11-09-1336, task M5-H6 (developer's finding, QA confirmed) |
| System | AI difficulty (Hard v Normal), measured by `Stress/HardVersusNormalTallyQaStressTests` |
| Fixed by | D27: 3d25e9d (implement) + 6b72729 (fix round 1, BUG-1185 re-pins), branch studio/2026-11-10-0146-data |

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

## D27 (2026-11-10, data track)
Hard's `gatherBonus` 0.25 -> 0.4 in both factions' `ai.json` (no other key; Normal / Easy untouched).

Candidates on Vathar's 14 matches (data copies of the shipped tree, Hard / Normal / none, seat 0 + seat 1):

| Hard values | Vathar |
| --- | --- |
| shipped (base) | 3/4/0 + 4/3/0 = 7 / 7 / 0 (matches this file) |
| `expandAfter` 300 -> 480 | 7 / 7 / 0 (every match identical to base) |
| `waveSizeMultiplier` 1.25 -> 1 | 2/5/0 + 2/5/0 = 4 / 10 / 0 |
| `gatherBonus` 0.25 -> 0.4 (shipped) | 6/1/0 + 5/2/0 = 11 / 3 / 0 |

Full 42 once on the branch (`HardVersusNormalTallyQaStressTests`): **32 / 8 / 2** (was 29 / 11 / 2).

| Map | Hard seat 0 | Hard seat 1 | Total |
| --- | --- | --- | --- |
| Raraku | 4 / 3 / 0 | 6 / 1 / 0 | 10 / 4 / 0 |
| Pale Hills | 5 / 0 / 2 | 6 / 1 / 0 | 11 / 1 / 2 |
| Vathar Crossing | 6 / 1 / 0 | 5 / 2 / 0 | 11 / 3 / 0 |

Per-seed Vathar (Hard's seat: result at tick): seat 0: s1 hard 21,191; s2 normal 28,041; s3 hard 26,979; s5 hard 28,752;
s6 hard 26,979; s7 hard 25,970; s8 hard 25,970. Seat 1: s1 hard 26,889; s2 hard 26,289; s3 hard 26,219; s5 hard 16,538;
s6 hard 26,219; s7 normal 32,166; s8 normal 32,166.
Base per-seed Vathar for comparison: seat 0: s1 normal 27,368; s2 hard 24,181; s3 normal 28,618; s5 normal 29,387;
s6 normal 28,618; s7 hard 25,339; s8 hard 25,339. Seat 1: s1 hard 25,063; s2 normal 29,374; s3 hard 22,370; s5 hard 28,694;
s6 hard 22,370; s7 normal 25,101; s8 normal 25,101.
Pale Hills lost one Hard win: seed 2, Hard in seat 1, Normal wins at 69,129. Still two no-results, but a different match:
Pale Hills Hard seat 0 seeds 7 / 8 (one match; seeds 7 and 8 pick the same openings), where the base's were seeds 3 / 6 (now
Hard wins at 42,129).

Fix round 1 (BUG-1185): `QA/StandardPresetPinQaTests` five Hard-seat rows re-pinned (old values in comments); docs/03 current-fact 25% mentions updated (BUG-1184).
