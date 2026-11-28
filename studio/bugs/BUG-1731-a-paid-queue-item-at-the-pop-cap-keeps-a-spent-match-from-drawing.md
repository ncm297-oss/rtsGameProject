# BUG-1731: a paid unit waiting in a queue at the population cap counts as "can still train" for ever, so a maxed stand-off never draws

| Field | Value |
| --- | --- |
| Severity | S4 (a design gap of the draw rule, found by QA as a note; no test red) |
| Status | fixed |
| Found | 2026-11-25-1437, task M6-S13 (QA note, filed by the Producer) |
| System | match end (`World.Draw.cs`, `CanStillTrain`) |
| Fixed by | M6-SH3 B (`World.CanStillTrain` (a): a waiting item counts only with population room now; `Draw/DrawReachTests.AQueuedPaidItem_AtTheCap_*`) |

## Repro
Two seats, no gold anywhere, each at its population cap with one paid unit in a production queue. Neither attacks.
`CanStillTrain` is true for both for ever (the queue clause), so the match is never spent and never draws.

## Notes
- The design (docs/01 2026-11-25-1437 PLAN) says "a unit in a production queue" without a pop clause; the code is the design.
- The AI attacks at its cap, and a human can cancel the item, so this needs two passive seats. Candidate cure: a queue
  item counts only if the seat has pop room for it or could gain it (owner-visible rule change: one hash-neutral code
  change, pins re-checked).
- Sibling question, not a bug: Pale Hills FFA seed 2 now draws at 58,378 where the base reached a win at 59,951. An AI
  that presses its attack while the clock runs belongs to the AI-play session.
