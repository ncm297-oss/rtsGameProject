# BUG-1870: The paler Stone Cairn wall erases Jade's luminance contrast (31 to 0) and Ochre's (43 to 12); docs/03 says no colour got worse

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (2026-11-27-1857, M6-V32: the Cairn is rebuilt as dark stacked stones with team cloth) |
| Found | 2026-11-26-1847, task M6-VH10 |
| System | building models (view), `tools/asset-manifest.json` `teblor_stone_cairn` colours; docs/03 "Implementation (M6-VH10)" |
| Fixed by | M6-V32 (view branch 9cdbe850): docs/04 "Building models, as built (M6-V32)", the luminance table; QA re-measured with its own method (`QaM6V32Test` `QALUMA`) |

## Repro
1. Windowed: `& $env:GODOT --path game res://tests/ViewHardenVh10Test.tscn -- --shots <dir>` (QA's run:
   `<scratchpad>\m6vh10-qa\shots`; the developer's pictures and the `before` pair are in `<scratchpad>\m6vh10-pictures`).
2. `python -I <scratchpad>\m6vh10-tools\cairn_measure.py <dir> 40` (and `60`), on the new pictures and on `before`.

## Expected
BUG-1830 asked for the Iron player's colour to read on the Cairn. Fixing one colour should not leave another one at the
same luminance as the wall. docs/03 "Implementation (M6-VH10)" justifies the tint with "a paler wall moves every other
colour's contrast the right way or keeps it", and that sentence should match the table under it.

## Actual
Here is the gap between the team top and the wall in luminance levels (Rec. 709), from QA's own pictures at zoom 40.
Zoom 60 gives the same figures within 1 level.

| Colour | Before | After |
| --- | --- | --- |
| Iron | 25 | 56 |
| Violet | 25 | 55 |
| Rose | 4 | 27 |
| Crimson | 36 | 66 |
| Cobalt | 41 | 72 |
| **Jade** | **31** | **0** (team 167, wall 167) |
| **Ochre** | **43** | **12** |
| Bone | 97 | 67 |

Before, three colours had a gap under 30 levels (Iron, Violet, Rose). After, three still do (Jade, Ochre, Rose). Jade has
no luminance contrast at all now and is told from the wall by hue alone. A red-green colour-blind player sees that
saturated green close to the warm grey wall. The docs/03 sentence quoted above contradicts the table printed under it,
where Jade, Ochre and Bone all lose contrast. The developer's report and docs/03 Question 1 do name Jade and Ochre, so
this is disclosed. The defect is the wrong justification sentence, and the trade-off that nobody has decided on yet.

## Notes
- No acceptance criterion is broken: criterion 6 is about Iron only, and Iron's gap is 56 (40 or more wanted).
- To fix it, either reword the docs/03 sentence ("Iron, Violet, Rose, Crimson and Cobalt gain; Jade, Ochre and Bone lose")
  or pick a wall luminance between the dark and the light team colours. Wall luminance of about 140-150 keeps Iron at
  30 or more but not at 40. The Producer or owner can choose to accept the trade-off until the Cairn becomes stacked rocks
  (docs/03 M6-V30 answer 2).

- M6-V32 (2026-11-27-1857): by the brief's measure (team part against the largest non-team part, `Stone` at 50) no colour is under 30: Iron 46, Ochre 115, Crimson 37, Cobalt 31, Jade 103, Violet 48, Rose 76, Bone 171. QA, against all non-team pixels (mode 58): Cobalt 23, Crimson 29; all eight read clearly by hue in the pictures. Closed by the Producer at the ACCEPT; Cobalt is the marginal one if the owner asks for more.
