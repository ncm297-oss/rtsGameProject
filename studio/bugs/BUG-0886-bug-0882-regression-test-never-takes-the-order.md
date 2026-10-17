# BUG-0886: BUG-0882's regression test never takes the Attack, so it passes on the unfixed code

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-17-0554, task M5-P2 (QA re-check after fix round 1) |
| System | tests: `Combat/OrderedChaseReachTests.AnOrderedAttackOnAHallRingedByTrees_IsGivenUp_NotPacedForEver` |
| Fixed by | |

## Repro
1. Check out 3ec22ee (before fix round 1, BUG-0882 still open), copy in the test from cbeb944, and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AnOrderedAttackOnAHallRingedByTrees"`: it **passes**.
2. The same scene with a probe (QA's scratch run, cbeb944): over 1,500 ticks no unit ever holds the hall as its target,
   the summed give-ups are 0, and unit 0 ends Idle at its spawn point (60, 50).

## Expected
CLAUDE.md "Testing rules": every bug fix gets a regression test that failed first. The test should stage an ordered
Attack that is taken (the chasers walk to the tree ring) and then given up.

## Actual
The ten Heavy Infantry stand at (60-64, 50-51) m. The hall's footprint is at (80-88, 60-68) m, about 18 m off, past a
Heavy Infantry's 14 m sight. There is no spotter, and the scene has no `TestSim.Explored`. So the Attack on an unseen
building is dropped when it is issued. The asserts (hall hp unchanged; no unit holds the hall or is Moving) hold
trivially.

## Notes
- The fix itself works. QA's `QA/FlankFixRound1QaTests.HallRingedByTrees_MeleeGivesUp_RangedAndDoorKeepOn` stages the
  same ring with a spotter (Ordered 10 HI, ring 1 deep: 3ec22ee keeps 4 of 10 on the hall at tick 2,000, cbeb944 gives
  all 10 up; Ordered 20 HI, ring 2 deep: 10 of 20 kept -> 0). The AI rows (`AiMilitaryQaTests.AWaveWhoseMembers...(10)`,
  `SimHardeningM5H1QaTests.FencedHall_*(10)`) are the dev's other evidence, and they are real.
- Related docs nit (same fix round, grouped here): docs/03 line 1313 (M4-H3 "Giving up a chase") says "Building targets
  are unchanged (the stand-off and the surround spots are in reach)". But the code applies `StandsInReachAtGoal` to
  ordered building chases too, and that is what fixes BUG-0882. The round's own "BUG-0882" paragraph says so: "a
  building whose every open cell lies out of reach ... ends an ordered or spot-walking chase". One of the two sentences
  needs rewording.
- Fix direction (developer's call): add `CombatScenes.Spotter` (or `Spot`-equivalent sight) so the order is taken, and
  assert that it was taken (some unit held the hall and walked) before asserting that it was given up.
