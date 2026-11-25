# BUG-1536: Stress row `HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt(seed 3, FFA)` red, not named in the M6-SH2 report: the four-seat match no longer ends by 144,000

| Field | Value |
| --- | --- |
| Severity | S3 (a four-seat AI match that never ends; the row was moved under the brief's own rule, so it no longer blocks) |
| Status | open (row re-staged by criterion 8's rule; diagnosis corrected by QA re-check 1). **Owner decision 2026-11-25: a draw rule** (docs/01); cure planned as the sim's M6-S13 |
| Found | 2026-11-15-2136, task M6-SH2 (QA) |
| System | AI end game (hunt), moved by the drop-off rule (BUG-1520) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HuntStressTests.FourAiSeats_OnPaleHills_EndWithTheOracleOnEveryHunt"`
2. Row `(seed: 3, teams: False)` fails after about 17-47 minutes: `seed 3: no result by 144,000 ticks`.

## Expected
The free-for-all on Pale Hills, seed 3, four Normal AI seats, ends with one seat left inside 144,000 ticks, the hunt
oracle clean on every hunting think (as on the base).

## Actual
| Tree | Result |
| --- | --- |
| base `fdc8710` | green: over at 107,419 (89:30); p0 defeated 42:36, p2 61:52, p1 (holy_war) wins with 3 hunts |
| base + item 0 only (no retrain) | red: no result at 144,000. p2 defeated 28:47, p3 out; **p0 (standard, 32 units, 5 hunts) and p1 (holy_war, 13 units, 0 hunts) both alive** |
| M6-SH2 head | red, the same message |

The hunt oracle itself stays clean (leaks 0, misses 0, oracle v fog 0). Two seats with armies stand off for more than
an hour of game time, and the hunting seat's five hunts never find the other's last units. This is BUG-1068 / BUG-1116's
family (the developer's BUG-1523 is the same shape on Pale Hills seed 5, two seats).

## Notes
- The retrain plays no part: item 0 alone gives the same result.
- With BUG-1523 that makes two Pale Hills matches that stopped ending under the drop-off rule. Pale Hills is also the
  map where Hard stopped leading (BUG-1522). Worth one trace of what the economy change does on Pale Hills in particular
  before the pins are accepted.

## M6-SH2 fix round 1 (the developer): diagnosed, the row re-staged
Probe every 6,000 ticks: from about 30,000 no seat gathers gold again (gathered gold frozen at 8,880 / 8,850 / 8,890,
banks 2-16), every seat banks only wood, so none trains a soldier; p0 (5 soldiers, starved from 52,420) waves and hunts but
cannot finish p1 (13 units) and p3 (22), which have no army and so are never starved (`ai.starvedMinArmy`). BUG-1460's shape
with gold. The row moves to the smallest ending seed, 1 (91,995, p2, 4 hunts; seeds 2 / 5 end, 4 does not). No bound raised.

## QA re-check 1: the map's gold is exhausted, not unknown
- QA probe (`HuntStressTests.Match("pale_hills", ..., 3)`, the free-for-all seats): live gold mines on the whole map 4 with
  2,372 gold left at tick 24,000; **0 mines and 0 gold from tick 30,000** on. Every seat knows 0 gold nodes, has 0 workers on
  gold, and banks wood only (p3 19,852 wood, 16 gold at 90,000). Seats p1 and p3 (no army) are never stamped starved; p0
  (5 soldiers, starved from 52,420) can't finish them.
- So this is not BUG-1460's shape (forests standing but unknown to the seat). The match has spent the map's gold, and every
  unit costs gold. That is a design question for the Producer: what a match with no gold left should do (an armyless
  seat counting as starved, a wood-only unit, a draw rule). docs/03's fix-round line "not split further: mines empty or
  unknown" can now say "empty".
- The re-stage follows criterion 8's rule ("moves to the smallest ending seed and is listed as a finding with an id, never
  re-bound"): seed 1 is the smallest ending seed and the oracle is unchanged; green in QA's Stress run (91,995). Honest.

## 2026-11-25-1437 PLAN (the Producer): the draw rule's design, planned as M6-S13
The one-line design of the owner row ("no side has a unit that can fight") would not end this match: p0 keeps five
soldiers, and every unit (workers too) has an attack. Design as planned (docs/01, the 2026-11-25-1437 PLAN row): the
match is spent when no undefeated seat can ever train another unit (nothing queued; bank + carried gold + the map's
gold if it has a worker is below its faction's cheapest unit; or no producing building and no worker); spent for
`rules.json` `draw.afterSeconds` (300) without a non-summoned unit dying or a seat being defeated = a draw, nobody
defeated. This row is measured and reported by M6-S13 (criterion 10), expected to end.
