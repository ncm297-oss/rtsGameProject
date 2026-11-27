# BUG-1830: Iron team colour on the grey Stone Cairn reads only as a slightly darker top

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-26-0717, task M6-V30 |
| System | building models (view), `tools/asset-manifest.json` `teblor_stone_cairn` palette |
| Fixed by | |

## Repro
1. Look at the developer's `m6v30-colours-1-4-40m.png` (M6-V30 scratchpad `m6v30-shots/`), the Iron row (top row), the
   Stone Cairn column (the grey tower, x 975-1065, y 10-160 at 1600 x 900).
2. Count the pixel colours in that crop (QA did it with PIL).

## Expected
The brief's scope line: "Each new model needs a team part that reads at zoom 40 in all eight player colours."

## Actual
The Cairn's timber is tinted lichen grey (`#77736a` / `#8b877c`) and Iron is slate. At zoom 40 the lit grey wall is
(134, 135, 134), luminance 135; the Iron team top is (106, 114, 126), luminance 113, and the Iron banner falls under the
top's shadow. The team part reads as a slightly darker, slightly bluer top, not as a player colour. The other seven colours
(Crimson, Gold, Jade, Violet, Rose, Bone, Azure) read clearly on the Cairn, and Iron reads on every brown Teblor model.

## Notes
The same weakness is known on the Malazan grey stone (docs/04 says Iron "is the quietest ... and still reads apart"); the
Cairn is the one Teblor model where it repeats, because its grey tint is the only grey Teblor timber. Options for a later
look task: a warmer or lighter tint for the Cairn's timber, or let the owner judge the picture. Related: docs/03
"Implementation (M6-V30)" question 2 (the Cairn is a grey timber stand-in, not piled stone).
