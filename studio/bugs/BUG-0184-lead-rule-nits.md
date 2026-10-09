# BUG-0184: Lead rule nits: a step of exactly the lead speed is not always led (float rounding); a re-led bolt can step 1.8 m in a tick

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 fixed) |
| Found | 2026-10-08-0913, task M4-2b QA re-check round 1 (sim track, QA full) |
| System | sim: `ProjectileSystem.Lead` / `Track` (BUG-0183 lead rule); a note for the view track |
| Fixed by | Item 1: M4-3a 977db1d (`ProjectileSystem.WithinLead`, 1e-4 relative slack; `Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading` un-skipped and green). Item 2 (the re-led bolt's 1.8 m step) is a view note: M4-V3 interpolates and never extrapolates, so it is handled on the drawing side; stays open as a sim nit |

## Repro
1. **The boundary.** Run `QA/ProjectileLeadQaTests.Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading`, which is
   skipped for this bug; remove the `Skip` to run it.
   - It builds a step of exactly `bolt.LeadSpeedPerTick` (0.25 m a tick) as `lead * (cos, sin)` in each of 360 headings.
   - It then calls `ProjectileSystem.Lead`. In 13 headings the result is not led (4, 12, 38, 53, 86, 132, 184, 185, 201,
     248, 315, 338, ...).
   - In play, `ProjectileLeadQaTests.Report_RealStepsOfAFiveMpsWalker_AgainstTheLeadTest` uses a Heavy Infantry set to
     5.0 m/s (data copy). It fails the `step^2 <= lead^2` test on 163 of 2,280 real movement steps (7 %).
2. **Steering speed.** `Stress/RangedSplashFuzzQaTests`, which reports the longest step a re-led shot takes: 1.45-1.80 m
   in a tick across the 6 seeds. A bolt's nominal step is 1.25 m (25 m/s), so a re-led bolt can move up to 44 % faster
   for a tick. That is 36 m/s, plus a sideways jink when its walker turns at a cell corner late in the flight.

## Expected
1. docs/03 "Leading" says a unit "whose step this tick is no longer than the projectile's `leadSpeed`" is led, and
   docs/02 says "no faster than". A unit walking at exactly the lead speed should be led in every heading.
2. docs/03 says "the bolt bends a little in flight for a turning walker". That is true. The size, up to about 0.55 m
   over the nominal step in one tick, is not stated anywhere the view track will read.

## Actual
1. The comparison runs on the float step vector, whose length is 0.25 +- 1 ulp. About 7 % of exactly-5 m/s steps are not
   led on that tick. A shot is still re-led on the walker's other ticks, so the 5.0 m/s walker is hit 100 / 100 at 12 m
   (`AWalkerAtExactlyTheLeadSpeed_IsLed_JustOverItIsNot`).
2. As measured above.

## Notes
- **Impact today: none.** No shipped unit is near the boundary. The foot units walk 2.2-4.4 m/s and the cavalry
  6.2-6.6 m/s, against a lead speed of 5. Item 1 only matters if the data track ever ships a unit at exactly the lead
  speed.
- **Possible fix for item 1** (developer's call): compare against `lead * lead` with a small slack (for example
  `lead * 1.0001f`). Or decide on the unit's data speed (`SpeedPerTick <= LeadSpeedPerTick`) together with "the unit
  stepped this tick".
- **Item 2** is a note for M4-V3 (projectile views). It is not a sim change: interpolating `PrevPosition` to `Position`
  already draws whatever the sim does.

## QA note (2026-10-08-1435, M4-3a)
Item 1 (the boundary) is fixed in 977db1d: `ProjectileSystem.WithinLead` tests the step against the lead speed with a
1e-4 relative slack, and `QA/ProjectileLeadQaTests.Lead_AStepOfExactlyTheLeadSpeed_IsLedInEveryHeading` is un-skipped
and passes (QA ran it). Item 2 (a re-led bolt's step, the view's interpolation note) is unchanged; the bug stays open for it.
