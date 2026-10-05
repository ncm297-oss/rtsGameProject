# BUG-0039: 128 units to 64 neighbouring goals: the pack rule and the 22% give-up bound hold on one map only

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-05-1446, task M1-6 |
| System | local movement (crowded arrival, give-up, shoving) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SeedSweepQaTests" --logger "console;verbosity=detailed"`
   (`Report_MoreGoalsThanCacheSlots_OnNewSeedMaps1To40`, `Report_MoreGoalsThanCacheSlots_OnPreMixMaps1To20`).
2. The asserting twin `MoreGoalsThanCacheSlots_PackRuleAnd22PercentBound_HoldOnNewSeedMaps1To40` is skipped under this bug.

The scenario is the dev's `MovementSystemTests.MoreGoalsThanCacheSlots_BuildsAtMostTheCapPerTick_AtMost22PercentGiveUp`
(128 units, 2 owners, 64 neighbouring goal cells within cost 15 of the center) run on other maps.

## Expected
The end-state pack rule (no two Idle units closer than 0.5 x (r_i + r_j), M1-4d-1) and the test's
own bound (at most 22% give up) hold on any default map, not only the one map the test pins.

## Actual
- New-seed maps 1-40 (after BUG-0014's seed mixing): pack rule broken on **25/40** maps (worst:
  seed 3, 0.294 m apart vs 0.400 required; seed 36, 0.289 vs 0.400); give-up over 22% (more than 28
  of 128) on **22/40** maps, worst 47/128 (36.7%, seed 3). The dev reported 26/40 and 10/40; QA
  measures 25/40 and 22/40 with the dev test's own `<= Capacity * 22 / 100` bound.
- Pre-M1-6 maps (`TestSeeds.PreMix(1..20)`, i.e. old seeds 1-20): pack rule broken on 12/20, give-up
  over 22% on 5/20 (worst 54/128, old seed 8). So this predates M1-6: the seed mixing only moved the
  test off the one map (old seed 21) where it happens to pass.
- Termination holds everywhere (0 units still Moving on all 60 maps).
- Related, same cause family: the QA stress row `Crowd_ToOneOrFourClosePoints(2500, 4, 6000, 33)` on
  new-seed maps arrives 822/2500 (32.9%, seed 3404, the row's own seed unmixed), 946/2500 (37.8%,
  3405), 739/2500 (29.6%, 3406) against its 33% bound (QA scratch run). That bound is a single-map
  regression guard for BUG-0032.

## Notes
- `TestSeeds.PreMix` itself is sound (QA checked `MixSeed(PreMix(x)) == x` and the reverse on 10,000
  random values) and M1-6 doesn't touch movement, so PreMix hides no regression from this change. It
  does keep single-map calibrations alive: these bounds are guards for one layout, not properties of
  the movement system. Consider sweeping maps when the movement bounds are next re-measured.
- Likely the same mechanics as BUG-0028 / BUG-0032 (groups to nearby points give up en masse) and
  BUG-0038 (hard-wall fallback drops friendly clips, which would explain end-state overlaps).
