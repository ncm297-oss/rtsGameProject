# BUG-1024: After fix round 1, M5-4g criterion 3 is still not met: 1 of 43 live sides has no Age II, and BUG-0981 flags are 9 on the report seeds (target 0)

| Field | Value |
| --- | --- |
| Severity | S2 (an acceptance criterion not met; the Producer may waive it) |
| Status | open (re-checked after fix round 2, c76bc5f: 4 / 49 live sides without Age II, flags 22 / 58, `DH6ThresholdQaTests` seed 25 still red) |
| Found | 2026-10-18-1814, task M5-4g re-check after fix round 1 (57dfa22) |
| System | AI reserve gate (`AiPlayer.MayReserve`, `ai.reserveArmyShare`) together with the gather shift (`AiPlayer.WantedGoldShare`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ReservePairingsStressTests.Pairing" --logger "console;verbosity=detailed"`.
   The row `Pairing_Twins_AgeTwoForEverySideAlive(1, 1, 3)` is red: "seed 19: side 0 alive at 20:00 without Age II".
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds"`
   is red: "seed 25: crossbow_push no wave and not defeated". It was green on the base and in M5-4g round 0.

## Expected
Brief criterion 3 (the 27 runs: the pairs report's smallest seeds and D21's second and third seeds):
- every side alive at 20:00 has reached Age II;
- the "BUG-0981 pattern" flag count goes from 11 to 0 on live sides.

Criterion 7: everything outside the listed data-owned rows is green.

## Actual
These are my own measurements from the 27 twin runs (Normal v Normal, menu map, 20 minutes). They match the developer's report.

| | base f531dd9 | M5-4g | fix round 1 |
| --- | --- | --- | --- |
| live sides at 20:00 without Age II | 23 / 41 | 0 / 43 | **1 / 43** |
| BUG-0981 flags, report seeds (#1) / all 27 runs | 11 / - | 4 / 10 | **9 / 17** |

- **The Age II miss: `crossbow_push` v `raider_rush` seed 19 (D21's third seed), Malazan.** The side ends with a peak army
  of 7 pop, no wave, and a bank of 910 gold / 15 wood at 20:00. Its flags are `ranged_weapons_1` (due 3:16), `age_ii` (due
  7:00), the expand and the Wickan Corral. `reserveThinks` is **0 / 1200**. The army never reaches the wave's size, so the gate
  never opens. Because the gather shift is driven by the reserve's "short" flags, it never runs either: the gatherers stay
  at `goldShare` 0.6 while the bank is one-sided for 13+ minutes. This is BUG-0981's original pattern, unchanged for every
  side under the wave size. BUG-1010 seeds 18 (58 gold / 1,365 wood) and 25 (46 / 95, 1 pop left) show it too: alive, no
  wave, no Age II, `reserveThinks` 0.
- **The flags.** On the report seeds 9 sides are flagged; 5 of them have a cause other than the report's order:
  - `standard` v `holy_war` seed 3, Malazan: the expand was reserved from 12:17 to 20:00 with 369 / 215. It was short of
    wood and defending for 435 of those thinks.
  - `crossbow_push` v `standard` seed 9, both sides: the expand with a free mine known.
  - `cadre_rush` v `standard` seed 13, Whirlwind: `whirlwind_ram_yard` 150 / 200 and `dryjhnas_prophecy` 200 / 150 with
    597 / 43 (wood-starved).

  The other 4 are expands with no free mine known (report-order artifacts, BUG-1023 item 1). Over all 27 runs: 17 flags,
  12 of them expands.
- **The newly red data-owned row (criterion 7).** `QA/Content/DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds`:
  on seed 25, `crossbow_push` was defeated at 11:26 on the base. Now it has no wave, is not defeated, and has 1 pop at
  20:00. The row is data-owned but not on criterion 7's list. Handle it like `KnownBeatenAlive` at the data re-print
  (an allowance for seed 25), or keep it red as evidence for this bug.

## Notes
- The developer reports all of this openly ("Age II misses 1 / 43", "flags 9 / 17"). The Producer decides whether the
  one Age II miss (a razed side under the wave size) is acceptable and whether the flag target is restated.
- A lever to measure, not a prescription: drive the gather shift from the first due step's shortfall even while the gate
  is closed (the reserve gated, the shift not). The developer says this variant was tried ("the gather shift kept with
  the gate closed") and lists it among the variants that did not bring the no-wave count back to 3. Ask for its numbers
  before choosing.

## Re-check after fix round 2 (c76bc5f)
My own measurements from the 27 twin runs. They match the developer's table in docs/03 exactly.

| | base f531dd9 | M5-4g | fix round 1 | fix round 2 |
| --- | --- | --- | --- | --- |
| live sides at 20:00 without Age II | 23 / 41 | 0 / 43 | 1 / 43 | **4 / 49** |
| BUG-0981 flags, report seeds (#1) / all 27 runs | 11 / - | 4 / 10 | 9 / 17 | **22 / 58** (31 flagged sides, 12 on the report seeds) |

- The 4 sides without Age II. **None of them reached Age II on the base either.** Each was without it at 20:00 or
  defeated without it:
  - `crossbow_push` v `raider_rush` seed 19, Malazan. Unchanged from round 1: `reserveThinks` 0 / 1200 and 910 / 15 at 20:00.
  - `crossbow_push` v `standard` seed 32, Malazan. Defeated at 15:30 on the base and at 12:35 in round 1. Now it is alive
    with 794 / 5: wood-starved, the reserve named on 3 thinks only, and the gather shift idle with the gate shut.
  - Whirlwind `standard`, seeds 9 and 13. Both were defeated without Age II on the base (19:16, 18:24). Now both are alive
    with 9-11 gold and no gold node known (`expand` flagged with `freeMine=False ungatedGold=False`). Age II was reserved
    for 43 s and 41 s (8:16-8:59 and 8:54-9:35, short wood). Then the gate shut and production spent the gold.
- The flags. 29 of 58 are `expand` steps: 21 with no free mine known, 15 of them with no gold node at all. The rest are 17
  buildings (Cadre Tower 5, Engineers' Yard 5, Wickan Corral 2, Ram Yard 2, others 1 each) and 12 researches
  (`moranth_supply` 5, `age_ii` 4, `dryjhnas_prophecy` 2, `ranged_weapons_1` 1). The count rises mostly because 49 sides
  survive (43 before) and the reserve is held on 12.6 % of thinks (21.8 % in round 1).
- `DH6ThresholdQaTests.CrossbowPushVersusRaiderRush_FourthAndFifthSeeds` is still red: "seed 25: crossbow_push no wave and not
  defeated".
- I don't file a new bug for the rise from 1 to 4 Age II misses. Against the base it is no side worse off (23 / 41 -> 4 / 49).
  Against rounds 0 and 1 it is the measured price of BUG-1025's fix (the developer's variants table: no gate form keeps
  both). It is still this bug, criterion 3 not met. **The Producer decides whether to waive it or restate the target.**
