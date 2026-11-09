# BUG-1154: The starved rule is cleared by any gatherable short kind, even when nothing it could ever gather makes a unit buyable

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-08-1556, task M5-9a (round 2, QA full) |
| System | AI (the starved wave: `AiPlayer.Starved`, clause (ii)) |
| Fixed by | |

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
