# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-05 (session 2026-10-05-1446, ACCEPT both tracks)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## Now

| Field | Value |
| --- | --- |
| Sim: milestone | M1 — Core sim, no graphics (started 2026-10-03); 6 / 8 criteria |
| Sim: next task | **Hardening session** (4 / 4 feature sessions): crowd routing M1-4d-3 + BUG-0037/0038, BUG-0034/0039 single-map bounds, BUG-0030, BUG-0040 tick limit |
| Sim: gate | **GO** |
| View: milestone | M2 — Presentation (started 2026-10-05); 1 / 10 criteria (3 more half-done) |
| View: next task | **M2-2** placeholder unit views (pooled, team colors, interpolated) + click/box/shift selection + right-click move · feature · QA standard |
| View: gate | **GO** |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on both session branches (0 warnings); conductor re-checks `main` after the two merges |
| Tests | sim branch 1214 / 14 skipped / 0 failed (2 m 24 s); view branch 1203 / 14 / 0 (2 m 3 s); both Producer re-ran; smoke PASS on both |
| Open bugs | 16 (S1: 0, S2: 0, S3: 12, S4: 4) — none block; BUG-0014 fixed this session |
| Sessions today | 4 / 8; feature sessions since last hardening: sim 4 / 4 (hardening next), view 1 / 4 |
| Last session | 2026-10-05-1446 · sim M1-6 replays + BUG-0014 · view M2-1 match scene + terrain + camera + screenshot · both ACCEPT, 0 fix rounds |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 (sim track) | 6 / 8 (core, data, terrain + nav + spatial hash, flow fields + movement, cross-map scenario, replays + golden) | In progress; left: hardening (now), perf test M1-7, CLI M1-8, sign-off |
| M2 (view track) | 1 / 10 (camera); SimRunner, terrain mesh and screenshot flag shipped but their criteria have remaining halves | In progress; M2-2 next |
| M3-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

### The game window shows a 3D map you can fly over (view track, M2-1, 2026-10-05)

- **What was built:** the first picture. `& $env:GODOT --path game` now opens a window with a
  generated map in 3D: flat plateaus at three heights (tan lowland, green upland, pale stone top),
  dark vertical cliff walls, orange sloped ramps with walls at their sides, a sun with shadows and a
  sky. The simulation ticks underneath at 20 ticks per second; a small white label in the top-left
  shows the tick number, game speed, how long the last tick took, and the frame rate. No units are
  drawn yet (next view task).
- **Try it:** arrow keys pan; push the mouse against a window edge to pan; hold the middle mouse
  button and drag to grab the ground; mouse wheel zooms between 20 m and 60 m above the ground.
  The camera can't leave the map. `& $env:GODOT --path game -- --seed 6` shows a map with all three
  heights in view at the start; `-- --speed 4` runs the sim at 4x.
- **Screenshot tool (for the studio and for you):** `& $env:GODOT --path game -- --screenshot
  C:\temp\shot.png --screenshot-after 2` saves a picture after 2 seconds and quits. Every later
  visual task is checked with it, so the studio no longer needs the optional Godot MCP install.
- **Producer decisions, revisit any time:**
  - *Placeholder colours* (three level tints, ramp orange, cliff brown) are hard-coded in the mesh
    builder for now; biome colours and real materials come with the art pass (M6). Change them by
    an inbox note until then.
  - *Camera numbers from the design doc as written:* 55° pitch, no rotation, 20-60 m zoom, 4 m per
    wheel notch, pan speed equal to the zoom height per second. Alternative: a rotating camera; the
    doc rules it out for readability.
  - *The pure camera/clock/mesh logic lives in `sim/Rts.Sim/ViewApi/`* so it is unit-tested
    without Godot. It reads the sim and never writes it (QA proved this: 100 mesh builds between
    ticks leave the game state hash untouched).
- **Rough edges:** ramps look a little steeper at their two ends than in the middle (about 31° vs
  22°), because heights are stored at cell centres; cosmetic, no fix planned unless it bothers you.
  Three tiny input nits (BUG-0041: a `--seed` with no value swallows the next flag; the start-up log
  prints the largest possible seed as -1; a clock corner case no code path reaches) go to the view
  hardening session. The test scene `game/tests/CameraClampTest.tscn` compiles into the game for
  now; it is excluded when the release build exists (M6).

### Replays and a determinism proof that outlives the session (sim track, M1-6, 2026-10-05)

- **What was built:** a replay file format and player. A replay stores the map seed and settings,
  every command the game accepted (with the tick it was given), and a fingerprint of the game state
  every 5 seconds. Playing it back re-runs the match from the seed and checks every fingerprint;
  the first mismatch stops it and names the tick. One "golden" replay is checked in: 200 units of
  every kind marching across the seed-1 map, 75 seconds of game time, 15 fingerprints, a 15 KB text
  file you can read in a diff.
- **What it means for you:** from now on, any change to movement, tick order, random numbers or
  unit stats that changes how a match plays out fails the test suite until a developer regenerates
  the golden on purpose and explains why in the commit. This is the guard that keeps future replays
  and saved games exact, and it's the backbone of the M6 replay viewer.
- **Also fixed (BUG-0014):** two particular seeds (0 and the largest possible number) used to give
  the same map. The seed is now scrambled before use, so every seed is its own map. Side effect:
  **every map changed once.** Seed 1 today is not the seed 1 of yesterday. Nothing you have seen is
  lost (nothing was on screen yet).
- **Producer decisions, revisit any time:**
  - *A replay is tied to the exact game data it was recorded with*, including display text. Change
    one number in `game/data/` and old replays refuse to play (with a clear code, never a silent
    desync). Alternative: hash only balance-relevant fields; rejected for now because a wrong guess
    there means a desync that looks like a bug.
  - *Fingerprints every 100 ticks (5 s)* by default; file size grows ~17 bytes per checkpoint, so a
    30-minute match is under 1 MB even with thousands of orders.
  - *Six movement tests keep running on their old maps* through a helper that undoes the seed
    scramble, because their pass/fail bounds were tuned to one map. QA showed those bounds fail on
    most other maps (BUG-0039, S3, pre-existing): the next sim session, a hardening one, re-measures
    them on many maps.
- **Rough edges:** a hand-edited replay can declare a tick count of two billion and make playback
  spin for hours (BUG-0040, S4; a limit is a few lines, hardening session). When the AI arrives (M5)
  the recording point inside the tick needs a decision (also BUG-0040).

### Two tracks started: the view track begins M2 while the sim track finishes M1 (2026-10-05, session 1446 plan)

- **What changed for you:** from this session the game window stops being an empty scene (see the
  M2-1 entry above: it landed). Units, selection and right-click orders are the next view task (M2-2).
- **Producer decisions, revisit any time:**
  - *Where the view's tests live.* Dev tests in `sim/Rts.Sim.Tests/ViewApi/`, QA tests in
    `sim/Rts.Sim.Tests/QA/ViewApi/`, Godot-side test scenes in `game/tests/`; `ViewApi/` may hold
    pure helpers with no sim reference (fixed-step clock, terrain geometry, camera limits) so that
    logic is unit-tested without Godot. Alternative: put those helpers in `game/scripts/`, where
    nothing can test them. Now also written into the ownership table in docs/07.
  - *The sim track keeps its public setup API additive* (no new required fields on `SimConfig`, no
    signature changes to `Simulation` or the data loader), so the view compiles after both merge.
    Held this session.
  - *First view slice order:* scene + terrain + camera + screenshot flag before unit views, because
    the screenshot flag is how the studio (and you) verify every later visual task.
  - *Bug numbering with two QA inspectors:* both filed a BUG-0039 this session. From now on the
    brief gives each track its own starting number (view = sim + 10). The view's bug is BUG-0041.
- **Docs drift for you to fix (one line, your file):** `CLAUDE.md` still says "Current milestone:
  M1" and "don't write gameplay code ahead of the roadmap". With two tracks that line is per
  track (sim M1, view M2). The agents don't edit `CLAUDE.md`; suggested text: "Current
  milestones: M1 (sim track), M2 (view track)".
- **Cap note:** `autopilot.md` says 8 sessions per day; the old dashboard said 10. The studio
  follows autopilot (8).

### The M1 headline works: an army crosses the map and climbs a ramp (M1-5, 2026-10-05)

- **What was built:** the proof test for M1's main promise. 200 soldiers of every kind (foot,
  cavalry, siege) start near the west edge of a generated map and are sent to the far side of a
  plateau, so they must funnel through a 6 m ramp. On every map tried (58 maps, plus 20 with a
  climb of two ramps, plus armies of 500 and 1,000) everyone arrives, nobody gives up, nobody ends
  up in a cliff, and running the same march twice gives the identical result tick for tick. The
  march takes 72-127 seconds of game time, under half the allowed limit.
- **What you'll see:** select your whole army and right-click across the map: the group streams
  through the ramp as a crowd, the ones pressed against the ramp's walls wait their turn instead
  of quitting, and the whole army arrives and packs up around the click point. Before this
  session, a few units (1-9 per march) got pinned at the corners of ramps and gaps between
  plateaus, stopped, and stayed behind.
- **Producer decisions, revisit any time:**
  - *Waiting in a moving queue doesn't count as stuck.* A unit only starts its 1-second give-up
    timer when nobody just ahead of it is making headway either. Trade-off: a jammed crowd (for
    example behind an enemy blocking a pass) takes a little longer to give up, about 10-14 seconds
    for 60 units instead of 9-10.
  - *Enemy units holding their ground are solid.* QA found that a walker squeezed between two
    standing enemies could slip through a 0.2 m slit (an old bug, made worse by the new patience).
    Now a line of enemies plugging a ramp holds: nobody gets through, everyone behind gives up.
    Your *own* standing units stay "soft" (a walker can still slip between two of them, and can
    shove idle ones aside), because making them solid too cut crowd arrivals in the tests.
- **Rough edges (S3, next hardening session):** if an enemy is standing exactly on the spot you
  clicked, your unit treats it as a friend and stops touching it, a hand's width inside it
  (BUG-0037); and a unit squeezed between an enemy and its own standing comrade can press into the
  comrade by up to 14 cm for a tick (BUG-0038). Neither lets anyone through a plug. Also, with
  500-1,000 units marching, a few pairs end up closer than they should (a known limit of the
  back-off rule).
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ScenarioTests" --logger "console;verbosity=detailed"` prints each seed's path length, ticks taken and the limit. Update M1-6: the same march is now the golden replay (`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`).

### Idle units step aside for walkers (M1-4d-2, 2026-10-05)

- **What you'll see:** soldiers walking through your own idle troops push them out of the way
  instead of stopping. A clustered group bends to let walkers by but doesn't split apart. A lone
  unit parked in a narrow pass steps aside once a friendly walker has been stuck behind it for
  0.5 s. Enemy units and units that are already walking are never pushed. A unit squeezed between
  a standing unit and a cliff can walk away again (BUG-0031 fixed).
- **Producer calls, revisit any time:**
  - Accepted although my own crowd targets were missed: groups sent to 4 nearby points now arrive
    35-44% of the time (was about 16%), against the 60-80% I asked for. The causes are structural
    (pathfinding ignores units; units waiting for a path and enemies act as walls) and were
    outside this task. QA filed them as S2 (BUG-0032/0033); I set them to S3 and scheduled a
    follow-up, M1-4d-3 crowd routing. Update 2026-10-05-1234: that follow-up now waits for the M1
    hardening session (your rule: S3 work goes there), and the cross-map scenario test (M1-5) went
    first because one army walking to one spot doesn't need it (confirmed: it passed without it).
    Update 1446: the hardening session is the next sim session.
  - Units standing on their own click point hold it. So a parked *group* in a 1-cell corridor still
    blocks its own army until you move it (BUG-0033 remainder).
- **Rough edge:** walkers crossing a cluster that mixes both players' units mostly give up (2 of
  200 in a test, 28 before shoving). M1-4d-3 targets this.
- **To watch it:** `dotnet test sim/Rts.Sim.Tests --filter "PastAFriendlyUnitParkedThereByAMove|WalkersCrossingASettledBlob" --logger "console;verbosity=detailed"`.

### Two studio sessions overlapped (incident note, 2026-10-05)

- At about 12:14 a second session (id 2026-10-05-1214) overwrote `studio/.session.lock` in your
  checkout while session 1013 was still running; your three studio-skill commits (724234a,
  432fafe, c089405) went to main meanwhile. The studio worktree was untouched and session 1013
  finished normally. If that second session is still chaining, two conductors may be running.
  Your "Downloads stay with the owner" inbox note was processed at the 1234 PLAN (reflected in
  the M6 queue line). Session 1234 ran on the studio-session skill as loaded at its start; your
  later two-track (sim + view) change to the skill applies from the next session. Update 1446:
  this session is the first on the two-track skill; no second conductor has shown up since.

### Units cluster around the click point, and give up when stuck (M1-4d-1, 2026-10-05)

- **What you'll see:** select 30 soldiers and right-click a spot. They walk there and pack into a
  tight cluster centered on the click, shoulder to shoulder, rather than lining up in a formation.
  Two units meeting in a narrow pass both step to their right and slip past each other.
  Spam-clicking the same spot doesn't make them restart or stutter.
- **Giving up:** a unit that makes no headway for 1 second (wedged behind a crowd, or blocked by
  standing units) stops and stands idle instead of jittering in place forever. Update M1-5: waiting
  behind a groupmate who is still moving no longer counts as "no headway".
- **Producer decisions:**
  - Clusters instead of formations for now: StarCraft-style clumping, where Age of Empires uses
    line or box formations. Formations can come back in M2 with group commands.
  - 1 second before giving up. Shorter feels snappier but units stop short more often; longer
    means more pushing and shoving before they quit.
  - Clicking the spot a unit is already heading to counts as the same order.
- **Rough edges until the next session (shoving):** units that are standing still never step
  aside yet. So two groups sent to spots close together bump into each other's clusters and many
  stop short: in a stress test, 84% of 500 units sent to 4 nearby points gave up 8-16 m early
  (BUG-0028). A unit squeezed between a standing unit and a cliff can't walk away (BUG-0031).
  Both should go away once idle units make room for moving ones. (Update: shoving landed in
  M1-4d-2; BUG-0031 fixed, BUG-0028 partly, see the entry above.)
- **Also open:** a short nudge order (under about 2.8 m, inside the same 2 m map cell) to a unit
  that has already arrived is ignored (BUG-0030). It's in the debt backlog.

### Pathfinding: shared arrow maps with a speed limit (M1-4b and M1-4c, 2026-10-04)

- **What it is:** when you send units somewhere, the game computes one map of arrows for that
  destination: every 2 m square points along the shortest route there. Every unit headed to that
  spot follows the same map, so moving 200 units costs about the same as moving one. The game
  remembers recent destinations (32 to 128, depending on how many units the game allows), so
  re-ordering to a recent spot is instant.
- **Speed limit (Producer decision):** at most 2 new arrow maps per tick, which is 40 per second,
  oldest orders first. Each takes about 0.7 ms to build, and the limit keeps every tick inside its
  time budget so the game never hitches.
- **What you'll see:** nothing in normal play. Only in a big burst: order 32 separate groups to 32
  different spots at the same instant and the last group starts walking about 0.8 s after the first.
- **Why it's saved with the game (Producer decision):** which arrow maps are remembered decides
  which units wait a tick, so that memory is saved and checked like the rest of the game state.
  That keeps replays and saved games exact.
- **Rough edges (debt backlog):** with more separate destinations active at once than the game
  remembers (only in very large games with many small groups), some older groups can stall while
  newer ones walk (BUG-0025). In same-instant order bursts, one side of the map gets served
  0.1-0.2 s sooner on average (BUG-0026, cosmetic).

### Terraced maps: plateaus, cliffs and ramps (M1-3 and M1-4a, 2026-10-03)

- **What you'll see:** maps have up to three height levels (ground, 4 m and 8 m up), each a flat
  plateau. Plateau edges are cliffs no unit can climb. The only way up is a ramp, 6 m wide and 8 m
  long with a gentle slope, entered only at its top or bottom because its sides are walled. Ramps
  are natural chokepoints, wide enough for about 7 foot soldiers or 4 horsemen side by side, so a
  few defenders can hold one. In M4 the high-ground rule raises the stakes: units below can't see
  up onto a plateau. Update M2-1: you can now see these in the game window.
- **Cliff edges (Producer decision):** the outer 2 m strip of each plateau counts as cliff, so
  units on high ground stand about one step back from the visible edge. The alternative (walls
  between squares) would let them stand right at the lip but makes pathfinding more complex.
- **Safety nets:** the map's outer ring is impassable, and any patch of ground no ramp reaches is
  sealed off, so a unit can never be stranded. The generator re-rolls a layout that comes out too
  blocked (under half walkable) or missing a height level, up to 8 times (Producer decision), then
  takes the best one, so map generation can't hang. A normal map takes about 4 ms to make.
- Also added: a fast "who's near me" lookup grid that movement and later combat use. Nothing to
  review there.

### First real game data (M1-2, 2026-10-03)

- **What it is:** the Malazan and Whirlwind rosters (7 units each, stats from the faction pages),
  the damage-type table and the economy rules now live as JSON in `game/data/`. The game checks
  every file when it starts and lists every mistake at once.
- **Values the docs didn't specify (Producer decision, first pass):**
  - **Body size:** 0.4 m radius for foot soldiers, 0.7 m for cavalry, 0.9 m for siege. This is the
    one you'll notice most: it decides how tightly crowds pack and how many units fit through a
    ramp at once.
  - Per-unit attack wind-up times, faction color palettes, and a placeholder for "requires Age II".
- **To change them:** edit the numbers in `game/data/factions/<faction>/units.json`, or ask the
  studio. No code changes are needed. Update M1-6: a data edit also means regenerating the golden
  replay (the studio does this; it's one command).

### Optional: Godot MCP server (M0)

- A tool that lets Claude launch the game and take screenshots more easily. Not needed: since
  M2-1 the game has its own `--screenshot` flag, which covers the studio's need.

## Requests for the sim track

What the view track needs from the sim and can't add itself (the Producer plans these for the sim
track right after S1/S2 bugs). None yet. Expected: `Stop`, `HoldPosition`, `AttackMove` command
kinds and shift-queued orders (M2-3); a "units in a world rectangle" query if the spatial hash's
public surface isn't enough for box selection (M2-2 may add it read-only in `ViewApi/`); public
read access to a cached flow field's directions for the debug overlay (M2-5); a `DataError.ToString()`
that doesn't print `: :` for empty file/path fields (BUG-0041 note, fold into the M3 data task).

## Feature queue: sim track (feature sessions, in order)

1. **Hardening session (now, 4 / 4):** see the sim debt backlog; batch to the size budget.
2. M1-7 perf test: 500 moving units, average tick < 4 ms; document maps > 256 as unsupported
   (BUG-0023). Watch the unenforced 1,000-walkers-crossing-1,500-blob row (3.1-3.4 ms).
3. M1-8 headless CLI in `tools/` printing hashes and timings (reuse `ReplayPlayer`; can record and
   play `.replay` files). Then the M1 end-of-milestone hardening session (if the one above didn't
   cover it) and sign-off.
4. M3 sim side: resource entities, gather/return loop, building placement and construction,
   production queues, Age II, full Malazan and Whirlwind data. Plus any **Requests for the sim
   track** above, which outrank M3 work.
5. M6 (far ahead): agents can't download. The Producer lists under "Waiting on you", when M5
   starts, the exact links for the Godot 4.7.2 .NET export templates and the Kenney/KayKit/Quaternius
   packs (docs/04) with the `asset-sources/` folder for each (owner note 2026-10-05).

## Feature queue: view track (feature sessions, in order)

1. **M2-2 (next):** placeholder unit views (pooled primitives, team colors from faction data,
   positioned by lerp(`PrevPosition`, `Position`, `SimRunner.Alpha`)), 100-200 units spawned at
   start through `SpawnUnit` commands; click / box / shift-add selection with a ground-plane ray
   (no physics bodies); right-click move through `Command.Move`. Ticks the `SimRunner` criterion.
   Needs nothing new from the sim unless box selection wants a rectangle query (read-only ViewApi).
2. M2-3: A attack-move (moves only), S stop, H hold, shift-queue; double-click type select, control
   groups, Tab subgroups. Needs new command kinds → request for the sim track first.
3. M2-4: minimap (click to move camera, right-click orders).
4. M2-5: debug overlay (nav grid, flow arrows, tick time graph); `SimRunner` interpolation check.
5. M2-6: placeholder audio for select and command (generated tones or CC0 already in repo; no
   downloads by agents).
6. M2-7: playable check, 100 placeholder units at 60 FPS → M2 sign-off (with the M2 hardening
   session). Trees and rocks as MultiMesh wait for M3's resource entities.

## Debt backlog: sim track (hardening sessions only; next one is the next sim session)

- **M1-4d-3 crowd routing (S3, most valuable, ~800 lines, QA full):** flow-field crowd cost or local
  detour so walkers go round other groups' blobs; un-anchored units walk back; no stuck ticks
  against walkers blocked only by field-waiting units; parked groups yield sideways in chokes.
  Closes BUG-0028/0032/0033. Re-measure the M1-4d-2 criterion-6 rows and the mixed-owner crossing
  (seed 73: 2/200); if a target is missed again, re-set it in docs/03 with the reason. **Any
  movement change regenerates the golden replay (`RTS_REGEN_GOLDEN=1`, explain in the commit).**
- **BUG-0037 (S3) + BUG-0038 (S3), same code as above:** groupmate checks in `Plan`/`WallLimit`/
  `AimCovered`/`RecheckAnchors` must also compare `Owner`; the hard-wall fallback must keep friendly
  clips. Skipped repros in `QA/HardWallQaTests`. Re-measure the two-owner cross-map rows after.
- **BUG-0034 (S3) + BUG-0039 (S3), single-map bounds:** `Perf_500Units_DistinctTargets...(32)` flaky
  (10-tick average, 1 in 3 runs): average more ticks or use a median. `MoreGoalsThanCacheSlots_...
  _AtMost22PercentGiveUp` and the `Crowd_ToOneOrFourClosePoints(2500, 4, ...)` 33% row hold on one
  map only (QA sweep: 25/40 and 22/40 new-seed maps fail the pack rule / 22%): re-measure over many
  maps after crowd routing, set bounds that hold on all of them or make the rows report-only, and
  retire `TestSeeds.PreMix` from them (new seeds).
- BUG-0040 (S4) part 1: format limit on `ticks` and `checkpoint-interval` (e.g. 24 h = 1,728,000
  ticks) in `Replay.Validate`; un-skip `QA/ReplayQaTests.AbsurdTickCount_IsRefusedAtRead`. Part 2
  (in-tick AI enqueue vs the phase-14 checkpoint) is a design note for M5.
- BUG-0030 (S3) Move within an arrived unit's own goal cell ignored (same-order rule by
  `ArrivalDistance` of the stored `Goal`).
- BUG-0025 (S3) evict the live field with the newest order + BUG-0026 (S4) rotate same-tick tie-break.
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256 (time-sliced builds or document the cap at M1-7).
- Loader: BUG-0008 (S3) duplicate JSON keys, BUG-0010 (S4) faction slots, `DataError.ToString()`
  empty-field formatting; fold into the M3 data task.
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf: 2,500 units in one tight blob 3.8 ms avg / 12.7 ms worst (Debug); 500 and 1,000 inside
  budget (0.14 / 0.55 ms crossing). Replay: 1,000 units x 2,500 ticks records in 6.0 s (Debug).
- Known limits in docs/03: crossing a settled mixed-owner blob mostly gives up (seed 73: 2/200);
  stopped units can overlap > 40%; back-off-limit pairs at 500-1,000 units; enemies Moving-but-standing
  keep the single clip; `NavGrid.Version` not hashed (must join the hash at M3); generator pinned by
  15 hashes + QA oracles; enqueue stamps `TickNumber + 1`; `SimInfo.Version` recorded in replays but
  not checked; .NET 8 support ends 2026-11-10, move to the next LTS at M6.

## Debt backlog: view track (hardening sessions only)

- BUG-0041 (S4): `FixedStepClock.Advance` should reject `delta <= 0` and `speed <= 0` separately;
  `LaunchOptions.Parse` must not consume a value starting with `--`; `Match` should log
  `unchecked((ulong)Seed)`. Un-skip the `FixedStepClockQaTests` row. Few lines; fold into M2-2 if
  the task touches those files.
- Dev test gap: `TerrainMeshBuilderTests` miss a dropped-wall mutant (only `QA/ViewApi/TerrainMeshQaChecker`
  catches it); add a wall-count assertion on the default map.
- Export hygiene (M6): exclude `game/tests/` from the release build; load `game/data/` in a way
  that works from a `.pck` (`FileAccess` or copy data next to the exe) instead of
  `ProjectSettings.GlobalizePath("res://data")`.
- Cosmetic: ramp ends ~31° vs 22° mid-ramp (heights at cell centres); no wall skirts on the map
  border (invisible on generated maps); 1024² mesh 904 MiB transient (outside supported sizes).
- Optional M0 item: Godot MCP server (owner install; not needed since `--screenshot`).

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-05 | [2026-10-05-1446](sessions/2026-10-05-1446.md) | sim M1-6 replays + golden + BUG-0014; view M2-1 match scene, terrain mesh, camera, screenshot flag | both ACCEPT, 0 fix rounds (QA PASS_WITH_ISSUES x2: 1 S3 + 2 S4 filed, BUG-0014 fixed); first two-track session |
| 2026-10-05 | [2026-10-05-1234](sessions/2026-10-05-1234.md) | M1-5 cross-map scenario test + queued-walker give-up rule + BUG-0035 enemies as hard walls | ACCEPT after 1 fix round (QA FAIL then PASS_WITH_ISSUES; S2 + S4 fixed in-session, 2 S3 filed) |
| 2026-10-05 | [2026-10-05-1013](sessions/2026-10-05-1013.md) | M1-4d-2 shoving of idle units + BUG-0031 fix + re-tightened assertions | ACCEPT after 2 fix rounds (QA FAIL on crowd targets; S2 x2 re-triaged S3, 1 S3 filed, 1 S3 fixed) |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (hardening) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
