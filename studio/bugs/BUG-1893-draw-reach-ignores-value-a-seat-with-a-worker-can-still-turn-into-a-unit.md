# BUG-1893: The exact draw rule calls a seat spent while its worker can still buy a drop-off from the bank and so field a unit

| Field | Value |
| --- | --- |
| Severity | S3 (as specified in docs/01 2026-11-27 second row (1), so not an acceptance-criterion miss; but the spec's stated reason does not hold on shipped costs, and the Producer's QA focus calls a wrongly early draw S2: the Producer's call) |
| Status | open |
| Found | 2026-11-27-0817, task M6-SH3 (QA, focus 1) |
| System | draw rule (`World.CanStillTrain`, docs/02 "Game flow" 4) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6Sh3DrawQaTests.Bug1893_"` (a pin of the rule as built; it passes).
2. The scene uses shipped data and the shipped 300 s wait. A Malazan seat has a Laborer, its Infantry Hall (a unit producer), no
   drop-off, a gold mine on the map, and a bank of **35 gold / 100 wood**. The cheapest Malazan unit by gold is the Crossbowman
   (36 gold / 45 wood); the Malazan Depot is a drop-off costing **0 gold / 75 wood** (25 s). The other seat is spent.

## Expected
docs/01 2026-11-25 (the owner's decision) and docs/02 "Game flow" 4: a match is drawn when nobody can make a unit any more.
This seat can: its Laborer builds a Depot from the bank, mines the map's gold, and the barracks trains a Crossbowman.

## Actual
`CanStillTrain(0)` is false and `MatchSpent` is true from tick 0: the draw clock runs. In the pin it runs 529 ticks, until the
Depot the Laborer was ordered to build stands; then the map's gold is reach and the clock stops. A player who doesn't start the
Depot within the 5-minute wait has the match drawn under them while their way back is in plain sight (the countdown notice is
the only hint).

The same family (all explicitly outside the rule by docs/01 (1), each a way a seat with a live worker can still turn paid or
banked value into a unit):
- **A paid item waiting at the population cap** with an empty bank: spent ((a) does not count it, (b) has an empty bank), but a
  Cancel refunds its full cost, which can buy a house (Billet 0 / 50) and leave enough for a unit: e.g. a waiting Catapult
  (200 / 150) refunds enough for a Billet and a Heavy Infantry (54 / 20). The clock does stop on the cancel's tick
  (`M6Sh3DrawQaTests.ACancelledItemAtTheCap_ItsRefund_StopsTheClockOnThatTick`), so only a passive seat is drawn.
- **A paid site that nobody builds** (a drop-off site, docs: "a site is not a drop-off"): its refund is not reach; the seat is
  spent until the site is finished or cancelled (`DrawRuleQaTests.ASiteCancelRefund_*` shows the cancel stops the clock).

docs/01's reason for leaving the bank's drop-off out, "the bank is then nearly always enough for a unit anyway", fails here
because the Depot costs only wood and every Malazan, Teblor and Whirlwind combat unit costs gold: a seat holding wood but
under ~36-70 gold (by faction) with gold still on the map is exactly the "mined-out base, wood left" state the draw rule is
meant to judge.

## Notes
- Not a regression against the old rule's direction for this state: the old rule (M6-S13) said "can train" here (map gold with
  any worker), so this is one of the states the new rule narrowed. The developer's fuzz ("old spent implies new spent") is
  green; this is the other direction, intended by the spec.
- No AI-vs-AI match in QA's tables shows it (Pale Hills / Vathar four-seat seeds 1-8, Raraku seeds 1-8: identical end states
  for every match that drew on the base), so the risk is a human player's quiet match.
- A cure inside the generic rule, if wanted: with a live worker and no finished drop-off, a seat whose bank pays for one of its
  faction's drop-off buildings counts the carried and map resources as reach (counters only: the drop-off types and costs are
  data). Same shape for a waiting item's or a site's refund. Owner / Producer decision; not built.
