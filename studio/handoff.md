# Handoff: current session plan

Written by the Producer at the PLAN of session 2026-10-05-2330 (both tracks GO). The ACCEPT of
this session replaces it with the brief for the next one.

## Where we are

- `main` = ab595a1 (both 1609 merges in). Build 0 warnings. Non-Perf suite rerun by the Producer
  at this PLAN: 1510 passed / 20 skipped / 0 failed in 3 m 7 s. Open bugs 18 (S3 12, S4 6), none block. Sessions today
  6 / 8 after this one. Sim feature sessions since hardening 0 / 4 → this one makes 1; view 2 / 4 → 3.
- Bug numbers this session: sim from BUG-0054, view from BUG-0064.
- M1: 6 / 8 (M1-7 perf test and M1-8 CLI left, then the end-of-milestone hardening and sign-off).
  M2: 3 / 10.

## Sim track

### Current session plan: M1-7 perf-test criterion + command kinds `Stop` / `HoldPosition` / `AttackMove` + shift-queued orders

Type feature, QA full, budget 1,500 changed lines (perf test ~150; commands + queue + replay
format ~600-900 incl. tests). Public setup API stays additive (no new required `SimConfig` fields,
no signature changes to `Simulation.Enqueue`, `Command.Move`, the loader).

**Goal.** Tick the M1 criterion "Perf test: 500 moving units, average tick < 4 ms" with a named,
enforced test, and give the view track the order vocabulary M2-3 needs (A / S / H / shift-queue)
so both tracks keep moving: three new command kinds and a per-unit order queue, recorded in
replays and hashed.

**Scope.**
1. Perf criterion: a dev test (Perf trait, `SerialCollection`) named for the criterion, e.g.
   `PerfCriterionTests.FiveHundredMovingUnits_AverageTickUnder4Ms`: 500 units of mixed types on the
   default 128 map, ordered to a far goal so that at least 95% are still `Moving` on every measured
   tick (assert it), 5 warm-up ticks then 200 measured; assert average < 4.0 ms and print avg /
   p99 / worst. Reuse `MoveScenario` / `CrossMapScenario`. Document in docs/03 "Platform" or
   "Flow fields" that maps larger than 256 x 256 are unsupported until time-sliced builds
   (BUG-0023 stays open, note added to the bug).
2. `CommandKind.Stop = 3`, `HoldPosition = 4`, `AttackMove = 5` (keep 0-2 as they are).
   `Command.Flags` (int): bit 0 = `Queued` (shift). Factories `Command.Stop(player, unit, queued)`,
   `Command.HoldPosition(...)`, `Command.AttackMove(player, unit, target, queued)`, and an optional
   `queued` parameter on `Command.Move` (default false, so the view's calls still compile).
   `IsValid`: positional kinds need a finite position; unit kinds need nothing else here (ownership
   and liveness are checked at apply time, as `ApplyMove` does).
3. Semantics (apply at phase 1, same checks as `ApplyMove`: alive, owned by the issuing player):
   - **Move / AttackMove** unqueued: clear the unit's queue and `Hold`, then exactly today's
     `ApplyMove` (AttackMove = Move until M4; the kind is stored in the queue entry so M4 can change
     it without a format change).
   - **Stop** unqueued: clear the queue and `Hold`; `State = Idle`, `GoalCell = -1`, velocity 0,
     `StuckTicks` 0, `BestRemaining` reset, `WalkBack = WalkBackNone` (the unit is goal-less, like a
     fresh spawn: shovable, no anchor). A Stop on an Idle goal-less unit is a no-op.
   - **HoldPosition** unqueued: as Stop, then `Hold[i] = true`. A holding unit is **never shoved**
     (not by the single shove, the chain shove, the parked-line yield, or a walk-back) and never
     walks back; otherwise it is an ordinary Idle friendly (soft clips, slippable) and an ordinary
     Idle enemy to the other player (hard wall / plug member). Any later unqueued order clears
     `Hold`. Nothing else (no target scanning) until M4.
   - **Queued (flag set)**: append to the unit's order queue (kind + position); if the unit is Idle
     and goal-less and not holding, the queue is popped on the next phase 7. A queued order on a
     unit whose queue is full is dropped (no error). Queued orders never reset the current move.
   - **Order queue advance** (phase 7 "Orders and targeting", new static `OrderSystem.Run(World)`
     called from `Simulation.Tick` between apply-commands and movement): for every live unit in slot
     order that is `Idle` with a non-empty queue, pop the head and apply it through the same code as
     the unqueued command (so build-cap, same-cell dedup and BUG-0030/0043 rules hold). Popping a
     `Stop` or `HoldPosition` clears the rest of the queue (they are terminal; document). A unit
     that gave up (Idle with `GoalCell` kept) also pops its next order: giving up on one leg does not
     cancel the rest.
   - Capacity: `OrderConstants.QueueCapacity = 8` per unit (engine constant, not a stat). Storage in
     `UnitStore` as flat parallel arrays sized `Capacity * QueueCapacity` plus `int[] QueueCount`
     (ring or shift-down; no allocation). Everything new (`Hold`, queue entries for live units in
     full, `QueueCount`) joins `StateHash`; the `QA/LocalMovementQaTests` reflection audit must still
     pass (hash every queue slot of a live unit, not just the used ones, so the audit's index-1
     mutation is seen; or extend the audit, with the reason).
   - `TryAlloc`/free resets `Hold`, `QueueCount` (and `Alive=false` slots are skipped by the hash
     as today).
4. Replays: `Replay.CurrentFormatVersion` + 1; command lines gain a 10th field (`Flags`) and the
   reader requires 10. Regenerate the golden in the same commit: every `c` line gains ` 0` and the
   15 checkpoint hashes must be **byte-identical** to today's (diff the `k` lines; say so in the
   commit message). `ReplayFormatTests` truncation / bit-flip rows re-run on the new golden.
   `StateHash` of pending commands includes `Flags`.
5. Docs: docs/03 "Orders and unit states" (what exists today: kinds, flags, queue capacity, pop
   timing, terminal orders, Hold semantics), "Tick model" phase 7 note, "Save/load and replays"
   format change; docs/02 "Controls" needs no change (S / H / A / Shift already listed).
6. MovementSystem: the only changes are the Hold checks in the shove / yield / walk-back paths
   (and `Stop(u, i)` reuse if convenient). No movement-rule changes: the golden checkpoints must not
   move.

**Out of scope:** combat targeting for AttackMove, Hold scanning, Patrol, M (move-ignore-enemies),
Attack(target), formations, the CLI (M1-8), any crowd-quality work (BUG-0028/0032), BUG-0044/0045.

**Acceptance criteria.**
1. The named perf test exists, is Perf-tagged and enforced, asserts ≥ 95% Moving on every measured
   tick and avg < 4 ms; passes alone and prints avg / p99 / worst. Roadmap M1-7 can be ticked from it.
2. `CommandKind` has `Stop`, `HoldPosition`, `AttackMove`; `Command.Flags` with `Queued`; factories
   as above; `Command.Move(int, EntityHandle, Vector2)` still compiles unchanged (the view calls it).
3. Unit tests (`OrderTests` or similar): Stop halts a walker on the next tick (velocity 0, Idle,
   `GoalCell` -1, `WalkBack` none) and is dropped for dead / recycled / foreign units and from the
   wrong player; HoldPosition marks `Hold` and a holding unit in a 1-cell corridor is never moved by
   30 walkers over 300 ticks (position bit-identical) while the same unit without Hold is pushed
   along; a plain Move clears Hold; AttackMove behaves exactly like Move (hash twin, 500 ticks,
   same positions); a shift-queued chain of 4 Moves visits all 4 points in order (each within
   `ArrivalDistance` before the next leg starts), works after a give-up on leg 2, and a queued Stop
   /Hold ends the chain; a 9th queued order is dropped; queued orders on a Moving unit don't restart
   it (`OrderTick` unchanged).
4. Determinism: a two-sim hash twin with random mixes of all kinds, queued and unqueued, over 2,000
   ticks on 3 seeds; replay round trip of such a run plays back with every checkpoint matching.
5. Golden regenerated once; `k` lines byte-identical to `main`'s; `ReplayFormat` refuses the old
   version with `FormatVersionMismatch`; the format tests cover the new field (bad flags value, a
   9-field line).
6. `StateHash` covers `Hold`, the queue entries and counts, and `Flags` of pending commands (the
   reflection audit passes with the new arrays listed, as `WalkBack` is at line 313).
7. Zero allocations per tick with 500 units cycling queued orders (an `AllocationProbe` row).
8. `dotnet build` 0 warnings; full suite green (Perf alone if it fails under load); docs per scope 5.

**Design references:** docs/03 "Tick model" (phases 1, 7, 9, 14), "Orders and unit states", "Local
movement" (shove rules: single, chain, parked-line yield, walk-back), "Save/load and replays";
docs/02 "Controls and camera" (A / S / H / Shift); CLAUDE.md rules 3, 4, 5.

**Tests required:** the perf criterion test; `OrderTests` (criteria 3); a determinism twin and a
replay round-trip with the new kinds; `ReplayFormatTests` additions; the hash audit; the allocation
row; `ArchitectureTests` green.

**Constraints most at risk:** no allocation in the queue (flat arrays, no `List<>` per unit); no
Dictionary iteration; tick order stays fixed (apply → phase 7 pop → movement); the golden's
checkpoints must not move (no movement-rule changes); public API additive.

### QA focus (sim)
Replay format: 10-field lines, flags out of range, old-version files, the golden's checkpoints vs
`main`, bit flips on the new field. Queue abuse: 4,096 queued orders at once on one unit, queued
orders to dead / recycled / foreign units, a queued Move to the unit's own cell, Stop spam
alternating with Move every tick (must terminate, hash twin), Hold then shove storms (30-200 walkers,
chain shoves, parked-line yields, walk-backs: a holding unit's position must be bit-identical over
the run), Hold units forming a plug on a ramp against their own army and against enemies (nobody
through), Hold + give-up interplay (walkers behind a holding unit give up in bounded time), a queue
head popped the same tick the unit died or was re-spawned. Determinism: hash twins over every kind
mix, replays of them, spawn-order permutations of queued chains. Hash audit for the new arrays.
Perf: the criterion test alone and under the full suite; 2,500 units with full queues (report).

## View track

### Current session plan: M2-4 minimap (left-click moves the camera, right-click orders)

Type feature, QA standard. **Budget: 500 lines of production code** (`game/scripts`, `game/scenes`,
`sim/Rts.Sim/ViewApi`), tests and docs on top, total under 1,200. Three view slices have run over;
this one must not. Needs nothing new from the sim; uses `Command.Move` exactly as M2-2.

**Goal.** The bottom-left minimap from docs/02: a top-down picture of the map with unit dots in
team colour and the camera's view outline; left-click jumps the camera there, right-click sends
the selection there. First HUD element, so it also creates the `Hud` CanvasLayer from docs/03.

**Scope.**
1. Pure `ViewApi.MinimapRaster`: bakes the heightmap into per-cell RGBA bytes (same three level
   tints, ramp and cliff colours as `TerrainMeshBuilder`, exposed as shared constants; impassable
   cells darker), and writes unit dots (one cell-sized dot per live unit, owner → faction
   `PrimaryColor`, later slots overwrite earlier) into a second buffer, 0 bytes per call after
   setup. Pure `ViewApi.MinimapTransform`: map metres ↔ minimap pixels for a rect that preserves the
   map's aspect (non-square maps letterboxed), `TryToMap(pixel, out Vector2)` false outside the map
   area.
2. `Hud` (CanvasLayer) with `Minimap` (a `Control` in the bottom-left corner, 220 px tall, with the
   terrain `ImageTexture` baked once at `Start`, a dots `ImageTexture` refreshed every 4 ticks
   (5 Hz, docs/03), and the camera trapezoid drawn in `_Draw` every frame (the four viewport
   corners projected to the ground plane at the camera focus height; corners whose ray doesn't hit
   are clamped at 200 m). The minimap consumes its mouse events (`MouseFilter.Stop`) so a click on
   it never reaches `SelectionController`.
3. Left-click (and drag with the button held) → `RtsCamera.SetFocus(map point)`. Right-click →
   `SelectionController.OrderMoveTo(Vector2 mapPoint)` (extract today's right-click path into that
   public method and call it from both places; the overflow warning stays there). Outside the map
   area or empty selection → nothing.
4. Edge panning is suppressed while the mouse is over the minimap (hovering the corner must not pan
   the camera).
5. Launch flag `--no-hud` (hides the HUD, for clean screenshots of the world). Optional, only if it
   fits the budget.
6. docs/03 "Implementation (M2-4)": node tree, redraw rates, transforms, what the minimap doesn't
   show yet (fog, resources, pings: M3/M4). The debug label stays as is.

**Out of scope:** fog, resource markers, alerts / pings, Alt+click, attack orders, any other HUD
panel, minimap zoom, M2-3 (A/S/H/queue) even if the sim's kinds land first, `game/data/` changes.

**Acceptance criteria.**
1. Screenshot (`--screenshot`) at `--units 100` and `--units 1000 --zoom 60`: the minimap sits bottom
   left, shows the three level tints, ramps and cliffs matching the 3D view, blue and orange dots
   where the armies stand, and a white trapezoid matching the camera's view. Producer inspects.
2. `ViewApi/MinimapRasterTests`: every cell's colour equals the mesh tint for its level (or the
   ramp / cliff colour); the 4x4 hand map and 3 generated maps; dots land on the right pixel for
   units at cell corners, centres and the map's last float; dead units draw nothing; 2,000 dots
   allocate 0 bytes (`AllocationProbe`).
3. `ViewApi/MinimapTransformTests`: pixel ↔ map round trip for all four corners, the centre and
   100 random points within half a pixel; non-square maps (160x48, 48x160) letterbox correctly;
   outside pixels return false; NaN / Inf rejected.
4. `game/tests/MinimapTest.tscn` prints `MINIMAP TEST PASS`: an injected left-click at a minimap
   pixel moves the camera focus to the matching map point (within one cell, after clamping);
   an injected right-click with 40 units selected enqueues 40 Moves to that point and they are
   Moving 3 s later; a click outside the minimap rect changes nothing; the dots texture at a unit's
   pixel has its faction colour after the first refresh; the trapezoid's corners are inside the
   map rect at both zoom limits and with the camera at a map corner.
5. Read-only: `ViewApi` additions hold no sim reference (`ArchitectureTests` / the QA source scan
   pass); the QA hash twin with minimap-issued orders matches a bare sim every tick.
6. Minimap refresh cost at 2,000 units ≤ 0.5 ms per refresh (print it in the test); FPS at
   `--units 1000` still at the vsync cap on this PC.
7. smoke PASS; build 0 warnings; full suite green; docs/03 section written; production-line count
   reported in the dev report.

**Design references:** docs/02 "Controls and camera" (Minimap paragraph, HUD layout); docs/03
"Rendering and presentation" (Hud node, Minimap at 5 Hz), "Implementation (M2-2)" (right-click
path, selection), "Debug tooling"; CLAUDE.md rules 1, 7, 8 (no player-facing text in C#: the
minimap has none).

**Tests required:** criteria 2-4; `ViewApiAllocationTests` row; `ArchitectureTests` green.

**Constraints most at risk:** budget (500 production lines); the view must not write sim state
except `Enqueue`; no `game/data/` edits; the `Control` must not swallow events outside its rect.

### QA focus (view)
Transform precision at pixel edges and on non-square maps; clicks on the minimap's border pixels
and just outside; right-click with 2,000 selected (overflow handling identical to M2-2); hash twin
with hundreds of minimap orders; 0 bytes per refresh; refresh cost on a 256 map with 2,000 units;
trapezoid at the zoom limits, at map corners, and when the camera focus is clamped; hover suppresses
edge pan; a minimap click never clears or changes the selection; dots for a unit that dies and is
respawned as the other faction; the texture after `--speed 8` (refresh keyed to ticks, not frames).

## Watch out for

- Merge order sim → view. Expected shared-file overlap: `studio/bugs/README.md` (sim 0054+, view
  0064+), `studio/qa/coverage.md` (both append), docs/03 (sim: "Orders and unit states", "Tick
  model", "Save/load and replays"; view: a new "Implementation (M2-4)" section). STATE, the session
  log, this file and roadmap ticks are written in the sim worktree only.
- The sim task changes the replay format and regenerates the golden: checkpoints must be
  byte-identical, or the commit must explain which movement rule moved (none is planned).
- The view task reaches `Command.Move(player, unit, target)`; the sim keeps that overload.
- Perf failures count only when they fail again alone (both tracks' QA overlap in wall-clock).
- Remote Control is unavailable in unattended sessions; don't retry it.
- `CLAUDE.md` still says "Current milestone: M1" (owner's file; drift noted for the owner in STATE).
