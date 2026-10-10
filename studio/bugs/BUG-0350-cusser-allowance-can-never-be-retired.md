# BUG-0350: The Cusser allowance can never be retired: the ability pin hard-codes "No effect on buildings" and an enemy/own wording

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-09-1155, task D10a |
| System | content tests (`Content/AbilityContentTests`) |
| Fixed by | 4f4e3ca (D10b): `AbilityContentTests.EffectProblems` reads "<amount> <type> damage", "full damage to buildings" / "No effect on buildings" and "friendly fire at N%" / "own units take half"; `PendingAbilities = { "Sandstorm" }`; `Cusser_IsLoaded_AndStrictlyPinned`, `AMutatedCusserField_*` (5), `AMutatedCusserCell_*` (15), QA `QA/Content/AbilityPinQaTests` (16); verified at the 2026-10-10-0215 ACCEPT |

## Repro
1. Scratch clone of `studio/2026-10-09-1155-data` (7a9a0ac). Add `cusser` to
   `game/data/factions/malazan/abilities.json` with the planned numbers (6 / 3.5 / 1.0 s / 45 s,
   `affects: enemy_units`, `damage {type: siege, amount: 120}`) and `"abilities": ["cusser"]` on
   `malazan_sapper`. (`buildings` / `friendlyFire` left out: today's loader rejects unknown fields.)
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AbilityContentTests" --logger "console;verbosity=detailed"`

## Expected
Green with report lines (it is), and once the sim's Cusser fields land and Cusser is dropped from
`PendingAbilities`, the page row (which is correct by design) pins strictly.

## Actual
Green, with these report lines:
```
malazan.md "Abilities" 'Cusser' Effect: '120 siege damage in the area, full damage to buildings (≈355 to a Town Hall), friendly fire at 50%' does not say 'Enemy units' (affects EnemyUnits)
malazan.md "Abilities" 'Cusser' Effect: '... friendly fire at 50%' does not say 'No effect on buildings'
```
`CellProblems` always requires the text "No effect on buildings" (comment: "Abilities never touch
buildings yet") and requires `enemy_units` rows to start with "Enemy units". The sim's Cusser is
`enemy_units` + `friendlyFire: 0.5` + `buildings: true`, and the page says "full damage to
buildings ... friendly fire at 50%". So the Cusser row can never pass strictly: removing it from
`PendingAbilities` turns two correct page facts into failures, and keeping it there leaves every
Cusser cell (range, cooldown, damage) unguarded forever. The developer flagged this as a deviation.

## Notes
Fix belongs with the sim's Cusser landing (or a follow-up data task): read the building and
friendly-fire fields from `AbilityDef` and derive the expected wording ("full damage to buildings" /
"No effect on buildings", "friendly fire at N%") instead of hard-coding it.
