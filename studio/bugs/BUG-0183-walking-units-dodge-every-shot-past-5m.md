# BUG-0183: A walking unit dodges every aimed shot fired from more than about 5 m, whatever its speed or heading (docs/02: "slow units almost never do")

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (rule pending the Producer's review, docs/01 item (g)) |
| Found | 2026-10-08-0913, task M4-2b (sim track, QA full) |
| System | sim: projectile hit rule (`ProjectileSystem.Strike`) with the shipped numbers (`common/projectiles.json` 25 m/s, 0.3 m tolerance; unit speeds 3-6.6 m/s); design rule docs/02 "Projectiles" |
| Fixed by | 0aeed2f (data-driven lead: `projectiles.json` `leadSpeed`, `ProjectileSystem.Lead` / `Track`, `ProjectileStore.Steer`); regressions `QA/ProjectileQaTests.HundredShotsAtAWalkingHeavyInfantry_AtEngagementRange_HitAtLeast95Percent(8, 12)` (un-skipped), `ProjectileTests.HundredShotsAtAWalkingHeavyInfantry_InEveryDirection_AtEngagementRange_HitAtLeast95Percent`, `QA/ProjectileLeadQaTests` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ProjectileTests.HitRateByDistance_Report" --logger "console;verbosity=detailed"`
   (the developer's report row).
2. `QA/ProjectileQaTests.HundredShotsAtAWalkingHeavyInfantry_AtEngagementRange_HitAtLeast95Percent` (skipped for this
   bug; remove the `Skip` to run): 100 bolts at a walking Heavy Infantry, one heading every 3.6 degrees, at 8 m and 12 m.

## Expected
docs/02 "Projectiles": "Fast units moving across the line of fire can dodge long shots; slow units almost never do."
Criterion 2 of the M4-2b brief: "100 shots at a walking Heavy Infantry hit >= 95 %", in the same criterion as "a
Crossbowman at 8 m". The Heavy Infantry is the slowest line unit (3 m/s).

## Actual
- QA row: **0 / 100 hits at 8 m, 0 / 100 at 12 m.**
- Developer's report: Heavy Infantry walking across `3 m 100% 4 m 100% 5 m 33% 6 m 0% 8 m 0% ... 15 m 0%`; walking
  *toward* the shooter `5 m 0%` onward; walking *away* `6 m 0%` onward; Horse Raider across `3 m 33% 4 m 16% 5 m 16% 6 m 0%`.
- The developer's gate row passes only because it shoots from 2-4.5 m
  (`ProjectileTests.HundredShotsAtAWalkingHeavyInfantry_InEveryDirection_Within4Point5m_HitAtLeast95Percent`). The
  Crossbowman's range is 15 m, the Archer's 14 m, the casters' 12 m.

The arithmetic: a bolt flies `ceil(d / 1.25)` ticks; a walker moves `speed / 20` m a tick; it is missed once it has
moved more than `radius + 0.3` m (0.7 m on foot). At 3 m/s that is any flight longer than 4 ticks, i.e. past 5 m, in
every direction, since the bolt flies to where the target *was*. The faster the unit, the shorter that range.

## Notes
- The implementation follows the documented rule exactly; the conflict is between the rule's numbers and the doc's
  own intent sentence (and the criterion's 95 % row). It needs a Producer / owner decision, for example: lead the
  target (aim where it will be at its current velocity, which keeps "fast units crossing can dodge" via the turn rate),
  scale the tolerance with flight time, or raise projectile speed / tolerance in data.
- Gameplay effect today: ranged units hit only standing or fighting targets past 5 m. A charging line or a walking
  column is never hit until it is within 5 m, so ranged units can't thin an approaching army. The counter-triangle
  rows still pass because the targets stop to fight.
- Not a code bug in `ProjectileSystem`; hit and miss logic, tolerance boundary (0.699 m hits / 0.701 m misses in 8
  directions) and the flight time are verified correct.

## Verification (QA re-check round 1, 2026-10-08-0913)
- Hit rates, reproduced (`ProjectileTests.HitRateByDistance_Report`):
  - Heavy Infantry walking across, toward or away: 100 % at 3, 4, 5, 6, 8, 10, 12, 15 and 16.3 m.
  - Horse Raider crossing: 33 / 16 / 16 % at 3 / 4 / 5 m, then 0 % from 6 m on.
  - Horse Raider toward the shooter: 66 % at 3 m, then 0 % from 4 m on.
- QA rows at 8 m and 12 m: 100 / 100 each.
- Attacks in `QA/ProjectileLeadQaTests`, all green except BUG-0184:
  - **Stop:** a walker ordered to Stop on each flight tick in turn, the landing tick included, is hit every time
    (8 m, 7 ticks; 16 m, 13 ticks).
  - **Reversal:** a walker that reverses every tick is hit (8 / 12 / 16 m, across, toward and away).
  - **Random turns:** a new random heading every 2 ticks, 60 / 60 hit at 10-16 m.
  - **Shove:** a shoved Idle target, moved 1.05 m during a 10-tick flight with Velocity 0, is hit.
  - **Death and slot reuse:** the target dies in flight and a new unit takes its slot. The bolt's impact point never
    moves again; it lands as a miss and the new unit takes 0 damage.
  - **Map ring:** walkers into the blocked map ring and its corners (Heavy Infantry; the 4.4 m/s Zealot, whose lead
    reaches into the ring) are hit 30 / 30, and every bolt stays on the map.
  - **Cliff:** walkers bending around a cliff block are hit 24 / 24.
  - **At the boundary:** a 5.0 m/s walker (data copy) is hit 100 / 100 at 12 m; a 5.2 m/s walker crossing is hit
    0 / 40. A cavalryman that pulls up in flight is re-led and hit; one that keeps galloping dodges (as docs/03
    describes).
- **Determinism:** the 6 seeds x 3,000 ticks shooter-heavy fuzz is green, with 1,078-1,849 re-leads per seed. Twins
  are hash-equal every tick and the replay round-trips.
- **Perf, each row alone in a fresh process:**
  - `Stress/ProjectileScaleQaTests.ThousandLedShots...`: 1,000 shots at 1,000 walkers, 76 % re-steered every tick.
    `Fly` costs 0.041-0.043 ms a tick, against 0.028 ms with no lead, at 0 B (budget 0.3 ms).
  - The developer's 1,000-in-flight row: 0.062 ms a tick, landing tick 2.15 ms.
  - Mixed 500 v 500: 1.87 / 1.35 ms.
- **Golden replay:** only the `data-hash` and `checksum` lines moved; the `k` lines are unchanged.
