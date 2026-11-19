# BUG-1621: `FarExpandProbeQaStressTests` (vathar_crossing, 2, Normal, 4) is red on the base: seat 2 silent 6,780 ticks

| Field | Value |
| --- | --- |
| Severity | S3 (a Stress row red before M8-3; no shipped-data behaviour changed by M8-3) |
| Status | open |
| Found | 2026-11-18-0926, task M8-3 (reported by the developer without an id; reproduced by QA on base and branch) |
| System | AI end game / AI think cadence (the four-seat Vathar Crossing FFA, same match as BUG-1610) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --no-build --filter "DisplayName~AProbedMatch&DisplayName~players: 4"`
2. On the diff base cc5614a (= 0317e7e's sim and data) and on M8-3's head bf08506 alike:
   `FAR | vathar_crossing | 2 | Normal v Normal | 4 | ends | 88998 (74:09) | p3 | waves 13 hunts 7 | 06F14EE840630135`,
   then `silent seat: p0 silent 17261..17640; p1 silent 10661..11420; p2 silent 30781..37561; p3 silent 29101..30240`
   (`silent max 379/759/6780/1139`). Base 29.5 min, branch 7.1 min wall (shared machine); same end tick, hash and spans.

## Expected
Every seat issues a command at least every `MatchCriterionSweepQaStressTests.SilentTicks` ticks (the Stress category had no
expected red row after D33, per the 2026-11-17-0816 handoff).

## Actual
Seat 2 (and seat 3 by 1,139) goes silent for 6,780 ticks (30,781-37,561); the match also runs to 88,998, past BUG-1610's
72,000 bound.

## Notes
- Not M8-3: QA's shipped-data twin (3 FFA / 2v2 / Hard 1v1 AI matches x 30,000 ticks, hash every 500 ticks) is identical
  between base and branch, and this row's end tick and hash are identical on both trees.
- Most likely the same cause as BUG-1610 (D33's changed openings on this map and seed; two survivors that stop acting). The
  developer's report says the row was already red on 0317e7e. The Producer may fold this into BUG-1610.
