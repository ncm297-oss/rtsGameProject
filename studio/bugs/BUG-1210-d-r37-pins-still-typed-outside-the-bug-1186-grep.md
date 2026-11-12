# BUG-1210: D-R37 leftovers: two page pins still typed to the two shipped factions outside BUG-1186's grep, one bare key lookup

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D28, 7093dec; on `main` since D28-L landed it, de29e6f, session 2026-11-11-2256; verified by QA) |
| Found | 2026-11-10-2116, task D-R37 |
| System | Tests: `sim/Rts.Sim.Tests/QA/Content/**` |
| Fixed by | D28 (2026-11-11-1317, data track): `QA/Content/D22PresetTextQaTests.PageHighLine_MatchesTheShippedPresets` (rows from `ContentFactions.Theory`, the opening read from the line) and `PageHighLine_AnOpeningTheFileLacks_IsCaught`; `QA/Content/LineMountedBonusParityQaTests.TheFactionPagesLineUnitNote_StatesTheShippedBonusVsMounted` (rows from `ContentFactions.Theory`, the Line unit by `UnitSlot.Line`); `QA/Content/UnitRosterQaTests.EveryUnit_TrainsAtItsOwnFactionsHallForItsSlot` (`UniqueHall` through `ContentFactions.Typed`, plus a `teblor_war_dog` row) |

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

## Fix (D28, 2026-11-11)
1. `D22PresetTextQaTests.PageHighLine_MatchesTheShippedPresets(faction)` runs one row per data faction. The opening is the first
   `` `id` `` after "Standard has no threshold" in the page's High bullet (`HighLineOpening`): malazan `cadre_rush`, whirlwind
   `holy_war`, teblor `standard`; it must be one of the faction's shipped openings (`HighLineOpeningProblem`, which names the
   page and the file). New row `PageHighLine_AnOpeningTheFileLacks_IsCaught` (malazan `cadre_push`, teblor `war_band`: red).
2. `LineMountedBonusParityQaTests.TheFactionPagesLineUnitNote_StatesTheShippedBonusVsMounted(faction)` runs one row per data
   faction, the Line unit found by slot (`teblor_warrior`'s note "×1.5 vs Mounted" against `bonusVs.mounted` 1.5); the data
   side's guard is now "one Line unit per shipped faction", not "at least 2".
3. `UnitRosterQaTests.EveryUnit_TrainsAtItsOwnFactionsHallForItsSlot` reads `UniqueHall` through `ContentFactions.Typed` and
   has the `teblor_war_dog` row (Shock Hall: the Stables & Kennels).
