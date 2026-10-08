# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-08-0913** (base `5ccbe0f`; second full session of
2026-10-08, cap 8). All three tracks **ACCEPT**: sim **M4-2b** (1 fix round), view **M4-V2** (0), data **D5** (0). The
next session's PLAN replaces the "Current session plan" sections below. **Bug ids next session:** sim from **BUG-0210**,
view from **BUG-0220**, data from **BUG-0230** (disjoint blocks; this session used 0180-0184, 0190, 0200).

## Where we are

- **M0, M1, M2, M3 Done.** **M4: 5 / 10 criteria** (1 attack / attack-move / chase / retaliation / priorities, now with
  the view's right-click / A + click on an enemy; 2 damage formula; 3 projectiles / splash / friendly fire; 4 death /
  corpses / rubble; the counter-triangle scenario rows). Left: 5 fog + high-ground vision + fog shader + building ghosts
  (M4-3), 6 abilities / statuses / zones (M4-4), 7 stealth / detection (M4-5), 8 the four signature abilities, 10 the
  fog-on sandbox Playable.
- **Every shipped unit fights now** (archers, crossbows, mages, priests, catapults, sappers, the ram): the sandbox fight
  (`& $env:GODOT --path game`, A + click) has arrows and stones in the rules but **no projectile visuals yet** (M4-V3).
- **Hardening counters after this session:** sim 3 / 4, view 3 / 4, data 1 / 4 (`hardening_every` 4). One more feature
  session per track, then each track's hardening session (or at M4's end, whichever first).
- **Open bugs:** S1 0, **S2 0**, S3 (sim: BUG-0144, 0149, 0151, 0157, 0134, 0080, 0046, 0050, 0028 / 0032, 0025, 0005,
  0023; view: none), S4 (sim 0182 item 2 / 0184 / 0153 3-4 / 0158 / 0142 / 0133 / 0113 / 0094 / 0040 / 0026 / 0002; view
  0190 / 0148 / 0126 3-6; data 0200 / 0090 towers part).
- **Repo health at ACCEPT** (each branch): build 0 warnings; see the session log for the Producer's reruns.

## Sim track

### Next task candidates (PLAN decides)

1. **M4-3 (sim half), first slice: three-state fog per player + the high-ground vision rule + tower fields.** The design
   is in docs/02 "Fog of war" / "High ground" / "Stealth and detection" (the detection part is M4-5) and docs/03 phase 12
   ("recompute fog every 4 ticks (5 Hz) per player"). Sim-owned pieces: a per-player visibility grid (unexplored /
   explored / visible) from every own unit's and building's `sight`, the rule "low ground can't see up" (a cell above the
   viewer's level is not seen unless the viewer is on a ramp top / same level; attacking from high ground reveals the
   attacker for 2 s), the `buildings.json` tower fields (`attack` / `sight` / `detector` for the Watch Tower, docs/02
   Buildings table: 10 pierce / 2 s, range 18, sight 24, detector 16 m; the data track's BUG-0090 request 8b) with
   validation and a tower attack path (buildings that shoot: a projectile from the footprint; `TechBonus` "and towers"),
   a read-only fog query for the view (`World.Visibility(player)` spans + a version counter so the shader uploads only on
   change) and `PlayerView` groundwork for M5 (optional). **Uncertain work: slice it** (fog grid + high-ground rule + view
   spans first, ~800 lines; towers second). Perf: 5 Hz per player over a 128 x 128 grid with ~200 sight sources must stay
   well under the 4 ms budget (`TightBlob2500` is at 4.3-4.5 of 4.6 ms: fog must not add per-unit work to the tick it
   runs in; consider spreading players across ticks). Hashed: the explored bits (they persist) but not the visible bits if
   they are derived every 4 ticks (say which in the brief). QA full.
2. Fold-ins if a few lines: BUG-0184 item 1 (`<=` with a small epsilon on the lead test, or document it), the
   `ProjectileImpact.Position` doc comment still says "where the target was when the shot was fired" (it is the impact
   point, which a led shot moves). BUG-0182 item 2 is a data note (below), not a sim fix.
3. After M4-3: M4-4 abilities / statuses / zones schema (then the data track fills `abilities.json` / `statuses.json`),
   M4-5 stealth / detection, the four signature abilities, the fog-on sandbox.

### Watch-outs (sim)

- The golden moved twice this session (`data-hash` for `projectiles.json`, then for `leadSpeed`); every `k` line was
  identical both times. M4-3 will move it again if `buildings.json` gets tower fields (data-hash) and, if fog changes
  nothing about movement, `k` lines stay identical: say so in the brief.
- `World.Neighbors` is shared scratch (movement phase 8-9, combat scans phase 7, splash phase 11): a fog pass in phase 12
  may use it too, but never across phases.
- `ProjectileStore` default size is `players x popCap / smallest shooter pop` (200 for two players); dev spawns past the
  cap can overflow it (shots lost cleanly). A scene with 500 archers sets `SimConfig.ProjectileCapacity`.

## View track

### Next task candidates

1. **M4-V3: projectile visuals.** `World.Projectiles` (`ProjectileStore`: `Capacity`, `Count`; per-slot read-only spans
   `Alive`, `Position`, `PrevPosition` (the launch point on the firing tick), `Target` (the impact point; a led shot's
   moves a little each tick), `ProjectileTypeId` (`GameData.Projectiles`: `Key`, `Kind` aimed / lob), `Owner`) and
   `World.Impacts` (`ProjectileImpact`: `Position`, `ProjectileTypeId`, `Owner`, `Hit`; emptied at the next tick's start
   like `World.Deaths`, so read it from `SimRunner.Ticked`). Draw one pooled MultiMesh instance per live slot
   interpolated between `PrevPosition` and `Position`; a lob gets a drawn arc (height from flight progress; the sim flies
   straight), an aimed shot a straight line; a small impact mark on `Hit` and a dust puff on a miss (placeholders until
   M6). **BUG-0184 note:** a re-led bolt can step up to 1.8 m in one tick (nominal 1.25) and bends slightly for a turning
   walker; interpolate, don't extrapolate. 0 B per frame in a steady brawl; a headless scene with seeds 1 / 6 and a hash
   twin. QA standard. Fold in BUG-0190 item 2 (a NaN entry guard in `ResolveEnemy`, two lines) and, if cheap, item 1
   (corpse disc at the highest terrain sample under its radius).
2. Then M4-3's view half (the fog shader over the terrain mesh, explored-but-not-visible building ghosts, the minimap
   fog layer and its Attack half on an enemy dot), ability feedback (M4-4), the fog-on sandbox Playable.

### Watch-outs (view)

- `tools/qa/scene-loop.ps1` (new, QA) runs every headless `*Test` scene; 31 / 31 this session. Use it in the report.
- Minimap dot timing rows (`ViewHardeningQaTests.DotRefresh_2000Units_WorstLayouts`) flake under CPU contention; a
  failure counts only alone.

## Data track

### Next task candidates

1. **D6, QA light or standard:** (a) BUG-0200 (fold the docs/02 "Ages" trailing-clause check into `TechContentTests.G`,
   reword C's "file attack.targets" message); (b) **the first balance report from the counter-triangle rows** (the sim
   prints survivors and time: Line beats Shock by 1,036-1,216 of ~1,200 cost, i.e. 14-15 of 16 Heavy Infantry survive a
   same-cost Horse Raider charge; Shock beats Ranged 840-960; Ranged beats casters 810-1,088): read docs/02 "Faction
   template" and the faction pages for the intended margins (if the docs give none, the data track proposes a target
   margin for the owner under For your review rather than tuning numbers); (c) BUG-0182 item 2: Sappers (range 8, splash
   2 m, friendly fire, no `minRange`) splash themselves when a melee unit closes to 0.5 m; friendly-fire kills are 23-28 %
   of deaths in shooter-heavy fuzz brawls. Options for the owner: a `minRange` on the Sapper (it then waits, no kiting
   yet), a smaller splash, or accept it as the Sapper's trade-off (docs/factions/malazan.md: "Fragile; wants an escort",
   which reads as intended; the Producer's default is to accept and document it, with the numbers). Any number
   change regenerates the golden `data-hash` (the data track does not regenerate; the brief names the sim's
   `DataContentHashTests` allow-list and QA regenerates with the reason, as D4 did) and gets a For your review table.
2. Towers' content after M4-3 lands the tower fields; `abilities.json` / `statuses.json` after M4-4; `ai.json` (M5).
3. Always first: owner review tweaks from the inbox.

### Watch-outs (data)

- Only the "Ages" paragraph of docs/02 is parsed by the content tests (plus the faction pages); the Producer rewrote the
  docs/02 "Projectiles" paragraph at ACCEPT (no test reads it).
- `docs/factions/shadow.md` "Edur Ram: attacks buildings only" has no test until Shadow data exists (M8).

## Watch-outs (all tracks)

- **Merge order:** sim, then view, then data. Expected shared-file conflicts: docs/03 (one subsection per track), docs/01
  (rows appended), `studio/bugs/README.md`, `studio/qa/coverage.md` (append both sides).
- **Perf:** the category in one process fails 8-9 wall-clock rows on base and head alike (machine load); a Perf failure
  counts only alone, first run in a fresh process (BUG-0158). `TightBlob2500` is at 4.3-4.5 of 4.6 ms.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file; suggested text under For your review, M1 entry).
- Sessions: a lock's age alone does not prove the previous session dead; check for running processes before resuming.
