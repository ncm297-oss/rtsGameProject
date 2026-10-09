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
| [BUG-0008](BUG-0008-loader-duplicate-json-keys-last-wins.md) | S3 | fixed | Duplicate JSON keys are silently resolved last-wins |
| [BUG-0009](BUG-0009-loader-null-entries-in-requires-tags.md) | S4 | fixed | Null or blank entries in `requires` / `tags` are copied into GameData |
| [BUG-0010](BUG-0010-loader-does-not-check-faction-slots.md) | S4 | fixed | A faction with no units, or a missing or doubled template slot, loads clean |
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
| [BUG-0069](BUG-0069-lone-minimap-dots-read-as-rim-colour.md) | S3 | fixed | A lone minimap dot reads as its rim colour (black or white), not its player colour |
| [BUG-0070](BUG-0070-m2-h1-doc-and-test-nits.md) | S4 | fixed | M2-H1 nits: stale docs/01 minimap row, docs/03 big-map range, twin double-tap constants, dev wall test blind at the far edges |
| [BUG-0071](BUG-0071-plug-cache-answer-depends-on-query-order-under-stale-hash.md) | S3 | fixed (M3-H1) | In the shove pass a cached plug answer can depend on which member was asked first (stale spatial hash) |
| [BUG-0072](BUG-0072-m1-9-nits.md) | S4 | fixed (M3-H1) | M1-9 nits: an invalid --record file name still fails only after the run |
| [BUG-0073](BUG-0073-continuous-depletion-starves-flow-fields.md) | S3 | fixed | A tree falling every tick leaves all but the 2 oldest goal groups without a flow field |
| [BUG-0074](BUG-0074-forest-placer-assumes-1x1-trees.md) | S3 | fixed | The forest placer assumes 1 x 1 trees; a larger tree footprint (valid data) seals pockets |
| [BUG-0075](BUG-0075-felled-interior-tree-leaves-unreachable-hollow.md) | S3 | fixed | Felling a forest's interior tree first leaves an open cell nobody can reach; an order onto it does nothing |
| [BUG-0076](BUG-0076-m3-1-nits.md) | S4 | fixed (M3-H1) | M3-1 nits: setup timing in docs, redundant flood fill, empty resources list loads clean |
| [BUG-0077](BUG-0077-building-placed-mid-walk-makes-units-give-up.md) | S3 | fixed | A building placed in front of a moving column makes the units behind it give up after 20 ticks |
| [BUG-0078](BUG-0078-exposure-counts-sealed-pocket-cells.md) | S3 | fixed (M3-3 never-seal rule; residual BUG-0093) | Exposure counts an open neighbour nobody can reach; a worker sent to a tree exposed only to a sealed pocket retries for ever |
| [BUG-0079](BUG-0079-m3-2-nits.md) | S4 | fixed (M3-3 + M3-H1) | M3-2 nits: missing cost is three errors (M3-3), tests over budget (waived), --workers 0 wording (M3-H1) |
| [BUG-0080](BUG-0080-back-to-back-closing-changes-starve-goal-groups.md) | S3 | open (known limit since M3-H1) | A closing change every p ticks lets only the 2p oldest goal groups walk while it lasts; measured bound in docs/03 "Known limits", the usable-stale fix was left (Producer decision) |
| [BUG-0081](BUG-0081-stale-usable-fields-cannot-be-rebuilt-at-load.md) | S3 | fixed (M3-H1 decision: save the cache contents, M6 implements) | Usable-but-stale flow fields can't be rebuilt from their keys at load (docs/03's save/load plan, M6) |
| [BUG-0082](BUG-0082-m3-2b-felling-tick-over-criterion.md) | S2 | fixed | With a tree felled every tick the 32-group scene averages 0.81 ms a tick, over M3-2b's 0.5 ms criterion; the perf row only bounds it relatively |
| [BUG-0083](BUG-0083-debug-overlay-label-allocates-per-frame-docs-claim-zero.md) | S3 | fixed | Overlay-on label line allocates ~600 B per frame; docs/03 "Debug tooling" claims 0 bytes per frame on and off |
| [BUG-0084](BUG-0084-m2-5-debug-overlay-nits.md) | S4 | fixed | M2-5 nits: cliff tint reads olive, arrow tips dip up to 9 cm into steep ramps, CS8602 in DebugOverlayTest, blind allocation probe |
| [BUG-0085](BUG-0085-default-match-spawns-army-inside-a-forest.md) | S3 | fixed | The default match (seed 1) spawns player 0's start army inside a forest |
| [BUG-0086](BUG-0086-m2-3b-props-nits.md) | S4 | fixed | M2-3b nits: every relist uploads every type's full 4,096-instance buffer; tight minimap perf margin |
| [BUG-0087](BUG-0087-sfx-sound-playing-at-quit-leaks-objectdb-in-existing-scenes.md) | S3 | fixed | A sound still playing at quit leaves an ObjectDB leak warning in existing test scenes (intermittent) |
| [BUG-0088](BUG-0088-orderstest-double-tap-row-flaky-under-cpu-load.md) | S4 | fixed | `OrdersTest.tscn` double-tap row fails under CPU load: process-time wait vs wall-clock tap window (pre-existing, seen at the 1744 ACCEPT) |
| [BUG-0090](BUG-0090-d1-building-text-ahead-of-schema.md) | S4 | open | D1 building descriptions promise tower attack/detection the schema lacks (comment item fixed in M3-3, requirements item in D3, sight item in M4-3a + D7) |
| [BUG-0091](BUG-0091-refused-builds-each-run-the-seal-flood.md) | S3 | fixed (M3-H1; residual BUG-0096) | Refused Builds each run the never-seal flood before the cheap checks; 100 in one tick cost 22 ms |
| [BUG-0092](BUG-0092-m3-3-nits.md) | S4 | fixed (M3-H1; residual BUG-0095) | M3-3 nits: tiny repair factor rounds to 0, a holding worker can't build on its spot, push-out fallback stacks units |
| [BUG-0093](BUG-0093-cancel-pocket-reopens-bug-0078.md) | S3 | fixed (M3-H1) | A cancelled walled-in site leaves a pocket; BUG-0078's stuck worker is reachable with player commands |
| [BUG-0101](BUG-0101-bench-order-across-moves-army-only-15-m.md) | S3 | fixed | `--bench` "order across the map" sends the army about 15 m, to the enemy start block next door |
| [BUG-0102](BUG-0102-bench-fps-field-biased-low-by-load-second.md) | S3 | fixed | The `bench:` line's `fps` reads low on short runs (averages Godot's once-a-second counter, first sample is the load second) |
| [BUG-0103](BUG-0103-m2-7-bench-nits.md) | S4 | fixed | M2-7 nits: vsync-on rows report smoothed deltas, "no bunching" remark, culture in two log lines, endless huge `--bench` |
| [BUG-0104](BUG-0104-bench-march-under-20-m-on-seed-21.md) | S3 | fixed | The 10 s bench moves seed 21's army centre only 19.1 m (QaM27's 20 m bound fits seed 1, not the map family) |
| [BUG-0105](BUG-0105-m2-h2-nits.md) | S4 | fixed | M2-H2 nits: minimap refresh row at 92-94% of its 0.3 ms limit (docs say 0.25 ms), Sfx exit-wait range in docs/03 |
| [BUG-0111](BUG-0111-d2-page-pin-gaps.md) | S4 | fixed | D2 page pins miss a false "+N pop" claim and Provides free-text edits; culture-dependent range text in UnitContentTests.G |
| [BUG-0132](BUG-0132-d3-pin-failure-messages.md) | S4 | fixed (D4) | D3 tech pins: some one-sided-edit failure messages omit the values or blame the page; "needs" reader ignores "requires" / "after" wording |
| [BUG-0094](BUG-0094-test-grove-spawns-skip-the-seal-check.md) | S4 | open | Hand-built test groves (`ResourceStore.Spawn`) skip the never-seal check and wall cells in before tick 1 |
| [BUG-0095](BUG-0095-push-out-on-a-full-level-scans-the-whole-map.md) | S3 | fixed (M3-H2) | Push-out on a level with too few free cells scans every ring of the map per leftover unit (9 ms at 128, 35 ms at 256) and stacks them on one point |
| [BUG-0096](BUG-0096-builds-refused-for-sealsground-each-flood.md) | S3 | fixed (M3-H2) | Builds refused for SealsGround still pay a flood each: 100 in one tick cost 33 ms (BUG-0091 residual) |
| [BUG-0097](BUG-0097-spawn-and-push-out-cross-to-another-plateau-of-the-same-level.md) | S3 | fixed (M3-H2) | A spawn (or push-out) on a full plateau lands on another plateau of the same level, 30+ m away |
| [BUG-0106](BUG-0106-m3-v1-tree-gather-bound-unmet-from-start.md) | S3 | wontfix (criterion reworded, 0925) | Right-click on the nearest tree from the start: workers reach Gathering at tick 123, not within 60 (criterion 3 wording vs walk time) |
| [BUG-0107](BUG-0107-m3-v1-nits.md) | S4 | fixed | M3-V1 nits: Malazan site vs finished colour, tiny wood cube at 60 m, minimap resource redraw on every building change |
| [BUG-0098](BUG-0098-m3-5-empty-tech-filter-matches-every-unit.md) | S3 | fixed | An empty `units` or `tags` filter in a tech effect silently matches every unit (the other faction's too) |
| [BUG-0099](BUG-0099-m3-5-tech-data-nits.md) | S4 | fixed (items 1, 3 M3-6; item 2 M3-H2) | M3-5 tech data nits: `requires` cycles load, an effect matching no unit loads, a tech id may equal a building id |
| [BUG-0108](BUG-0108-right-click-on-a-building-box-top-picks-the-ground-behind-it.md) | S3 | fixed | A right click on the visible top of a building's box picks the ground behind it: half of a damaged hall's top gives a Move, not a Repair |
| [BUG-0109](BUG-0109-ghost-click-ignores-the-click-position.md) | S3 | fixed | A placement click ignores where it lands: it builds at the last frame's drawn anchor, or is swallowed if that one was red |
| [BUG-0110](BUG-0110-ui-json-non-object-root-loads-empty.md) | S3 | fixed | ui.json whose root is not an object (`[]`, `null`, a number or a string) loads with no error and blank labels |
| [BUG-0122](BUG-0122-m3-v2-nits.md) | S4 | fixed | M3-V2 nits: mid-word wrap on "Quartermaster's Depot", Shift-click floods duplicate Builds, ghost lags a panning camera, small reason text |
| [BUG-0100](BUG-0100-m3-6-unmeetable-requirements-load.md) | S3 | fixed (M3-H2) | Requirements that can never be met load clean (another faction's building, an any-of only its own tech opens) |
| [BUG-0112](BUG-0112-m3-6-d3-building-requires-break-sim-tests.md) | S3 | fixed (M3-H2) | With D3's building requires merged, 25 of the sim's tests fail (construction fuzz, never-seal, requirement fuzz): merge hazard for the data track |
| [BUG-0113](BUG-0113-m3-6-nits.md) | S4 | open | M3-6 nits: type-mismatch errors read "malformed JSON ... Nullable`1[Int32]", the 10k-unit load test now times a failing load |
| [BUG-0123](BUG-0123-selection-panel-allocates-every-tick-under-repair.md) | S3 | fixed | The selection panel allocates a string every tick while the selected building is repaired (~52 B/tick); a greyed production button looks enabled |
| [BUG-0124](BUG-0124-m3-v3-tests-queue-age-ii-without-halls.md) | S2 | fixed | M3-V3's tests queue Age II at a Town Hall with no halls; merged with M3-6 the view branch is red and `main`'s smoke gate fails until it lands |
| [BUG-0133](BUG-0133-push-out-leftover-offsets-repeat-after-24-passes.md) | S4 | open | Push-out leftovers share a point once a cell takes more than 24 (offsets repeat); docs/03 says no two share a point |
| [BUG-0134](BUG-0134-building-requiring-tech-researched-only-at-its-own-slot-loads.md) | S3 | open | A building requiring a tech researched only at its own slot (Armory requires Melee Weapons) loads clean and can never be built |
| [BUG-0125](BUG-0125-resource-right-click-column-takes-open-ground-behind-a-tree.md) | S3 | fixed | The resource right-click pick treats a tree as a full 2 x 2 x 3.5 m column, so a click on open ground just north of a tree or mine targets the node |
| [BUG-0126](BUG-0126-m3-v3b-nits.md) | S4 | open (items 1-2, 4 fixed) | M3-V3b nits: a locked building's ghost reads "Needs more", Age II reads "Locked" while queued / researched after a hall dies, props relist on building changes, smaller leftovers |
| [BUG-0135](BUG-0135-m4-1-pre-m4-two-player-scenes-now-fight-suite-red.md) | S1 | fixed | M4-1 turns 194 pre-M4 test rows red: two-player scenes now fight (suite red) |
| [BUG-0136](BUG-0136-smoke-fails-ui-json-missing-states-attacking.md) | S1 | fixed on merge | Headless smoke FAILS: `ui.json: missing states.attacking` (new `UnitState.Attacking`) |
| [BUG-0137](BUG-0137-chase-livelock-unreachable-or-detoured-targets.md) | S2 | fixed | Chasing a target the unit cannot reach (or only by a detour) never ends: attack-moves stop for good, Idle units dither forever |
| [BUG-0138](BUG-0138-site-under-construction-damage-undone-by-build-tick.md) | S2 | fixed | A site under construction with a builder is effectively invulnerable: every build tick resets its hit points from progress |
| [BUG-0139](BUG-0139-battering-ram-attacks-units.md) | S3 | fixed | The Battering Ram attacks units; the Whirlwind page says it attacks buildings only |
| [BUG-0140](BUG-0140-tightblob2500-row-flaky-after-m4-1.md) | S3 | fixed | `TightBlob2500_OnePlayer` (4.5 ms) now fails about 1 run in 6 after M4-1 (+0.08 ms) |
| [BUG-0141](BUG-0141-retaliation-never-ends-when-anchor-cell-is-blocked.md) | S3 | fixed | A retaliation whose anchor cell becomes blocked never ends (mode stays `Retaliate` / `AttackMove` for good) |
| [BUG-0142](BUG-0142-m4-1-nits.md) | S4 | open (items 3-4 fixed) | M4-1 nits: CanPlace vs Build with an Attacking worker, slot scan phase decides in-reach duels |
| [BUG-0143](BUG-0143-max-give-ups-cap-stalls-brawls.md) | S2 | fixed | The `MaxGiveUps` cap stalls every brawl: units that gave up three crowd-blocked chases stand Idle beside reachable enemies |
| [BUG-0144](BUG-0144-chasers-field-wait-under-placement-churn.md) | S3 | open | Chasers wait up to 1.7 s for a flow field when buildings are being placed (BUG-0080 threshold 1 s) |
| [BUG-0145](BUG-0145-m3-playable-retask-pulls-gold-miners-budget-overrun.md) | S2 | fixed | M3PlayableTest's idle re-task pulls working gold miners off the mine; seed 1 overran the 16,000-tick budget in 1 of 4 runs |
| [BUG-0146](BUG-0146-laborers-wedge-gathering-out-of-reach-of-a-tree.md) | S2 | fixed | Laborers wedge in Gathering out of reach of a tree (sim); wood income stops for the rest of the match |
| [BUG-0147](BUG-0147-view-breaks-on-m4-1-merge.md) | S3 | fixed | The view on top of M4-1: smoke fails (no `states.attacking`), 7 of 27 scenes fail, the M3 Playable proof among them |
| [BUG-0148](BUG-0148-m3-v4-nits.md) | S4 | partly fixed (2, 3, 4) | M3-V4 nits: ~1 % facet-sliver picks, "+1" colour and rally walk unchecked, no replay on a non-step exception |
| [BUG-0149](BUG-0149-stall-count-kept-across-target-switch-gives-up-reachable-enemy.md) | S3 | open | A stall count carried across a target switch makes a chaser give up a reachable enemy behind a short detour and stand Idle in sight of it |
| [BUG-0150](BUG-0150-retaliator-ping-pongs-between-two-targets-forever.md) | S2 | fixed | A retaliator ping-pongs between two targets forever (give-up never builds); four movement rows switched to combat off |
| [BUG-0151](BUG-0151-gather-perf-row-up-40-percent-after-wedge-fix.md) | S3 | open (Producer re-triage S2 → S3: crowd cost, criterion re-stated) | After the BUG-0146 fix the 200-worker gather Perf row costs ~40 % more per tick (0.32 of 1 ms; per-walker cost unchanged, denser crowd at the nodes) |
| [BUG-0152](BUG-0152-reissued-attack-cancels-windup-spam-does-no-damage.md) | S2 | fixed | Re-issuing the same Attack / AttackMove cancels the wind-up: an order every 5 ticks or faster never deals damage |
| [BUG-0153](BUG-0153-m4-2a-nits.md) | S4 | open (1-2 fixed) | M4-2a nits: queued Attack target in public `QueuePosition`, a shove row lost combat-on, two brief rows not meetable as worded |
| [BUG-0154](BUG-0154-reissued-attack-move-keeps-building-cannot-redirect.md) | S2 | fixed | Since the BUG-0152 fix an attack-move anywhere keeps an in-reach fight: the player can't redirect a unit off a building onto its attacker |
| [BUG-0155](BUG-0155-d4-shared-text-pin-gaps.md) | S4 | fixed | D4 shared-text pins: H's name check is case-sensitive and skips faction tech names (caught only by the 160-char limit); age_ii at 158 / 160 |
| [BUG-0156](BUG-0156-unit-hitting-building-ignores-attacker.md) | S3 | fixed | A unit hitting a building in reach never turns on an enemy unit killing it (tier 0 skipped while in reach) |
| [BUG-0157](BUG-0157-jittered-attack-move-spam-drops-chasers-fights.md) | S3 | open | Attack-move spam to a new point every 1-3 ticks still costs 10-26 % of a brawl's damage: chasers out of reach lose their fight on each click |
| [BUG-0158](BUG-0158-blob-scan-perf-row-reads-machine-slowdown-as-delta.md) | S4 | open | QA Perf row `TightBlob2500_OneEnemyAtTheFarCorner_ScansNearFree` reads a sustained-load machine slowdown (4.35 then 7.2 ms runs) as the far enemy's cost; fails alone on base and head alike |
| [BUG-0160](BUG-0160-m4-v1-combat-view-nits.md) | S4 | fixed | M4-V1 nits: F12 line covers "K / L", a unit hit before its first frame never flashes, corpses read black for both teams, F12 fallback labels in C# |
| [BUG-0180](BUG-0180-bug-0156-repick-keys-on-stale-last-attacker.md) | S3 | fixed | The BUG-0156 fix keys on any live LastAttacker: a long-gone attacker makes a building-hitter leave the building for a passer-by |
| [BUG-0181](BUG-0181-replay-recorder-accepts-non-default-projectile-capacity.md) | S3 | fixed | ReplayRecorder accepts a non-default ProjectileCapacity; the replay plays with the default and fails its first checkpoint |
| [BUG-0182](BUG-0182-m4-2b-nits.md) | S4 | open (item 1 fixed) | M4-2b nits: a positive speed below float range loads as a 0 m step; Sappers splash themselves in melee (data note) |
| [BUG-0183](BUG-0183-walking-units-dodge-every-shot-past-5m.md) | S2 | fixed | A walking unit dodges every aimed shot fired from more than about 5 m (0 / 100 at 8 m on a walking Heavy Infantry); docs/02 says slow units almost never do |
| [BUG-0184](BUG-0184-lead-rule-nits.md) | S4 | open (item 1 fixed in M4-3a) | Lead rule nits: a step of exactly the lead speed is not always led (float rounding, 7 % of steps at 5.0 m/s); a re-led bolt can step 1.8 m in a tick (nominal 1.25) |
| [BUG-0190](BUG-0190-m4-v2-attack-order-view-nits.md) | S4 | fixed (M4-V3; residual BUG-0226 item 1) | M4-V2 nits: corpse discs on a ramp are half buried; ResolveEnemy takes a NaN unit entry as "no unit" |
| [BUG-0200](BUG-0200-d5-pin-gaps-ages-clause-and-targets-message.md) | S4 | fixed (D6; residual BUG-0230 item 1) | D5 pins: docs/02 Ages trailing clause ("two production halls, or one hall and the Forge") unpinned (QA row added); explicit `"targets": "all"` fails C with a "page (absent)" message |
| [BUG-0210](BUG-0210-minimaptest-red-on-main-after-m4-2b-every-unit-fights.md) | S2 | fixed (M4-V3, `--no-combat`; the slot was shot dead, no sim regression) | `MinimapTest.tscn` red on `main` since M4-2b (39 of 40 Moving 3 s after the minimap order): a pre-combat 2,000-unit scene now has archers shooting; fix as BUG-0147 (`--no-combat`), view track first item; blocks the M4-V2 merge |
| [BUG-0211](BUG-0211-seed21-wedge-replay-checkpoints-unhashable-after-fog.md) | S3 | open | `GatherWedgeQaTests`' checkpoint prefix is off (19 -> 0) since M4-3a hashes the fog: re-record the seed-21 replay |
| [BUG-0212](BUG-0212-sfxtest-red-after-fog-units-move-off-the-click-point.md) | S2 | fixed (M4-V3 r1, `--no-combat`) | `SfxTest` red on the M4-3a sim (the clicked unit leaves its screen point; fights start differently): view adds `--no-combat` |
| [BUG-0213](BUG-0213-qam24test-flaky-after-fog-slot-reuse-race.md) | S2 | fixed (M4-V3 r1, `--no-combat`) | `QaM24Test` fails ~1 run in 3 on the M4-3a sim ("slot not reused by the enemy"): view adds `--no-combat` |
| [BUG-0214](BUG-0214-1024-world-memory-bound-red-after-fog.md) | S2 | fixed | `World_1024Map_CacheStays32_MemoryBounded` red: 230.4 MB vs 228 MB (the fog's ~2.3 MB); Producer re-baseline call |
| [BUG-0215](BUG-0215-fog-visible-bits-not-derived-from-hashed-state.md) | S3 | open | The fog's visible bits are not a function of hashed state: equal hashes, different futures; docs/03's "save re-stamps at load" is wrong |
| [BUG-0216](BUG-0216-fog-small-findings.md) | S4 | open | Fog nits: circle capped by the map's side not its diagonal (sight 64 on < 46-cell maps), holding Catapult reach vs docs, view/combat 4-tick disagreement |
| [BUG-0217](BUG-0217-vision-gate-cost-in-multilevel-brawls.md) | S3 | fixed | The vision gate costs ~25 % of the tick in big 3-level brawls (257k calls a tick at 1,000 v 1,000); no perf row covers it |
| [BUG-0218](BUG-0218-attack-order-scenes-red-on-sim-view-merge-unseen-attacks-dropped.md) | S2 | fixed | On the sim + view merge, `AttackOrderViewTest` and `QaV6Test` fail every run: their explicit Attacks on unseen enemies (the Tent, far picks) are dropped per M4-3a; the view must stage seen targets; gates the integration |
| [BUG-0219](BUG-0219-unitpickerqa-queued-attacks-on-unseen-raiders-red-on-sim-view-merge.md) | S2 | fixed (M4-VH1, 2cddf26) | `UnitPickerQaTests.ThreeQueuedAttacks_ThenStop_ClearsEverything` red on the sim + view merge (4,003 / 15 / 1): its queued Attacks target Raiders 20 m out of sight and M4-3a drops them (the BUG-0218 class, in an xUnit row); view stages them in sight (`CombatScenes.Spot`); gates the M4-3a sim merge |
| [BUG-0220](BUG-0220-sfxtest-flakes-under-cpu-contention.md) | S3 | fixed (M4-VH1) | `SfxTest.tscn` flakes under CPU contention ("Select / Command played 0 times"): a 60 ms scene-tree timer against the 50 ms wall-clock rate limit; quiet 8 / 8 at a1ccd91 and at 4598751 |
| [BUG-0221](BUG-0221-impact-mark-held-until-drawn-is-drawn-invisible.md) | S3 | fixed | An impact mark held "until drawn once" is first drawn at age 1, fully transparent, when a frame runs more ticks than its life (13 % of marks at 8x / 30 fps) |
| [BUG-0222](BUG-0222-m4-v3-projectile-view-nits.md) | S4 | fixed (M4-VH1; residual BUG-0250 item 1) | M4-V3 nits: a skipping tracker misses a same-target lob reuse; a lob slides its last 0.6 m along the ground; the mark ring replaces live marks while free slots exist |
| [BUG-0223](BUG-0223-corpse-disc-hangs-a-level-up-beside-a-cliff.md) | S3 | fixed | The BUG-0190 corpse fix (`TerrainHeight.MaxUnder`) lifts a corpse disc 4 m into the air when the unit dies within ~0.6 m of a blocked cliff cell |
| [BUG-0224](BUG-0224-corpse-disc-hangs-beside-a-ramps-side-wall.md) | S3 | fixed | After the BUG-0223 fix, a corpse disc on low ground beside a ramp's side wall still hangs up to 2.16 m (rim samples within radius x one level per cell of the centre count across the wall) |
| [BUG-0225](BUG-0225-no-combat-scene-list-omits-sfxtest-and-qam24test.md) | S4 | fixed | docs/03 `--no-combat` scene list omits SfxTest and QaM24Test, and the BUG-0147 line still says "QaM24 passes without it" |
| [BUG-0226](BUG-0226-m4-v3-round-2-nits.md) | S4 | fixed (M4-VH1; notes BUG-0250 item 2) | M4-V3 round-2 nits: the corpse disc's 0.15 m slack lets it up a ramp beside a side wall's foot (~0.5 m; no ramp seams exist); AttackOrderViewTest's own-Billet row silently skipped on seed 6; QaV6Test's Tent row never has the box behind |
| [BUG-0230](BUG-0230-d6-report-nits.md) | S4 | open (items 1, 3 fixed in D7; item 2 for D8) | D6 nits: an appended contradicting sentence after the docs/02 Ages clause passes G and QA; the margins harness is an unpinned copy of `CounterTriangleTests`; the Whirlwind siege row sits in the "Winner keeps" column |
| [BUG-0250](BUG-0250-m4-vh1-nits.md) | S4 | open | M4-VH1 nits: a skipping tracker still misses a collinear same-target lob reuse (docs/03 says "from elsewhere starts over"); `TerrainHeight.Straddle` copies QA's 512-step oracle; the double-Cancel guard is not reset with a new `Simulation` |
| [BUG-0251](BUG-0251-economyviewtest-fatal-at-shutdown-under-load.md) | S3 | open | `EconomyViewTest.tscn` sometimes crashes at shutdown under load after printing PASS (Godot .NET finalizer FATAL, exit -1073741795; ~6 % under load, also with the pre-M4-VH1 timer) |
| [BUG-0260](BUG-0260-ages-clause-anchor-blind-to-same-line-dash.md) | S4 | open | D7's Ages clause anchor in `TechContentTests.G` passes a contradicting sentence written after " - " on the same line (section text is space-joined); QA's oracle now catches it |
