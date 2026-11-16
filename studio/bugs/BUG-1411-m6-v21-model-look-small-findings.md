# BUG-1411: M6-V21 look nits the developer's "reads badly" list misses (melee wind-up, palette-matching player colours, Crossbowman's clip map)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 3 fixed in docs by M6-VH8, session 2026-11-15-1536; items 1-2 are the owner's taste) |
| Found | 2026-11-14-1246, task M6-V21 (QA) |
| System | unit models (art / view) |
| Fixed by | M6-VH8 (item 3, docs) |

## Repro
QA's pictures (windowed, 1600 x 900, shipped data):
`& $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V21Shots.tscn -- --colours <dir> --set 1` (and `--set 2`),
and `... -- --props <dir>` (close-ups from a test camera 4 m away: idle, walk, four phases of the attack).

## Items
1. **The melee wind-up reads like a T-pose up close.** UAL1 `Sword_Attack` (`attack_melee`) at about 0.25 s holds both arms
   out almost horizontal: upper-arm rotations are close to the rest pose, for example `upperarm_r` (0.215, -0.671, 0.272,
   0.656) against rest (0.180, -0.684, 0.180, 0.684). The library has the tracks, so this is the source clip, not missing keys.
   The shield is bound to `spine_03`, so it stays on the chest while the arms swing wide
   (`props-malazan_heavy_infantry-attack-1.png`). At the 20-40 m RTS zoom it is barely visible. docs/04 lists UAL2
   `Sword_Regular_A/B/C` as candidates; one of them (with its `_Rec`) may read better.
2. **Player colours that match a faction palette.** Colour 2 "Ochre" (#C8892E) on the Whirlwind (ochre / sand outfits) and
   colour 8 "Bone" (#E3D3AC) on the Whirlwind sand / bleached outfits colour the whole figure one tone. The player still
   reads (every one of the eight colours is distinct from the others on both palettes at 20 m and 40 m), but at 40 m the
   Bone Whirlwind figures are also close to the sand ground. Together with the developer's "player 1 slate close to the grey
   Malazan outfits", this is the owner's taste call, not a defect.
3. **The Crossbowman's and Desert Archer's libraries hold the worker clips.** `QaM6V21Test` row (4) prints `[attack build
   gather_gold gather_wood idle walk]` for every model type. A clip map with no entry for a state falls back to the state's
   own name (the documented pre-V21 rule), so soldier types carry `build` / `gather_*` they never play. Harmless (references,
   no copies), but docs/04's clip-map table reads as if a map lists only what a type plays.

## Expected / Actual
See the items. None breaks a criterion.

## Notes
Item 1: the developer's list covers the shield clipping the arms and the edge-on shield side-on, but not this wind-up pose.

## M6-VH8
Item 3: docs/04's clip-map table now says what a type holds (every state's clip, by the map or the state's own name)
against what it plays. Note: the overlays' line (M6-VH8) is measured over every held clip, so every model type's line
is set by `gather_wood`'s raised hand today; dropping the worker clips from soldier libraries would lower a soldier's
line by about 0.16 m by itself. Items 1-2 stay the owner's.
