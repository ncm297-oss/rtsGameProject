# BUG-1600: The new figures in the setup's other colours: a Bone Priest shows no team colour, crimson props match the Crimson player, the hp bar hides the Mage's staff head, Mage and Heavy Infantry alike in grey

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (items 1-3, M6-VH9, verified by QA 2026-11-24-1431); item 4 measured only (owner taste) |
| Found | 2026-11-17-0816, task M6-V25 (QA) |
| System | unit models (view, art pass): `tools/asset-manifest.json` tints / prop colours |
| Fixed by | M6-VH9: `tools/asset-manifest.json` Priest and Zealot re-tint; `UnitViews.OverlayLineOf` with the props' top; rows `ViewHardenVh9Test` (C), (B) |

## Repro
1. `dotnet build game/RtsGame.csproj`
2. `& $env:GODOT --path game --resolution 1600x900 res://tests/QaM6V25Shot.tscn -- --shots <dir>`: four players in the
   setup's Crimson `#B82633`, Cobalt `#1F3F9F`, Jade `#2EA35A` and Bone `#E3D3AC` (`ui.json` colours 3, 4, 5, 8), each with a
   Cadre Mage, Sapper, Heavy Infantry, Priest, Zealot and Raider at 60 % hp; `qa-m6v25-colours-40m.png`, `-20m.png`, `-40m-gray.png`.
   The developer's sheets used only two colours (slate and yellow), so none of this shows in them.

## Expected
docs/04 style guide: "team colors are the only strong accents"; "Every unit slot must be identifiable by shape alone in a
grayscale screenshot". Each player's figure carries its colour visibly at 20-60 m, and no non-team part looks like a player
colour.

## Actual
1. **Bone Priest:** its team parts (hood, jerkin) in Bone `#E3D3AC` sit on a bleached outfit tint `#e2d9c2` (brightness 1.4)
   and inside a robe prop `#d9d0bc`. At 40 m and 20 m the whole figure is one off-white mass, with the red veil as the only
   accent. Nothing on it says "Bone player", while every other player's Priest carries a Bone-coloured robe block.
2. **Crimson on non-team parts:** the Zealot's head-wrap prop `#7e1d1d`, its crimson outfit tint `#8a2a26` (trousers, boots)
   and the Priest's veil `#8e1f24` are strong reds next to the Crimson player colour. A Cobalt or Jade Zealot reads at 40 m as
   half red (head and legs); a Crimson Zealot is red from head to foot.
3. **The bar hides the staff head:** for a hurt or selected Cadre Mage, the hp bar (line 1.961 m) sits over the bronze staff
   head at the default zoom and at 20 m (Crimson, Cobalt and Jade rows). The staff head is the feature the report gives as
   the Mage's identifier ("a staff with a bronze head above its head").
4. **Grey copy at 40 m:** the Mage's robe and the Heavy Infantry's shield are both dark squares. Their mean grey (0-255) is
   67 vs 83 for Crimson and 54 vs 79 for Cobalt, against 58 vs 150 for Jade and 66 vs 213 for Bone. With a dark player
   colour the two read apart only by the 1-2 px staff and the helmet.

## Notes
- No acceptance criterion is broken. The props never take the team colour (`AtlasModelTest`: team prop triangles 0; the props
  texel draws the same for both players). The four types take the colour on the outfit parts the manifest marks. These are
  readability findings for the Producer and owner, several of them taste.
- Cheap levers are all manifest edits: a darker or sand-toned robe for the Priest (the faction page's "sun-bleached white"
  clashes with the Bone player), a less saturated wrap and veil, or a shorter staff.
- Pre-existing cousins: the Whirlwind Camp Follower `#d8cba8` and Desert Archer `#ddd3b8` tints are also close to Bone, but
  there the team part is the whole jerkin, not a hood inside a white robe.

## QA verification (2026-11-24-1431, M6-VH9)
Independent distances from the manifest and `ui.json` (RGB 0-255, Euclidean): Priest outfit tint and robe `#a39577` 103.7 from Bone; veil `#5e1a20` 92.8 from Crimson; Zealot outfit tint `#6e3a26` 77.7 and head-wrap `#5a1c1e` 96.8 from Crimson: all over 60. Pictures (`crop-1-4-20m.png`, `crop-5-8-20m.png`): the Bone Priest's hood reads white on a sand robe; the Zealot's legs are brown. Mage bar: line 1.961 -> 2.309 m, the bar's lower edge 2.579 m over the staff head's 2.165 m at the default zoom and at 20 m (screen y 366 vs 377, 259 vs 285). Item 4 (Mage robe vs Heavy Infantry shield in grey) is measured in docs/03 and left for the owner.
