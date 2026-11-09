# BUG-1154: The starved rule is cleared by any gatherable short kind, even when nothing it could ever gather makes a unit buyable

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (M5-9a fix round 1, 65524da; QA re-check 2026-11-08-1556) |
| Found | 2026-11-08-1556, task M5-9a (round 2, QA full) |
| System | AI (the starved wave: `AiPlayer.Starved`, clause (ii)) |
| Fixed by | 65524da "M5-9a: fix QA findings (round 1)": `Starved()` judges (i) on `ProductionManager.Pick` (split from `TrainOne`, same logic; with no producer free, every entry) and (ii) per unit (`CanEverPay`: a unit is saved only if every kind it lacks is obtainable). Proof: QA's fail-first `QA/StarvedWaveQaTests.AGoldStarvedSeat_WithWoodUnderACost_ButNoGoldEver_IsStarved` un-skipped and green; `StarvedWaveTests.ASeat_WhosePickIsNeverPayable_*`; QA rows below |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StarvedWaveQaTests.AGoldStarvedSeat_WithWoodUnderACost_ButNoGoldEver_IsStarved"`
   with the row's `Skip` removed (fail-first, skipped under this id): the flat scene, a Malazan Normal seat with every gold
   mine on the map emptied, 0 gold, wood held at 0 (its workers gather wood), three Heavy Infantry, a Heavy Infantry
   (54 g / 20 w) / Crossbowman (36 g / 45 w) mix, an enemy tent in sight.
2. Fails: `stamp -1, wave at -1`. The seat can never buy anything again (every unit needs gold and no gold exists), but
   it is never stamped starved, so the starved wave never fires.
3. Found first in the developer's own scene shape (`StarvedWaveTests.Scene`, 100 wood) at Hard with 12 Heavy Infantry:
   at tick 480 the seat spent 50 wood (100 -> 40, under a Crossbowman's 45), `Starved()` turned false, the stamp cleared
   at 490 and was set again at 610: the wave moved by 610 ticks with no change in the seat's real prospects.

## Expected
docs/02 "AI opponent" (round 2): "A side that can no longer grow attacks with what it has". A seat for which **every**
composition entry is short of some kind it cannot get (no node known in reach, no worker on it) can no longer grow and is
starved, whatever other kind it can still gather.

## Actual
`Starved()` collects `shortGold` / `shortWood` over all entries and then returns false if **either** short kind is
gatherable:

```csharp
if (shortGold && (Gathering(EconomyManager.Gold) || WalkForNode(ResourceKind.Gold))) return false; // (ii)
if (shortWood && (Gathering(EconomyManager.Wood) || WalkForNode(ResourceKind.Wood))) return false;
```

So an entry short of gold **and** wood (a Crossbowman at 0 gold / 40 wood) makes wood a "short kind", wood is gathered,
and the seat reads as not starved, although gathering wood never makes that entry (or any other) buyable. This follows
the brief's literal wording of (ii) ("for each resource kind that is short in (i)"), so it is the rule as specified, not
a slip in the code; the spec misses the case.

Measured in whole matches (`Stress/StarvedWaveProbeQaStressTests`, thinks where every entry is short of a kind the seat
cannot get but the shipped rule says not starved): Pale Hills seed 1 shipped, seat 1 (the starved winner): 38 thinks,
longest run 20 thinks; a corrected rule would first come due at 28,160 instead of 29,940. That is not what keeps the row
over 36,000 (BUG-1153's slow raze is), but any gold-starved seat whose wood bank sits under a mix unit's wood cost
(45 for a Crossbowman, 150 for a catapult) never gets a stamp; a seat at 0 gold / 0 wood with wood workers is never starved.

## Notes
- Suggested rule: starved iff for every entry, (gold short and gold unobtainable) or (wood short and wood unobtainable),
  where unobtainable = no worker at that kind and no node of it known in reach. It needs both kinds' availability once per
  think (still no allocation); the cheap tests stay first.
- QA's `AMixedShortage_WithOneKindStillGatherable_IsNotStarved_BothEmptiedIs` already pins the case the fix must keep
  (40 g / 30 w, mines emptied, forest standing: a Crossbowman's gold is there, its wood is gatherable, so not starved).
- Related: BUG-1153 (the row this lever is for), the developer's finding that (i) ignores whether a unit can be trained at
  all (Pale Hills seed 2: 48 gold, enough for a Crossbowman, trains nothing; Raraku seed 5).

## Re-check fix round 1 (2026-11-08-1556, QA, fix 65524da)
Fixed, verified:
- The fail-first row, un-skipped, is green in the fresh-clone full run and alone (stamp 0, wave at 1,200).
- QA adds `QA/StarvedWaveQaTests.APickHeldAtItsExactGoldCost_IsNotStarved_OneUnderIs` (no mine, Barracks only, so the pick is
  Heavy Infantry at 54 g): gold held at 54 is never stamped and trains (5 buys); gold held at 53 is stamped at 0, trains
  nothing, and waves at 1,200.
- QA adds `Stress/StarvedPickOracleQaStressTests.AStampedThink_SendsNoTrain`, which checks every think of every seat on 6
  whole matches (Pale Hills 1 / 2, Raraku 5 Normal v Normal to 72,000; Raraku 1 and Pale Hills 3 Easy v Normal to 36,000;
  Pale Hills 2 four-seat FFA to 144,000): **0** Train commands on a think that ends stamped (the check and production agree
  on the pick). Raraku 5 seat 0 is stamped 1,669 of 2,618 thinks from 17,940; the match ends at 52,360.
- Note (not a bug): QA's round-2 metric "nothing buyable but not starved" (`StarvedWaveProbeQaStressTests`) still counts some
  thinks (Pale Hills 1 seat 1: 33, longest run 3, was 38 / 20). That metric tests `ArmyCount`, but the rule's (iii) tests
  `Free` units, so these are mid-command thinks, not the rule's gap.

## Developer notes, fix round 1 (2026-11-08-1556; applied by the Producer at ACCEPT)

## Fixed (M5-9a round 2, fix round 1, 2026-11-08-1556, the developer)
- `AiPlayer.Starved` now judges (i) on the unit production would train next (`ProductionManager.Pick`, split out of
  `TrainOne` unchanged: production waits for that one rather than buy a cheaper one), or, with no producer free for any
  entry, on every entry; and (ii) per unit: starved when that unit (with no pick, every entry) is short of some kind the
  seat cannot get (no own worker at it, no node of it known that it may gather). A gatherable short kind no longer saves
  a unit also short of an unobtainable one. Each kind's availability is asked at most once a check; allocation-free.
- Fail-first: QA's `QA/StarvedWaveQaTests.AGoldStarvedSeat_WithWoodUnderACost_ButNoGoldEver_IsStarved` un-skipped (failed
  on the old rule, green now); QA's `AMixedShortage_WithOneKindStillGatherable_IsNotStarved_BothEmptiedIs` still green.
  The developer's finding (pick ignored): `Ai/StarvedWaveTests.ASeat_WhosePickIsNeverPayable_IsStarved_ThoughACheaperUnitIsPayable`
  (fails on the old rule in the Barracks case, green now).
- Effect on whole matches: Pale Hills seed 2 (BUG-1068) ends at 53,797, the Pale Hills seed 2 FFA at 80,278, Raraku seed 5
  (BUG-1152) at 52,360; Pale Hills seed 1 (BUG-1153) moves from 71,660 to 68,664 and still misses 36,000.
- Docs: docs/02 "AI opponent" (the starved sentence), docs/03 "Implementation (M5-9a)" round 2 (the rule; "Fix round 1").
