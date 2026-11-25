# 04 — Art Pipeline

How art gets into the game: style rules, where assets come from, how they're named and imported,
and the licensing log. Until M6 the game runs entirely on procedural placeholder art.

## Style guide

- **Grounded, not cartoonish (owner decision 2026-10-08).** Realistic human proportions (no big
  heads or stubby limbs), restrained materials, weighty animation. Still low-poly for the budgets
  below, but pick the more realistic option whenever sources differ. Simple textures and normal
  maps (as in the Quaternius Universal packs) are fine; no photographic textures.
- **Readable at RTS zoom (20-60 m camera distance).** Silhouette beats detail. Every unit slot
  must be identifiable by shape alone in a grayscale screenshot.
- **Muted world, saturated teams.** Terrain, buildings, and clothing use earthy, desaturated
  tones; team colors are the only strong accents.
- **Lighting:** one directional sun with soft shadows, ambient light from a sky color. No
  dynamic day/night in v1.
- **Polygon budgets:** units ≤ 3k triangles, large units (Hounds, Champion, Destriers) ≤ 6k,
  buildings ≤ 5k, props ≤ 500. ~400 animated units must hold 60 FPS.

### Scale and dimensions

| Thing | Size |
| --- | --- |
| Engine unit | 1 Godot unit = 1 m |
| Map cell | 2 m |
| Human | ~1.8 m tall |
| Tiste Andii / Edur | ~2.0 m (1.1×) |
| Teblor | ~2.7 m (1.5×) |
| Horse at shoulder | ~1.6 m; Teblor destrier ~2.2 m |
| Hound of Shadow | ~1.6 m at shoulder |
| Buildings | Fit their cell footprint (2×2 = 4 m, 3×3 = 6 m, 4×4 = 8 m) with a small margin |

Models face **-Z** (Godot's forward), origin at the feet / ground center. A building's front (its door) faces **+Z**,
toward the match camera: buildings never turn (M6-V26).

### Team color

Each unit and building model has one material slot named `team` (banners, shields, cloaks,
trim). A shared shader replaces that slot's color with the player color. Import tooling renames
the right material to `team` per the asset manifest.

**As built (M6-V20, units).** The manifest marks whole outfit parts `"team": true`; the tooling gives them one material
`team` with its own texture: the outfit texture's brightness alone (grey), scaled so its mean is 0.8 (`teamShade`), so
every model's team part is about as bright as a capsule in the same colour. `game/shaders/team_colour.gdshader` draws it as
`player colour x shade x 1.25` (the per-player colours the capsules use, one `ShaderMaterial` per model type and player,
made at `UnitViews.Bind`). The rest of the outfit keeps its own texture, tinted toward the faction palette (`tints`).
Producer default for the first two units (owner may revisit): the Laborer's tunic (the Peasant's body part); the
Crossbowman's hood, pauldron **and jerkin** (the Ranger's body part). The jerkin was added after the first screenshot: the hood
and pauldron alone were 2-3 px of colour at the default zoom and the two players' Crossbowmen did not read apart.
Nothing is drawn over a model's team surface but the hit flash (M6-VH7, BUG-1320, Producer default, owner may revisit): the
worker's gathering / returning / building tint, a 45 % overlay over the whole figure, hid the outfit and the player colour,
so a model doesn't take it (its clip and the cargo marker show what it does); capsules keep it.
M6-V21 (Producer default, owner may revisit): the Heavy Infantry's team parts are its jerkin, pauldron **and its big shield**
(a box prop drawn on the team surface, "Unit models, as built"); the Raider's jerkin and hood; the Desert Archer's as the
Crossbowman's; the Camp Follower's tunic as the Laborer's.
M7-V5 (developer's pick, owner may revisit): the Teblor Gatherer's and Shaman's tunic (the Peasant body), the Warrior's and
Breaker's jerkin and pauldron, the Javelin Thrower's jerkin (the Ranger body; no pauldron, so it reads apart from the Warrior).
M6-V25 (developer's pick, owner may revisit): the Cadre Mage's and the Priest's jerkin and hood (their robes are their own
colour), the Sapper's jerkin, the Zealot's tunic (the Peasant body).
M6-V26 (developer's pick, owner may revisit): a building's team part is its tiled roof (the pack's `Main` material) on the
Garrison Keep and the Barracks; "Building models, as built" below.
**One material per model since M6-V24.** A unit model is one surface with one material, still named `team`, drawing one
atlas: a 512² tile per role texture (the team part's brightness, outfit, skin, head, hair) and a tile of the props' flat
colours; the atlas **alpha is the team mask** (0.5 on the team tile, 0.75 on the props' tile, 1 elsewhere). The same
`team_colour.gdshader` draws the whole figure: a team texel as before (`player colour x shade x 1.25`), any other texel as
its own colour, so which parts take the player colour is unchanged (the same triangles, `AtlasModelTest`). Still one
`ShaderMaterial` per model type and player. Layout and the reasons in "Unit models, as built" (M6-V24) below and docs/03
"Implementation (M6-V24)".

### Faction palettes and silhouettes

| Faction | Palette | Silhouette identity |
| --- | --- | --- |
| Malazan | Steel grey, oxblood red, dull bronze | Big rectangular shields, crossbows, hooded mages, sapper satchels |
| Whirlwind | Ochre, sand, bleached white, crimson | Veils and head-wraps, curved blades, flails, light horses |
| Teblor | Earth brown, bloodwood red-brown, bone white, furs | 1.5× scale, long hair, huge wooden swords |
| Shadow | Ash grey skin, slate, dusk violet, dark teal | Tall slender Edur, spindly demons, horse-sized hounds |
| Andii | Black, midnight blue, silver, moonlight | Tall, dark-skinned, silver hair, long blades, longbows, ravens |

## Placeholder art (M1-M5)

Generated in code so the game is fully playable before any asset is downloaded:

- **Units:** primitive meshes in team color, one distinct shape per slot. Worker: small capsule
  with a box tool. Line: capsule with a flat shield slab. Ranged: thin capsule with a long stick.
  Shock: elongated box body with a capsule rider. Caster: cone robe with a sphere head. Siege: box
  with a cylinder arm or log. Unique: a faction-specific combination. Scale follows the faction
  (Teblor 1.5×).
- **Buildings:** boxes sized to the footprint with a prism roof; height and roof color by slot.
  Under construction: scaffold-colored and scaled by progress.
- **Terrain:** heightmap mesh with vertex colors by biome and slope. Trees as cone + cylinder
  MultiMesh. Gold mines as yellow rock clusters.
- **Effects:** simple particles for projectiles, splash, Burning, Sandstorm, Darkness.
- **Audio:** short generated tones and noise bursts per event (select, command, attack, death,
  build complete, alert) so audio plumbing exists from M2. Since M6-V12 the events play real clips cut from the Sonniss
  bundle (below, "Import process", audio); each event's tone stays as its fallback when its file is missing.

## Asset sources

All candidate packs are CC0 (public domain, no attribution required, commercial use allowed).
**Check contents and license again at download time** and record each one in the licensing log.

### Characters and animation

| Pack | Source | Use |
| --- | --- | --- |
| KayKit – Character Pack: Adventurers | <https://kaylousberg.itch.io/kaykit-adventurers> | Base humanoids (Knight, Barbarian, Mage, Rogue, Rogue_Hooded) + 25+ weapon accessories |
| KayKit – Character Pack: Skeletons | <https://kaylousberg.itch.io/kaykit-skeletons> | Wraiths (with a ghost shader) |
| KayKit – Character Animations | <https://kaylousberg.itch.io/kaykit-character-animations> | 161 animations for KayKit's Rig_Medium/Rig_Large: idle, walk/run, melee, ranged, spellcasting, death, tool use (mining, chopping, hammering) |
| KayKit: Fantasy Weapons Bits | <https://kaylousberg.itch.io/fantasy-weapons-bits> | Extra weapons (flails, curved blades, polearms) |
| Quaternius – Ultimate Animated Animal Pack | <https://quaternius.com/packs/ultimateanimatedanimals.html> | 12 animated animals: horses for cavalry; check for a dog/wolf (war-dogs, Hounds) |
| Quaternius – Universal Base Characters | <https://quaternius.com/packs/universalbasecharacters.html> | Alternative humanoid base with realistic proportions (rigged, ~13k tris: needs decimation) |
| Quaternius – Modular Character Outfits – Fantasy | <https://quaternius.com/packs/modularcharacteroutfitsfantasy.html> | Outfits for the Universal Base Characters |
| Mixamo (Adobe account) | <https://www.mixamo.com> | Gap-filler animations; auto-rigging for humanoids |

**Humanoid base: Quaternius Universal (owner decision 2026-10-08).** The owner prefers a
realistic look over cartoonish, so the default humanoid stack is Quaternius **Universal Base
Characters** + **Modular Character Outfits – Fantasy** + **Universal Animation Library** 1 and 2
(one shared humanoid rig, CC0), with **Mixamo** packs retargeted onto that rig for gaps (bow,
great sword, sword and shield, magic). KayKit drops to a fallback for props and weapon bits. The
M6 look test still happens at the start of M6 (not earlier), now to confirm the Quaternius
stack at RTS zoom and settle palettes, with MPFB2 (MakeHuman for Blender, CC0 output) as the
step up if it still reads too soft.

**The art call: "look: go" (owner decision 2026-11-12).** After the look test (below) the owner keeps the Quaternius
stack and adds outfits and team colour: the art pass (M6 criterion 5's second half) builds on it, starting with the import
tooling and the units the outfits on disk cover. **Inventory finding (Producer, 2026-11-13):** the Modular Character
Outfits – Fantasy edition in `asset-sources/` (Standard and Source) holds **two outfits, Peasant and Ranger** (male and
female, as whole outfits and as modular parts: arms, body, legs, feet, the Ranger's hood and pauldron), and no animal
pack is on disk. Producer default until the owner says otherwise: workers wear the Peasant, ranged units the Ranger,
and the other slots are those two outfits recoloured to the faction palette with weapons, shields and props kit-bashed
in Blender for the silhouette; horses, dogs and siege engines stay placeholders until a source is chosen (the owner's
download or an AI-generated model, "Coverage plan" below).

| Pack | Source | Use |
| --- | --- | --- |
| Quaternius – Universal Animation Library 1 and 2 | <https://quaternius.com/packs/universalanimationlibrary.html> | 250+ animations on the Universal rig: locomotion, melee and armed combos, deaths, work actions |
| Mixamo packs (Great Sword, Pro Longbow, Pro Magic, Pro Sword and Shield) | <https://www.mixamo.com> | Retargeted onto the Universal rig; FBX, 30 fps, no keyframe reduction, in-place locomotion |

**Already downloaded (owner, 2026-10-08), in `asset-sources/` on the desktop PC:** Universal Base
Characters (Standard + Source, incl. a Godot project zip and hairstyles), Modular Character
Outfits – Fantasy (Standard + Source), Universal Animation Library 1 (Standard, Pro, Source) and 2
(Standard, Source), Ultimate Fantasy RTS (`quaternius-ultimate-fantasy-rts-2022-08/`), and the four
Mixamo packs above (`mixamo/<pack>/`), and Stylized Nature MegaKit (Standard, Pro, Source; preferred
over the KayKit forest for the realistic look). Blender 5.2 is the owner's install for `.blend`
sources and model edits. Ground textures: 23 ambientCG PBR materials at 1K JPG (CC0) in
`asset-sources/ambientcg/<id>/` (grass, ground, rock, rocks, paving stones, asphalt; each with
Color, NormalGL / NormalDX, Roughness, AO, Displacement and a Godot `.tres`). Poly Haven scanned
models (CC0) in `asset-sources/polyhaven/<id>/`: 4 trees (island, jacaranda, small, searsia
shrub), a dead trunk, boulders, cliffs and mossy rocks, as 4K `.blend` + textures (heavy: decimate
and downsample to the budgets above). Kenney UI Pack RPG Expansion (CC0) in
`asset-sources/kenney-ui-pack-rpg-expansion/`. Sonniss GDC 2026 Game Audio Bundle (royalty-free,
no attribution, no resale of raw files; licence PDF inside) in `asset-sources/sonniss-gdc-2026/part1-5/`,
~7.5 GB. Still to download before M6: fonts (e.g. Cinzel, EB Garamond; SIL OFL), music, the Godot
export templates.

### Buildings, nature, props

| Pack | Source | Use |
| --- | --- | --- |
| Quaternius – Ultimate Fantasy RTS | <https://quaternius.com/packs/ultimatefantasyrts.html> | 128 models: buildings in several evolution stages (construction states!) + nature |
| Quaternius – Medieval Village MegaKit | <https://quaternius.com/packs/medievalvillagemegakit.html> | Modular pieces for kit-bashing faction buildings |
| KayKit – Medieval Hexagon Pack | <https://kaylousberg.itch.io/kaykit-medieval-hexagon> | Buildings and props (ignore the hex tiles) |
| KayKit – Forest Nature Pack | <https://kaylousberg.itch.io/kaykit-forest> | Trees, rocks, foliage |
| KayKit: Resource Bits | <https://kaylousberg.itch.io/resource-bits> | Gold, wood, and carried-cargo props |
| Quaternius – Stylized Nature MegaKit | <https://quaternius.com/packs/stylizednaturemegakit.html> | Biome variety (desert scrub, conifers) |
| Kenney – Castle Kit | <https://kenney.nl/assets/castle-kit> | Siege weapons (Catapult), towers, walls for later |
| Kenney – Fantasy Town Kit | <https://kenney.nl/assets/fantasy-town-kit> | Extra modular building pieces |

### UI, fonts, audio

| Pack | Source | Use |
| --- | --- | --- |
| Kenney – UI Pack + UI Pack: RPG Expansion | <https://kenney.nl/assets> | Panels, buttons, frames for HUD and menus |
| Kenney – Fonts | <https://kenney.nl/assets/kenney-fonts> | Placeholder UI fonts |
| Sonniss – GDC 2026 Game Audio Bundle (downloaded, `asset-sources/sonniss-gdc-2026/`) | <https://sonniss.com/gameaudiogdc> | **In use (M6-V12):** every SFX clip, cut by `tools/audio/trim-wav.ps1` |
| Kenney – RPG Audio, Impact Sounds, Interface Sounds | <https://kenney.nl/assets> | SFX: combat, building, UI (not downloaded; Sonniss covers it) |
| freesound.org (CC0 filter only) | <https://freesound.org> | Specific SFX gaps (war cries, sandstorm wind) |
| Kevin MacLeod / incompetech (CC BY, needs attribution) or Kenney music jingles (CC0) | <https://incompetech.com> | Placeholder music |
| Suno / Udio (paid plans for ownership) | — | Optional original music later |

Exact Kenney page URLs can change; the import tooling records the actual URL and version used
in the licensing log.

### Coverage plan for the vertical slice and beyond

| Need | First choice | Fallback |
| --- | --- | --- |
| Human infantry, mages, workers | Quaternius Universal Base Characters + Modular Outfits + recolor | MPFB2 bodies; KayKit Adventurers (cartoonish, last resort) |
| Horses (Lancer, Horse Raider, Rider) | Quaternius animal pack horse + Universal Base Character rider | AI-generated |
| Catapult | Kenney Castle Kit | Kit-bash in Blender |
| Battering Ram / Edur Ram | Kit-bash (Medieval Village pieces + wheels) | AI-generated |
| Teblor | Universal Base Character (male) at 1.5× + outfits. **As built (M7-V5):** Gatherer, Warrior, Javelin Thrower, Shaman and Breaker (the two outfits on disk recoloured, the pack's `Hair_Long`, props as boxes; "Unit models, as built"). The Destrier Rider and the War-dog stay capsules until a horse / dog source is chosen | MPFB2 giant body; AI-generated |
| War-dogs | Quaternius dog/wolf if present | **AI-generated** |
| Hounds of Shadow | Quaternius wolf at 1.6×, dark material | **AI-generated** |
| Aptorian Stalker | — | **AI-generated** |
| Great Raven | — | **AI-generated** |
| Eleint-blooded Champion | Universal Base Character at 1.3× + outfit + horns | AI-generated |
| Wraiths | KayKit Skeletons + ghost shader | — |
| Buildings (all factions) | Quaternius Ultimate Fantasy RTS + kit-bash, recolored per palette | AI-generated hero buildings |

## AI-generated models (Meshy / Tripo)

Used only where packs fall short. Workflow:

1. **Prompt template:** "Low-poly game asset, realistic proportions, muted natural colors,
   simple textures, not cartoonish, [subject], [pose: T-pose / standing neutral], [palette colors], RTS unit, clean
   silhouette, single object, plain background."
2. Generate several candidates, pick by silhouette at small size, not by close-up detail.
3. Reduce to budget (Blender Decimate, or the service's low-poly option), bake or map colors to
   the faction palette atlas.
4. Rig: the service's auto-rig for humanoids; quadrupeds may need Mixamo-style manual work or a
   simple custom rig. Claude writes Blender Python scripts for the repetitive steps.
5. Import like any other asset (below).

Licensing: the free tiers of these services can come with restrictions (attribution, public
outputs, or no commercial use), and terms change. **Record the plan and terms in the licensing
log at generation time.** For a private hobby project any tier works; before a public release,
every generated asset must be on terms that allow it.

## Directory layout and naming

```
asset-sources/                       # gitignored: raw downloaded zips, extracted as-is
  kaykit-adventurers-1.0/
  quaternius-ultimate-fantasy-rts/
game/assets/
  models/
    common/props/                    # trees, rocks, gold mines
    malazan/malazan_heavy_infantry/malazan_heavy_infantry.glb
    malazan/malazan_barracks/malazan_barracks.glb   # a building: found by its id (M6-V26, docs/01 2026-11-18 (b) (e))
    whirlwind/...
  animations/                        # one shared AnimationLibrary per rig (universal_humanoid.glb, imported as AnimationLibrary)
  textures/palettes/                 # palette atlases
  audio/
    sfx/<category>/<event>_<variant>.wav
    music/<track>.ogg
  ui/
  fonts/
tools/
  asset-manifest.json                # source file -> destination, scale, material renames
```

- Everything **snake_case**. File names match data ids (`malazan_heavy_infantry.glb` for unit id
  `malazan_heavy_infantry`), so the data's `model` field is a path stem.
- **`.glb`** for models (single file, binary, LFS). **`.wav`** for short SFX, **`.ogg`** for music.
- Raw packs never enter git. Only files listed in the manifest are copied into `game/assets/`.

## Import process

1. Download the pack (owner clicks download; Claude provides the exact link) and extract it to
   `asset-sources/<pack-name>-<version>/`.
2. Add entries to `tools/asset-manifest.json`: source path, destination path, scale, rotation
   fix, which material becomes `team`, palette overrides.
3. Run `tools/import-assets.ps1`: copies and renames per the manifest, converts FBX to glTF via
   Blender's command line where needed, and writes a summary.
4. Run `& $env:GODOT --headless --path game --import` so Godot generates `.import` files.
   Commit the `.import` files (they hold import settings, not binaries).
5. Add the pack to the licensing log below in the same commit.
6. Check in game at RTS zoom, take a screenshot, compare against the placeholder.

**Unit models, as built (M6-V20).** `powershell -File tools/import-assets.ps1 [-Only <id>|library] [-SourceRoot <dir>]
[-Blender <exe>] [-NoGodot]` does steps 2-4 with no manual step; the outputs are committed, so a clone without Blender or
`asset-sources/` builds, imports and runs, and the script only regenerates them.

- **Blender** (5.2, background mode): `-Blender`, else `$env:BLENDER`, else `C:\Program Files\Blender Foundation\Blender
  5.2\blender.exe`; missing, the script prints one `FAIL: Blender not found at ...` line and exits 2. Sources: `-SourceRoot`,
  else `<repo>/asset-sources`, else (in a git worktree) the main checkout's. Every licence file the manifest lists is read at
  each run and must still say CC0, or the run stops.
- **The manifest** (`tools/asset-manifest.json`) per model: `id`, `destination` (`game/assets/models/<data model>/<id>.glb`),
  `maxTriangles`, `textureSize`, `yawDegrees` (the rotation fix), `materials` (source material -> role: `outfit`, `skin`,
  `head`), `tints` (per role: a palette colour and how far toward it), `parts` (source glTF, mesh name, triangle share,
  `team`, and for the base character `keepBones`: only the vertices weighted to `Head` / `neck_01` are kept, the pack's
  Readme), `props` (boxes bound to a bone, placed by a clip's pose: the Crossbowman's crossbow sits in `hand_r` aimed forward at
  30 % of the shot clip) and `clips` (docs/04's state name -> a library clip name; since M6-V21, before it the source clip),
  plus `library.clips` (library name -> `UAL1/<clip>` or `UAL2/<clip>`, one name one clip).
- **`tools/art/build_unit.py`** per model: imports the parts onto the outfit's 65-bone armature, cuts the head, decimates each
  part to its share (Decimate collapse, before the armature), merges the parts' UV maps into one, rebuilds each role's
  material with its base colour texture only (no normal / roughness maps: invisible at 20-60 m), scaled to `textureSize` and
  tinted, builds the props (at most 200 triangles), joins everything into one skinned mesh, puts the yaw on the armature
  node (glTF faces +Z; the bone tracks stay untouched), and exports one `.glb` without animations; it fails over
  `maxTriangles` or without a `team` material. The library: the `library.clips` from the non-root-motion
  UAL1 / UAL2 `.glb` files, renamed `<name>_loop` (Godot's import rule for a looping clip; the suffix is dropped), on one
  armature with a one-triangle skin (so the tracks import as `Skeleton3D` bone tracks), one NLA track per clip, without
  the bones the manifest's `library.dropBonePrefixes` names (the 40 finger bones, M6-VH7; "Animation setup" below).
- **Godot import settings** (written by the script; each `.import` keeps its uid, so a re-run gives the same files): a model
  `.glb` imports with no animations and no tangents, its textures extracted beside it (`<id>_<role>.png`; since M6-V24 one,
  `<id>_atlas.png`, and a texture of an earlier build the model no longer holds is deleted with its `.import`), each set to
  VRAM compressed with mipmaps (M6-V24: alpha border fix off); the library `.glb` imports as an `AnimationLibrary`
  (`importer="animation_library"`).
- **The run prints** each model's triangles, height, feet, materials and texture sizes (M6-V24: its atlas layout), every
  generated file with its size (M6-V24: and the first 16 hex digits of its SHA-256), the library's clip map, and `IMPORT
  ASSETS PASS`. Two runs give byte-identical files (M6-V20's check; M6-V24: the 23 binaries' hashes equal over two runs).
- **Budget as built:** Laborer 2,597 triangles, Crossbowman 2,727 (the crossbow 48), four 512² textures each, library 6 clips;
  all generated binaries about 4.5 MB (about 4.2 MB since M6-VH7 dropped the finger tracks from the library).
- **Second slice (M6-V21): six model types.** The Producer's default (the owner may revisit): the two outfits on disk
  recoloured, props as boxes.

  | Type | Outfit parts (team parts in bold) | Outfit tint | Props | Triangles |
  | --- | --- | --- | --- | --- |
  | Malazan Heavy Infantry | Ranger **body**, **pauldron**, arms, bracers, legs, boots; bare head | oxblood `#6a3430` 0.6 | big rectangular shield (0.50 x 0.86 m, **team**, on `spine_03`), short sword (`hand_r`) and box helmet (`Head`), both steel `#6c7178` | 2,507 |
  | Whirlwind Camp Follower | Peasant **body**, arms, legs, feet (the Laborer's mesh) | bleached sand `#d8cba8` 0.55 | none | 2,597 |
  | Whirlwind Raider | Ranger **body**, **hood**, arms, legs, boots | ochre `#b08a3e` 0.6 | curved blade (four boxes, the last two turned up 18° / 40°; `hand_r`), steel `#b4b6b8` | 2,547 |
  | Whirlwind Desert Archer | Ranger **body**, **hood**, **pauldron**, arms, legs, boots (the Crossbowman's mesh) | sand `#ddd3b8` 0.6 | bow (a grip, two limbs turned back 18°, a string; `hand_r`), wood `#5a3d22` | 2,727 |

  Each has four 512² textures (head, outfit, skin, team) and at most five surfaces (props of one colour share one material:
  the Heavy Infantry's sword and helmet are one `steel` surface). The models are 1.82-1.88 m tall (the helmet), feet at the
  origin, 65 bones. The generated binaries are 11.8 MB (six models with their textures, and the library).
- **Manifest additions (M6-V21).** A box may carry `rotation` (Euler XYZ degrees about its own centre), so a curved blade or a
  bow's limbs are a few turned boxes. Props that name one `material` share it (and must name one `colour`). A prop whose
  `material` is `team` joins the team surface: all its UVs sample one point of the team texture, the centre of the most even
  32 x 32 texel square whose mean is nearest `teamShade` (`build_unit.py` `team_uv`), so the shield is drawn in the player
  colour at about the brightness of the rest of the team part, with no extra surface or draw call. **Clip maps:** a model's
  `clips` maps docs/04's state names to library clip names, and `library.clips` maps each library name to its source clip
  (`UAL1/<clip>` or `UAL2/<clip>`); the script fails on a map naming a clip the library lacks, or a library clip no model
  plays. The builder writes the model's map onto its armature node as glTF extras (`{"clips": {...}}`, the only extras in the
  file); Godot imports them as the node's `extras` metadata, and `UnitModels` gives each model type its own
  `AnimationLibrary` of the shared clips under the state names (docs/03 "Implementation (M6-V21)").
- **Third slice (M7-V5): the five Teblor humanoids** (eleven model types). The two outfits on disk recoloured to the Teblor
  palette, the pack's long hair, props as boxes in bloodwood. Each file holds the **base figure** (1.84-1.85 m, as every
  other model); the view draws it at the data's `scale` (1.5: 2.76-2.78 m), uniformly, feet on the ground, and the rings
  keep the data radius (docs/03 "Implementation (M7-V5)"). No size is baked into a file or typed in C#.

  | Type | Outfit parts (team parts in bold) | Outfit tint | Props (bloodwood `#5e3324`, sRGB) | Triangles |
  | --- | --- | --- | --- | --- |
  | Teblor Gatherer | Peasant **body**, arms, legs, feet; long hair | earth brown `#6b5038` 0.55 | a wood-cutting tool (a 0.8 m haft and a head; `hand_r`, placed by `TreeChopping_Loop`'s first frame) | 2,724 |
  | Teblor Warrior | Ranger **body**, **pauldron**, arms, legs, boots; long hair | bloodwood red-brown `#7a3b2a` 0.6 | a huge wooden greatsword (grip, guard, a 1.18 x 0.18 m blade; `hand_r`, `Sword_Attack`'s first frame) | 2,765 |
  | Teblor Javelin Thrower | Ranger **body**, arms, legs, boots; long hair | furs grey-brown `#8a7660` 0.6, brightness 1.2 | a 1.5 m javelin in `hand_r` (`OverhandThrow` at 30 %) and three javelins on the back (`spine_03`) | 2,609 |
  | Teblor Shaman | Peasant **body**, arms, legs, feet; long hair | bone white `#d6cbb3` 0.7, brightness 1.8 | a 1.7 m staff with a knob (`hand_r`, held upright at `Idle_Loop`'s first frame) | 2,736 |
  | Teblor Breaker | Ranger **body**, **pauldron**, arms, bracers, legs, boots; long hair | dark earth `#4f3a2a` 0.65, brightness 0.8 | a great maul (a 1 m haft, a 0.24 x 0.26 x 0.40 m head; `hand_r`, `Sword_Attack`'s first frame) | 2,802 |

  Each has five 512² textures (hair, head, outfit, skin, team) and six surfaces (the five roles and the props' one
  `bloodwood` material), 65 bones, one `team` surface. **What stands in (developer's default, owner may revisit):** hair:
  the Universal Base Characters' `Hair_Long` (rigged to `Head`), decimated to 400-450 triangles and tinted near-black brown
  (`#2a1d15`, brightness 0.4); furs: none on disk, so the outfits' tints carry the palette (the Javelin Thrower's grey-brown
  "furs", the Shaman's bone white) and the long hair the silhouette; weapons: boxes, as M6-V21's. The Javelin Thrower's hand
  javelin stands above the head in the rest pose, so a model's height in the tests is measured without the props' own
  surfaces. Sizes (Git LFS): the five models with their textures 12,364,925 bytes (11.8 MB); the library 312,660 bytes
  (267,832 before its two new clips); all the art pass's generated binaries 24,767,612 bytes (23.6 MB).
- **Manifest additions (M7-V5)**, each opt-in, so the six earlier entries build byte-identically: a part's `alignBone`
  (`Head`) moves the part by the difference between that bone in its own armature and in the model's (the pack's
  `Hair_Long` is rigged on an armature whose `Head` is 5 cm lower than the male base character's); a tint's `brightness`
  scales the tinted texture (a tint alone keeps each texel's brightness, so light hair stayed light and a dark outfit
  dark); a model's `propColourSpace: "srgb"` names its prop colours as they show on screen (the builder converts them to the
  linear base colour glTF holds). Without it a prop's hex is written as the linear value itself, as M6-V20 / V21 built:
  those props show lighter than their hex (the crossbow's `#4a3626` draws about `#937f6c`); left as it is (re-cutting
  them is BUG-1411's).
- **One-shot clips (M6-VH9, BUG-1601).** A model may carry `oneShot` beside its `clips`: a state name -> the share of that
  clip's length at which the figure releases its shot (the Sapper: `"oneShot": { "attack": 0.28 }`, its throwing hand's
  fastest frame in `OverhandThrow`, measured on the skeleton by `ViewHardenVh9Test`). The builder checks the state is in the
  clip map and the share is in (0, 1), and writes the marks into the armature's extras beside the clip map (`{"oneShot":
  {...}}`) only for a model that has any, so every other file is byte-identical. The view then plays that clip once per
  attack with its release on the shot leaving, and holds the clip's first frame (the ready pose) between shots (docs/03
  "Implementation (M6-VH9)"); an unmarked clip loops from the crowd phase as before.
- **Stale extracted textures (fixed in M7-V5).** Godot re-extracts a model's texture beside it only when the `.png.import`
  names the md5 Godot wrote (`generator_parameters`), and the script's `.import` rewrite dropped it, so a changed tint never
  reached the game (no texture had changed since M6-V20, so nothing showed it). The script now gives each rebuilt model's
  texture imports a placeholder md5 before the Godot import: every texture is extracted again (the same bytes when nothing
  changed: the six earlier models' files are byte-identical after a run) and keeps its uid.
- **One surface, one material, one atlas (M6-V24).** After the join, `build_unit.py` (`build_atlas`) bakes the model's role
  textures and its props' flat colours into one texture and gives the mesh one material, `team`; the M6-V20 to M7-V5
  surfaces above (four to six a model, each a draw call and a shadow-pass draw call) are now one. Nothing in the manifest
  changed: `textureSize` is the tile size.
  - **Layout:** tiles of `textureSize` (512) in the order team, outfit, skin, head, hair, then the props' colours, row by row
    from the top-left, on the fewest columns that make a near-square grid: 2 x 2 (1024 x 1024) for the Laborer and the Camp
    Follower (four roles, no prop), 3 x 2 (1536 x 1024) for the other nine (five or six tiles; an unused sixth tile is black,
    alpha 1). The layout rides in the file as glTF extras beside the clip map (`{"atlas": {"grid": [c, r], "tile": 512,
    "tiles": {"team": [0, 0], ...}}}`); `UnitModels.AtlasOf(type)` reads it.
  - **UVs:** each face's UVs move into its role's tile (`(column + u) / columns`), kept half a texel inside it (the
    decimation leaves 4 hair triangles a Teblor model 0.15 texel under 0, which would sit on a tile's edge); a prop face of
    its own colour samples the centre of its colour's block in the props' tile (one colour: the whole tile, so it is the same
    colour at every mip level); a team prop (the shield) samples the team tile as before. The builder fails on a UV more
    than half a texel outside its texture (it would read the next tile).
  - **The mask** is the atlas alpha: 0.5 marks a team texel, 0.75 a prop texel (drawn at roughness 0.8 as the props' own
    materials were), 1 any other (roughness 0.9). No texel is transparent, so an importer's alpha handling never touches the
    colours; the script also turns Godot's `process/fix_alpha_border` off for the extracted texture. Why the alpha and not a
    second texture: one texture fetch, one file, and the mask travels inside the `.glb` with the colours it marks.
  - **The shader** keeps each sample inside its tile (half a texel of the mip level read), so filtering never carries a team
    texel onto a skin or prop texel and no seam shows at the far zooms. Culling and lighting are the old materials': every
    face double-sided except a team texel's (culled as the team surface was), Burley diffuse (docs/03 "Implementation
    (M6-V24)").
  - **Sizes:** the imported texture is VRAM-compressed with alpha (DXT5, 1 byte a texel, where the role textures were DXT1):
    about 2.1 MB of video memory a 3 x 2 model and 1.4 MB a 2 x 2 one with mipmaps, 21.7 MB for the eleven (8.6 MB before:
    49 DXT1 512² textures). The prop colours are 565-quantized by the compression (the Teblor bloodwood `#5e3324` reads `#5a3121`), within
    the role textures' own compression error. The art pass's generated binaries (the eleven models with their textures and
    the library): **28,828,257 bytes (27.5 MB), was 24,767,612 (23.6 MB)**: the `.glb`s 15,864,564 (was 13,875,584), the
    extracted textures 12,963,693 (was 10,892,028); the atlas holds every texel the role textures did, plus an alpha channel
    and (nine models) one unused tile.
- **Fourth slice (M6-V25): the last four foot units** (fifteen model types). The two outfits on disk recoloured to the
  Malazan and Whirlwind palettes, built straight into the M6-V24 atlas (one surface, one `team` material, a 3 x 2 grid:
  team, outfit, skin / head, props, one unused tile), props as boxes with `propColourSpace: "srgb"` (their hex is the
  colour on screen). After it the capsules are the six types with no source on disk: the Wickan Lancer, the Horse Raider,
  the Destrier Rider, the War-dog, the Catapult and the Battering Ram (the owner's packs).

  | Type | Outfit parts (team parts in bold) | Outfit tint | Props (sRGB) | Triangles |
  | --- | --- | --- | --- | --- |
  | Malazan Cadre Mage | Ranger **body**, **hood**, arms, legs, boots | dull bronze `#7a6545` 0.6 | an oxblood robe `#4e2a26` (a waist box and four panels flared 8° outward, on `pelvis`), a 1.75 m staff `#4a3626` with a bronze head `#a07c3c` (`hand_r`, upright at `Idle_Loop`'s first frame) | 2,494 |
  | Malazan Sapper | Ranger **body**, arms, bracers, legs, boots; bare head | leather `#6e5a3c` 0.6 | satchels `#4f3b26`: a 0.40 x 0.19 x 0.50 m pack on the back (`spine_03`, its top over the shoulders) with a strap across the chest, a bag on each hip (`pelvis`); a clay munition `#b0703a` in `hand_r` (`OverhandThrow` at 30 %) | 2,339 |
  | Priest of the Whirlwind | Ranger **body**, **hood**, arms, legs, boots | bleached `#e2d9c2` 0.65, brightness 1.4 | a bleached white robe `#d9d0bc` (the Mage's robe shape), a crimson veil `#8e1f24` over the lower face (`Head`) | 2,470 |
  | Whirlwind Zealot | Peasant **body**, arms, legs, feet; bare head | crimson `#8a2a26` 0.6 | two curved blades `#b4b6b8` (the Raider's blade in `hand_r` and `hand_l`, `Sword_Attack`'s first frame), a crimson head-wrap `#7e1d1d` with a tail (`Head`) | 2,568 |

  **What tells them apart** (developer's read of the pictures, owner may revisit): the Mage is the only Malazan figure with
  a long robe and a staff over its head; the Sapper the only one with a pack standing over its shoulders and bags on its
  hips (the Laborer is bare, the Crossbowman hooded with a crossbow, the Heavy Infantry has the big team shield and helmet);
  the Priest the only Whirlwind figure in a long white robe (since M6-VH9 a sand robe, below), its hood in the team colour
  and a red veil; the Zealot the only one with a red head-wrap and a blade in each hand (the Raider is hooded with one blade,
  the Camp Follower bare).
  **Stand-ins (developer's default):** no robe, veil, satchel or turban mesh is on disk, so each is boxes bound to one bone
  (the robe to the pelvis: in a stride the legs show through its front and sides, which at 20-60 m reads as a split
  robe). The models are 1.82-1.90 m tall (the hood, the head-wrap), feet at the origin, 65 bones. Sizes (Git LFS): the four
  `.glb` 5,242,232 bytes and atlases 4,371,895 (9,614,127, 9.2 MB); all the art pass's generated binaries **38,442,384 bytes
  (36.7 MB), was 28,828,257 (27.5 MB)**. A full run leaves the eleven earlier models and the library byte-identical.

  **Re-tint in the setup's colours (M6-VH9, BUG-1600; developer's pick, owner may revisit).** The rule (style guide: "team
  colors are the only strong accents"): no non-team tint or prop colour of a figure lies within 60 (RGB 0-255 a channel,
  Euclidean) of a player colour it must read against, checked by `ViewHardenVh9Test` row (C) from this manifest and
  `ui.json`, which also prints every model type against all eight setup colours. The Priest's team parts (hood, jerkin) took
  the Bone colour on a bleached outfit and robe 19-22 away from it, so a Bone Priest showed no team colour; the Priest's veil
  and the Zealot's wrap and trousers were strong reds 45-62 from Crimson. Producer default: the reds go darker (maroon,
  rust), not away from red.

  | Type | Was | Now | Nearest player colour now |
  | --- | --- | --- | --- |
  | Priest of the Whirlwind | outfit tint bleached `#e2d9c2` 0.65, brightness 1.4; robe `#d9d0bc`; veil crimson `#8e1f24` | outfit tint weathered sand `#a39577` 0.65 (brightness 1); robe the same sand `#a39577`; veil maroon `#5e1a20` | Bone 104 (outfit, robe), Crimson 93 (veil) |
  | Whirlwind Zealot | outfit tint crimson `#8a2a26` 0.6; head-wrap `#7e1d1d` | outfit tint rust `#6e3a26` 0.6; head-wrap maroon `#5a1c1e` | Crimson 78 (outfit), 97 (wrap) |

  So the Priest reads as a long **sand** robe (no longer white) with its hood and jerkin in the player colour and a dark veil;
  the Zealot as a team tunic over rust trousers with a dark red wrap. The table's other pairs under 60 are left for the
  owner (`ViewHardenVh9Test`'s printout): the Camp Follower's, Desert Archer's and Teblor Shaman's light outfits 13-16 from
  Bone (their team part is the whole jerkin or tunic, not a hood inside a robe), the steel blades 56 from Bone, the Raider's
  ochre outfit 28, the Mage's bronze staff head 44 and the Sapper's clay munition 36 from Ochre, and the dark leathers and
  woods 43-59 from Iron. Only the two figures' files changed (the eleven others, the Sapper's atlas and the
  library are byte-identical over two runs). Sizes: the Priest `.glb` 1,318,860 and atlas 1,099,600 bytes (were 1,362,152 /
  1,142,890), the Zealot 1,219,256 / 1,014,341 (were 1,226,548 / 1,021,633), the Sapper `.glb` 1,326,368 (was 1,326,344:
  its one-shot mark below); all the art pass's generated binaries **38,856,086 bytes (37.1 MB), was 38,957,228**.

  **The figures as built are the art pass's first reading of the faction pages (BUG-1602).** Where they differ from the
  pages' "Art notes" it is the stand-in, not a change of design; revisit with the owner's packs:
  - **Priest of the Whirlwind** (`whirlwind.md`: "tall headdresses with a staff trailing ribbons"): a hood (the outfit's,
    team colour), a flat veil and a box robe; no headdress or staff (none on disk; boxes on the head bone read as a hat, not
    a headdress).
  - **Zealot** (`whirlwind.md`: "bare-chested, red-painted, twin knives"): clothed in the Peasant outfit (its tunic is the
    team part; a bare chest would leave no team colour on the figure), a head-wrap, and two copies of the Raider's 0.7 m
    curved blade rather than knives (a knife is 2-3 px at 40 m).
  - **Sapper** (`malazan.md`: "leather aprons with munition satchels and a crouched stance"): the satchels are built; no
    apron (an apron box hides the legs' stride) and the idle stance is the library's upright one (no crouched idle clip).
  - The Cadre Mage matches its notes ("hooded robes, no armor").
- The view: `UnitViews` draws a type whose file loads as its model, any other type as its capsule (docs/03 "Implementation
  (M6-V20)").

**Building models, as built (M6-V26).** The first two buildings, the Malazan Garrison Keep and Barracks, are models from
the Ultimate Fantasy RTS pack (CC0, already on disk); every other building type is still its box.

- **The manifest:** `tools/asset-manifest.json` `buildings`, one entry per building: `id` (the data's building id),
  `destination` (`game/assets/models/<faction id>/<building id>/<building id>.glb`: the view finds the file by the id, no
  `model` field in `buildings.json`, docs/01 2026-11-18 (b) (e)), `source` (one pack glTF, relative to `asset-sources/`),
  `maxTriangles` (docs/04's 5,000), `tileSize` (texels a side of each colour tile), `yawDegrees` (0: the pack's entrance
  already faces +Z, toward the match camera), `viewElevationDegrees` (the lowest camera elevation the hidden-face pass
  allows for, 15), `decimate` (source material -> triangles after a collapse decimation of that material's faces),
  `team` (the source materials that take the player colour), `teamShade` (the team tiles' grey, 0.8 as a unit's team
  texture's mean) and `colours` (every other source material -> its flat colour: the atlas texel, sRGB; lit by the sun it shows lighter). The pack's
  `License.txt` joined the manifest's `licences`, read at each import.
- **The builder:** `tools/art/build_building.py`, run by `tools/import-assets.ps1` after the unit models (`-Only <building
  id>` builds one). It imports the source's one mesh, merges its split corners and triangulates, **drops the faces no camera
  above the ground can see** (from each face's centre and three points near its corners, every direction toward the sky
  between 15 and 90 degrees of elevation on the face's front side is blocked by the model itself: undersides, the backs of
  blocks set against a wall, faces inside other parts; the camera only ever looks down at 35-55 degrees and buildings never
  turn), decimates the `decimate` materials, moves the origin to the centre of the ground plan at its lowest point, turns it
  by the yaw, shades it flat (the pack's low-poly look), and bakes one atlas: a square grid (a power of two a side) of
  `tileSize` flat-colour tiles, one per source material (the team materials share one `team` tile); each face's three UVs sit
  on its tile's centre, so it reads one colour at every mip level. The mesh has one surface and one material named `team`
  drawing the atlas, whose **alpha is the team mask** (0.5 on the team tile, 1 elsewhere), exactly as a unit model's since
  M6-V24, and the layout rides in the file as glTF extras on the mesh node (`{"atlas": {"grid": [4, 4], "tile": 32, "tiles":
  {...}}}`). So `team_colour.gdshader` draws a building unchanged: a team texel as `player colour x shade x 1.25`, any other as
  its own colour. Godot extracts the atlas beside the model as `<id>_atlas.png` and imports it with the units' texture
  settings (VRAM compressed, mipmaps, alpha border fix off).
- **The pick** (developer's, owner may revisit): the pack's TownCenter models are a fountain and a statue plaza (First and
  Second Age), not a keep, so both buildings come from the Barracks line, which is a stone hall with a tiled roof:
  **Garrison Keep = `Barracks_SecondAge_Level3`** (the hall with three crenellated stone towers: the fortress of the two) and
  **Barracks = `Barracks_SecondAge_Level1`** (the same hall without towers: smaller, lower, plainly a barracks with its
  crossed swords over the door). The First Age barracks are bare wood with small red banners as their only team part, which
  at 40 m would be 2-3 pixels of colour; the Second Age roofs are large, face the camera, and are one material (`Main`).
- **The team parts:** the tiled roof (`Main`) on both. **The tint** toward the Malazan palette (steel grey, oxblood, dull
  bronze), muted: the stone bodies, stairs and bases `#6e7176` (the pack's `Stone`), the crenellations and quoins
  `#8f9195` (`Stone_Light`), the stair walls `#7e8084` (`Walls`), timber and the tower tops a dark oxblood-brown `#4d3a30`
  (`Wood`) and `#5c4334` (`Wood_Light`), the swords' hilts dull bronze `#7a6440` (`Gold`), guards `#4a4d52`
  (`Metal_Light`) and blades `#9a9da1` (`White`).

  | Building | Source | Triangles (source, after the hidden faces, built) | Size as built (pack units; width x depth x height) | Fitted (footprint) |
  | --- | --- | --- | --- | --- |
  | Garrison Keep | `glTF/Barracks_SecondAge_Level3.gltf` | 8,352, 5,516, **4,940** (the roof's 996 decimated to 420) | 1.711 x 1.643 x 1.846 | 7.5 x 7.2 x 8.1 m (8 x 8 m) |
  | Barracks | `glTF/Barracks_SecondAge_Level1.gltf` | 3,104, 2,158, **2,158** | 1.010 x 1.407 x 0.893 | 3.9 x 5.5 x 3.5 m (6 x 6 m) |

  Each has one 128 x 128 atlas (a 4 x 4 grid of 32-texel tiles: team and eight colours, seven unused tiles black).
- **The fit** (docs/04 "Scale and dimensions", in `BuildingModels`, not in the file): one uniform scale, the largest that
  keeps the ground plan `FitMargin` (0.25 m) inside every edge of the footprint (cells x 2 m from the data), the plan
  centred on the footprint's centre, its lowest point on the ground at that centre (as the box's base). The file keeps the
  pack's size; nothing about a footprint is baked into it.
- **Facing and the data `scale` (docs/01 2026-11-20 (2), answers 5 and 6 of docs/03 "Implementation (M6-V26)").** A
  building's front (its door) faces **+Z**, toward the match camera, and never turns: a building file keeps the pack's
  +Z entrance (`yawDegrees` 0), unlike a unit's -Z forward. The data `scale` (the Teblor buildings' 1.25) applies to a
  model's **height only**, as it does to a box since M7-V4: the fit's uniform scale for the plan, times `scale` on the
  vertical axis, so a scaled model stays inside its footprint and stands taller. No building with a model has a `scale`
  other than 1 yet, so `BuildingModels` does not apply it today; the first scaled building model (a Teblor one) brings
  the code with it. The drawn top (`BuildingModels.Top`) is what picking and the bar read either way (docs/03
  "Implementation (M6-VH9)").
- **Sizes (Git LFS):** the two `.glb` 348,712 + 164,296 bytes and the two atlases 918 + 918: **514,844 bytes (0.49 MB)**
  for the building art; all the art pass's generated binaries **38,957,228 bytes (37.2 MB), was 38,442,384 (36.7 MB)**.
- **The view:** `BuildingViews` draws a finished building whose type's file loads as its model, any other (and every site)
  as its box (docs/03 "Implementation (M6-V26)").

**Audio (SFX, M6-V12).** Sources are long, mostly 96 kHz / 24-bit stereo files; only short cuts enter the repo.

1. The pack is already extracted under `asset-sources/` (the Sonniss bundle: `asset-sources/sonniss-gdc-2026/part1-5/`).
   Find a cut: `powershell -File tools/audio/trim-wav.ps1 -Info <source.wav> [-WindowMs 25]` prints the header and a peak
   envelope (dBFS per window) to read where the sound starts and fades.
2. Add a row to `tools/audio/sfx-manifest.json`: `event` (an `SfxEvent` name), `source` (relative to `asset-sources/`),
   `destination` (`game/assets/audio/sfx/<category>/<event>_<n>.wav`, n = 1-3, category `ui`, `combat` or `building` as
   `Sfx`'s clip table says), `start` and `length` in seconds, `normalize` (peak to -1 dBFS) and `fadeMs` (in and out,
   default 5, so a cut mid-waveform doesn't click).
3. Run `powershell -File tools/audio/trim-wav.ps1` (no install: PowerShell and .NET): it rebuilds **every** clip from the
   manifest, byte for byte the same each run (in a git worktree it reads the main checkout's `asset-sources/`; or
   `-SourceRoot <dir>`). It copies whole sample frames (block align) from the source's data chunk under a fresh header
   (PCM 8 / 16 / 24 / 32-bit or 32-bit float, the source's rate and channels; no decoding to another format, no
   resampling), and fails the run for a clip over 3 s or 2 MB, more than 20 clips, or a destination off the pattern.
4. `& $env:GODOT --headless --path game --import`, then set each new clip's `.import` to `force/mono=true`,
   `force/max_rate=true`, `force/max_rate_hz=44100`, `compress/mode=0` (16-bit PCM) and import again; commit the `.wav`
   (LFS, `.gitattributes`) and its `.import`. `SfxTest` checks every clip loads mono, 16-bit, at most 44.1 kHz, at most 3 s,
   and that every `.wav` is in the manifest.
5. Licensing log: one row per source pack naming the files cut from it, in the same commit. Never commit a source or an
   uncut file.

Music: `.ogg` files dropped in `game/assets/audio/music/` play on the `Music` bus in name order, looping the list
(`MusicPlayer`); none is shipped.

### Look test (M6-V10, 2026-10-17)

`game/tests/LookTest.tscn` puts the owner's humanoid stack beside a stylized stand-in and today's
capsule on flat level-0 ground under the real `RtsCamera` (55° pitch, default zoom 40 m), 3 m apart.
Screenshots: [default zoom](images/look-test-2026-10-17.png), [max zoom-out, 60 m](images/look-test-2026-10-17-far.png).
The files were copied by hand into `game/assets/models/looktest/` (11 files, 22.1 MB) under their
pack names; the asset manifest and `tools/import-assets.ps1` (steps 2-3 above) land with the art pass,
which also renames to snake_case. Findings:

- **Universal Base Character (Superhero_Male_FullBody, the only male in the Standard edition):**
  14,318 triangles (body 12,566, hair/brows 984, eyes 768), about 4.8× the 3k unit budget, so the
  art pass must decimate. Textures: body base colour, normal and roughness at 2048², hair base colour
  and normal at 2048², eyes 256². At RTS zoom the 2048² detail is invisible; 512² would do. The
  glTF names two textures that aren't in the pack under those names (`T_Hair_1_Normal_png.png`,
  `T_Eye_Normal_png.png`); they were copied from `T_Hair_1_Normal.png` / `T_Eye_Normal.png` under the
  names the glTF expects. Godot imports the glTF's textures as lossless 2D without mipmaps; their
  `.import` files were set to VRAM compressed + mipmaps (normal maps flagged), which the art pass's
  import script should do too.
- **Scale and facing:** 1.82 m tall as imported (the 1.8 m rule holds with no scale). It faces +Z
  (glTF's convention, toward the camera), not this doc's -Z: the manifest's rotation fix applies.
- **Animation:** the UAL1 clips drive the character directly. Both use the same 65-bone rig with the
  same bone names, and Godot puts both skeletons at `Armature/Skeleton3D`, so the library's track paths
  resolve with no BoneMap retarget (the pack's `Godot_Setup.png` BoneMap / humanoid profile is needed
  only to retarget other rigs, e.g. Mixamo). Godot's importer reads the `_Loop` suffix as the loop flag
  and drops it: the clips are `Idle`, `Walk`, `Sword_Attack`, `Death01`, ... (43 in UAL1 Standard).
  `Walk` is in place in the non-`_RM` file.
- **Team colour:** no slot to recolour. The character's materials are `MI_Hair_1`, `MI_Eyes` and one
  body material `MI_Superhero_Male`; the `team` slot has to come from an outfit material in the art pass.
- **Outfits not used:** a Modular Outfit (e.g. `Male_Ranger`) would attach without Blender (same
  65-bone skeleton), but each outfit ships three 4096² textures (about 30 MB) plus its own 2048² body
  set (about 8.5 MB), which breaks the look test's ~40 MB budget. Downsample them in the art pass.
- **Stylized stand-in:** Ultimate Fantasy RTS has no characters (buildings and nature only), so the
  stand-in is `Barracks_FirstAge_Level1.fbx`. Godot 4.7 imports the FBX natively with no errors:
  1,466 triangles, eight colour-only materials, no textures. The pack is authored about 1 m across,
  so the test scales it ×5.57 to the barracks' 3-cell footprint (a manifest scale for the whole pack).
- **The read (dev's):** at the default zoom the character is about 24 px tall, the capsule 27 px.
  It reads as a person with realistic proportions, the idle pose is visible, and the image isn't
  soft. But the bare skin-tone body has little contrast on the sand-coloured ground and is thinner
  than the capsule, so it's harder to spot. At 60 m it's about 15 px, an orange speck: the silhouette
  alone won't identify a unit slot there. Clothing, team colour and the selection rings will have to
  carry it. The stylized barracks reads well at both zooms (chunky shapes, flat colours) and doesn't
  clash with the realistic figure at this distance.
- **A clone without the LFS objects (M6-VH3, BUG-0860):** these files are Git LFS objects. On a machine
  that cloned without `git lfs pull`, a model is a pointer file whose import fails; `LookTest` then prints
  `LookTest: FAIL could not load <path> (missing, or an LFS pointer: run git lfs pull and re-import)` and
  exits 1 (it used to hang). `--character <res path>` swaps the character. The release preset leaves
  `assets/models/looktest/` out (BUG-0861).

### Animation setup

- The Quaternius Universal characters and both Universal Animation Libraries share one humanoid
  rig, so one `AnimationLibrary` serves every humanoid. Mixamo clips (and any other humanoid)
  retarget onto it with Godot's `BoneMap` and humanoid skeleton profile at import; use the
  in-place versions of locomotion clips.
- Required clips per unit: `idle`, `walk` (or `run`), `attack` (per weapon type), `cast`
  (casters), `hit`, `death`, and for workers `gather_gold`, `gather_wood`, `build`.
- Loop flags and root motion settings are part of the import step (root motion off: the sim
  moves units).
- **As built (M6-V20):** `game/assets/animations/universal_humanoid.glb`, six looping clips: `idle` (UAL1 `Idle_Loop`),
  `walk` (UAL1 `Walk_Loop`, in place), `attack` (UAL1 `Pistol_Shoot`: UAL 1 and 2 Standard have no crossbow, bow or rifle
  clip; the one-armed shot aims the crossbow, and the Laborer uses the same clip, a jab with an empty hand, until a
  per-weapon `attack_<weapon>` clip lands), `gather_gold` (UAL2 `Farm_Harvest`: no pick swing in either library),
  `gather_wood` (UAL2 `TreeChopping_Loop`), `build` (UAL1 `Fixing_Kneeling`). `cast` (since M6-V23 the Casting state's slot,
  named `attack_cast`: see "The Teblor clips"), `hit` and `death` come with the slices
  that need them.
- **Per-weapon attack (M6-V21).** The library has seven clips: the six above and `attack_melee` (UAL1 `Sword_Attack`, 1.53 s:
  a one-handed slash with a lunge, about one loop per 1.5 s melee cooldown). Each model's clip map says what its `attack` is:
  the Heavy Infantry and the Raider play `attack_melee`; the Crossbowman, the Desert Archer and the two workers play `attack`
  (`Pistol_Shoot`). Candidates considered for the melee swing: UAL1 `Sword_Attack` (chosen), UAL2 `Sword_Regular_A` (0.43 s,
  needs its `_Rec` recovery to loop), `Sword_Regular_B` / `_C`, `Sword_Regular_Combo` (3.0 s) and `Sword_Heavy_Combo`
  (4.33 s: too long for one blow), `Melee_Hook` and UAL1 `Punch_Jab` / `Punch_Cross` (unarmed), `Shield_OneShot` (a shield
  bash). **No bow clip:** neither UAL1 nor UAL2 Standard (nor the UAL1 Pro edition on disk) has a bow, crossbow or rifle
  clip; `Spell_Simple_Shoot` (an open-hand cast) and `OverhandThrow` were considered and rejected, so the Desert Archer keeps
  `Pistol_Shoot`, its bow held upright in the extended hand. A real draw needs the Mixamo Pro Longbow pack retargeted
  (out of M6-V21's scope).
- **The Teblor clips (M7-V5).** The library has nine clips: the seven above, `attack_throw` (UAL2 `OverhandThrow`, 1.33 s:
  a one-armed overhand throw with a step) and `attack_cast` (UAL1 `Spell_Simple_Shoot`, 0.5 s: an open hand thrust forward,
  the libraries' one spell release). Every Teblor state maps to a clip:

  | Type | idle | walk | attack | gather_gold / gather_wood / build | Returning, Casting |
  | --- | --- | --- | --- | --- | --- |
  | Gatherer | `idle` | `walk` | `attack_melee` | `gather_gold` / `gather_wood` / `build` (the workers') | `idle` |
  | Warrior, Breaker | `idle` | `walk` | `attack_melee` | (never in those states) | `idle` |
  | Javelin Thrower | `idle` | `walk` | `attack_throw` | (never) | `idle` |
  | Shaman | `idle` | `walk` | `attack_cast` (its attack is a magic bolt) | (never) | Returning `idle`; Casting `attack_cast` since M6-V23 (Blood-oil Frenzy's 0.5 s cast) |

  The table lists what each type plays. A type's players **hold** more than that (BUG-1411 item 3, M6-VH8): a state its
  clip map does not name plays the library clip of the state's own name (the pre-M6-V21 rule), so every type, a soldier
  too, holds `gather_gold`, `gather_wood` and `build` (references to the shared clips, no copies) though only a worker is
  ever in those states. The overlays' line (docs/03 "Implementation (M6-VH8)") is measured over every clip a type holds,
  so today every model type's highest bone is a finger of the raised hand in `gather_wood`.

  Why: the Gatherer swings its tool, so its melee attack is the sword swing, not the Laborer's empty-handed `Pistol_Shoot`
  jab. `OverhandThrow` is the only throw in UAL1 / UAL2 (M6-V21 rejected it for the bow; a javelin is what it is for). For
  the cast, `Spell_Simple_Shoot` was chosen over `Spell_Simple_Idle_Loop` (2.1 s, hands held out, no release) and
  `Spell_Simple_Enter` / `_Exit` (0.54 / 0.44 s, a raise and a lower). The Breaker's maul swings with the one-handed
  `Sword_Attack` (no two-handed heavy blow in either library short enough for one hit: UAL2 `Sword_Heavy_Combo` is 4.33 s);
  BUG-1411's look at `Sword_Regular_A/B/C` would cover it too. The `Casting` state played `idle` for every model type, as
  M6-V20 set (`UnitViews.ClipFor`); since M6-V23 it plays `attack_cast` (the seventh state slot is named after it, so every
  type holds it by the state-name rule; a library without it plays `idle`). The other rows' Casting column reads `idle`
  for types that never cast; a caster among them would play `attack_cast` too.
- **The last foot units' clips (M6-V25).** No new library clip (nine, unchanged); every state each can be in maps to one:

  | Type | idle | walk | attack | Casting | Returning, worker states |
  | --- | --- | --- | --- | --- | --- |
  | Cadre Mage, Priest | `idle` | `walk` | `attack_cast` (a magic bolt) | `attack_cast` (Telas Fire's 0.8 s, Sandstorm's 1.2 s) | Returning `idle`; gather / build the workers' clips by state name (never entered) |
  | Sapper | `idle` | `walk` | `attack_throw` (its attack throws a munition) | `attack_throw` (the Cusser's 1.0 s throw: the map names it for the cast slot) | as above |
  | Zealot | `idle` | `walk` | `attack_melee` | (no ability: `attack_cast` by state name, never entered) | as above |

  The cast gesture follows the cast for all three (M6-V24's rule: from its first frame when the cast is seen, once, ending
  as the ability lands). The Sapper's cast slot is mapped to the throw (the manifest's `"attack_cast": "attack_throw"`), so
  its Cusser looks like its attack, not an open-hand spell.
- **No finger tracks (M6-VH7, BUG-1321).** The library carries 25 of the rig's 65 bones: the import deletes the 40 finger
  bones (`thumb_`, `index_`, `middle_`, `ring_`, `pinky_`, 01-03 and the `_04_leaf` ends, both hands; the manifest's
  `library.dropBonePrefixes`, checked against `dropBoneCount` 40) and their curves before export, so each clip has 75 tracks
  instead of 195. The models keep all 65 bones (their skins are bound to them); with no track the fingers stay at the rest
  pose, which a 1.8 m figure seen from 20-60 m doesn't show. The library went from 509,800 to 234,948 bytes. A future clip
  that needs a hand pose (a grip) would add those bones back for that rig only.
- **Animation rate (M6-VH7, view constants in `CrowdAnimator`).** Godot doesn't step the models' players itself (manual
  callback mode): each frame the 48 shown figures nearest the camera advance by the frame's time, every other shown figure
  advances once in 3 frames by all the time it waited (20 Hz at 60 FPS; spread over the frames by slot), hidden ones not at
  all. Clips keep their true speed; far figures move in coarser steps, which at 40-60 m doesn't read. Clips stay authored
  at the library's 30 fps.
- **Game speed (M6-V21, docs/01 2026-11-14 (d) (f)).** The figures' clips run at the game speed: each frame's advance is the
  frame's time times the runner's `GameSpeed` (2x on Fast, 0.75x on Slow), and a paused match (the pause menu, the
  match-over banner) advances nothing.

## Licensing log

Every third-party asset in the repo gets a row, added in the same commit that imports it.

| Asset / pack | Author | Source URL | License | Version / date | Used for |
| --- | --- | --- | --- | --- | --- |
| Quaternius – Universal Base Characters (Standard) | Quaternius | <https://quaternius.com/packs/universalbasecharacters.html> | CC0 1.0 (pack's `License_Standard.txt`) | Standard (free) edition, no version number; downloaded 2026-10-08 | Look test (M6-V10): `Superhero_Male_FullBody.gltf` + `.bin` + 7 textures in `game/assets/models/looktest/`, from `asset-sources/Universal Base Characters[Standard]/Universal Base Characters[Standard]/Base Characters/Godot - UE/`. **M6-V20:** the head of `SuperHero_Male` (the vertices weighted to `Head` / `neck_01`, decimated) and its base colour texture cut to 512² (`<id>_head.png`), inside `malazan_laborer.glb` and `malazan_crossbowman.glb` (`tools/asset-manifest.json`; the licence file is read at each import). **M6-V21:** the same head and texture inside `malazan_heavy_infantry.glb`, `whirlwind_camp_follower.glb`, `whirlwind_raider.glb` and `whirlwind_desert_archer.glb` (no new source file). **M7-V5:** the same head inside the five `game/assets/models/teblor/<id>/<id>.glb`, and a new file from the same pack and licence: `Hairstyles/Rigged to Head Bone/glTF (Godot -Unreal)/Hair_Long.gltf` (+ `.bin`, `T_Hair_2_BaseColor.png`), decimated, moved onto the male `Head`, its texture cut to 512² and darkened (`<id>_hair.png`) |
| Quaternius – Universal Animation Library 1 and 2 (Standard) | Quaternius | <https://quaternius.com/packs/universalanimationlibrary.html> | CC0 1.0 (each pack's `License.txt`) | UAL1 Standard and UAL2 Standard, no version numbers; downloaded 2026-10-08 | Look test (M6-V10): `UAL1_Standard.glb` (no root motion), from `asset-sources/Universal Animation Library[Standard]/Universal Animation Library[Standard]/Unreal-Godot/`. **M6-V20:** six clips re-exported into `game/assets/animations/universal_humanoid.glb`: `Idle_Loop`, `Walk_Loop`, `Pistol_Shoot`, `Fixing_Kneeling` from `UAL1_Standard.glb`, `Farm_Harvest`, `TreeChopping_Loop` from `asset-sources/Universal Animation Library 2[Standard]/Universal Animation Library 2[Standard]/Unreal-Godot/UAL2_Standard.glb`; the `Pistol_Shoot` pose also places the crossbow (licence files read at each import). **M6-V21:** `Sword_Attack` from the same `UAL1_Standard.glb` added to the library as `attack_melee` (seven clips); its first frame places the Heavy Infantry's sword and the Raider's blade, `Idle_Loop`'s the shield and helmet, `Pistol_Shoot`'s the bow (no new source file). **M7-V5:** `Spell_Simple_Shoot` from `UAL1_Standard.glb` as `attack_cast` and `OverhandThrow` from `UAL2_Standard.glb` as `attack_throw` (nine clips); `TreeChopping_Loop`, `Sword_Attack`, `OverhandThrow` and `Idle_Loop` poses place the Teblor props (no new source file) |
| Quaternius – Modular Character Outfits – Fantasy (Standard) | Quaternius | <https://quaternius.com/packs/modularcharacteroutfitsfantasy.html> | CC0 1.0 (pack's `License_Standard.txt`, read at each import by `tools/import-assets.ps1`) | Standard (free) edition, no version number; downloaded 2026-10-08 | M6-V20: from `asset-sources/Modular Character Outfits - Fantasy[Standard]/Modular Character Outfits - Fantasy[Standard]/Exports/glTF (Godot-Unreal)/Outfits/`: `Male_Peasant.gltf` (body, arms, legs, feet) in `game/assets/models/malazan/malazan_laborer/malazan_laborer.glb`; `Male_Ranger.gltf` (body, hood, pauldron, arms, legs, boots; not the bracers or belts) in `.../malazan_crossbowman/malazan_crossbowman.glb`; **M6-V21:** from the same two files (no new source file), `Male_Ranger.gltf` (body, pauldron, arms, **bracers**, legs, boots) in `.../malazan_heavy_infantry/malazan_heavy_infantry.glb`, (body, hood, arms, legs, boots) in `game/assets/models/whirlwind/whirlwind_raider/whirlwind_raider.glb`, (body, hood, pauldron, arms, legs, boots) in `.../whirlwind_desert_archer/whirlwind_desert_archer.glb`, and `Male_Peasant.gltf` (body, arms, legs, feet) in `.../whirlwind_camp_follower/whirlwind_camp_follower.glb`; **M7-V5:** from the same two files (no new source file), `Male_Peasant.gltf` (body, arms, legs, feet) in `game/assets/models/teblor/teblor_gatherer/` and `.../teblor_shaman/`, `Male_Ranger.gltf` (body, pauldron, arms, legs, boots) in `.../teblor_warrior/`, (body, arms, legs, boots) in `.../teblor_javelin_thrower/`, (body, pauldron, arms, bracers, legs, boots) in `.../teblor_breaker/`; decimated, textures (`T_Peasant_BaseColor`, `T_Ranger_BaseColor`, `T_Regular_Male_Dark_BaseColor`) cut from 4096² / 2048² to 512², tinted, and a grey team copy; the extracted `<id>_<role>.png` beside each model |
| Quaternius – Ultimate Fantasy RTS | Quaternius | <https://quaternius.com/packs/ultimatefantasyrts.html> | CC0 1.0 (pack's `License.txt`; its header says "Ultimate Platformer Pack", a slip in the pack) | Aug 2022 (folder `quaternius-ultimate-fantasy-rts-2022-08`) | Look test (M6-V10): `Barracks_FirstAge_Level1.fbx`, from `asset-sources/quaternius-ultimate-fantasy-rts-2022-08/Ultimate Fantasy RTS - Aug 2022/FBX/`. **M6-V26:** from `asset-sources/quaternius-ultimate-fantasy-rts-2022-08/Ultimate Fantasy RTS - Aug 2022/glTF/`, `Barracks_SecondAge_Level3.gltf` in `game/assets/models/malazan/malazan_garrison_keep/malazan_garrison_keep.glb` and `Barracks_SecondAge_Level1.gltf` in `game/assets/models/malazan/malazan_barracks/malazan_barracks.glb` (hidden faces dropped, the Keep's roof decimated, the pack's flat colours replaced by the Malazan palette and a team grey in one 128² atlas, `<id>_atlas.png` beside each; `tools/asset-manifest.json` `buildings`); the pack's `License.txt` is read at each import (`licences`) |
| Epic Stock Media – Board Game: Sound Set Kit for Tabletop and Digital Games (Sonniss GDC 2026) | Epic Stock Media | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `ui/select_1.wav` from `UIClick_UI Button Analog Vintage Double Click Neutral Dry Press 11_ESM_BG.wav`; `building/building_done_1.wav` from `GAMEBoard_Event Board Reset Organic Multiple Pieces Wood Small 02_ESM_BG.wav` |
| Cinematic Sound Design – UI Interaction Elements (Sonniss GDC 2026) | Cinematic Sound Design | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `ui/command_1.wav` from `Accept Boing Crunch.wav`; `ui/refused_1.wav` from `Deny Muted.wav`; `ui/ready_2.wav` from `Ting Coins.wav` |
| CB_Sounddesign – Applicable Sounds: Organic UI and Building Games SFX (Sonniss GDC 2026) | CB_Sounddesign | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `ui/ready_1.wav` from `UIMisc_Kalimba 3 Up_CB Sounddesign_APPlicable Sounds.wav` |
| Federico Soler – Effective Trailer Alarms Vol. 2 (Sonniss GDC 2026) | Federico Soler | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `ui/alert_1.wav` (the first 1.2 s) from `EffectiveTrailer_Alarms_Vol2_HalfNotes_003.wav` |
| David Dumais Audio – Melee Weapons Sound Effects Pack 2 (Sonniss GDC 2026) | David Dumais Audio | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `combat/melee_hit_1.wav` from `METLImpt_METAL SWING HIT Weapon Swing To Metallic Body Impact And Resonant Tail 01_DDUMAIS_MWP2.wav`; `combat/shot_1.wav` from `METLFric_SWING SCRAPE Swift Melee Weapon Swing With A Long Blade 14_DDUMAIS_MWP2.wav` |
| 344 Audio – Cinematic Fight Vol. 1 (Sonniss GDC 2026) | 344 Audio | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `combat/melee_hit_2.wav` and `combat/unit_death_2.wav` (two of its four punches) from `FGHTImpt_4 x Punch, Body 02_344 Audio_Cinematic Fight Vol 1.wav` |
| 344 Audio – Historical Weapons Vol. 2 (Sonniss GDC 2026) | 344 Audio | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `combat/melee_hit_3.wav` and `building/building_placed_1.wav` (two of its knocks) from `WEAPBlnt_Spear And Stick Impact, Wooden MKH 2_344 Audio_Medieval Weapons Vol 2.wav` |
| 344 Audio – Anime Fight Voices Vol. 1 (Sonniss GDC 2026) | 344 Audio | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `combat/unit_death_1.wav` from `VOXMale_Male Adventurer, Active Yell 19_344 Audio_Anime Fight Voices.wav` |
| Epic Stock Media – Fantasy Game 2: Sound Kit for Enchanted Realms (Sonniss GDC 2026) | Epic Stock Media | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `combat/cast_1.wav` from `MAGAngl_Magic Light Spell Enchantment Potion Effect Tonal Bright 03_ESM_FG2.wav` |
| Cinematic Sound Design – Colossal Impacts (Sonniss GDC 2026) | Cinematic Sound Design | <https://sonniss.com/gameaudiogdc> | Sonniss GDC Game Audio Bundle licence: royalty-free, commercial use, no attribution; raw files not redistributed or resold (bundle's `License - GDC Game Audio.pdf`, `Readme.txt`) | GDC 2026 bundle, downloaded 2026-10-08 (`asset-sources/sonniss-gdc-2026/`) | SFX (M6-V12): `building/building_razed_1.wav` from `Woosh Debris.wav` |
