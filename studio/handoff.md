# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-17-0554** (the second full session of 2026-10-17, 2 / 8). Base `main`
**4d5af86** (M6-S1, M6-V10, D20 integrated). Golden `data-hash` on `main` **C059AD620DE581F1**. Counters at the start: **sim 3 / 4, view 3 / 4,
data 1 / 4**: this session's sim and view tasks are the **last feature sessions before M5-H3 / M6-VH3**. Bug ids this session: **sim from
BUG-0880, view from BUG-0890, data from BUG-0900.** No open S1 / S2; S3 14 (BUG-0871 set `fixed` at this PLAN), S4 17.

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced). **M6 1 / 10** (criterion 4 ticked; criterion 2 waits only on the view's columns,
  **this session's view task ticks it**; criterion 3's save half on Requests 42; criterion 1 on team, Requests 38; criterion 5's art pass on
  the owner's call).
- **Waiting on the owner:** the art call from the look test (docs/04 "Look test (M6-V10)"); D19's table, the Shock pass, `goldShare` 0.6 → 0.5,
  BUG-0780, "densest cluster"; the Perf fresh-boot run (BUG-0764); the fonts / music / export templates.
- **Inbox at this PLAN:** empty (nothing under New).

## Standing rules (1-30)

Unchanged (see the 2036 handoff in git history for the long form; 28: `game/data/common/ui.json` is view-track text; 29: the art
pipeline's tooling is view-track work when it comes; 30: a Producer-fixed docs drift found by another track's QA keeps its bug file on
that track's branch, the next PLAN sets the status).

---

## Sim track

### Current session plan (2026-10-17-0554): M5-P2 pathing slice 2, "flank through your own line"

**Type** feature (counter 3 / 4 → 4 / 4). **QA tier** full (movement + combat chase). **Size** ~800 lines (uncertain work: one system, the
first part only). The owner's playtest note 5 ("infantry behind crossbowmen get stuck instead of going around them").

**Goal.** A melee unit whose way to its target is blocked by its own side's planted fighters (own units `Attacking` / `Casting`, which are
hard walls and never shoved) walks round the end of that line instead of stalling and giving up. Today (`CombatSystem.FriendFightsTarget`,
docs/03 "Chasing": "nor does one shooting from farther than the chaser's own reach") a melee chaser queued behind its own ranged line gives
the target up after `GiveUpScans` (2 s) and stands Idle; the detour (M1-4d-3, `MaxDetourTurn` 90°) can't see round a 10 m line from 2 m away.

**Scope (in).**
1. **The flank rule** (`MovementSystem`, plan pass): when a walker's line to its aim is blocked by a block of **own planted fighters**
   (`Attacking` / `Casting`, standing; the chain of touching or near-touching ones, bounded like `MaxPlugCluster`), and the ordinary detour
   finds no side within `MaxDetourTurn`, the walker aims at the nearer **end** of that block (the end whose arc toward the aim is shorter;
   ties to the right), a turn of up to a new `MovementConstants.MaxFlankTurn` (dev's pick, ≤ 150°), on open ground by the walkers' own
   rule; it re-aims at its goal once the block no longer lies between. The near-goal exception ("no detour when aiming at the goal itself")
   does not apply when the block is own planted fighters. Enemy standing units and plugs are **not** flanked by this rule (BUG-0045 holds).
2. **No give-up while flanking** (`CombatSystem`): a stalled chase whose chaser is flanking (made a flank step on this scan window), or
   is queued behind own planted fighters that are `Attacking` the **same target** from beyond the chaser's reach, keeps the target (the
   `FriendFightsTarget` exception widened to "a friend fights it from any range **and** a standing point in reach exists on foot" — reuse
   `ReachableOnFoot` / the field-cost check; a building target counts here too, via the slice-1 `QueuedAtRing` path, unchanged).
3. **Move orders** through one's own line: a plain Move whose way is blocked by own planted fighters flanks the same way (same rule, no
   special case), so a group sent past its archers gets there.
4. docs/03: a new "Implementation (pathing slice 2)" section (rule, constants, the before / after table, cost) and the slice-1 "Not yet
   (slice 2)" line updated to what is still left (the far-corner fill order, unit-v-unit crowding away from buildings → "slice 3").
5. **BUG-0790** (S3) folded in **only** if the fix is a few lines in `EconomySystem.StandPoint` / `WalkToFootprint` or the crowd-arrival
   rule; then un-skip `GatherPocketStressTests (MineOneSide, 40)` and `Bug0790FaceProbeQaTests`' regression row. Else leave it for M5-H3.

**Scope (out).** The House row's far-corner jam / "far faces first" fill order; unit-v-unit crowding away from buildings; a crowd cost
in the flow fields (BUG-0028 / 0032 / 0046 / 0050 stay open); any `Command` kind; any data field; any change to enemy-plug or shove rules;
ranged units' own behaviour (they keep standing where they shoot).

**Acceptance criteria.**
1. **The owner's scene, Attack:** flat open ground, 10 `malazan_crossbowman` planted in a row 1 m apart, `Attacking` an enemy Town Hall
   (hp topped up each tick, as `SurroundTests` does) from their max range; 10 `malazan_heavy_infantry` spawned 3-4 m behind the row's
   middle ordered `Attack` on the hall. **≥ 8 / 10 strike the hall within 200 ticks (10 s), 0 give-ups**; the before count printed in the
   test name's output (today's expected: ≤ 3). The same scene with `whirlwind_desert_archer` / `whirlwind_raider`.
2. **Attack-move and retaliation:** the same layout with `AttackMove` past the hall, and with an enemy unit (not a building) behind the
   line as the target: ≥ 8 / 10 reach striking range within 200 ticks, 0 give-ups.
3. **Move through the line:** 10 Heavy Infantry ordered `Move` to a point 8 m beyond the crossbow row: ≥ 9 / 10 arrive within 200 ticks.
4. **Nothing flanks an enemy:** the BUG-0045 plug rows and `CrowdRoutingTests` green unchanged; a new row: 10 standing **enemy** units in
   the same line, 10 attackers behind: no attacker passes the line by the flank rule (the line is hard; the old behaviour, counted).
5. **No regression in the crowd rows:** cross-map seeds 1-8 200 / 200 with 0 give-ups; the `CrowdRows` table (500 to 4 points, 2,500 to
   4 points, 500 random goals, 128 to 64 goals, the blob crossing) re-measured before / after on the same scenarios and printed in
   docs/03's slice-2 table; none worse than its base by more than the noise band docs/03 M1-4d-3 records (± the 0.1 mm shove-margin
   noise: 4-point rows ±8 %, give-up rows ±1 point). BUG-0143's 500 v 500 brawl row unchanged.
6. **Determinism:** a hash twin of criterion 1's scene in both spawn orders (bit-equal positions, as the detour test); `ReplayGoldenTests`
   green: if the seed-21 recording's `k` lines move (movement), re-record **once** in the same commit with the reason in the commit and
   docs/03, and say so in the report (the owner's `user://replays` then refuse to play: the Producer tells the owner).
7. **Cost:** 0 B per tick in the criterion 1 scene (the allocation probe pattern); paired A/B on `500 moving avg` and the `2,500 tight blob`
   rows within noise (BUG-0764: paired runs only, never a single red row); the flank test runs only for a stuck walker (one spatial query
   at most, like `OwnFighterAhead`).
8. docs/03 updated (item 4); `MovementConstants` new values named and bounded in docs/03.

**Design references.** docs/03 "Local movement" (M1-4d-1 / M1-4d-3: detour, queued, chain shove, plugs BUG-0045), "Chasing"
(`FriendFightsTarget`, BUG-0143, `GiveUpScans` 10, `ChaseProgress` 0.1 m), "Implementation (pathing slice 1)" (`QueuedAtRing`, `ReachableOnFoot`,
"Not yet (slice 2)"), M4-H3's `OwnFighterAhead` corridor rule (BUG-0461 / 0511); docs/02 "Controls and camera" (orders unchanged).

**Tests required.** `Movement/FlankTests` (criteria 1-4 rows with before / after printed, the hash twin, the 0 B probe), `CrowdRows` re-run
with the table in the test output, `CombatSystem` / `SurroundTests` / `CrowdRoutingTests` / `WallPinTests` / `CastWalkInTests` green,
`ReplayGoldenTests`, the full non-Perf suite, Perf paired A/B. If BUG-0790 is folded in: the two un-skipped rows green on seeds 1-8.

**Constraints.** No allocation in the plan pass; sums order-free (the 64-bit fixed-point pattern); no dictionary iteration; `SimMath` only;
nothing new hashed unless it decides a tick (then say so and regenerate the golden once); no Godot; no stat in C#.

### Next after this (M5-H3, the sim's hardening: counter 4 / 4)
BUG-0764 first (an owner fresh-boot Perf run wanted; re-baseline with dated reasons or a calibration row), BUG-0790 if still open, BUG-0791,
BUG-0850 (a few lines), BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3, whatever slice 2's QA files. Then M5-4c slice 2 (criterion 4),
Requests 38 (teams), Requests 42 (the save format incl. `MatchStats`, the surround claims, the Blackboard, the flow-field cache, the fog bits),
D14 b schema, M5-4d, D14 c, M5-5, M5-6.

### Watch-outs (sim)
- Only the sim moves the golden. `SurroundClaim` / `SurroundHolder`, the Blackboard, `MatchStats` are save state outside the hash (Requests 42).
- Any `rules.json` / data change moves `ContentHash` and expires the owner's recordings: say so in For your review.
- Three builds share the machine: wall-clock Perf rows flake under load; paired A/B only.

---

## View track

### Current session plan (2026-10-17-0554): M6-V11 the post-match stats columns and the army-value graph (ticks M6 criterion 2)

**Type** feature (counter 3 / 4 → 4 / 4). **QA tier** standard. **Size** ~600-900 lines (design clear: docs/02 "Game flow" 5, the sim's
M6-S1 getters on `main`).

**Goal.** The Victory / Defeat stats screen (and the pause menu's Stats, and the end of a watched replay) shows what docs/02 "Game flow" 5
lists: resources gathered, units trained / killed / lost, buildings built / lost, time to Age II, and the army value over time as a graph,
read from the sim's M6-S1 counters. Today the table shows kills, losses, gold / wood in hand and the defeat time (M6-V4).

**Scope (in).**
1. `MatchStats.Columns` becomes, in this order: Gathered gold, Gathered wood, Units trained, Units killed, Units lost, Buildings built,
   Buildings killed, Buildings lost, Age II at, Defeated at. **Producer decision:** the two "in hand" columns go (docs/02 doesn't list them
   and the table is ten columns wide already); the old `gold_in_hand` / `wood_in_hand` keys may stay in `ui.json` unused or be removed
   (then fix every test that names them). Cells from `World.GatheredGold` / `GatheredWood` / `UnitsTrained` / `UnitKills` / `UnitLosses` /
   `BuildingsBuilt` / `BuildingKills` / `BuildingsLost` (per-player spans), `TechResearchedTick(p, age_ii)` × 50 ms through `ui.Clock`
   ("—" / a `ui.json` `not_reached` text before it), `DefeatedAt` as today.
2. **The army-value graph** under the table: one polyline per player in the player's colour (`match.PlayerRgb`), x = sample index ×
   `World.StatsSampleTicks` × 50 ms (time axis with minute ticks, labels from `ui.json`), y = `ArmyValueSamples(p)` scaled to the largest
   sample of any player (0 → the baseline). One `Control` subclass drawing in `_Draw` (or one `Line2D` per player): **no node per sample.**
   An empty graph (a match shorter than one interval) shows the axes and the title, no line. Title and axis texts from `ui.json`
   `match_over` (`army_value`, `graph_time`, `graph_value`, `not_reached`, the new column labels: `gathered_gold`, `gathered_wood`,
   `units_trained`, `units_killed`, `units_lost`, `buildings_built`, `buildings_killed`, `buildings_lost`, `age_ii_at`); `MatchOverText`
   gets the matching members. Player-facing text only in `ui.json`.
3. The screen still fits 1280 × 720 (the smallest resolution the settings offer): the table's font or spacing may shrink; a windowed
   screenshot of a finished skirmish's stats screen at 1280 × 720 and at 1600 × 900, **looked at** (criterion 6 of M6-V4's pattern).
4. The replay watcher: at a watched replay's banner the same screen shows the recomputed counters (no change expected; a row in
   `ReplayPlaybackTest` reads one column).
5. docs/03 "Implementation (M6-V11)" (what each column reads, the graph's scaling, the 50 ms conversion) and docs/02 "Game flow" 5 left as is
   (it already says this).

**Scope (out).** Any sim change (the getters exist; `sim/` diff must be empty); the replay header's interval (a replay watched under a
different `SimConfig.StatsSampleTicks` draws a different graph: documented, not fixed); save / load (Requests 42); Stats from the pause
menu before the banner (unchanged: M6-V4's rule); per-unit-type breakdowns; export of stats to a file.

**Acceptance criteria.**
1. `MatchOverViewTest`: for a finished 2-seat skirmish, every cell of every new column equals the sim's span value for that player
   (`Number(w.X[p])`), the Age II cell is `not_reached` before the tech and `ui.Clock(TechResearchedTick × 50 ms)` after (a scene that
   researches `age_ii` through `Command.Research` with a dev grant of gold / wood, or the `TestSim` seams the view tests already use);
   `FreeForAllTest` / `QaM6V5Test` 4-seat rows updated to the new column indexes (found by label, as today).
2. The graph: a test reads the drawn point count per player = `ArmyValueSamples(p).Length` and the last point's y maps the last sample
   (a `GraphPoints(p)` read-only accessor on the graph control for tests); a 0-sample match draws no line and no error; 4 players draw 4 lines
   in 4 colours equal to the swatches.
3. Every new string is a `ui.json` key (`UiText` forward-key test extended; no C# literal in the diff's view code except dev log lines).
4. Smoke PASS; `tools/qa/scene-loop.ps1` all scenes green (re-run alone a wall-clock row that flakes under the three builds); `sim/` diff empty.
5. Both screenshots taken and described in the report (what is readable, what overlaps); `docs/images/` gets neither (not an art task).
6. docs/03 "Implementation (M6-V11)" written; the roadmap's M6 criterion 2 is ticked by the Producer at ACCEPT.

**Design references.** docs/02 "Game flow" 5; docs/03 "Implementation (M6-V4)" (the screen, `MatchStats.Columns`, `MatchOverText`),
"Implementation (M6-S1)" (the getters' exact meaning: gathered counts drop-offs only, trained excludes summons, "built" excludes the start hall,
the sample cadence 200 ticks, samples oldest first, sampling stops at capacity), "Implementation (M6-V9)" (the watcher's banner path).

**Tests required.** `MatchOverViewTest` (new column rows, the Age II row, the graph rows), `ShellTest` / `FreeForAllTest` / `ReplayPlaybackTest`
green with the new indexes, QA's `QaM6V4Test` updated, the `UiText` key test, smoke, the scene loop.

**Constraints.** Views hold no gameplay state (read the spans at `ShowFor` once, as today); no sim write; text only from `ui.json`; one
node per graph, not per sample; the screen reads nothing from the replay file.

### Next after this (M6-VH3, the view's hardening: counter 4 / 4)
BUG-0860 first (S3: null-check every `GD.Load` in `LookTest` through `Fail` + a per-scene timeout in `scene-loop.ps1`), BUG-0830, BUG-0550
item 2, BUG-0281 item 2, BUG-0663, the `--colour` collision warning's wording, BUG-0861 if the art pass hasn't, whatever M6-V11's QA files.
Then: save / load when Requests 42 lands; `tools/export.ps1` when the owner's templates arrive; teams (Requests 38); criterion 6's playable
run; the art pass when the owner answers the look call.

### Watch-outs (view)
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- A clone on the owner's second machine needs `git lfs pull` before `LookTest` runs (BUG-0860 until fixed).

---

## Data track

### Current session plan (2026-10-17-0554): STOP (cheap)

No owner note in the inbox (D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 and "densest cluster" all still wait for a yes /
no); the D14 b openings need the strategies schema (not on `main`); the M7-M9 rosters wait for their milestones; the 12th-house `ai.json`
tweak stays parked to batch with the next hash-moving data change (it would expire the owner's recordings on its own). Nothing unblocked:
the data track STOPs this session and resumes on the first inbox answer or the D14 b schema.

- **First, any owner answer in the inbox:** D19's table, the Shock pass, `goldShare`, BUG-0780 ("accept" closes it with a dated line on both
  pages; "Catapult pass" = a sweep first, then one header move), "densest cluster" (sim), the look call (view).
- **Parked:** the 12th-house step (Malazan step 18, Whirlwind step 19) never reaches the D15 C skip and buys 2 pop for a house's cost; one
  `ai.json` line per faction, moves the golden. Not alone.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on every tree on this PC; an owner fresh-boot run is wanted before M5-H3 re-baselines. Paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- **BUG-0871** set `fixed` at this PLAN (file + README row, sim worktree).
