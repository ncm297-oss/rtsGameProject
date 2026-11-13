# BUG-1320: The gathering tint covers a worker model completely, hiding its outfit and team colour

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-11-13-1118, task M6-V20 (QA) |
| System | unit views (models, worker state tint) |
| Fixed by | |

## Repro
1. `& $env:GODOT --path game --resolution 1600x900 res://tests/ModelShots.tscn -- --skirmish <dir>` (a real skirmish start as
   Malazan; the five starting Laborers are sent to the nearest gold and wood, picture at tick ~405).
2. Look at `<dir>/models-skirmish.png`.

## Expected
docs/04 "Team color" and M6-V20's goal: a Laborer reads as a team-coloured figure at the default zoom. The worker's state tint
(M5) should mark what it is doing without hiding whose it is.

## Actual
The three Laborers gathering at the mine are flat bright green figures: the M5 `GatheringTint` overlay (0.25, 0.95, 0.35,
alpha 0.45, unshaded) now goes on the model's whole mesh (`UnitViews.Sync`: `(model ?? view).MaterialOverlay`). The outfit,
skin and the `team` surface are all washed out. Only the two walking workers show their slate team colour. Returning
workers (yellow tint) and building workers (blue tint) are covered the same way. Workers spend most of a match gathering, so
the most common model in a match mostly shows the tint colour instead of the player colour. The developer reported this in
picture (d). QA re-took the picture on a fresh clone and saw the same:
`C:\Users\Nick\AppData\Local\Temp\claude\162c01fae57881b0\7571422d-c18f-4f75-8bfa-6665ab55f256\scratchpad\qa-v20\pics\models-skirmish.png`.

## Notes
- The brief required the tint to keep working on a model, and it does. This is a readability problem that only showed up
  once the tint was on a model. On a capsule, the 45 % overlay over the player colour still read as "player colour, tinted".
  On a model it hides the textured figure.
- Options for the Producer or owner: a lower alpha on models; the tint on the `team` surface only; or a marker or ring
  instead of a whole-body overlay (the cargo marker already shows what a worker carries). Not a criterion of M6-V20.
- `EconomyViewTest` (which pins the tints) runs with models off. On models only the build tint is pinned (`ModelViewTest`,
  `QaM6V20Test` row 5).
