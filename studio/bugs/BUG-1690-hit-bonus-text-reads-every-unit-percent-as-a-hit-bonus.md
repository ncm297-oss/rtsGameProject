# BUG-1690: The unit description reading refuses every percentage that is not a hit bonus (a true Hamstring "slows by 20%" fails)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D-H10, 2026-11-25; by the ruling, docs/01 2026-11-24 ACCEPT (3)(b)) |
| Found | 2026-11-24-1431, task D35 |
| System | content tests (`Content/UnitHitBonusText`, D-H2 description reading) |
| Fixed by | D-H10: `Content/UnitHitBonusText` |

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

## Fix (D-H10, 2026-11-25)
- `Content/UnitHitBonusText` reads a percentage as a hit bonus only when the unit has the matching field (a `nearbyOwn`
  passive for the pack's per-mate and cap roles) or the sentence has first-shot words (the stealth role) or pack words
  ("other ... within", "each other" / "per other" / "for every other"). Any other percentage is not read (the passive and
  status pins own it). A "within N m" is the pack's reach only in a sentence with pack words, or in an "each" / "per"
  sentence of a unit that has the pack, so an on-death "Warriors within 6 m" is not read.
- `QA_TheWarDogsHamstringSlow_IsNotReadAsAPackBonus` un-skipped and green; `HitBonusPinTests.APercentageThatIsNoHitBonus_IsNotRead`
  (six sentences: a Hamstring slow, a Frenzy cap, an on-death reach, a Breaker's slow, a stealth unit's slow) and new
  `AWrongNumber_FailsNamingTheUnitAndTheField` rows (a pack sentence on a unit without the pack still fails at the per-mate,
  cap and reach fields). Two rows of the old table pinned the pre-ruling reading ("Stronger in a crowd, up to +30%" as a
  cap, "Fights best within 8 m of a Shaman" as a reach on a unit with neither field) and moved to the not-read rows; the
  Archer's "Hurls javelins, 50% harder." now fails as its first-shot bonus not stated (was "reads as no hit bonus").
- Limit (the ruling's words): a unit that has the pack still reads an "each" sentence's percentage as the per-mate bonus
  ("Each bite also adds 5%" on a Hound fails at `damageBonus`), pinned in `AWrongNumber_FailsNamingTheUnitAndTheField`.

## QA verification (D-H10, 2026-11-25-0924)
The two QA rows un-skipped and green; true Hamstring / Frenzy / on-death ("Zealots within 6 m") / "per cent" sentences
are left alone and wrong pack, cap, reach and first-shot numbers still fail (`QA/Content/DH10TextPinQaTests`). The three
changed `HitBonusPinTests` rows follow the ruling (two moved to not-read; the Archer row still fails, now at the field).
Two residual shapes still refuse a true sentence (an on-death "other ... within", a status % beside "hidden" /
"stealthed"): BUG-1720.
