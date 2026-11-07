# BUG-1122: M5-4c slice 2 nits: the wounded boundary's float rounding; the fuzz oracle admits a unit Attack from any seat

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-07-0926, task M5-4c-2 |
| System | AI (MilitaryManager.Wounded), QA oracle |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HardKnobsQaTests.Wounded_AtExactlyTheThreshold_IsNotWounded_ForEveryUnitType"`
2. Read `Stress/CommandDoorFuzzStressTests.cs` `AssertAiCommandLegal`'s new `Attack && !TargetIsBuilding` branch.

## Expected
(a) docs/03 "Implementation (M5-4c slice 2)": "a unit is wounded when its hp is below ... `retreat.hpFraction` of its
max hp"; ai.json "a unit below it is wounded". At exactly the fraction a unit is not wounded, for every type.
(b) The AI-command oracle admits a unit `Attack` only where the slice sends one: a seat whose difficulty has
`focusFire`, on an enemy unit the seat sees.

## Actual
(a) `Wounded` computes `u.Hp < 0.3f * maxHp` in float. 0.3f is 0.30000001192...; for a 50-hp type the product rounds up
to 15.0000019, so a `whirlwind_desert_archer` at exactly 15 of 50 hp counts as wounded (the test: `whirlwind_desert_archer
(50 hp) at exactly 15 hp counts as wounded`). The other 7 whole-hp thresholds of the shipped types (40, 60, 70, 130, 150,
220, 240 hp) round down and hold; an hp tech can move a type onto a rounding-up max. One hp at one boundary: no play
effect worth more than a note, but the docs' "below" is not what the code does there.
(b) The branch accepts any live non-player-1 unit, from a seat on any difficulty, with no fog test, so a Normal seat
sending a unit Attack, or an Attack on a unit it can't see, would pass that oracle. The fog half is covered elsewhere
(`HardKnobsMatchTests`, `HardKnobsQaTests.PaleHills_FourSeats_TheFocusIsAlwaysSeen`); the difficulty half is not.

## Notes
- (a) Fix idea (the developer's call): keep the loaded fraction as the data's double and compare in double, or compare
  against the product rounded to a few decimals. Converting the stored 0.3f back to double keeps its error.
- (b) Fix idea: require `ai.Knobs.FocusFire` (or the seat's difficulty block) for a unit Attack, and the seat's fog grant.
