# Bugs

One file per bug, filed by the QA inspector (or anyone). The Producer triages them; open S1/S2
bugs outrank new features.

- Name: `BUG-<nnnn>-<short-slug>.md`, numbered in order (look at the highest existing number).
  Two tracks run at once, so the Producer's brief gives each track its first number for the
  session (sim: next free; view: next free + 10); never reuse a number the other track may take
  (session 1446 collided on BUG-0039; the view's became BUG-0041).
- Status moves `open` → `fixed` (with proof) or `wontfix` (with the Producer's reason).

## Template

```markdown
# BUG-<nnnn>: <title>

| Field | Value |
| --- | --- |
| Severity | S1 / S2 / S3 / S4 |
| Status | open |
| Found | <SESSION_ID>, task <TASK_ID> |
| System | <e.g. pathfinding, economy, data loader, HUD> |
| Fixed by | <commit and regression test, when fixed> |

## Repro
1. <exact steps or the failing test name and command>

## Expected
<what the docs or criteria say should happen>

## Actual
<what happens, with output excerpts>

## Notes
<suspected cause, related bugs>
```

## Index

| Id | Sev | Status | Title |
| --- | --- | --- | --- |
| [BUG-0001](BUG-0001-smoke-exit-code-hides-script-load-failure.md) | S3 | fixed | Headless smoke run exits 0 when the C# script fails to load |
| [BUG-0002](BUG-0002-sln-release-builds-game-debug.md) | S4 | open | RtsGame.sln Release configuration builds RtsGame in Debug |
| [BUG-0003](BUG-0003-simmath-sin-out-of-range-for-huge-angles.md) | S3 | fixed | SimMath.Sin/Cos return values far outside [-1, 1] for huge angles |
| [BUG-0004](BUG-0004-rejected-enqueue-advances-sequence.md) | S3 | fixed | A rejected Enqueue (queue full) still advances the player's sequence counter |
| [BUG-0005](BUG-0005-command-sort-quadratic-under-flood.md) | S3 | open | CommandQueue insertion sort is O(n^2); 10k interleaved commands stall ~107 ms |
| [BUG-0006](BUG-0006-spawn-accepts-non-finite-position.md) | S4 | fixed | SpawnUnit accepts NaN/Infinity positions into sim state |
| [BUG-0007](BUG-0007-loader-accepts-huge-numbers-as-infinity-or-overflow.md) | S3 | fixed | Data loader turns huge numbers into float Infinity or a negative int instead of rejecting them |
| [BUG-0008](BUG-0008-loader-duplicate-json-keys-last-wins.md) | S3 | open | Duplicate JSON keys are silently resolved last-wins |
| [BUG-0009](BUG-0009-loader-null-entries-in-requires-tags.md) | S4 | fixed | Null or blank entries in `requires` / `tags` are copied into GameData |
| [BUG-0010](BUG-0010-loader-does-not-check-faction-slots.md) | S4 | open | A faction with no units, or a missing or doubled template slot, loads clean |
| [BUG-0011](BUG-0011-ramp-sides-walkable-steeper-than-30-degrees.md) | S3 | fixed | Ramp sides are walkable: a unit can step 1.6-3.2 m sideways off a ramp (39-58 degrees) |
| [BUG-0012](BUG-0012-mapgen-int-overflow-passes-validate-then-crashes.md) | S3 | fixed | MapGenParams.Validate and the Heightmap ctor overflow on huge ints; Generate then crashes |
| [BUG-0013](BUG-0013-mapgen-worst-case-params-take-tens-of-seconds.md) | S4 | fixed | Map generation with params Validate allows can take ~35 s |
| [BUG-0014](BUG-0014-seed-max-gives-same-map-as-seed-zero.md) | S4 | fixed | Seed ulong.MaxValue generates exactly the same map as seed 0 |
| [BUG-0015](BUG-0015-mapgen-worst-case-still-slow-with-big-ramps.md) | S2 | fixed | Worst valid map params still take ~45 s (Debug) / ~7 s (Release): ramp size not in the time bound |
| [BUG-0016](BUG-0016-nearest-enemy-returns-nan-positioned-unit.md) | S3 | fixed | SpatialHash.NearestEnemy returns a NaN-positioned unit that QueryRadius excludes |
| [BUG-0017](BUG-0017-flaky-flood-allocation-test.md) | S3 | fixed | `Flood_10000Commands_OneTick_AllocatesNothing` failed once in 11 full suite runs, not reproduced |
| [BUG-0018](BUG-0018-flow-field-cache-thrash-rebuilds-per-unit.md) | S2 | fixed | More than 32 live move goals rebuilds a flow field for every unit every tick (~320 ms/tick) |
| [BUG-0019](BUG-0019-move-to-blocked-cell-full-map-scan.md) | S3 | fixed | Every Move to a blocked cell runs a full-map nearest-passable scan (500 Moves = 63 ms) |
| [BUG-0020](BUG-0020-arrival-across-blocked-corner.md) | S3 | fixed | A unit "arrives" across a blocked corner: arrival is a straight-line check |
| [BUG-0021](BUG-0021-field-cache-contents-change-results-unhashed.md) | S3 | fixed | Since the build cap, flow-field cache contents change movement but the cache is not hashed (breaks save/load later) |
| [BUG-0022](BUG-0022-build-cap-starves-groups-and-favors-low-goal-cells.md) | S3 | fixed | Build cap: re-ordered groups can freeze for good, and low goal-cell indexes always go first |
| [BUG-0023](BUG-0023-single-field-build-exceeds-tick-budget-on-large-maps.md) | S3 | open | One flow-field build on a 512/1024 map blows the 8 ms tick budget (1024: 55 ms) |
| [BUG-0024](BUG-0024-full-suite-perf-asserts-flaky-under-parallel-load.md) | S3 | fixed | Full suite flaky on the workstation: wall-clock Perf asserts fail under parallel load |
| [BUG-0025](BUG-0025-over-capacity-eviction-ignores-order-age-endless-churn.md) | S3 | open | Live goals > cache slots: eviction by goal cell, not order age; older groups stall while newer walk, every build is churn |
| [BUG-0026](BUG-0026-same-tick-ties-favor-low-goal-cells.md) | S4 | open | Same-tick order bursts: the player whose goals have low cell indexes starts 2-4 ticks sooner on average |
| [BUG-0027](BUG-0027-back-off-never-gives-up-units-stay-moving-forever.md) | S2 | fixed | Back-off never counts toward giving up; crowded units stay Moving forever (refused back-off, period-2 oscillation) |
| [BUG-0028](BUG-0028-groups-to-nearby-points-give-up-en-masse.md) | S3 | open | Groups sent to nearby points give up en masse (84-87%); walkers give up against units that are only waiting |
| [BUG-0029](BUG-0029-move-spam-makes-arrived-blob-churn.md) | S3 | fixed | Re-issuing the same Move every tick makes an arrived blob churn indefinitely |
| [BUG-0030](BUG-0030-move-within-arrived-units-goal-cell-ignored.md) | S3 | fixed | A Move to another point of an arrived unit's goal cell is ignored (up to ~2.8 m) |
| [BUG-0031](BUG-0031-unit-overlapping-standing-unit-against-cliff-pinned.md) | S3 | fixed | A unit overlapping a standing unit with a cliff behind it can't walk away in any direction |
| [BUG-0032](BUG-0032-shoving-misses-give-up-targets-even-with-one-owner.md) | S3 | open | Crowds to nearby points arrive 35-44% and random-goal give-ups 6-20%, far from the M1-4d-2 targets (filed S2; Producer: needs unit-aware routing, task M1-4d-3) |
| [BUG-0033](BUG-0033-friendly-unit-parked-by-move-never-steps-aside.md) | S3 | fixed | A parked friendly *group* never steps aside in a 1-cell corridor (lone units fixed in M1-4d-2; filed S2, Producer: task M1-4d-3) |
| [BUG-0034](BUG-0034-distinct-targets-perf-test-flaky-in-full-suite.md) | S3 | fixed | `Perf_500Units_DistinctTargetsInterleavedBySlot_CostPerTick(32)` failed once in a full suite run (4.45 ms vs < 4 ms) |
| [BUG-0035](BUG-0035-walkers-squeeze-between-standing-enemies.md) | S2 | fixed | Walkers squeeze between two standing enemy units (0.6 m deep); 40% of a crowd walks through a 3-unit enemy plug |
| [BUG-0036](BUG-0036-queued-rule-guard-test-does-not-exercise-a-hold.md) | S4 | fixed | The M1-5 "jammed group still gives up" test never exercises a hold; four safety mutants of the queued rule pass the dev suite |
| [BUG-0037](BUG-0037-idle-enemy-holding-walkers-goal-cell-is-no-wall.md) | S3 | fixed | An Idle enemy holding the walker's goal cell counts as its arrived groupmate, so it is no wall (walked 0.12-0.16 m into in one tick) |
| [BUG-0038](BUG-0038-hard-wall-fallback-drops-friendly-clips.md) | S3 | fixed | The hard-wall fallback drops the clips of the walker's own standing units; a walker beside an enemy slides into an anchored friendly |
| [BUG-0039](BUG-0039-more-goals-bounds-hold-on-one-map-only.md) | S3 | fixed | 128 units to 64 neighbouring goals: the pack rule and the 22% give-up bound hold on one map only (25/40 and 22/40 new-seed maps fail) |
| [BUG-0040](BUG-0040-replay-tick-count-unbounded-and-in-tick-enqueue.md) | S4 | open | Replay header has no tick-count limit; phase-14 checkpoints can't match commands enqueued during a tick (M5) |
| [BUG-0041](BUG-0041-view-launch-args-and-clock-input-nits.md) | S4 | fixed | View input nits: clock takes negative x negative as time, a missing flag value eats the next flag, seed max logs as -1 |
| [BUG-0042](BUG-0042-walk-back-shoves-arrived-walker-off-its-goal.md) | S2 | fixed | Walk-back undoes the corridor-pair fix: the pushed pair walks home and shoves the arrived walker 8-24 m back, goal-less |
| [BUG-0043](BUG-0043-reorder-within-goal-cell-makes-walker-give-up.md) | S2 | fixed | One re-order of a walking unit to another point of its goal cell (1.2 m) makes it give up mid-route (regression from the BUG-0030 change) |
| [BUG-0044](BUG-0044-tight-blob-2500-over-slice-perf-target.md) | S3 | fixed | 2,500 tight blob at its 4.5 ms target; two-player rows much slower: contested blob 10.5 ms (2.3x base), 2,500 to 4 points 1.9x (round 2 plug search); crossing +75% |
| [BUG-0045](BUG-0045-shoves-press-friendlies-into-and-through-enemy-plug.md) | S3 | fixed | Shoves press friendly units into and through an enemy plug: fixed for plugs of up to 4 enemies; 5+ (small infantry across a 2-cell choke, 5-cell corridors) still leak (pre-existing) |
| [BUG-0046](BUG-0046-walker-paths-depend-on-neighbor-spawn-order.md) | S3 | open | Walker positions still depend on the neighbors' spawn order (Constrain clip order; pre-existing) |
| [BUG-0047](BUG-0047-m1-4d-3-test-guard-gaps.md) | S4 | fixed | M1-4d-3 test-guard gaps: 2,500 blob perf unguarded, tests ending at first Idle, loose chain-shove checker, >24 h replays unreadable |
| [BUG-0048](BUG-0048-crowd-never-stops-under-field-cache-churn.md) | S1 | fixed | Livelock: 94 of 128 units Moving forever with 2 field builds per tick (64-goal row, map seed 51; regression) |
| [BUG-0049](BUG-0049-64-goal-bounds-break-on-new-maps-stray-anchor.md) | S3 | fixed | The re-bounded 64-goal row breaks on new maps: 49 give-ups on seed 61, a stray anchor on seed 64 |
| [BUG-0050](BUG-0050-random-goal-give-ups-rose-after-fix-round-1.md) | S3 | open | 500 units to 500 random goals: give-ups rose to 4.7% mean (2.3% before fix round 1; target 3%) |
| [BUG-0052](BUG-0052-terrainheight-at-throws-on-huge-coordinates.md) | S3 | fixed | TerrainHeight.At throws instead of clamping for coordinates beyond ~4.3e9 m or +Infinity |
| [BUG-0053](BUG-0053-m2-2-test-and-doc-nits.md) | S4 | fixed | M2-2 nits: facing test can't see a yaw sign error, a stale SimRunner remark, picker box height in docs |
| [BUG-0054](BUG-0054-unknown-command-flags-recorded-replay-write-throws.md) | S3 | fixed | A command with unknown Flags bits is accepted and recorded; ReplayFormat.Write then throws (same for an undefined kind) |
| [BUG-0055](BUG-0055-own-walkers-slip-past-friendly-holding-plug.md) | S3 | fixed | Own walkers slip past a friendly holding unit plugging a 1-cell corridor (10 of 30, up to 1.05 m overlap; soft clip) |
| [BUG-0056](BUG-0056-m1-7-order-nits.md) | S4 | fixed | M1-7 nits: Queued flag valid on Spawn/Noop, corridor Hold test misses passing walkers, Hold lasts one tick under a queued order |
| [BUG-0057](BUG-0057-m1-8-cli-nits.md) | S4 | fixed | M1-8 CLI nits: timings include recorder hashing despite docs, unwritable --record found only after the run, 0-checkpoint replays "pass" |
| [BUG-0058](BUG-0058-give-up-while-backing-off-leaves-stray-anchor.md) | S3 | fixed | A unit that gives up while backing off can stop 1.1 m from its goal and keep its goal cell (stray anchor); found and fixed in M1-9 (`SettleBackedOff`) |
| [BUG-0064](BUG-0064-minimap-dot-readability-and-large-map-sampling.md) | S4 | fixed | Minimap dots hard to tell from ramps (Whirlwind) and cliffs (Malazan); maps over 220 cells drop dots |
| [BUG-0067](BUG-0067-m2-3-input-nits.md) | S4 | fixed | M2-3 nits: an exact 300 ms double-tap depends on the clock value; A-targeting outlives an emptied selection and eats the next click |
| [BUG-0068](BUG-0068-minimap-right-click-while-targeting-orders-move.md) | S3 | fixed | A minimap right-click while A-targeting orders a Move and leaves targeting armed |
| [BUG-0069](BUG-0069-lone-minimap-dots-read-as-rim-colour.md) | S3 | open | A lone minimap dot reads as its rim colour (black or white), not its player colour |
| [BUG-0070](BUG-0070-m2-h1-doc-and-test-nits.md) | S4 | open | M2-H1 nits: stale docs/01 minimap row, docs/03 big-map range, twin double-tap constants, dev wall test blind at the far edges |
| [BUG-0071](BUG-0071-plug-cache-answer-depends-on-query-order-under-stale-hash.md) | S3 | open | In the shove pass a cached plug answer can depend on which member was asked first (stale spatial hash) |
| [BUG-0072](BUG-0072-m1-9-nits.md) | S4 | open | M1-9 nits: an invalid --record file name still fails only after the run |
| [BUG-0073](BUG-0073-continuous-depletion-starves-flow-fields.md) | S3 | open | A tree falling every tick leaves all but the 2 oldest goal groups without a flow field |
| [BUG-0074](BUG-0074-forest-placer-assumes-1x1-trees.md) | S3 | open | The forest placer assumes 1 x 1 trees; a larger tree footprint (valid data) seals pockets |
| [BUG-0075](BUG-0075-felled-interior-tree-leaves-unreachable-hollow.md) | S3 | fixed | Felling a forest's interior tree first leaves an open cell nobody can reach; an order onto it does nothing |
| [BUG-0076](BUG-0076-m3-1-nits.md) | S4 | open | M3-1 nits: setup timing in docs, redundant flood fill, empty resources list loads clean |
| [BUG-0077](BUG-0077-building-placed-mid-walk-makes-units-give-up.md) | S3 | open | A building placed in front of a moving column makes the units behind it give up after 20 ticks |
| [BUG-0078](BUG-0078-exposure-counts-sealed-pocket-cells.md) | S3 | open | Exposure counts an open neighbour nobody can reach; a worker sent to a tree exposed only to a sealed pocket retries for ever |
| [BUG-0079](BUG-0079-m3-2-nits.md) | S4 | open | M3-2 nits: missing cost is three errors, tests over budget, --workers 0 wording |
| [BUG-0083](BUG-0083-debug-overlay-label-allocates-per-frame-docs-claim-zero.md) | S3 | open | Overlay-on label line allocates ~600 B per frame; docs/03 "Debug tooling" claims 0 bytes per frame on and off |
| [BUG-0084](BUG-0084-m2-5-debug-overlay-nits.md) | S4 | open | M2-5 nits: cliff tint reads olive, arrow tips dip up to 9 cm into steep ramps, CS8602 in DebugOverlayTest, blind allocation probe |
| [BUG-0085](BUG-0085-default-match-spawns-army-inside-a-forest.md) | S3 | open | The default match (seed 1) spawns player 0's start army inside a forest |
| [BUG-0086](BUG-0086-m2-3b-props-nits.md) | S4 | open | M2-3b nits: every relist uploads every type's full 4,096-instance buffer; tight minimap perf margin |
