# BUG-0762: `EveryUnitFightsTests`' Battering Ram row now passes on a collapse: "500 damage after 1 tick", no hit landed

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-16-0847, task M5-H2 (QA) |
| System | sim-track test `sim/Rts.Sim.Tests/EveryUnitFightsTests.cs` vs the collapse rule (BUG-0703, standing rule 24) |
| Fixed by | ac80818 (`TestSim.KeepIn(sim, 1)` after the attacker + an `IsAlive(tent)` guard in `EveryUnitFightsTests`) |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~EveryUnit_DealsDamage1v1_ToAHeldLaborerOrTent" --logger "console;verbosity=detailed"`
   on 47ca4a2 and on its base 8c39c08; compare the `whirlwind_battering_ram` line.

## Expected
Every shipped unit deals damage 1v1: the buildings-only Ram hits a lone Tent and its hit points drop.

## Actual
```
base 8c39c08: whirlwind_battering_ram: 179 damage after 7 ticks at a 0.25 m gap (melee)
head 47ca4a2: whirlwind_battering_ram: 500 damage after 1 ticks at a 0.25 m gap (melee)
```
The scene spawns player 1's Tent straight into the store and gives player 1 no unit (a Tent trains nothing), so since
M5-H2 player 1 is out on tick 0 and the Tent collapses. A freed slot's `Hp` reads 0, so `RunUntil(Hp < full)` returns after
one tick and the row records the Tent's whole 500 hit points as the Ram's damage. The row is green whatever the Ram does.
QA found it by instrumenting `World.Collapse` in a scratch clone and running the whole non-Perf suite: of the tests that
trigger a collapse on tick 0-3, this row and the data track's `BuildingKillTimeReportTests` (BUG-0761) are the two where the
collapse silently replaces what the test measures. The other early collapses are in defeat / win-rule tests that stage them
on purpose.

## Notes
- Fix (sim track, rule 24): `TestSim.KeepIn(sim, 1)` (or a held Laborer of player 1) in the buildings branch, after the
  attacker is placed so the attacker's slot doesn't move; and/or measure `IsAlive(tent) ? full - Hp : full` only when a
  `DeathEvent` that isn't `Collapsed` freed it.
- Minor, same class, no fix needed: `QA/ViewApi/GhostQaTests.Ghosts_EqualTheSimsUnseenEntries_*` stages player 1 with Tents
  and no unit, so its first Tent collapses on tick 0 / 25 (seeds 1 / 6) and player 1 is defeated for the rest of the run.
  Its later store-seam Tents stand (the collapse happens on the defeat tick only), so the fuzz keeps its coverage and its
  preconditions; a keeper would restore the scene as written.

## Verification (QA re-check, 2026-10-16-0847, fix round 1)
- ac80818 prints `whirlwind_battering_ram: 179 damage after 7 ticks at a 0.25 m gap (melee)`, the base 8c39c08 line exactly;
  every other row green. The guard makes a collapsed / freed Tent fail the row instead of passing as damage.
- `GhostQaTests`' early Tent collapse (the "no fix needed" note) left as is.
