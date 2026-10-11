# BUG-0581: M6-V1 nits: the map size items are a C# literal ("N × N"); a bigger map is only a bigger empty field; the dev could not set BUG-0560's status

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-11-0634, task M6-V1 (QA) |
| System | view: skirmish setup (`SkirmishSetup.cs`), `--map-size`; studio process |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game`, Skirmish: the map size list reads "128 × 128", "192 × 192", "256 × 256".
2. `& $env:GODOT --path game -- --map-size 256 --seed 5 --no-fog --screenshot <dir>/m256.png --screenshot-after 2 --zoom 120`
   and look at the minimap.
3. `git diff 1964e12...HEAD -- studio/` is empty.

## Expected
1. CLAUDE.md rule 8 / brief: every player-facing text from `ui.json` (`setup` section).
2. A 192 or 256 skirmish is a bigger battlefield.
3. Brief acceptance 4: "bug statuses set by the dev".

## Actual
1. `SkirmishSetup.Build`: `MapSizeOption.AddItem($"{size} × {size}")`. Numbers and a sign, so the rename pass is not
   affected, but it is still on-screen text from C# (a `setup.map_size_item` format such as `"{0} × {0}"` would close it).
   The dev flagged it as a deviation.
2. On 256 the town halls, the default 100-unit armies and the 8 mines sit in the middle of the map as on 128 (the halls
   about 55 m apart, the armies touching at tick 0); only the empty margin and the 12 forests spread out. The docs/03
   entry says the counts stay at the 128 map's; it does not say start positions do not scale. Sim-side map generation
   and start placement, so a note for whoever plans the map slice, not a view defect.
3. The dev's rules forbid `studio/**`, so the brief's "the dev sets the bug's status" for BUG-0560 could not be done.
   QA set it after verifying (see BUG-0560). Producer: align the brief wording with the dev's rules.
