# BUG-1935: A seat with no drop-off (and none it can pay for) that knows a node waits for its pick for good

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-27-1857, task M6-S15 (QA, focus 3: "a seat that sits", a shape C does not name) |
| System | AI production (`ProductionManager.SpendInstead`, `AiPlayer.PickNeverPayable` / `CanEverPay`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M6S15QaTests.C_NoDropOffAndNoneItCanPay_AKnownMine"`
   (`QA/M6S15QaTests.cs`; the red row is skipped with this id, the `_Measured` row pins what happens today).
2. Scene: `SpendWhatItHasTests.Scene(mix, 40, 74, keepGold: true, noHall: true)`: a Malazan Normal AI seat with no Keep and
   no drop-off, 40 gold / 74 wood (one wood under the Depot, so A2's small drop-off is not bought), a gold mine it knows, a
   finished Barracks and Crossbow Range, a live Crossbowman; its pick is the Heavy Infantry (54 / 20).

## Expected
docs/02 "AI opponent" (M6-S15): "when the unit it would train next can never be paid for (... short of a resource no income
can bring ...) it trains instead the most wanted unit of its army mix it can pay for now". This seat has no income at all:
no drop-off, none being built, and a bank that pays for none. The Crossbowman (36 / 45) is payable now and its Range is free,
so it should be trained.

## Actual
6,000 ticks: 0 Trains, 0 Builds, 1,282 Gathers (its Laborers re-sent to the mine every think, nowhere to deliver); bank
40 / 74 throughout; `PickNeverPayable()` false at every think (the known mine counts as income); `World.CanStillTrain(0)`
true all along (the bank pays the Crossbowman), so the draw clock never runs for this seat: it holds a match open for as
long as it stands.

## Notes
- C's own `ReserveFillable` already reads "income" as a known node **and** somewhere to deliver (a finished drop-off or a
  drop-off site); `PickNeverPayable` uses `Starved`'s (ii) (`CanEverPay`: a known node is enough), as the brief worded it
  ("part (ii) as `Starved` reads it"). The same test of income in both would cover this seat. The brief's guard ("a seat
  that knows a node it may gather of the short kind waits as today") was written for a seat that can deliver.
- Not caused by M6-S15 (the base waited the same); a shape the oracle of QA focus 3 asked for.
