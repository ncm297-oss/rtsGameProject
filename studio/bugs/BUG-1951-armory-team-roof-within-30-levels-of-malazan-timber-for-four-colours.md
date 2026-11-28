# BUG-1951: The Armory's team roof sits within 30 luminance levels of the dark Malazan timber for Iron, Crimson, Cobalt and Violet (and Ochre and Jade on the mode)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-1857, task M6-V32 (named by the developer as a possible bug, confirmed by QA's own measurement) |
| System | building models (view), `tools/asset-manifest.json` `malazan_armory` (and the shared Malazan `Wood` `#4d3a30`) |
| Fixed by | |

## Repro
1. Windowed: `& $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V32Test.tscn -- --shots <dir> --shots-only`.
2. Read the `QALUMA malazan_armory <colour>` lines. QA's method is independent of `LastBuildingsTest`'s atlas-tile
   classifier: model pixels are those whose ray meets the model; team pixels are model pixels that change when the same
   spot is drawn in two other player colours; the rest are non-team.

## Expected
docs/02 / docs/04: a player's colour reads on every building at zoom 40. The M6-V32 brief (criterion 7) asks for a
luminance gap of 30+ levels between the team part and the largest non-team part next to it, and for a gap under 30 to be
named (it is, in docs/04 and docs/03 question 2).

## Actual
QA's numbers (Rec. 709 on the picture, 0-255; team / non-team, mode and median):

| Colour | Team mode / median | Non-team mode / median | Gap mode / median |
| --- | --- | --- | --- |
| Iron | 96 / 99 | 75 / 98 | **21 / 1** |
| Ochre | 95 / 166 | 75 / 98 | **20** / 68 |
| Crimson | 91 / 91 | 75 / 98 | **16 / 7** |
| Cobalt | 93 / 84 | 75 / 98 | **18 / 14** |
| Jade | 89 / 155 | 75 / 98 | **14** / 57 |
| Violet | 51 / 100 | 75 / 98 | **24 / 2** |
| Rose | 124 / 127 | 75 / 98 | 49 / **29** |
| Bone | 218 / 221 | 75 / 98 | 143 / 123 |

The non-team mode (75) matches the developer's `Wood` mode (75) exactly. In the pictures
(`qa-m6v32-malazan_armory-colours-1-4-40m.png`) Crimson, Ochre and Cobalt still read by hue; Iron's slate roof is the
weakest, grey on grey stone under dark beams.

## Notes
The same `Wood` timber frames every Malazan roof since M6-V26, so this is a faction-wide palette question, not an Armory
defect; the developer's docs/03 "Implementation (M6-V32)" question 2 recommends a look task with the owner. Related:
BUG-1870 (the Cairn, now met), BUG-1910 (Bone on the Holy Camp).
