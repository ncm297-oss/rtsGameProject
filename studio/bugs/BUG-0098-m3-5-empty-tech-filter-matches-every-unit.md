# BUG-0098: An empty `units` or `tags` filter in a tech effect silently matches every unit

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-07-1131, task M3-5 |
| System | data loader (techs.json), tech bonus |
| Fixed by | 8928418 (M3-6): empty `units` / `tags` is one error; `QA/TechDataQaTests.AnEmptyUnitsOrTagsFilter_IsRejected_NotReadAsEveryUnit` un-skipped |

## Repro
1. In `game/data/factions/malazan/techs.json`, set Moranth Supply's first effect to
   `"appliesTo": { "units": [] }` (or `{ "tags": [] }`).
2. Load the data: it loads with 0 errors.
3. Research it for player 0: `World.TechBonus(0, whirlwind_zealot, AbilityCooldown)` and
   `TechBonus(0, malazan_heavy_infantry, AbilityCooldown)` are both -300.

Pinned by `QA/TechDataQaTests.BUG0098_Today_AnEmptyUnitsFilter_ReachesEveryUnit`; the wanted behaviour is
`AnEmptyUnitsOrTagsFilter_IsRejected_NotReadAsEveryUnit` (skipped, BUG-0098).

## Expected
docs/03 "Data format": "`appliesTo` is required (`{}` = every unit); each filter it sets must match", and "a faction
tech's `units` must be its own". A filter that is *set* to an empty list can't be matched by any unit, so it should be
one error at the list (or match nothing). It should never quietly mean "every unit of every faction".

## Actual
`TechReader.Resolve` turns `[]` into an empty `ImmutableArray<int>`, and `TechState.Matches` reads
`IsDefaultOrEmpty` as "no filter". So a data author who empties the list to switch an effect off gets the opposite:
the effect reaches every unit, including the other faction's units (the own-faction check only runs per entry, so an
empty list skips it).

## Notes
Easy fix: in `BuildEffect`, a present but empty `units` / `tags` list is one `DataError` at the list path. Content
that loads but plays wrong, so S3. The shipped data has no empty filter lists.
