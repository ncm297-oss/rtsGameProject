# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" (further down) is non-blocking: what the studio built or decided
on its own, explained in terms of what you'd see in the game.

_Last updated: 2026-10-05 (session 2026-10-05-1013, PLAN; backlog re-ordered per the owner's
"Speed up" note)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## Now

| Field | Value |
| --- | --- |
| Milestone | M1 — Core sim, no graphics (started 2026-10-03) |
| Current task | **M1-4d-2 shoving of idle units + BUG-0031 fold-in + re-tighten assertions** (session 2026-10-05-1013, in progress; closes roadmap criterion 4) |
| Gate | **GO** (session running) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-05-0742` @ 40d4a46 + ACCEPT commit 150ceda (0 warnings, 0 errors); merged to `main` 2026-10-05 (owner-approved; docs/01 conflict resolved by keeping both rows) |
| Tests | 962 passed / 11 skipped / 973 in one process (~2 min; known-bug skips: BUG-0005, 0008 x2, 0010 x2, 0014, 0023 x2, 0025, 0030, 0031 x3); `tools/qa/smoke.ps1` PASS. Quick loop: `--filter "Category!=Perf&Category!=Soak"` |
| Open bugs | 11 (S1: 0, S2: 0, S3: 8, S4: 3) — none block |
| Sessions today | 2 / 10 (this one included); feature sessions since last hardening: 1 / 4 (M1-4c counts as hardening, owner ruling) |
| Last session | 2026-10-05-0742 · M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering · ACCEPT (1 fix round) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 3 / 8 (+ criterion 4: flow fields, cache, Move, build cap, separation, crowded arrival, give-up, steering done; shoving open) | In progress |
| M2-M9 | — | Planned |

## For your review

Non-blocking. Each entry says what was built or decided, what you'd notice in the game, and how
to change it. Nothing is on screen yet (graphics arrive in M2), so "what you'd see" describes how
it will play. To change anything, write it in `studio/inbox.md`, for example "use formations
instead of clusters" or "make giving up take 2 seconds".

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

1. M1-4d-2 shoving (current). Closes criterion 4.
2. M1-5 scenario test: 200 units across 128x128 with obstacles, all arrive, none stuck or in
   blocked cells.
3. M1-6 replay format + determinism test + one golden replay (fix BUG-0014 seed mixing in the same
   task: it changes every map hash, so do it before the golden exists).
4. M1-7 perf test: 500 moving units, average tick < 4 ms; document maps > 256 as unsupported (BUG-0023).
5. M1-8 headless CLI in `tools/` printing hashes and timings. Then the M1 hardening session and sign-off.

## Debt backlog (hardening sessions only; next one after 3 more feature sessions or at M1 end)

- Movement S3: BUG-0030 (Move within an arrived unit's own goal cell ignored: same-order rule by
  `ArrivalDistance` of the stored `Goal`), BUG-0028 leftovers (don't count stuck ticks against a
  walker blocked only by units waiting for a field), BUG-0025 (S3, evict the live field with the
  newest order) + BUG-0026 (S4, rotate same-tick tie-break).
- BUG-0005 (S3) per-player command buckets (O(n^2) insertion sort under a flood); before M5.
- BUG-0023 (S3) single field build > tick budget on maps > 256 (time-sliced builds or document the cap at M1-7).
- Loader: BUG-0008 (S3) duplicate JSON keys, BUG-0010 (S4) faction slots; fold into the M3 data task.
- BUG-0014 (S4) seed mixing: goes with M1-6 (feature) because of the golden hashes.
- BUG-0002 (S4) `.sln` Release config maps RtsGame to Debug; with `tools/export.ps1` (M6).
- Perf: 2,500 units in one tight blob average 3.8 ms / worst 12.4 ms (Debug); 500 and 1,000 are
  inside budget. Query-dominated in crowded buckets; revisit if M1-7 raises the target.
- Notes to keep: `NavGrid.Version` is not hashed (must join the hash when M3 tree depletion changes
  passability); generator pinned by 15 hashes + QA oracles, `NavGrid.Flood` relies on the blocked
  outer ring; enqueue stamps `TickNumber + 1` (revisit at M2-1 if felt); Godot MCP at M2; .NET 8
  support ends 2026-11-10, move to the next LTS at M6.

## Recent sessions

| Date | Session | Task | Result |
| --- | --- | --- | --- |
| 2026-10-05 | [2026-10-05-0742](sessions/2026-10-05-0742.md) | M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering | ACCEPT after 1 fix round (QA: S2 + S3 fixed in-session, 3 S3 open) |
| 2026-10-04 | [2026-10-04-2056](sessions/2026-10-04-2056.md) | M1-4c build-cap determinism + fairness; suite de-flaked | ACCEPT, 0 fix rounds (QA: 1 S3 + 1 S4 filed; 4 bugs closed) |
| 2026-10-04 | [2026-10-04-0120](sessions/2026-10-04-0120.md) | M1-4b GameData in sim + flow fields + LRU cache + `Move` | ACCEPT after 1 fix round (QA: S2 + 2 S3 fixed in-session, 4 S3 open) |
| 2026-10-03 | [2026-10-03-2220](sessions/2026-10-03-2220.md) | M1-4a ramp walls + param safety + spatial hash | ACCEPT after 1 fix round |
| 2026-10-03 | [2026-10-03-1235](sessions/2026-10-03-1235.md) | M1-3 heightmap + nav grid + 2 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-1151](sessions/2026-10-03-1151.md) | M1-2 data loader + 3 bug fixes | ACCEPT |
| 2026-10-03 | [2026-10-03-0907](sessions/2026-10-03-0907.md) | M1-1 sim core | ACCEPT |
| 2026-10-03 | [2026-10-03-0826](sessions/2026-10-03-0826.md) | M0-1 solution skeleton + toolchain | ACCEPT, M0 signed off |
