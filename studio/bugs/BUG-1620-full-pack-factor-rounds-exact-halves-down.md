# BUG-1620: A full pack's factor (1 + 3 x 0.1f) rounds exact halves down, against the hand calculation

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-18-0926, task M8-3 |
| System | combat (hit factors, `Combat/HitFactor.cs`, `Combat/DamageCalc.cs`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HitBonusQaTests.AFullPack_45x1point3"` after removing its
   `Skip` (test data: the Heavy Infantry's attack 45, a full pack of three holding mates, a light armor-0 dummy).
2. `HitBonusQaTests.StackedFactors_DisagreeWithAHandCalculation_OnlyOnAFullPacksExactHalves` (active) counts the cases.

## Expected
docs/02 "Damage formula": `damage = max(1, round(raw) - armor)`, round half up. 45 x 1.0 x 1.3 = 58.5 -> **59**. The QA
focus of M8-3 asked for "no off-by-one against a hand calculation" for stacked factors.

## Actual
**58.** The pack factor is `1f + 3 * 0.1f` = 1.29999995 in float, so every raw that is an exact half at x 1.3 rounds down.
A sweep of attack 1-300 x the table's multipliers x `bonusVs` {1, 1.25, 1.5, 2, ...} x status {0, 0.2} x stealth {0, 0.5}
x pack 0-3 against an exact decimal hand calculation: the new factors' disagreements are all at a full pack (182 pack-only,
98 pack x stealth, 27 pack x stealth x Frenzy); 1.1, 1.2 and 1.5 never disagree on their own. Smallest cases: 45 x 1.3,
15 siege x 3.0 (structure) x 1.3, 10 siege x 3.0 x 1.5 x 1.3 (all 58.5 -> 58).

The shipped pipeline already disagrees with exact arithmetic on some halves without M8-3 (e.g. 36 x 0.7 x 1.25 = 31.5 ->
31), and docs/03 "Implementation (M8-3)" Known limits (3) says a half "can round either way by the float's last bit". At a
full pack it is not either way: it is always down. The designed Hound (16 melee) does not land on a half against any class
in today's table, so no designed number is wrong today.

## Notes
Options for the Producer: (a) leave it and sharpen Known limits (3) to "a full pack (1.3) rounds an exact half down"; (b)
round the factor product to a few decimals before use, or compute `raw` in double, so hand numbers hold (that moves no
shipped hash: no shipped unit has a factor today, and status-only factors 1.2 did not disagree in the sweep). Same rounding
class as the existing `bonusVs` / multiplier halves.

Grouped here (by inspection, not reproduced): the pack query's slack (`radius + MaxUnitSpeed + 1 m` around phase-2 grid
positions) does not cover a unit pushed out of a newly placed footprint in phase 4 (`ConstructionSystem.SetDown` can move a
unit several metres after the grid rebuild), so such a mate is not counted for that one tick's hits. Same limit as the
splash query; docs/03 Known limits (2) says "none has been seen". One tick, rare, deterministic.
