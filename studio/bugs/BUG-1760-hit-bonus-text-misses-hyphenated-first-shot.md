# BUG-1760: The description check no longer reads "first-shot" (hyphen), so a first-shot bonus a unit lacks passes

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (D37, verified by QA 2026-11-26-0207) |
| Found | 2026-11-25-1437, task D36 (QA) |
| System | content tests: `sim/Rts.Sim.Tests/Content/UnitHitBonusText.cs` (description vs `attack.fromStealthBonus`) |
| Fixed by | D37 (2026-11-26-0207): `FirstShotWords` allows `[\s\p{Pd}]+` between "first" and shot / hit / strike / attack and between "from" and hiding / stealth. QA's `D36Bug1720QaTests.QA_HyphenatedFirstShot_*` un-skipped and green; `Content/UnitHitBonusTextBug1760Tests`; QA re-check `QA/Content/D37Bug1760QaTests` (21 rows: en / em / figure dash, U+2010, non-breaking hyphen, capitals, plurals, from-hiding / From-Stealth fail "data none"; "first" and "shot" far apart do not count; 12 dash rows red on the base regex, all green on D37). |

## Repro
1. Un-skip `QA/Content/D36Bug1720QaTests.QA_HyphenatedFirstShot_OnAUnitWithoutTheField_Fails` (2 rows) and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~D36Bug1720QaTests"`.
2. Or by hand: `UnitHitBonusText.Problems(<teblor_warrior>, "Hidden, its first-shot bonus is +50%.")`.

## Expected
A sentence that states a first-shot bonus on a unit with no `attack.fromStealthBonus` is refused ("data none"), as it is
for "first shot" (two words). The ruling (docs/01 2026-11-25 ACCEPT (5)) names "first shot" as a first-shot word; the
hyphenated spelling is the same words and is the usual English adjective ("first-shot bonus", "first-shot damage").

## Actual
Both rows return no problem: the Warrior's description may claim "+50% first-shot damage" and the check passes. D36's
`FirstShotWords` is `\bfirst\s+(?:shot|hit|strike|attack)s?\b|\bfrom\s+(?:hiding|stealth)\b`: `\s+` does not match a hyphen,
and "first" is no longer enough on its own (it was a stealth word on the base 2336d58, so these sentences were refused
there: a regression of the check's strictness, though no shipped description uses the spelling today).

On a unit that has the field a wrong hyphenated number is still caught, but only as "the field unstated" and only when no
other sentence states the field (`QA_HyphenatedFirstShot_WrongNumber_OnTheForgedUnit_IsCaughtAsUnstated`).

## Notes
- Cure, for the data track: allow `[\s-]+` between "first" and the noun. Close neighbours worth a row at the same time:
  "opening shot", "ambush" (not in the ruling; the Producer's call).
- Related, S4, same class: on a unit with the field, a status percentage beside a stealth word that happens to equal the
  field's number ("Hidden enemies it strikes are Slowed 50% for 2 s" on a 50% unit) counts as stating the field, by D36's
  reading of "or when the unit has the field" (teblor.md D36 Question 2).
- On-death words are the ruling's three ("dies", "death", "dying"); "is slain", "died", "falls" are not, so a true
  on-death sentence written that way is refused (fails closed, not open). Note only.
