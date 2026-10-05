# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-05 (session 2026-10-05-1013, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | none (next PLAN picks; recommended M1-4d-3 crowd routing, see handoff) |
| Gate | **GO** (chain to the next session) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-05-1013` @ b447ac7 (0 warnings, 0 errors); ACCEPT, merge pending |
| Tests | 1014 passed / 11 skipped / 1025 in one process (1 m 53 s; known-bug skips: BUG-0005, 0008 x2, 0010 x2, 0014, 0023 x2, 0025, 0030, 0033 pair); `tools/qa/smoke.ps1` PASS. Quick loop: `--filter "Category!=Perf&Category!=Soak"` |
| Open bugs | 13 (S1: 0, S2: 0, S3: 9, S4: 4) — none block |
| Sessions today | 2 / 10; feature sessions since last hardening: 2 / 4 |
| Last session | 2026-10-05-1013 · M1-4d-2 shoving + BUG-0031 · ACCEPT after 2 fix rounds (QA FAIL x3 on crowd targets; Producer re-triaged S2 → S3) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 4 / 8 (sim core, data loader, terrain + nav grid + spatial hash, flow fields + local movement incl. shoving) | In progress |
| M2-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. Nothing is on screen yet (graphics arrive in M2), so "what you'd see" describes how
it will play. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

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
    follow-up, M1-4d-3 crowd routing, before the scenario test.
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
  Your "Downloads stay with the owner" inbox note is processed at the next PLAN (already
  reflected in the M6 queue line).

### Units cluster around the click point, and give up when stuck (M1-4d-1, 2026-10-05)

- **What you'll see:** select 30 soldiers and right-click a spot. They walk there and pack into a
  tight cluster centered on the click, shoulder to shoulder, rather than lining up in a formation.
  Two units meeting in a narrow pass both step to their right and slip past each other.
  Spam-clicking the same spot doesn't make them restart or stutter.
- **Giving up:** a unit that makes no headway for 1 second (wedged behind a crowd, or blocked by
  standing units) stops and stands idle instead of jittering in place forever.
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
  Both should go away once idle units make room for moving ones.
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
  up onto a plateau.
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
  studio. No code changes are needed.

### Optional: Godot MCP server (M0)

- A tool that lets Claude launch the game and take screenshots more easily. Not needed until M2,
  when there's something on screen, and the game will have its own screenshot option anyway.

## Feature queue (feature sessions, in order)

1. M1-4d-3 crowd routing: flow-field crowd cost (or local detour) so walkers go round other
   groups' blobs; un-anchored units walk back; no stuck ticks against walkers blocked only by
   field-waiting units; parked groups yield sideways in chokes. Closes BUG-0028/0032/0033. Carry
   the M1-4d-2 criterion-6 targets and re-measure the same rows.
2. M1-5 scenario test: 200 units across 128x128 with obstacles, all arrive, none stuck or in
   blocked cells.
3. M1-6 replay format + determinism test + one golden replay (fix BUG-0014 seed mixing in the same
   task: it changes every map hash, so do it before the golden exists).
4. M1-7 perf test: 500 moving units, average tick < 4 ms; document maps > 256 as unsupported (BUG-0023).
5. M1-8 headless CLI in `tools/` printing hashes and timings. Then the M1 hardening session and sign-off.
6. M6 (far ahead): agents can't download. Before M6 the Producer lists under "Waiting on you" the
   exact links for the Godot 4.7.2 .NET export templates and the Kenney/KayKit/Quaternius packs
   (docs/04) with the `asset-sources/` folder for each, well before they block work (owner note 2026-10-05).

## Debt backlog (hardening sessions only; next one after 2 more feature sessions or at M1 end)

- BUG-0034 (S3) `Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(32)` flaky (10-tick
  average; 1 in 3 full runs): average more ticks or use a median, keep the 4 ms budget.
- BUG-0030 (S3) Move within an arrived unit's own goal cell ignored (same-order rule by
  `ArrivalDistance` of the stored `Goal`).
- BUG-0025 (S3) evict the live field with the newest order + BUG-0026 (S4) rotate same-tick tie-break.
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256 (time-sliced builds or document the cap at M1-7).
- Loader: BUG-0008 (S3) duplicate JSON keys, BUG-0010 (S4) faction slots; fold into the M3 data task.
- BUG-0014 (S4) seed mixing: goes with M1-6 (feature) because of the golden hashes.
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf: 2,500 units in one tight blob 2.6 ms avg / 10.4 ms worst (Debug); 500 and 1,000 inside budget.
- Known limits in docs/03: crossing a settled mixed-owner blob mostly gives up (seed 73: 2/200);
  stopped units can overlap > 40%; `NavGrid.Version` not hashed (must join the hash at M3);
  generator pinned by 15 hashes + QA oracles; enqueue stamps `TickNumber + 1`; Godot MCP at M2;
  .NET 8 support ends 2026-11-10, move to the next LTS at M6.

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-05 | [2026-10-05-1013](sessions/2026-10-05-1013.md) | M1-4d-2 shoving of idle units + BUG-0031 fix + re-tightened assertions | ACCEPT after 2 fix rounds (QA FAIL on crowd targets; S2 x2 re-triaged S3, 1 S3 filed, 1 S3 fixed) |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
