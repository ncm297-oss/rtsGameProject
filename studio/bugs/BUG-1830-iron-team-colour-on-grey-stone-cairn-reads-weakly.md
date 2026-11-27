# BUG-1830: Iron team colour on the grey Stone Cairn reads only as a slightly darker top

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-11-26-0717, task M6-V30 |
| System | building models (view), `tools/asset-manifest.json` `teblor_stone_cairn` palette |
| Fixed by | M6-VH10 (2026-11-26-1847, view track) |

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

## Fix (M6-VH10)
A wall tint (the brief's choice): the Cairn's timber is a paler stone grey, `Wood` `#938e83`, `Wood_Light` `#aca79a`
(was `#77736a` / `#8b877c`), so the lit wall is lighter and the slate Iron team top stands further below it. The team part
is unchanged. Measured on `ViewHardenVh10Test`'s windowed pictures (all eight colours at zoom 40 and 60) with the
scratchpad's `m6vh10-tools/cairn_measure.py` (team pixels told from wall pixels by the difference between the 1-4 and
5-8 pictures and an empty one): at zoom 40 the Iron team top is luminance 111 against the lit wall's 136 before (gap 25)
and 167 after (gap **56**, criterion: at least 40); zoom 60 the same within 1 level; the team area is unchanged (1,882 px
at zoom 40). The other seven: Ochre 12, Crimson 66, Cobalt 72, Jade 0, Violet 55, Rose 27, Bone 67 (Jade and Ochre read
by hue, as Rose did at a gap of 4 before); docs/03 "Implementation (M6-VH10)" has the table. Every other model file is
byte for byte (two full re-imports, SHA-256 of all 152 files).
