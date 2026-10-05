# Studio state

The dashboard. The Producer rewrites it at the end of every session. **Owner: read "Waiting on
you" first.** "For your review" is non-blocking: things the studio decided or finished on its own.

_Last updated: 2026-10-05 (session 2026-10-05-1013, ACCEPT)._

## Waiting on you

- Nothing blocking. The studio runs on autopilot (chain sessions, self sign-off) until the end
  of the roadmap or a cap/incident stops it.

## For your review

- 2026-10-05 (M1-4d-2, shoving): walkers now push friendly idle units out of their way; a unit
  parked alone in a corridor yields after the walker has been stuck 0.5 s; blobs bend but don't
  split; enemies and moving units are never shoved. A unit pinned against a cliff by a neighbor can
  walk away again (BUG-0031 fixed). **Producer calls, revisit any time:** (1) I accepted the task
  although my own crowd targets were missed (groups to 4 nearby points arrive 35-44%, I asked for
  60-80%): the causes are structural (the flow field ignores units; units waiting for a field and
  enemies are walls) and were outside the task's scope. QA filed them as S2 (BUG-0032/0033); I set
  them to S3 and scheduled a follow-up task M1-4d-3 (crowd routing) before the scenario test.
  (2) Design defaults in docs/01: units standing on their point hold it; a parked *group* still
  blocks a corridor for its own army until moved by hand (BUG-0033 remainder). To watch it:
  `dotnet test sim/Rts.Sim.Tests --filter "PastAFriendlyUnitParkedThereByAMove|WalkersCrossingASettledBlob" --logger "console;verbosity=detailed"`.
- 2026-10-05 **incident note:** at about 12:14 a second session (id 2026-10-05-1214) overwrote
  `studio/.session.lock` in your checkout while session 1013 was running; your three studio-skill
  commits went to main meanwhile (724234a, 432fafe, c089405). The studio worktree was untouched and
  the session finished normally. Your "Downloads stay with the owner" inbox note is processed at the
  next PLAN (already reflected in the M6 line below).
- 2026-10-05 (M1-4d-1): units no longer walk through each other or stack on one point. Producer
  decisions (docs/01): crowded arrival instead of formation offsets for M1; give-up after 1 s; a
  re-issued order to the held goal cell is the same order.
- 2026-10-04 (M1-4c): deterministic, fair flow-field build cap (2 per tick); cache metadata hashed.
- 2026-10-03/04 (M1-2/3/4a/4b): data loader, terraced maps + nav grid, ramps, spatial hash, flow
  fields + LRU cache, `Move`. Producer decision: `MaxAttempts` cap is 8.
- Optional M0 item: Godot MCP server for Claude Code (your install; see SETUP.md). Not needed
  before M2.

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
