# BUG-1860: Pale Hills four-seat FFA seed 4 also never ends after M6-S14 (the base drew at 63,124); both stalls are seats counted "can still train" on gold they cannot use

| Field | Value |
| --- | --- |
| Severity | S3 (measure and report under criterion 11 / rule 48; not a criterion row) |
| Status | open (M6-SH3 B: p1's half fixed, the draw rule exact; the match is held by the AI: BUG-1890 / 1891) |
| Found | 2026-11-26-1847, task M6-S14 (QA, tier full; QA focus 3) |
| System | AI end game + the draw rule (`World.CanStillTrain`, docs/02 "Game flow" 4) |
| Fixed by | |

## Repro
1. `Stress/M6S14FourSeatQaStressTests` (any seed class, `FourSeats_Report(map: "pale_hills", teams: False)`), or
   `DrawStalledRowsReportTests.Play(DrawStalledRowsReportTests.Build("pale_hills", 4, 4, null), 144_000)`.
2. QA's table, base f40cb16e against 0058e09d (Release, the base figures equal the developer's Debug ones): Pale Hills FFA
   seed 2 DRAW 58,378 -> NO END by 144,000 (BUG-1854), **seed 4 DRAW 63,124 -> NO END by 144,000** (not measured by the
   developer), seed 5 won 73,778 -> drawn 44,326, seed 6 drawn 51,216 -> won 81,444, seed 8 won 62,611 -> drawn 67,743.

## Expected
docs/02 "Game flow" 4: a match ends, or once no seat can train for 5 minutes it is drawn. Every Pale Hills four-seat seed
1-8 ended on the base.

## Actual
QA's probe (state every 12,000 ticks from 60,000; nothing changes from about 72,000 to 144,000):
- Seed 2 (BUG-1854's trace): the map's gold is gone. p0 and p2 (Malazan) hold 40 and 50 gold with 28 workers each, no army,
  and never train. Their faction's cheapest unit is the Crossbowman (36 gold + 45 wood); the draw rule reads gold only
  ("wood is not looked at"), so both count as "can still train" for 84,000 ticks. p1 / p3 (Whirlwind, 6 and 14 gold) cannot.
- Seed 4: p2 out. p1 has no building (cap 0), one worker carrying 10 gold, 21 gold banked: with a worker alive the rule adds
  the map's 726 gold, so "can still train", but it has nowhere to drop gold off and cannot afford a hall. p3 has 6 workers
  carrying 30 gold that is never delivered, 40 banked; p0 has 26 workers and 52 gold, no army. Nobody acts and nobody is
  "spent".
- In neither match was a unit queued after 60,000, and no seat with a hall had fewer than 4 workers: **the worker floor is
  not the cause** (QA focus 2's question against BUG-1731: a floor Train needs a worker's gold, which is at least the
  faction's cheapest unit's, so the floor can never send while the clock runs; it can only block the clock through a paid
  worker waiting at the cap, BUG-1857).

## Notes
- M6-S14's play changes (all four cures) reach these end states; the draw rule's documented limits (gold only; map gold
  counted for any seat with a worker, even one with no drop-off) turn them into matches that never end.
- Candidate rules for the hardening: count wood for a unit that needs it; count the map's gold only for a seat with a
  finished drop-off (or able to pay for one).

## M6-SH3 B (2026-11-27-0817)
p1 (one worker, no drop-off, 21 gold + 10 carried) is now "cannot train" (the map's gold counts only with a finished drop-off).
p0 (52 gold, 11,155 wood; 726 gold left on the map within reach of 26 workers and four drop-offs) and p3 (40 gold + 30
carried, two drop-offs) can still pay for units and neither trains nor mines the 726 gold: BUG-1890 (production) and
BUG-1891 (the gold left unmined / undelivered). After B the match is unchanged (no end by 144,000, same end hash
28B445B7F11EC069).
