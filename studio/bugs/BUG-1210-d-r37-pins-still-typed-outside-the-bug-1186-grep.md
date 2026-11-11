# BUG-1210: D-R37 leftovers: two page pins still typed to the two shipped factions outside BUG-1186's grep, one bare key lookup

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-10-2116, task D-R37 |
| System | Tests: `sim/Rts.Sim.Tests/QA/Content/**` |
| Fixed by | |

## Repro
1. `git grep -n -E 'InlineData\("(malazan|whirlwind)", "' -- sim/Rts.Sim.Tests/QA/Content` on D-R37's HEAD (`c49e971`),
   ignoring the mutation-probe rows (D26a / DR35 page edits).
2. Read `QA/Content/UnitRosterQaTests.cs` line 107.

## Expected
BUG-1186's intent: when D28 adds `teblor`, every page pin covers it or fails naming the missing row.

## Actual
D-R37 met BUG-1186's literal grep, but these pins (outside the grep, since their rows carry a second argument) still run
only for the two shipped factions, so once D28 lands they skip the Teblor rather than failing:

- `D22PresetTextQaTests.PageHighLine_MatchesTheShippedPresets`: `[InlineData("malazan", "cadre_rush")]`,
  `[InlineData("whirlwind", "holy_war")]` (lines 68-69). D-R37's new teblor.md High-start bullet is checked for wording
  only (`TeblorPageParseQaTests.HighStartBullet_PassesTheD22Pin`), not for the opening id against `ai.json` once it ships.
  The developer disclosed this one.
- `LineMountedBonusParityQaTests.TheFactionPagesLineUnitNote_StatesTheShippedBonusVsMounted`:
  `[InlineData("malazan", "malazan_heavy_infantry")]`, `[InlineData("whirlwind", "whirlwind_raider")]` (lines 40-41). The
  Warrior's page note, moved to ×1.5 in this task, won't be compared with `teblor_warrior`'s `bonusVs.mounted` after D28.
  The data side is covered: `EveryShippedLineUnit_CarriesDocs02sTemplateBonusVsMounted` loops over every Line unit.
- `UnitRosterQaTests` line 107: `UniqueHall[u.Key]` throws a bare `KeyNotFoundException` for `teblor_war_dog`, where it
  should fail by name through `ContentFactions.Typed`. Disclosed.

## Notes
Fold into D28: derive the rows from `ContentFactions.Keys` (the opening id from the page's High bullet; the Line unit from
`UnitSlot.Line` per faction) and key `UniqueHall` through `Typed`. Nothing fails today.
