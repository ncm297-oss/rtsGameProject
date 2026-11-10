# BUG-1166: Regen rate nits: off-grid rates are rounded silently, and a status magnitude rounds differently from the same faction rate

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-09-2226, task M7-1 |
| System | data loader / regeneration (`DataLoader.RegenPerTick`, `RegenSystem.StatusRate`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~RegenQaTests.AStatusMagnitude_ConvertsAsTheSameFactionRate"`
   with the two `Skip`s removed: `DataLoader.RegenPerTick(0.175)` = 4 but `DataLoader.RegenPerTick(0.175f)` = 3 (1.525: 31
   vs 30).
2. A `faction.json` `regen.hpPerSecond` of `0.07` loads as 1 unit a tick (0.05 HP/s), `0.025` as 1 (0.05 HP/s, double what
   was written), `0.03` as 1. No error and no warning.

## Expected
- One written rate gives one rate a tick wherever it is written. docs/03 "Implementation (M7-1)" says each regenerating
  entry's magnitude is "converted the same way" as the faction's.
- A rate off the format's 0.05 HP/s step is refused the way `pop` refuses a value that isn't a multiple of 0.5
  ("pop {pop} is not a multiple of 0.5"), or at least the rounding is documented as silent.

## Actual
- The faction rate converts from the JSON double. A status magnitude is stored as a `float` (`StatusStore.Magnitude`) and
  converted every tick from that float. At the half-step midpoints (x.x25 / x.x75: 657 of the 100,000 values 0.001-100 in
  steps of 0.001) the float sits just under the midpoint and rounds down where the double rounds up. No value on the
  0.05 grid differs, and every designed rate (1, 1.5, 2.5, Darkness 2x) is on the grid.
- Off-grid rates load rounded half away from zero, with no error.

## Notes
Nothing ships a regen value yet, so nothing plays wrong today. Two ways to fix it: refuse rates that aren't a multiple of
0.05 (as `pop` does), or convert a status magnitude once at apply time from the loader's rounded value. Either removes
both nits. Related observation, documented and not a bug: a regenerating entry of d ticks heals on d - 1 of them (a
5 s x 2 HP/s status gives 9.9 HP, leaving 360 units carried), the same rule as a slow's.
