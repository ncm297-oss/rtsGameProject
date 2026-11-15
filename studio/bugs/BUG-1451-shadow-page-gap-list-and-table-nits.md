# BUG-1451: D-R38 shadow.md nits: G14 asks for a field that already exists, the Edur Ram difference row is hard to read, and the open questions are not on the page

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D-H9, verified by QA 2026-11-15-0506) |
| Found | 2026-11-14-2156, task D-R38 (QA) |
| System | docs (`docs/factions/shadow.md`) |
| Fixed by | D-H9 (session 2026-11-15-0506) |

## Repro
1. **G14 lists an existing field as wanted.** shadow.md "Schema gaps (D-R38)" G14, "Field wanted": "a per-faction
   `ai.json` `counters` override, or a Unique entry in the shared rule". The second option already exists:
   `DataLoader.Ai.CounterMasks` accepts any slot id from `DataLimits.SlotIds` in an answer list, `unique` included. So
   `"ranged": ["shock", "unique"]` in `common/rules.json` loads today. The real gap is only the per-faction part: the
   shared rule would also send every other faction's unique (Sapper, Zealot, War-dog) against Ranged.
2. **The Edur Ram's difference row.** "Against docs/02's template": the Stat cell has 8 labels
   (`Armor / Attack / CD / Range / Speed / Sight / Cost / Train`), but the Page and Template cells have 9 values each,
   because Cost is written as two numbers ("150 / 120"). Read by position, the values don't line up with the labels
   ("Cost" → 150, "Train" → 120). The numbers are right, but the cell reads wrong. Writing the cost as "150 G 120 W", or
   splitting the row, would fix it.
3. **Open questions only in the developer's report.** The brief's sixth bullet asks for "a list of open questions for
   the Producer ... each with your recommendation". The developer's report says Q1-Q11 exist, but neither shadow.md nor
   any studio file in the worktree has them. They live only in the returned report. Nothing in the repo carries them to
   the session that lands the roster (for example Q4, the "Warrior" substring collision, which QA confirmed: the
   proposed descriptions of `shadow_warrior_lodge`, `shadow_kennels` and the `standard` opening contain the Teblor unit
   name "Warrior", which `BuildingContentTests` F (`d.Contains(u.DisplayName)`) refuses).

## Expected
A gap row names only fields that don't exist. A difference row reads one value per label. The open questions are
recorded where the roster session will find them.

## Actual
As above.

## Notes
Also noted (no change asked for): the Shadow bonus name "Children of Shadow" and the Teblor bonus "Children of the
Mountains" share the faction word "Children" (`FactionWords`). That only matters to generic texts, so nothing breaks,
but the owner may want distinct bonus names.

## Fix (D-H9)
- Item 1: G14's "Field wanted" names only the per-faction `ai.json` `counters` override and says why the `unique` answer that
  loads today in the shared rule is not enough (it would send every faction's unique against Ranged and Casters).
- Item 2: the Edur Ram's difference row split in two (Cost on its own row, "150 / 120" against "200 / 150"), one value per
  label; no number changed.
- Item 3: shadow.md "Decisions (D-R38)": the Producer's answers (docs/01 2026-11-15 (a) item (3)), one line a question.
- Q4 (the "Warrior" collision): `UnitContentTests` E and `BuildingContentTests` F blank out the faction's own unit and
  building names before the foreign-name check (`Content/OwnNames`); rows `E_OwnNamesAreBlankedFirst_AForeignNameStillFails`,
  `F_OwnNamesAreBlankedFirst_AForeignUnitStillFails`.
