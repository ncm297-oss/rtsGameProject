# BUG-0182: M4-2b nits: a positive speed below float range loads as a 0 m step; Sappers splash themselves in melee

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-0913, task M4-2b (sim track, QA full) |
| System | sim: data loader (`BuildProjectiles`), projectile store; data note for the data track |
| Fixed by | |

## Repro
1. **Speed underflow.** `QA/ProjectileLoaderQaTests.ASpeedThatIsNoFloatStep_IsRejected("1e-50")` (skipped for this
   item): set `arrow.speed` to `1e-50`. The loader accepts it (`c.Pos`: positive double) and stores
   `SpeedPerTick = (float)(1e-50 / 20) = 0`. Every arrow then gets `ticks = MaxFlightTicks` (72,000) and a zero
   velocity: it hovers at the shooter for an hour, then lands. With the shipped 1-shot-per-shooter store sizing, the
   store fills and further shots are lost. The other end is already guarded (`1e40` is "above the maximum 1000000").
2. **Sapper self-splash (data note, not a sim bug).** A Sapper (range 8, no min range, splash 2 m, friendly fire)
   fighting a melee unit at 0.5 m fires a sharper that explodes about 1.3 m from its own center, inside its own splash:
   per docs/03 "the shooter itself included" it takes friendly fire on every shot. In
   `Stress/RangedSplashFuzzQaTests` (shooter-heavy random brawls) 23-28 % of all deaths are friendly-fire kills
   from Sappers and Catapults together (e.g. seed 14: 267 of 984); the share from self-splash alone was not split out.

## Expected
1. A speed whose per-tick step is 0 (or below some sane floor) is a `DataError` at `projectiles[i].speed`, like the
   upper bound.
2. Item 2 is for the data track's balance pass (a Sapper `minRange`, or accept the suicide-bomber feel); no sim change
   asked.

## Actual
As above.

## Notes
Inputs in item 1 are far outside the documented range (docs/02: 12-25 m/s); no crash or hang.
