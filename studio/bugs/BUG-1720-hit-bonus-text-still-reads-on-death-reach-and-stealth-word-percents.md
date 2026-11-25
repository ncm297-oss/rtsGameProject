# BUG-1720: UnitHitBonusText still refuses two kinds of true sentence after BUG-1690's fix

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-25-0924, task D-H10 (QA) |
| System | content tests (`Content/UnitHitBonusText`, the D-H2 / D35 description reading) |
| Fixed by | |

## Repro
1. Remove the `Skip` from `QA_AnOnDeathSentenceWithOther_IsNotReadAsThePack` and
   `QA_AStealthWordStatusPercent_IsNotReadAsTheFirstShot` in `sim/Rts.Sim.Tests/QA/Content/DH10TextPinQaTests.cs`.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DH10TextPinQaTests"`

## Expected
Ruling docs/01 2026-11-24 ACCEPT (3)(b): a percentage is a hit bonus only when the unit has that field or the sentence
has first-shot or pack words. The QA focus for D-H10 asks that a "within 6 m" in an on-death sentence and a status
percentage are left to the passive and status pins. A true description is not refused.

## Actual
(a) An on-death sentence written with "other" is read as the pack's reach, on a unit that has no pack (the shipped Zealot):
```
"When a Zealot dies, other Zealots within 6 m gain Frenzied (+50% attack speed) for 5 s."
whirlwind_zealot description passives[nearbyOwn].radius: 'within 6 m' reads as the pack's reach, data none
"Its death drives the other Zealots within 6 m wild."   (the shipped description plus its reach)
whirlwind_zealot description passives[nearbyOwn].radius: 'within 6 m' reads as the pack's reach, data none
```
`PackWords` matches "other ... within" anywhere in the sentence, so a Martyrdom sentence counts as pack words. (The
brief quotes "other ... within" as the pack words, so this half is the ruling's own wording meeting the on-death shape;
the same sentence without "other" passes.)

(b) A status percentage in a sentence that names stealth but no first shot is read as `attack.fromStealthBonus`, though
the ruling's words are "first-shot words" and the unit may lack the field:
```
teblor_warrior (no fromStealthBonus): "Hidden enemies it strikes are Slowed 20% for 2 s."
teblor_warrior description attack.fromStealthBonus: '20%' reads as the first shot from stealth, data none
javelin thrower forged with fromStealthBonus 0.5: "Its first shot from hiding deals +50% damage. While stealthed it moves 20% slower."
teblor_javelin_thrower description attack.fromStealthBonus: '20%' reads as the first shot from stealth, data +50%
```
`StealthWords` (`first|stealth|stealthed|hiding|hidden|hides?`) gives the stealth role ahead of any field check.

No shipped description has either shape today, so nothing is red; the next roster's text (the Shadow's) could be.

## Notes
- Possible cure: the stealth role only with first-shot words ("first", "opening shot") or on a unit with
  `fromStealthBonus`; the pack's reach only in a sentence that also has a pack percentage ("adds", "% damage for each
  other"), or not in a sentence with an on-death word ("dies", "death").
- The developer's pinned limit (a unit with the pack reads an "each" sentence's percentage as the per-mate bonus,
  `HitBonusPinTests` row "Each bite also adds 5%") is the same family; it is pinned as a failing row, so a later cure
  turns that row red on purpose.
