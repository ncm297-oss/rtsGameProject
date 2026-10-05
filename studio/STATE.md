# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-05 (session 2026-10-05-1013, PLAN; backlog re-ordered per the owner's
"Speed up" note)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-05 (M1-4d-1): units no longer walk through each other or stack on one point. A group
  packs into a blob around the click point (crowded arrival), units push apart and keep right when
  passing, and a unit that makes no progress for 1 s gives up. Producer decisions, revisit any time
  (docs/01 change log): crowded arrival replaces formation offsets for M1 (offsets can return at M2
  with group commands); give-up after 20 ticks; a re-issued order to the same goal cell is the same
  order. To watch it: `dotnet test sim/Rts.Sim.Tests --filter "FiftyUnits_ToOneOpenPoint|HeadOnInOneCellCorridor" --logger "console;verbosity=detailed"`.
  Open S3s from this task (next session): groups sent to nearby points give up too often until
  shoving exists (BUG-0028), a Move within an arrived unit's own cell is ignored (BUG-0030), a unit
  pinned between a standing unit and a cliff can't leave (BUG-0031).
- 2026-10-04 (M1-4c): deterministic, fair flow-field build cap (2 per tick, oldest order first),
  cache metadata hashed; suite green in one `dotnet test` run. Producer decisions: cap of 2; cache
  size formula; BUG-0025/0026 fixed together after M1-4d.
- 2026-10-04 (M1-4b): units move along cached flow fields at their JSON speed.
- 2026-10-03 (M1-4a/3/2): ramps as corridors, spatial hash; terraced maps + nav grid; first real
  game data in `game/data/`. Producer decision: `MaxAttempts` cap is 8.
- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.

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
