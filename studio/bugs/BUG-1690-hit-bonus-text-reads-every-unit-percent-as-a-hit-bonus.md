# BUG-1690: The unit description reading refuses every percentage that is not a hit bonus (a true Hamstring "slows by 20%" fails)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-24-1431, task D35 |
| System | content tests (`Content/UnitHitBonusText`, D-H2 description reading) |
| Fixed by | |

## Repro
1. Unskip `QA_TheWarDogsHamstringSlow_IsNotReadAsAPackBonus` in
   `sim/Rts.Sim.Tests/QA/Content/D35HitBonusTextQaTests.cs`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D35HitBonusTextQaTests"`

The test gives the shipped War-dog (which has an `onHit` Hamstring passive, Slowed magnitude 0.2, and no pack passive)
the description "Big shaggy dogs. Each bite slows its victim by 20% so the giants can catch it."

## Expected
The sentence is true to the data, so the reading passes it, or at least stays silent on a number it doesn't own.
`UnitHitBonusText` is meant to check the two M8-3 hit bonuses only.

## Actual
```
teblor_war_dog description passives[nearbyOwn].damageBonus: '20%' reads as the bonus for each other unit near, data none
```
Every percentage in a unit description is given a role. A sentence with "each" / "per" makes it the pack's per-unit
bonus; any other sentence fails as "reads as no hit bonus ... and a unit has no other percentage field". That claim is
false. A unit's passives carry status magnitudes (the Hamstring's Slowed 20%, a `whenBelowHp` Frenzy's attack speed). `EveryShippedUnitDescription_StatesItsHitBonuses` runs over every
shipped unit, so the first description to state one of those numbers turns the suite red with a misleading message.
No shipped description has a percentage today, so nothing fails now.

## Notes
A possible fix: only read a percentage when the unit has the matching field, or when the sentence has first-shot or pack
words ("other ... within"), and leave other percentages to the passive and status pins. The same applies to "within N m",
which an `onDeath` passive sentence ("Warriors within 6 m") would also use.
