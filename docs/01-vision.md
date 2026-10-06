# 01 — Vision

## Pitch

> A Malazan-inspired dark-fantasy RTS: five very different peoples gather, build, and out-fight
> each other in 20-30 minute skirmishes.

The player picks a faction, starts with a Town Hall and five workers, and plays a classic
StarCraft / Age of Empires match against one to three AI opponents: gather gold and wood, build a
base, climb from Age I to Age II, raise an army, and destroy every enemy building.

## Pillars

Every feature should serve at least one of these. If a feature serves none, it waits.

1. **Factions that feel like the books.** Each people plays differently in a way a reader would
   recognize: the Malazans' disciplined combined arms and reckless sappers, the Teblor's giant
   warriors, Shadow's ambushes, the Whirlwind's fanatic swarm, the Andii's few, ancient, elite
   soldiers. Differences come from a shared template plus targeted mechanics, not from five
   separate games.
2. **Readable at a glance.** Low-poly, flat-shaded art with strong silhouettes and team colors.
   A screenshot should tell you who is winning a fight. No visual noise that hides game state.
3. **Snappy controls.** Commands respond on the next tick (50 ms). Selection, control groups,
   queuing, and hotkeys behave the way StarCraft and Age of Empires players expect.
4. **Counters matter.** Damage types, armor classes, and abilities make composition and
   positioning more important than raw numbers. Every unit has something it beats and something
   that beats it.
5. **An AI that plays fair.** The AI sees only what its fog of war shows and issues the same
   commands a player can. Difficulty comes from better decisions, plus one transparent economic
   bonus on Hard.

## Tone

Grim, grounded military fantasy. Soldiers are tired professionals; armies are made of people who
die. Magic is warren-based and dangerous: a mage's spell is a rare, decisive event on the
battlefield, not constant fireworks. The visual style is low-poly but the palette is muted and
earthy, with team colors as the only saturated accents.

## Audience and success

The audience is the owner. The project succeeds when:

- there is a skirmish mode the owner enjoys playing against the AI, and
- the owner understands how the codebase works and can change it with Claude's help.

Publishing is optional. If it happens, it happens after the rename pass (see below).

## Scope caps

These are guardrails for a hobby project. Raising one is a deliberate decision recorded in the
table at the bottom of this page.

| Thing | Cap |
| --- | --- |
| Factions | 5 total; **2 in the vertical slice** (Malazan vs. Whirlwind), then one per milestone |
| Units per faction | 7 (6 template slots + 1 unique), plus a few summoned/temporary units |
| Buildings per faction | 10 slots (the plan said 9; a Camp drop-off was added, see [02](02-game-design.md#buildings)) |
| Players per match | 2-4 (1 human + 1-3 AI) |
| Maps | 3 hand-tuned + a seeded procedural generator |
| Units on the field | ~100 per player, ~300-400 total |
| Ages | 2 |
| Resources | 2 (Gold, Wood) |
| Match length | 20-30 minutes at Normal difficulty |

### Explicitly out of scope ("later, maybe")

- Campaign and scripted missions
- Hero units (the ability system makes them easy to add later)
- In-game map editor UI (the generator and data format come first)
- Walls and gates (pathfinding and gate logic are a milestone of their own)
- Multiplayer of any kind, including cross-machine determinism
- Naval units, garrisoning, trade, relics, diplomacy
- Mod support beyond "the data is JSON"

## Settled decisions

Decided during the planning session on 2026-10-02. To change one, edit this table (add the date
and reason) and update the affected docs in the same commit.

| Topic | Decision |
| --- | --- |
| Reference feel | StarCraft / Age of Empires: gather, build, army, fight |
| Platform | Windows desktop, native |
| Dimension | 3D, low-poly flat-shaded style; gameplay on a 2D plane with a heightmap |
| Players | Single-player vs AI opponents; multiplayer not planned |
| Engine | Godot 4.7.x (.NET build) + C# |
| Architecture | Pure .NET sim library (no Godot dependency) + Godot presentation layer |
| Ambition | Learning / hobby project: a skirmish mode you enjoy, a codebase you understand |
| Art sources | Free CC0 packs (KayKit, Kenney, Quaternius) + AI-generated 3D where packs fall short |
| Theme | Dark fantasy inspired by *The Malazan Book of the Fallen* |
| Factions | Five on a shared template: Malazan Empire, Tiste Andii, Shadow, the Whirlwind, Teblor |
| Vertical slice | Malazan vs. Whirlwind |
| Source control | GitHub (private) is the source of truth; Git LFS for binaries; work outside sync folders |
| Resources | 2: Gold and Wood |
| High ground | Yes: StarCraft 2-style vision rule (low ground can't see up) |
| Hero units | Not in v1 |
| Teblor ranged | Weak Javelin Thrower (no pure-melee asymmetry) |
| Andii shock slot | Andii Rider (expensive, elite) |
| Studio workflow | Producer (PM) agent on Fable orchestrates sessions; a separate QA inspector agent tests every change ([07](07-studio-workflow.md)) |

### Change log

| Date | Change | Reason |
| --- | --- | --- |
| 2026-10-02 | Building slots 9 → 10 (added Camp) | Economy needs a drop-off building separate from the Town Hall |
| 2026-10-02 | Keyboard camera pan uses arrow keys, not WASD | WASD collides with A (attack-move), S (stop), and grid hotkeys |
| 2026-10-02 | High-ground vision rule moved from "later, maybe" into v1 | Owner decision; terrain gains discrete elevation levels and ramps |
| 2026-10-02 | Resources, heroes, Teblor ranged, Andii shock resolved to their defaults | Owner decision |
| 2026-10-02 | Added the studio workflow (Producer + QA agents, scheduled sessions) | Owner wants agents to run more of the work between sessions |
| 2026-10-03 | M0 accepted; M1 (core sim) started | Owner sign-off after re-running build, tests and smoke on main |
| 2026-10-03 | Producer may chain sessions, sign off milestones, and default [OPEN] items; owner check-in at end of plan | Owner authorization (see studio/autopilot.md) |
| 2026-10-03 | Terrain cliffs are blocked nav *cells* on the plateau rim (not per-edge rules); ramps join one level to the next; the map's outer ring is blocked | Producer decision (M1-3), owner may revisit; keeps flow fields per-cell ([03 Navigation grid](03-technical-design.md#navigation-grid)) |
| 2026-10-04 | Flow-field builds are capped per tick; the cache's keys/LRU order count as sim state (to be hashed and saved), and misses are served oldest order first | Producer decision (M1-4b, BUG-0018/0021/0022), owner may revisit; keeps the tick budget and determinism ([03 Flow fields](03-technical-design.md#flow-fields)) |
| 2026-10-04 | Build cap is 2 fields per tick; cache holds `clamp(units/8, 32, 128)` fields under a 64 MiB budget; same-tick ties break by goal cell (map-side bias up to ~4 ticks, BUG-0026) and over-capacity eviction stays plain LRU (BUG-0025) until a later task | Producer decision (M1-4c), owner may revisit; ~1.4 ms Debug per tick for the two builds, well inside the 4 ms budget ([03 Build cap](03-technical-design.md#flow-fields)) |
| 2026-10-05 | Studio pace: QA depth by risk tier, S3/S4 debt worked every 4th session and at milestone end, tasks up to 1,500 lines when the design is clear | Owner decision to speed up development ([07 Pace and debt](07-studio-workflow.md#pace-and-debt-set-2026-10-05)) |
| 2026-10-05 | Crowded arrival replaces formation offsets for M1: a unit arrives within 1 m of the point or when it touches an arrived groupmate, so a group packs into a blob (stopped units overlap at most 40%); a unit that makes no progress for 1 s gives up and goes Idle; a re-issued order to the goal cell a unit already holds is the same order | Producer decision (M1-4d-1), owner may revisit; formation offsets can return at M2 with group commands ([03 Local movement](03-technical-design.md#local-movement)) |
| 2026-10-05 | Shoving: a walker pushes friendly Idle units out of its way, never enemies or Moving units; units holding a goal bend with their blob rather than leave it, and a unit standing on its point holds it unless it stands alone and the walker has been stuck 0.5 s; a shoved unit that loses touch with its point drops its goal. Crowd targets (groups to nearby points mostly arrive, parked groups yield in chokes) are deferred to unit-aware routing, task M1-4d-3 | Producer decision (M1-4d-2; BUG-0032/0033 triaged S3), owner may revisit ([03 Local movement](03-technical-design.md#local-movement)) |
| 2026-10-05 | Queued walkers don't give up: a unit making no headway while a groupmate just ahead of it is still moving waits its turn instead of counting toward giving up (a jammed group still gives up). Enemy units holding their ground are hard walls: a walker never goes deeper into one, even when squeezed between two; the army's own standing units stay soft (single clip, shovable) | Producer decision (M1-5; BUG-0035), owner may revisit; holding friendly standing units hard too cut crowd arrivals below the measured floors ([03 Local movement](03-technical-design.md#local-movement)) |
| 2026-10-05 | The studio runs two tracks per session: `sim` (rules) and `view` (Godot presentation); the view track starts M2 while the sim track finishes M1, and a milestone is Done only when both parts are | Owner decision ([07 Two tracks](07-studio-workflow.md#two-tracks-in-parallel-since-2026-10-05)) |
| 2026-10-05 | `sim/Rts.Sim/ViewApi/` may hold, besides read-only sim queries, pure presentation helpers with no sim reference (fixed-step clock, terrain mesh geometry) so they are unit-testable without Godot; the view track's xUnit tests live only in `sim/Rts.Sim.Tests/ViewApi/` (dev) and `sim/Rts.Sim.Tests/QA/ViewApi/` (QA) | Producer decision (session 2026-10-05-1446), owner may revisit; keeps the two tracks' test files from colliding ([07 Two tracks](07-studio-workflow.md#two-tracks-in-parallel-since-2026-10-05)) |
| 2026-10-05 | Crowd routing is local (no crowd cost in the flow fields yet): a walker detours round standing units that aren't its group (shorter arc, ties right, not near its goal), a no-progress tick behind any moving walker within 2 m or behind a unit waiting for its field is queued (the latter counts 1 tick in 4 so jams end), a unit shoved off its blob walks back once, a parked line of up to 3 yields whole to a blocked walker, enemy plugs are recognized up to 4 units (longer plugs can leak: BUG-0045, S3 until the M1 end-of-milestone hardening). Crowd rows accepted below the Producer's targets again (4 points: 51% / 34%), each beating the old code on the same setup | Producer decision (M1-4d-3 hardening), owner may revisit ([03 Local movement](03-technical-design.md#local-movement)) |
| 2026-10-05 | Team colour is the faction's `PrimaryColor`, and player p plays faction p in data order (Malazan, Whirlwind) until the M6 lobby; placeholder units are capsules sized by the unit's data radius; start armies stand in two half-disc blocks either side of the map centre (debug layout until real start locations); click radius at least 12 px; Shift + click on empty ground keeps the selection | Producer decision (M2-2), owner may revisit ([03 Implementation (M2-2)](03-technical-design.md#implementation-m2-2)) |

## IP and naming policy

Malazan names, places, and characters belong to Steven Erikson and Ian C. Esslemont. For a private
hobby project, using them during development is fine and keeps the design docs clear.

**Before any public release, even a free one on itch.io, do a rename pass to original names.**
The architecture makes this a data-only change: all player-facing text comes from `displayName` /
`description` fields in `game/data/`, and internal ids are never shown to the player. Model and
file names use the same internal ids, so they need no change.

Rules for the rename pass:

- Generic words stay (sapper, zealot, raven, ram, longbow, bloodwood).
- Invented proper nouns and distinctive terms go (Malazan, Tiste, Teblor, Wickan, Moranth,
  K'risnan, Kurald Galain, Emurlahn, Aptorian, Eleint, Dryjhna, Raraku, Genabackis, Cusser, Sharper).
- No character names anywhere in shipped data (Karsa, Rake, Quick Ben, Coltaine, Shadowthrone).
- Faction visual identity should already be original (our palettes and silhouettes), so art can stay.

Each faction page has a full codename → release-name table for its units, buildings, and
abilities. The faction-level proposals:

| Internal codename | Proposed release name |
| --- | --- |
| Malazan Empire | The Iron Legions |
| Tiste Andii | The Nightborn |
| Shadow (Tiste Edur / Hounds of Shadow) | The Shade Kin |
| The Whirlwind (Army of the Apocalypse) | The Storm Faithful |
| Teblor | The Bloodwood Clans |
| Warren (magic system) | Way or Path (pick one at the rename pass) |
| Raraku (desert biome / map) | The Holy Desert |
| Genabackis (forest biome) | The Northern Wilds |
| Seven Cities (steppe biome) | The Sun Coast |
