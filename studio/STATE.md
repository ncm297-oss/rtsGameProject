# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-05 (session 2026-10-05-0742, ACCEPT)._

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
| Current task | next: **M1-4d-2 shoving + BUG-0028/0030/0031 + re-tighten assertions** (closes roadmap criterion 4); then BUG-0005 + BUG-0025/0026 small task, M1-5 scenario test |
| Gate | **GO** (next session starts when the conductor is ready) |
| Tools on this PC | Godot 4.7.2 .NET, .NET SDK 8.0.425, Git 2.53 + LFS 3.7; `GODOT` user variable set |
| Build | green on `studio/2026-10-05-0742` @ 40d4a46 + ACCEPT commit 150ceda (0 warnings, 0 errors); merged to `main` 2026-10-05 (owner-approved; docs/01 conflict resolved by keeping both rows) |
| Tests | 962 passed / 11 skipped / 973 in one process (~2 min; known-bug skips: BUG-0005, 0008 x2, 0010 x2, 0014, 0023 x2, 0025, 0030, 0031 x3); `tools/qa/smoke.ps1` PASS. Quick loop: `--filter "Category!=Perf&Category!=Soak"` |
| Open bugs | 11 (S1: 0, S2: 0, S3: 8, S4: 3) — none block |
| Sessions today | 1 / 10 |
| Last session | 2026-10-05-0742 · M1-4d-1 separation, crowded arrival, give-up, adjacent-cell steering · ACCEPT (1 fix round) |

## Milestone progress

| Milestone | Criteria met | Status |
| --- | --- | --- |
| M0 | 7 / 7 required | **Done** 2026-10-03 (optional MCP item open) |
| M1 | 3 / 8 (+ criterion 4: flow fields, cache, Move, build cap, separation, crowded arrival, give-up, steering done; shoving open) | In progress |
| M2-M9 | — | Planned |

## Backlog (outside the current task)

- M1-4d-2 shoving of idle units (standing units step aside for walkers); BUG-0030 same-order rule
  by `ArrivalDistance` of the stored `Goal`, not goal cell; BUG-0031 `Constrain` push-out vs cliffs;
  re-tighten `BuildCap_500UnitsWith500DistinctGoals_EveryUnitArrives_NoDeadlock` (name is false:
  59/500 give up) and `MoreGoalsThanCacheSlots` (41/128); consider not counting stuck ticks for
  units waiting on a field (BUG-0028).
- BUG-0005 per-player command buckets + BUG-0025 (S3) / BUG-0026 (S4) eviction by newest order
  and rotated tie-break: one small task after 4d-2, before M5.
- Perf: 2,500 units in one tight blob average 3.8 ms / worst 12.4 ms (Debug); 500 and 1,000 are
  inside budget. Separation cost is query-dominated in crowded buckets; revisit if M1-7 raises the target.
- Then M1-5 scenario test (200 units, obstacles), M1-6 replay + determinism golden (BUG-0014
  first), M1-7 perf test (document maps > 256 unsupported, BUG-0023), M1-8 headless CLI.
- `NavGrid.Version` is not hashed: fine while the grid is immutable; must join the hash when M3
  tree depletion changes passability.
- Generator pinned by 15 hashes + QA oracles; `NavGrid.Flood` relies on the blocked outer ring.
- Loader: BUG-0008 (S3), BUG-0010 (S4) with the M3 data task; follow-ups for buildings, techs,
  abilities, `ai.json`, maps; cross-field checks.
- BUG-0002 (S4): `.sln` Release config maps RtsGame to Debug; fix with `tools/export.ps1` (M6).
- Enqueue stamps `TickNumber + 1` (up to 50 ms input latency); revisit at M2-1 if felt.
- Evaluate a Godot MCP server at M2. .NET 8 support ends 2026-11-10: move to next LTS at M6.

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
