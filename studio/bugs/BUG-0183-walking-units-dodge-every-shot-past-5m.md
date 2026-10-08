# BUG-0183: A walking unit dodges every aimed shot fired from more than about 5 m, whatever its speed or heading (docs/02: "slow units almost never do")

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-08-0913, task M4-2b (sim track, QA full) |
| System | sim: projectile hit rule (`ProjectileSystem.Strike`) with the shipped numbers (`common/projectiles.json` 25 m/s, 0.3 m tolerance; unit speeds 3-6.6 m/s); design rule docs/02 "Projectiles" |
| Fixed by | |

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
