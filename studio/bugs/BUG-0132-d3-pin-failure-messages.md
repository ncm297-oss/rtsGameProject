# BUG-0132: D3 tech pins fail on every one-sided edit, but some messages hide the values or blame the page

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-07-1415, task D3 |
| System | content tests (`sim/Rts.Sim.Tests/Content/TechContentTests.cs`, `RequiresText.cs`) |
| Fixed by | |

## Repro
Scratch clone of 8bcca04, one edit at a time, then
`dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~Content|FullyQualifiedName~DataValidation"`:
1. `docs/factions/malazan.md` Techs row cost `200 / 150` -> `250 / 150` (page only): `TechContentTests.C` fails with
   `malazan.md Techs row moranth_supply cost`. Same shape for `time` and `researched at`.
2. `game/data/factions/malazan/techs.json` `"gold": 200` -> `250` (data only): `A` fails with a bare
   `Assert.Equal() Failure: Values differ` (no tech id, no field name); `B` fails with
   `malazan.md faction upgrade line '... 200 G / 150 W ...' should start '... 250 G / 150 W ...'`.
3. `RequiresText.Needs` only reads the word "needs". A description that says "requires Age II", "after Age II" or
   "unlocked by a Legion Barracks" is not matched against `requires` (no shipped text does this today).

## Expected
The brief's QA focus: a one-sided edit fails a test whose message says which side and what the two values are, so the
owner can tell at a glance whether the page or the data moved.

## Actual
Every one-sided edit fails at least one test (checked for cost, time, effect amounts, page line, Techs table,
Buildings Requires, data `requires`, descriptions; see the D3 QA report). But:
- `C`'s cost / time / researched-at messages give the field, not the page value or the data value.
- `A` asserts tuples with `Assert.Equal` and no message, so a data-only edit's first message does not name the tech.
- `B` / `C` always phrase the mismatch as the page being wrong ("should start ..."), even when the data moved.

## Notes
Add the two values to `C`'s messages ("page 250 / 150, data 200 / 150"), give `A` a `$"{u.Id} ..."` message per
assert, and say "page vs data" rather than "should". Optionally widen `Needs` to "requires" / "after" or assert no
description uses those words. Data track, test-only.
