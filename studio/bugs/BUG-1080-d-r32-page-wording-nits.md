# BUG-1080: D-R32 page wording nits (seed 32 "still standing", an unmeasured cause, criterion 5's bug files)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-26-2108, task D-R32 (QA, light) |
| System | docs/factions/malazan.md, docs/factions/whirlwind.md; studio/bugs (BUG-1010, BUG-1011, BUG-1027) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D21OpeningsQaTests.EveryPairing_SecondSeed" --logger "console;verbosity=detailed"`
2. Read the `seed 32 (#2 of the pairing)` line and malazan.md "Age II against the Whirlwind" intro.

## Expected
The page says what the run shows.

## Actual
1. malazan.md "Age II against the Whirlwind": "`crossbow_push` seed 32 is still standing with 794 gold and 5 wood".
   The run prints `malazan crossbow_push ... defeated never ... at 20:00 pop 3 / cap 0, gold 794, wood 5`: every
   building that gives pop is gone, so 3 pop of units remain. "Not defeated" is true; "still standing" reads as a base
   that holds. Say "not defeated at 20:00 (3 pop, no pop building left)".
2. whirlwind.md, standard opening's "Houses at the cap": "wood runs short on this seed (0-50 in hand from 13:00 while
   the gold piles up)". No row prints the wood bank over time on that run; the dev's report lists it as unproven. Either
   quote a measurement or drop the clause. (The cap numbers themselves check out: 92 = 2 Holy Camps x 10 + 9 finished
   Tents x 8; 84 = 20 + 8 x 8. On malazan.md "the second Keep now stands" also checks out: 100 = 2 Keeps x 10 + 10
   finished Billets x 8.)
3. Criterion 5's "bug files updated" is not done: the game-dev may not edit `studio/**`. BUG-1011 (all five nits fixed
   in this commit, verified), BUG-1010 (the page line now quotes the 5-seed counts and names seed 25) and BUG-1027 (the
   five data-owned rows are green; the four `Stress/ReservePairingsStressTests` rows stay red, sim-owned) need their
   status lines updated by the Producer.

## Notes
Text only. No number on a pinned cell is wrong.
