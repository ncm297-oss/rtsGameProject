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

Models face **-Z** (Godot's forward), origin at the feet / ground center.

### Team color

Each unit and building model has one material slot named `team` (banners, shields, cloaks,
trim). A shared shader replaces that slot's color with the player color. Import tooling renames
the right material to `team` per the asset manifest.

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
  build complete, alert) so audio plumbing exists from M2.

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
sources and model edits. Still to download before M6: realistic ground textures (ambientCG /
Poly Haven, CC0), UI, fonts, audio, the Godot export templates.

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
| Kenney – RPG Audio, Impact Sounds, Interface Sounds | <https://kenney.nl/assets> | SFX: combat, building, UI |
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
| Teblor | Universal Base Character (male) at 1.5× + outfits | MPFB2 giant body; AI-generated |
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
    malazan/buildings/malazan_barracks.glb
    whirlwind/...
  animations/                        # shared AnimationLibrary resources (.tres) per rig
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

### Animation setup

- The Quaternius Universal characters and both Universal Animation Libraries share one humanoid
  rig, so one `AnimationLibrary` serves every humanoid. Mixamo clips (and any other humanoid)
  retarget onto it with Godot's `BoneMap` and humanoid skeleton profile at import; use the
  in-place versions of locomotion clips.
- Required clips per unit: `idle`, `walk` (or `run`), `attack` (per weapon type), `cast`
  (casters), `hit`, `death`, and for workers `gather_gold`, `gather_wood`, `build`.
- Loop flags and root motion settings are part of the import step (root motion off: the sim
  moves units).

## Licensing log

Every third-party asset in the repo gets a row, added in the same commit that imports it.

| Asset / pack | Author | Source URL | License | Version / date | Used for |
| --- | --- | --- | --- | --- | --- |

No third-party assets are in the repository yet. The first rows land with the first imported
pack in M6.
