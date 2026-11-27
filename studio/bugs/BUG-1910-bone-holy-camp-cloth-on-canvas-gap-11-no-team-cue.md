# BUG-1910: A Bone player's Holy Camp has no team cue: cloth 235 on canvas 224 (gap 11 levels)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-27-0817, task M6-V31 |
| System | building models (view), `tools/asset-manifest.json` Whirlwind kit `canvas` colour; docs/04 "Building models, as built (M6-V31)" |
| Fixed by | |

## Repro
1. Windowed: `& $env:GODOT --path game res://tests/WhirlwindBuildingsTest.tscn -- --shots <dir>`, then look at
   `m6v31-holy-camp-bone-40m.png` (the developer's copy is in `<scratchpad>\m6v31-pictures`).
2. Or windowed `res://tests/QaM6V31Test.tscn -- --shots <dir>`: `qa-m6v31-ochre-bone-level0-40m.png` /
   `-level1-40m.png` (Ochre player 1, Bone player 2, a Holy Camp, Raider Camp and Tent each, on sand and on grass).

## Expected
Criterion 6 of M6-V31 is "measure and report"; a gap under 30 levels is named. Team colour is how a player tells whose
headquarters this is (docs/02 readability). A colour the setup screen offers should mark the faction's main building.

## Actual
QA's own luminance histogram of the developer's Bone picture (120 x 120 px round the camp): grass 183, the Bone cloth
faces 232-238, the canvas wall 223. The developer's table has Bone 235 on canvas 224, gap **11** (Ochre 43, Jade 54, the
rest 82-129). In the pictures the Bone Holy Camp is one pale shape: cloth, walls and the small tents read as the same
bleached canvas; only the crimson rug and dark doors break it. On sand (level 0) the Bone tents also sit close to the
ground's colour. The Bone Raider Camp and Tent read as tents by shape, not by colour.

## Notes
The developer named it in the report and in docs/04 (and asked the Producer, docs/03 "Implementation (M6-V31)"
question 2: a darker canvas would drop Ochre, Jade and Rose under 30 instead). Same family as BUG-1870 (a neutral wall
erases one setup colour); a cure that does not trade one colour for another is a team trim that is not cloth-coloured
(a dark band or pennant outline), or a per-colour canvas shade. The owner's taste call.
