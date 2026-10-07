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
| 2026-10-05 | Orders: `Stop`, `HoldPosition` and `AttackMove` (a plain move until M4) exist as command kinds; Shift queues up to 8 orders per unit (a 9th is dropped silently); a queued `Stop` / `Hold` ends the chain; a unit that gives up on one leg still starts the next; a holding unit is never shoved or walked back, and any later order (including a queued one when it starts) releases the hold. A holding unit is still a soft wall to its own army (walkers can slip past it in a 1-cell choke, BUG-0055); the M1 end-of-milestone hardening session makes holders block their own army too, if the crowd rows allow it | Producer decision (M1-7), owner may revisit ([03 Orders and unit states](03-technical-design.md#orders-and-unit-states)) |
| 2026-10-05 | Minimap: bottom-left, 220 px, map letterboxed to keep its aspect; terrain uses the 3D mesh's placeholder palette, unit dots are one map cell in the player's (= faction's) colour (since 2026-10-07 a 2 x 2 block in a contrasting rim, see that row) refreshed 5 times a second, the camera's view is a white outline; left-click or drag moves the camera, right-click orders the selection; `--no-hud` hides the HUD | Producer decision (M2-4), owner may revisit ([03 Implementation (M2-4)](03-technical-design.md#implementation-m2-4)) |
| 2026-10-06 | Order and selection keys as in 02 "Controls and camera", with these details: A with nothing selected does nothing; while A is armed, a left click on the map attack-moves and disarms, a click off the map does nothing, Esc / right-click / S / H disarm (a minimap right-click still orders a plain move: BUG-0068, fixed in the view hardening session); Ctrl + digit with an empty selection keeps the group; recalling an empty group changes nothing; Ctrl + Shift + digit assigns; a double-tap window of 0.3 s; Tab order is ascending unit type id and resets on every new selection; no "holding" indicator (a queued order ends Hold) | Producer decision (M2-3), owner may revisit ([03 Implementation (M2-3)](03-technical-design.md#implementation-m2-3)) |
| 2026-10-06 | M1 hardening (M1-9): a unit holding position is a hard wall to its own army too (nobody slips past a holder in a choke); an enemy plug is any cluster of up to 32 standing enemy units (or own holders) too close to pass between that spans a passage wall to wall (was a line of up to 4); a command of an undefined kind, with unknown flag bits, or with the queued flag on a spawn / no-op is refused when enqueued (so every recorded replay reads back); movement still depends on spawn order in the wall clips (BUG-0046, a known limit: a slot-free order was measured and re-rolled three fitted test bounds) | Producer decisions (BUG-0055 2026-10-05, BUG-0054 / 0056 / 0045 triage), carried out in M1-9; the crowd rows held ([03 Implementation (M1-9)](03-technical-design.md#local-movement)) |
| 2026-10-06 | M1 signed off by the Producer (autopilot, `stop_at_milestone_end: no`) after the M1-9 hardening: retro in [05 Retros](05-roadmap.md#retros); M3 is Next for the sim track. Two known limits stand instead of fixes: BUG-0046 (wall clips in slot order; the slot-free sort was built, measured and shelved because it re-rolls crowd outcomes and three fitted test bounds, for an independence nothing relies on; revisit with the crowd-cost work after M4) and BUG-0050 (4.7% random-goal give-ups vs the 3% target; every terminating alternative measured 4.6-6%). The minimap dot refresh at 2,000 units is held to the 0.25 ms QA limit, not the M2-4 figure of 0.063 ms (rimmed dots cost 0.13 ms) | Producer decisions, owner may revisit |
| 2026-10-06 | Resource nodes (M3-1): a gold mine is 2 x 2 cells and a tree 1 x 1 (`common/resources.json`); a depleted mine's cells reopen like a felled tree's; generated maps get forests and mines only when asked (`MapGenParams.Forests` / `GoldMines` default 0, so M1 maps and replays keep their layouts); the resource store holds 4,096 nodes by default (`SimConfig.ResourceCapacity`); every mine holds the start-mine amount until start locations exist | Producer decisions (M3-1 brief), owner may revisit ([03 Implementation (M3-1)](03-technical-design.md#implementation-m3-1)) |
| 2026-10-06 | Debug overlay: F12 (rebindable action `debug_overlay`), `--debug-overlay` starts it on; shows the nav grid, the flow arrows of one goal (the lowest-slot selected unit's) in a 40 x 40-cell window around the camera, a graph of the last 120 tick costs with the 4 ms budget line, and live / Moving unit and cached-field counts; per-unit state labels and the dev console are deferred | Producer decision (M2-5), owner may revisit ([03 Implementation (M2-5)](03-technical-design.md#implementation-m2-5)) |
| 2026-10-06 | Resources in the match (M2-3b): the 128 map gets 12 forests and 8 gold mines by default (`--forests` / `--mines`, 0-64, override; `0 0` gives the bare map); trees and mines are drawn as placeholder MultiMesh props (a dark-green cone on a brown trunk; a dark slate block over the mine's footprint with a gold top) and marked on the minimap (trees dark green, mines gold, under the unit dots); rocks have no sim footprint and are deferred to the M6 art pass | Producer decisions (M2-3b brief), owner may revisit ([03 Implementation (M2-3b)](03-technical-design.md#implementation-m2-3b)) |
| 2026-10-06 | Placeholder audio (M2-6): two sounds synthesized in code at start-up, no audio files: a short high blip when a click, box, type-select or group recall changes a non-empty selection, and a rising two-note confirm when an order goes out (any order kind, 3D view or minimap); each plays at most once per frame and 50 ms apart; SFX volume is a -6 dB constant until the M6 settings screen; `--mute` mutes everything | Producer decisions (M2-6 brief), owner may revisit ([03 Audio (M2-6)](03-technical-design.md#audio-m2-6)) |
| 2026-10-06 | The studio adds a third track, `data` (faction content: rosters, stats, costs, techs, names, AI build orders) beside `sim` and `view`. The sim track keeps every data schema and `game/data/common/`; the data track fills `game/data/factions/` against schemas already on main, and each accepted data task is listed for the owner as a change table (merge first, review after). Sessions no longer re-arm the routine (a schedule-started session always asks before changing a scheduled task); an owner-run watcher session starts the next session and turns on Remote Control, with an hourly routine run as the fallback | Owner decision ([07 Tracks](07-studio-workflow.md#tracks-in-parallel), [07 How sessions start](07-studio-workflow.md#how-sessions-start)) |
| 2026-10-06 | Worker gather loop (M3-2): `buildings.json` ships the Town Hall slot only for now; buildings are placed by a dev / test `SpawnBuilding` command until construction (M3-3); a node is gathered only while a passable cell is 4-adjacent to its footprint (forests are eaten from the outside, which closes BUG-0075); a worker works or deposits within 1.25 m of the footprint (`EconomyConstants.Reach`) and, standing out of reach, walks again every 20 ticks; the drop-off is the nearest by straight line; any other order keeps the cargo, a Gather of the other resource discards it | Producer decisions (M3-2 brief), owner may revisit ([03 Implementation (M3-2)](03-technical-design.md#implementation-m3-2)) |
| 2026-10-06 | Grid changes and flow fields (M3-2b): the nav grid counts *closing* changes (cells blocked: a building placed, a node spawned) apart from all changes; an *opening* change (a tree felled, a building removed) leaves cached flow fields usable, so walkers keep following them while the 2-builds-per-tick cap refreshes them (missing fields first, then refreshes, oldest first), and a unit standing on a newly opened cell waits for its goal's rebuild; a closing change makes every field unusable and resets every walker's progress mark, so a detour round a new building isn't read as "no progress" (closes BUG-0073 and BUG-0077); a wood resource type's footprint must be 1 x 1 (closes BUG-0074) | Producer decisions (M3-2b brief), owner may revisit ([03 Flow fields](03-technical-design.md#flow-fields)) |
| 2026-10-06 | Construction (M3-3): a placement must leave every two passable cells that connect still connected, so a building never seals ground (closes BUG-0078; the dev `SpawnBuilding` obeys it too); enemy units or own units holding position in the footprint block a placement, other own units there are set down on the nearest free cell outside it; workers build with `Build` (several on one site share it, at docs/02's `t x 3 / (n + 2)`), `Cancel` refunds the unbuilt fraction of the cost (rounded down), `Repair` restores at half the one-builder rate for a quarter of the cost scaled by damage and stops when the player can't pay; player p plays faction p mod 2 until a lobby (M6) | Producer decisions (M3-3 brief), owner may revisit ([03 Implementation (M3-3)](03-technical-design.md#implementation-m3-3)) |
| 2026-10-06 | Playable check (M2-7): the studio proves 60 FPS with a scripted benchmark, `--bench <seconds>` (box-select the army, order it across, four minimap corner jumps, zoom 20 / 60 m, A + click, three Shift-queued moves, H, S, looping every 10 s), which prints one `bench:` line (avg / p50 / p99 / worst frame ms, fps, ticks, mean tick cost); the pinned bar is avg under 16.7 ms and p99 under 33 ms at 100 units per player with vsync off (measured 0.72 / 1.31 ms on the dev PC); the first 30 frames after the armies spawn are skipped as load time; `--vsync on\|off` overrides the project's vsync; units turn between ticks the short way round (`PrevFacing` to `Facing`). The owner's own playtest confirms the criterion | Producer decisions (M2-7 brief), owner may revisit ([03 Implementation (M2-7)](03-technical-design.md#implementation-m2-7)) |
| 2026-10-07 | M2 hardening (M2-H2): a minimap unit dot is a 2 x 2 block in the player's colour (the four cells round the cell corner nearest the unit) inside a one-cell contrasting rim, 4 x 4 cells, so a lone unit reads in its player's colour at 220 px (replaces "one map cell" in the 2026-10-05 Minimap row and M2-H1's one-cell centre in a 3 x 3 rim; BUG-0069, Producer default, the owner may pick another style); the debug start blocks stand in a clearing (no tree, mine, cliff edge or border ring beside a unit, the whole block on one level); the `--bench` "order across" step targets the passable cell nearest (0.85 x width, 0.5 x height) for an army west of centre (mirrored for east), else the corner opposite the army, its `fps` is frames / seconds, and `--bench` takes at most 3,600 s | Producer decisions (M2-H2 brief), owner may revisit ([03 Implementation (M2-4)](03-technical-design.md#implementation-m2-4), [Implementation (M2-7)](03-technical-design.md#implementation-m2-7)) |
| 2026-10-07 | Sim hardening (M3-H1): a save file (M6) stores the flow-field cache's contents (direction bytes, costs and stamps of every used slot), so save then load reproduces the unsaved run exactly; replays replay from tick 0 and need nothing (BUG-0081). Cells a destroyed or cancelled building (or a felled node) frees where nobody can reach them stay blocked as pocket cells until an opening beside them joins them to open ground (BUG-0093); a worker's own Hold never blocks its own Build; push-out never stacks two units; repair factors below 2^-16 and a `resources.json` without a gold or a wood type are data errors. Closing changes still make every flow field unusable (the usable-stale alternative was not taken; the measured bound is in 03 Known limits, BUG-0080) | Producer decision, owner may revisit ([03 Save/load and replays](03-technical-design.md#saveload-and-replays), [03 Navigation grid](03-technical-design.md#navigation-grid)) |
| 2026-10-07 | M2 signed off by the Producer (autopilot, `stop_at_milestone_end: no`) after the M2-H2 hardening: retro in [05 Retros](05-roadmap.md#retros); the view track moves to M3 (HUD, build ghost, worker feedback). The owner's playtest and listening (STATE "For your review") stand as feedback, not as a gate. Open S3/S4 carried: BUG-0104 (bench march bound on seed 21), BUG-0105 (minimap refresh margin, doc figures) | Producer decision, owner may revisit |
| 2026-10-07 | Production (M3-4): a building's queue holds 5 items, each paid when queued and refunded in full when cancelled (also when the building is destroyed); the head item takes its population when it starts training, so a full cap pauses the queue; trained units spawn on the nearest free cell round the building (nearest the rally point), never stacked, and wait for one if none is free; a worker rallied onto a mine or tree gathers it; units with a `requires` list (Sapper, Zealot) can't be trained until Age II exists (M3-5); population counts dev-spawned units past the cap; `SetRally` names its building by a nav cell so the rally point keeps full precision | Producer decisions (M3-4 brief), owner may revisit ([03 Implementation (M3-4)](03-technical-design.md#implementation-m3-4)) |
| 2026-10-07 | Economy in the window (M3-V1): the default match gives each player a finished Town Hall beside its army (nearest open 4 x 4 spot with a clear ring, on its own side of the map centre) and `rules.json` `startingWorkers` (5) workers on the hall's side facing the nearest gold mine (`--workers <n>` overrides, `--no-bases` drops both for tests); the resource bar sits top right, "Gold N  Wood N" with the faction's data names, no population yet; a right-click on a tree or mine sends the selected workers to gather and moves everyone else there (Shift queues; the minimap stays a move); placeholder buildings are boxes in the player's colour, a site a lighter slate box rising with a yellow progress bar, a damaged building a green hit-point bar; a standing worker is tinted green (gathering), amber (waiting with a load) or blue (building) and carries a gold or brown cube while loaded | Producer decisions (M3-V1 brief) with builder details, owner may revisit ([03 Implementation (M3-V1)](03-technical-design.md#implementation-m3-v1)) |
| 2026-10-07 | Techs (M3-5): `common/techs.json` holds the techs every faction shares (Age II and the six Forge upgrades), `factions/<id>/techs.json` the faction upgrade (both required); a tech names the building *slot* that researches it, so a common tech resolves to each faction's building of that slot; research runs through the production queue (a queue item is a unit or a tech, one timer per building), takes no population, and is refunded in full on cancel or when its building is destroyed; a tech is queued once per player at a time and researched once; the Forge table's "+1 / +2" are totals, so level 2 adds one more point; "melee units" means a melee attack type (workers included), "non-siege" means not the siege slot (the Sapper gets armor); an ability-cooldown effect names the unit whose ability it changes; `CancelTrain` cancels any queue item; `requires` ids are checked at load on techs, buildings and units, gating them is M3-6; combat applies the bonuses in M4 | Producer decisions (M3-5 brief) with builder details, owner may revisit ([03 Implementation (M3-5)](03-technical-design.md#implementation-m3-5)) |
| 2026-10-07 | Building in the window (M3-V2): the view's own player-facing text and menu lists live in a view-only `game/data/common/ui.json` (command names and hotkey hints, placement-refusal texts, the build menus' slot lists: basic = House, Camp, Infantry Hall, Ranged Hall, Shock Hall, Forge; advanced = Caster Hall, Siege Works, Watch Tower), never read by the sim; the command card's unit commands sit on the middle row (Attack on A, Stop, Hold, Move) with Build advanced / Build basic on the V / B cells and a site's Cancel on the B cell, row one left for abilities; M + click is a plain Move; the ghost centres the footprint on the cursor cell (cell minus half the footprint, clamped onto the map); Shift keeps the ghost and queues each later placement behind the first; a left click on an own building selects it alone (enemy buildings, boxes and Tab never do) | Producer default (`ui.json`) and builder details (M3-V2 brief), owner may revisit ([03 Implementation (M3-V2)](03-technical-design.md#implementation-m3-v2)) |
| 2026-10-07 | Producing in the window (M3-V3): a selected own finished building's command card is its production card: the units it trains, then the techs it researches (shared techs first, the faction's own last), on the grid cells Q W E R T ... in that order, greyed with a short reason line ("Can't afford", "Queue full", "In a queue", "Researched", "Locked") exactly when the sim would refuse; the queue strip (up to 5 squares, head first, with a progress bar) sits above the card and a click on an item cancels it with a full refund; the selection panel sits bottom centre between the minimap and the card: one unit shows a coloured placeholder portrait, name, HP, attack, armor, range, speed and state, with tech bonuses as a green "+N" (shown, not yet applied in combat: M4); several units show up to 24 type-coloured portraits with "+N" for the rest, the active Tab subgroup outlined, a click selecting that unit alone; a building shows its name and HP; with an own finished building selected a right-click (3D view or minimap) sets its rally point, a right-click on the building clears it, and a yellow cone with a line from the building's edge marks it; the resource bar adds "Pop a / b" (red at the cap) and flashes "Age II" when the age is reached; a right-click whose ray meets a building's box first means that building, not the ground behind it (BUG-0108); all new labels and reasons live in `ui.json` (`train`, `research`, `states`, `hud`) | Producer brief (M3-V3) with builder details, owner may revisit ([03 Implementation (M3-V3)](03-technical-design.md#implementation-m3-v3)) |

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
