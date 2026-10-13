# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-12-1930** (first full session of 2026-10-12, 1 / 8; base `main`
516e5c0, all three tracks GO). Counters before it: **sim 2 / 4, view 2 / 4, data 3 / 4** (all feature sessions; this session makes
them 3 / 4, 3 / 4, 4 / 4: the data track's next is D-H4). Bug ids this session: **sim from BUG-0590, view from BUG-0600, data from
BUG-0610.** Inbox: empty (no owner answers on D13 / D14 / D11 / D12 yet).

## Where we are

- `main` 516e5c0: build 0 errors, **1 warning** (`game/tests/QaShellTest.cs:62` CS8602, BUG-0581 item 4: the view's first item).
  Non-Perf suite and smoke: see the session log's Checks (run at this PLAN). Golden `cross_map_seed1.replay` is **format 5**
  (`seat 0 malazan human normal`), `data-hash 5760E83CB301B62E`, 1 player, no AI seat.
- **M5 1 / 6** (criterion 2 advanced). **M6 criterion 1 advanced.** No open S1 / S2. Open S3: BUG-0574 (sim), BUG-0580 (view).
- The AI on `main`: build order + economy + production through the command door, D14's 26-step orders. **It never leaves its
  base:** no scout (starves of wood on a generated map by pop 36), no army orders, no fighting. `ai.json` + `rules.json` `ai`
  are outside `ContentHash` until this session's M5-3.

## Standing rules (1-14, unchanged)

1-10 as in the 0314 handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; every agent's scratch folder `<track>-<session>-<purpose>` with absolute
paths; a data brief names every sim-track test that pins a value it changes).
11. **Bug statuses:** the dev may not edit `studio/**`; its report names the status it proposes per bug; QA sets what it
    verifies; the Producer sets the rest at ACCEPT.
12. **Content-independent sim tests:** a sim test that reads `game/data/factions/**` writes against its own scene data or
    property checks, never a shipped count (BUG-0571).
13. Restore a file checked out from another commit with `git checkout HEAD -- path`.
14. **The budget is a line.** M5-2 shipped ~3,200 against ~1,500 by not slicing. Every brief below names what to slice first; the
    dev reports its line count; the Producer checks it at ACCEPT.

---

## Sim track

### Current session plan: M5-3, the AI scouts, rallies and attacks · feature · QA full · ~1,500 lines incl. QA

**Goal.** Give the AI a scout (so it finds forests, mines and the enemy base: today it starves of wood on a generated map), a
military manager (rally, attack waves at the `ai.json` thresholds, retreat at the loss fraction, enemy memory from the view),
and put `ai.json` + `rules.json` `ai` into the content hash. Items 1-5 required; **item 3's retreat half is the first to slice to
M5-4 if the budget runs out**; everything in "Out" is M5-4 / M5-5.

**Scope.**
1. **Scout manager** (`Ai/ScoutManager.cs`, runs after the executor and before the economy manager, so the scout is taken before
   the idle workers are sent gathering). The scout leaves at the first think at or after `rules.json` `ai.scoutAfter` s (default
   15): the own worker nearest the hall with no AI command pending (idle first, else a gatherer); the Blackboard keeps its slot +
   generation; the economy manager and `PickBuilder` skip it. Sweep = a fixed waypoint list computed once from the hall centre and
   the map: the 8 compass points at `ai.scoutRadius` m (default 40) from the hall, clamped inside the map; then the point-mirror of
   the hall through the map centre (where the generator puts the other base); then the 4 quadrant centres. One `Command.Move` per
   think at most, to the next unvisited point, only when its last command applied; a point is visited when the scout is within
   `ai.scoutReach` m (default 6) of it or the scout is Idle after its Move applied (it got as close as it could). The sweep ends
   (the scout is released: the economy manager sends it to gather next think) when every point is visited, or earlier once a
   forest, a mine and an enemy building (seen or ghost) are all known. A dead scout is not replaced (periodic re-scouting: M5-4).
2. **Enemy memory** on the `Blackboard`, from the view only: known enemy buildings are the view's seen enemy buildings plus its
   ghosts (no second copy of the fog's memory); the last-seen enemy army = centroid, count and tick of the enemy non-worker units
   seen this think, kept until replaced.
3. **Military manager** (`Ai/MilitaryManager.cs`, runs last). Army = own live units with `Slot != Worker`, not `Casting`, no AI
   command pending. **Rally:** the point `ai.rallyDistance` m (default 12) from the hall centre toward the nearest known enemy
   building (toward the map centre when none is known); an idle army unit not in a wave and farther than `ai.rallyReach` m (default
   6) from it gets one `Move` (never re-sent while pending or within reach). Rally is Moves, not `SetRally` (Producer decision:
   the point moves with what the AI learns; record in docs/01). **Wave start:** no wave in progress, `Now >= waveCooldownUntil`,
   army half-pop >= `AttackMinHalfPop x WaveSizeMultiplier` and army cost (sum of `CostGold + CostWood`) >= `AttackMinCost x
   WaveSizeMultiplier`, and a target known (the known enemy building nearest the rally point): every army unit gets
   `AttackMove(target footprint centre)`; the wave records its members (slot + generation, a per-unit-slot array), start half-pop,
   target and tick; if the queue fills mid-send the rest go next think (members flagged "not yet sent"). **Each think in a wave:**
   dead members drop out; a member Idle within `rallyReach` of the target point with the target seen gets `Attack(target,
   isBuilding: true)`, Idle elsewhere gets `AttackMove` again (one command per unit per think); a target that is gone (not seen
   live, no ghost at that slot) re-targets the nearest known enemy building to the wave's centroid, or with none known the wave
   ends and the members `Move(rally)`. **Retreat:** live half-pop < `(1 - RetreatLossFraction) x start` → every live member
   `Move(rally)`, the wave ends, `waveCooldownUntil = Now + ai.waveCooldown` s (default 60). Bounded: at most one wave start per
   cooldown.
4. **`ai.json` + `rules.json` `ai` into `ContentHash`:** every field of `AiDef` / `AiDifficultyDef` / `AiStep` / `AiComposition` /
   `AiRulesDef` incl. the six new knobs; a `DataContentHashTests` row per field; `AiLoaderTests.AnAiJsonEdit_LeavesTheContentHash`
   inverted; **one golden regen** (only the `data-hash` line changes; every `k` line equal: no AI seat in the golden);
   `SameGameDataHashes` carries the new hash. The reason in the commit message.
5. **BUG-0574** (S3): don't judge a Gather while the worker carries a full load of that kind or has no drop-off; clear a mark when
   the node is next seen alive; un-skip QA's `AiGatherMarkQaTests.ALiveMine_IsNotMarked_WhenALoadedWorkerHasNoDropOff`.
   **Requests 36 a:** `ProductionManager` weighs `(live + queued) x HalfPop / weight` (composition weights are pop shares on the
   pages); **BUG-0541 item 1 + BUG-0573 item 2:** a weight that rounds to 0f is a load error at its field.
6. New `rules.json` `ai` knobs with `DataValidationTests` rows: `scoutAfter` (s, 0-3,600), `scoutRadius` (m, 1-64), `scoutReach`
   (m, 1-16), `rallyDistance` (m, 1-64), `rallyReach` (m, 1-16), `waveCooldown` (s, 0-3,600). The sim ships the defaults above in
   `game/data/common/rules.json` (sim-owned).
7. **Headless AI-vs-AI proof** (`Ai/AiVersusAiTests`): Malazan vs Whirlwind, both Normal, generated 128 map, seeds 1-3, 6,000 ticks:
   no exception; both seats' wood gathered > 0 by tick 3,000 (the scout found a forest); both have >= 1 army unit; each has sent >=
   1 `AttackMove`; recorded, and played back to equal checkpoints. (M5-5's 20-minute match with a winner is later.)
8. docs/03: "Implementation (M5-3)" (the managers, the waypoint list, the wave state machine, the knobs, cost, memory), the
   "AI architecture" tree, "Not yet" lines; docs/01 change-log row for the Producer decisions (the early scout goes before gathering;
   rally by Moves; `waveCooldown` bounds the loop). Memory per AI seat itemised (the wave arrays).

**Out:** defense, rebuilding a lost hall / house, scouting ramps / high ground before attacking up, `focusFire` / `pullBackWounded` /
`gatherBonus` / the ability manager / periodic re-scouting (all M5-4); the 20-minute winner test (M5-5); the lobby (view); any
`game/data/factions/**` edit (the data track's; the sim changes no faction file this session); any change to `PlayerSeat` /
`SimConfig.Seats` shape (the view reads them this session); any new `Command` kind (none is needed: say so in the report).

**Acceptance criteria.**
1. Scouting: an AI seat with the shipped Malazan order on a generated 128 map, seeds 1-3, knows a wood node by tick 1,200 and has
   wood income above 0 by tick 2,000; the scout sends at most one `Move` per think, each to a waypoint of the documented list; the
   scout is released and gathers once the sweep ends; `PlayerViewArchitectureTests` passes with the new files.
2. Waves: in a scripted flat scene (`AiScenes.Flat`) where the AI knows an enemy hall (seen or ghost) and its army reaches both
   thresholds (x `WaveSizeMultiplier`), every army unit gets an `AttackMove` to the hall's centre within one think; below either
   threshold none is sent; Easy (x 0.6) waves at a smaller army than Normal; a razed target re-targets the next known enemy
   building, or with none known the wave ends and the members `Move` home.
3. Retreat: a wave that loses `lossFraction` of its starting half-pop sends `Move(rally)` to every live member within one think;
   no wave starts before `waveCooldown`; attack → retreat → attack over 6,000 ticks starts at most `6000 / waveCooldownTicks + 1`
   waves (a test with the cooldown removed fails it).
4. Rally: idle army units beyond `rallyReach` of the rally point are sent once; the point lies `rallyDistance` from the hall toward
   the nearest known enemy building; no re-send while pending or within reach (command count per think bounded by new idlers).
5. Hash: `DataContentHashTests` has a row per `ai.json` and `rules.json` `ai` field, each moving the hash; the golden is regenerated
   once, only its `data-hash` line changes, every `k` line equal; `SameGameDataHashes` updated.
6. BUG-0574: QA's row un-skipped and green; a new test shows a mark clears when the node is seen alive again.
7. Pop weights: a 3-pop unit and a 1-pop unit at equal weight → three small for one big; a weight rounding to 0f is a load error
   naming `compositions[k].units.<id>`.
8. Determinism + replay: `AiDeterminismTests` (two AI seats that fight, 3 seeds x 2,000 ticks) equal every tick; `AiReplayTests`
   plays an AI-vs-AI recording back with every seat human to equal checkpoints; `CommandDoorFuzzStressTests` with an AI seat green.
9. `AiVersusAiTests` as item 7, seeds 1-3.
10. Cost: a think with a 200-unit army (the `AiPerfTests` scene extended) under the 1 ms gate, allocation-free (`AllocationProbe`);
    the memory row's comment and docs/03 itemise the new per-slot arrays.
11. Build 0 warnings; non-Perf green, Perf alone green; docs/03 + docs/01 as item 8; `rules.json` knobs validated; bug statuses
    proposed by the dev, set by QA; the line count reported.

**Design references.** docs/02 "AI opponent" (priority order; thresholds are faction data; Easy = small waves via
`waveSizeMultiplier` 0.6; the AI sees only its fog view); docs/03 "AI architecture", "Implementation (M5-1)" (what the view answers,
the `Ai/` source rule) and "(M5-2)" (think point, one command per step per think, `PendingOrder`, `ICommandSink`, the executor's
build path), "For the data track (M5-1)" (`attack`, `retreat`, `waveSizeMultiplier`); docs/factions/malazan.md + whirlwind.md "AI
build order (Normal)" (composition targets are pop percentages); `studio/bugs/BUG-0574`, BUG-0541, BUG-0573.

**Tests required.** `Ai/ScoutManagerTests`, `Ai/MilitaryManagerTests` (flat scenes with scripted enemy buildings / units through
`AiScenes`), `Ai/AiVersusAiTests`, extended `AiDeterminismTests` / `AiReplayTests` / `AiPerfTests`, `DataContentHashTests` rows,
`DataValidationTests` rows for the six knobs, the `AiLoaderTests` weight-0 row, the BUG-0574 regression (un-skipped QA row + the
clear-on-seen test). All content-independent (rule 12).

**Constraints.** No `World` / store / `Simulation` / `FogStore` in `Ai/` (the scan); no allocation in a think (wave arrays sized
per unit slot at construction); slot-order iteration only; the AI's `SimRng` only if randomness is needed (prefer none); every
command through `ai.Send`, a full queue ends the think; no `game/data/factions/**`, no `game/**`, no `ViewApi/`; the golden regen
in the same commit with the reason; ~1,500 lines incl. QA: slice retreat before overrunning.

**QA focus (full).** The AI reads nothing hidden while scouting (the architecture scan; the hash twin with two AI seats reading
every think); a wave against a building that is gone (ghost) and one razed mid-wave; attack-retreat loops bounded (mutation: drop the
cooldown → the bound test fails); a wave into two towers (members die, retreat fires, no infinite re-send); 2 AI seats x 6,000
ticks x 3 seeds invariants + playback; the hash proof (every field of both files moves it; `k` lines equal); think cost with a
200-unit army and 8 seats; `CommandCapacity` (408): a 200-unit wave start that fills the queue sends the rest next think; a
waypoint on a cliff / off-map (clamped, the scout gives up and moves on); the scout killed mid-sweep (no crash, no replacement);
BUG-0574's row; the `k`-line proof on the regenerated golden.

### Watch-outs (sim)
- `AiPlayer.Send` stops sending for the think when the queue is full: batch per think, keep "not yet sent" flags.
- `Blackboard.NodeDropped` keyed by slot + generation never clears (BUG-0574): clear on seen-alive.
- `AiPlayerTests` use their own quiet / short build orders (rule 12); keep it so. The data track edits no `ai.json` this session.
- The view reads `SimConfig.Seats` / `PlayerSeat` this session: don't change their shape.

---

## View track

### Current session plan: M6-V2 + M5-V2, the lobby's seats, the sandbox AI seat, BUG-0580 · feature · QA standard · ~1,000 lines incl. QA

**Goal.** The setup screen's faction and difficulty lists are greyed "Coming soon"; the sim's match seats landed last session.
Wire them so the owner can pick each slot's faction and the AI's difficulty and play against the AI from the menu, and give the
dev / sandbox scenes `--ai` and `--faction` flags so QA and M5's playable criterion can run against the AI. Expect a weak AI
(no scouting) until the sim's M5-3 merges ahead of this branch.

**Scope.**
1. **First:** BUG-0581 item 4, `game/tests/QaShellTest.cs:62` CS8602 (a null check): `main` back to 0 warnings.
2. `SkirmishSetup`: a live faction `OptionButton` per slot from `GameData.Factions` (`DisplayName`, id order; defaults slot 1 =
   faction 0, slot 2 = faction 1, today's rule); the AI slot's difficulty list Easy / Normal / Hard from new `ui.json` `setup` keys
   `difficulty_easy` / `difficulty_normal` / `difficulty_hard` (default Normal); `coming_soon` dropped from `ui.json` and `SetupText`
   if nothing uses it. Start builds the match the M6-V1 way, through the command line (`--seed --map-size --faction 1 <id>
   --faction 2 <id> --ai <difficulty>`) and `LaunchOptions.Parse`, so a skirmish and a dev run share one path. The picks survive
   Back and Quit to menu.
3. `LaunchOptions`: `--faction <slot> <id>` (slot 1 or 2, a faction id) and `--ai <easy|normal|hard>` (slot 2 becomes an AI seat;
   without it slot 2 stays human, so every existing scene and test is unchanged); both in `MatchFlags`; a junk value is refused the
   way `--seed junk` is (follow that path; documented). `SimRunner.Start` always builds explicit `SimConfig.Seats`: one
   `PlayerSeat(faction, Human | Ai, difficulty)` per player (`PlayerCount` entries; players past slot 2 get `p mod factions`, human),
   and keeps them as `SimRunner.Seats` for tests.
4. BUG-0581 item 1: the map size items from a `ui.json` `setup.map_size_item` format (`{0}`), no C# literal on the screen.
5. **BUG-0580** (S3): clear held-button state in `RtsCamera` / `SelectionController` on pause / resume (`_Notification` paused /
   unpaused, or `Main.PauseMatch` tells both); the two `KNOWN BUG-0580 probe` prints in `QaShellTest.PauseBurstAndLeaks` become
   `Check`s.
6. **Match-over dev line:** a pure `ViewApi` helper (`ViewApi/MatchOver.cs`, read-only over `World.Buildings` spans) answers "player
   p's building count went from above 0 to 0 this tick"; `Match` prints `Match over: player p has no buildings left (tick n)` once.
   Not a screen (M6 criterion 2 needs the sim's rule: file **Requests 37** in the report: `World.IsDefeated(p)` / a match-over event
   per docs/02 "Victory/defeat").
7. Headless `game/tests/AiSeatTest.tscn` (+ `.cs`, in `tools/qa/scene-loop.ps1`): `Main.tscn` no args → Skirmish → slot 2 Whirlwind,
   Hard → Start: `SimRunner.Seats[1]` is `(whirlwind, Ai, Hard)` and `World.FactionOf(1)` agrees; by tick 400 player 1 has a worker
   `Gathering` / `Returning` or a building with a queue item (the AI acted; read `World` between ticks); with
   `RecordCheckpointInterval` set the recorder's replay header holds `seat 1 whirlwind ai hard` and `ReplayPlayer.Run` plays it
   back to equal checkpoints (every seat human on playback, M5-2's rule); the same seats from `--ai hard --faction 2 whirlwind`;
   `--faction 1 whirlwind --faction 2 malazan` swaps the local faction (the HUD's card / colours read `FactionOf`); 3 quit-to-menu
   cycles with an AI seat: no orphan nodes, managed memory flat; pause with an AI seat: no tick.
8. docs/03 "Implementation (M6-V2)" (flags, seats, the setup, the dev line, Requests 37); the smoke row says the gate runs both
   passes (QA's note from 0634); `ui.json` keys and `ShellTest`'s key rows updated.

**Out:** 2-4 slots, colour, team (M6-V3); the victory / defeat screen and stats; save / load; settings; the Hard "25 % gather bonus"
tooltip (the sim applies it in M5-4; M6-V3 shows it); `DataContentHashTests.cs`; `sim/Rts.Sim.Tests/Replays/`; any sim change outside
read-only `ViewApi/` (a seat accessor the view needs that `SimConfig` lacks goes to Requests).

**Acceptance criteria.**
1. Branch build 0 warnings (BUG-0581 item 4).
2. Both faction lists live with every loaded faction's `displayName`; the AI slot's difficulty from `ui.json`; Start gives
   `SimConfig.Seats` = the picks (slot 1 human, slot 2 AI at its difficulty), `World.FactionOf(0 / 1)` agree; picks survive Back and
   Quit to menu (`ShellTest` rows).
3. `--faction` / `--ai` parse, sit in `MatchFlags`, set the seats; without them every scene gets today's seats (loop green incl. the
   new scene; `M4PlayableTest` 2 seed-runs unchanged); junk values refused like `--seed junk`, the match still starts with defaults.
4. `AiSeatTest`: the AI acts by tick 400; the recorded replay has the `seat 1 whirlwind ai hard` line and plays back equal; the
   faction swap shows in the local player's faction; 3 quit cycles with an AI seat leak nothing; no tick while paused.
5. BUG-0580: a press held into the pause and released during it leaves no camera drag and no selection box after Resume (the two
   probes are `Check`s and pass).
6. BUG-0581 item 1: no C# literal on the setup screen; each new `ui.json` key missing is one load error naming it (`ShellTest`).
7. The match-over helper has xUnit rows in `sim/Rts.Sim.Tests/ViewApi/` (hand-built worlds: 1 → 0 buildings prints once; 0 at tick 0
   never prints; a rebuilt hall then razed prints again); a normal 400-tick match prints nothing.
8. smoke PASS (both passes), the scene loop green, docs/03 "Implementation (M6-V2)", Requests 37 in the report, the line count reported.

**Design references.** docs/02 "Game flow" 2 (slots: human / AI, faction, difficulty), "AI opponent" (three difficulties),
"Victory/defeat" (no buildings left); docs/03 "Implementation (M6-V1)" (boot rule, the setup, the text rule), "Implementation (M5-2)"
Seats + Recorded commands (playback with every seat human); STATE "Requests for the sim track" 36 (`SimConfig.Seats`, `PlayerSeat`,
`SeatController`, `AiDifficulty` has no display name: the view keys `setup.difficulty_*`).

**Tests required.** `ShellTest` rows extended (lists, picks, keys, flags); `game/tests/AiSeatTest.tscn` in the loop; `QaShellTest`'s
probes as `Check`s; xUnit `ViewApi/MatchOverTests`.

**Constraints.** Dumb views: the shell holds only the picks, the seats are the sim's input; player-facing text only from `ui.json`;
no sim change outside read-only `ViewApi/`; `PlayerSeat` as on `main` (the sim's M5-3 doesn't change it); the sim merges first:
expect `rules.json` and `DataContentHashTests.cs` to change under you, never touch them; ~1,000 lines incl. QA.

**QA focus (standard).** The dropdowns' picks surviving quit to menu and a second Start; `--ai` / `--faction` with junk, a repeated
flag, a slot of 3; the AI seat under the human's input flood (`QaShellTest`'s leak rows with an AI seat); 10 quit cycles with an AI
seat (the `AiPlayer` goes with the sim); pause at 8x with an AI seat (no think, no tick); the faction swap's HUD card and colours;
the match-over line with `--no-bases` (never prints: nobody had a building); a hostile data clone with a faction lacking `ai.json`
(the loader refuses the data set: the view must report the load error as it does today, not crash on Start).

---

## Data track

### Current session plan: D15, the D14 attack rows and the house-rule overlap report · feature · QA light · ~400 lines incl. QA

**Goal.** D14 (both factions' 26-step `ai.json`) landed without a QA inspection (the dev was blocked by sim pins, fixed on the sim
branch). Write the attack list the inspector would have run as permanent QA rows, so the next `ai.json` change (the owner's tweaks,
M7's Teblor order) fails by name; and measure the house-rule overlap D14 flagged (12 Billets / Tents in the order + the economy
manager's house rule) on the flat AI scene, as a report with a proposal. **No data file changes this session** (the sim's M5-3 puts
`ai.json` into the hash and regenerates the golden; two tracks never move it in one session).

**Scope.**
1. `sim/Rts.Sim.Tests/QA/Content/AiContentQaTests.cs`: each row mutates a copy of a shipped file (`AiScenes.DataWithAi(faction,
   edit)` is the sim-owned helper in `Ai/AiScenes.cs`; use it or a local JSON copy) and must fail by name, at the loader's field path
   or in `Content/AiContentTests`' pins: (a) a step id of the other faction (`buildOrder[k].id`, "belongs to faction"); (b) an Age II
   unit in the Age I composition (`E_...` names the unit); (c) the same at + do + id twice (an error at the second); (d) a `pop` not
   a multiple of 0.5; (e) a `research` step for a tech no building of the faction researches (pin what happens: a loader error, or
   `D_...`'s "reachable" catches it; if neither, file an S4 naming the gap); (f) a one-line edit of each page's build-order table
   (a time, a count, an id) fails `A_EachPage_IsItsAiFile_BothWays` naming the step and the page; (g) each page's composition table
   = the file's weights and sums to 100 per age; (h) `minArmyCost` = the cost of `minArmyPop` pop of the Age I mix (1,544 / 861)
   **recomputed from `units.json`**, never a literal; (i) a `count` of 201 and a 257th step refused at their fields. Both factions.
2. **House-rule overlap report** (`QA/Content/AiHouseRuleReportTests.cs`, reported, asserted only "no exception"): `AiScenes.Flat`
   with the shipped Malazan order, Normal, 12,000 ticks, resources in sight: how many houses the order's house steps built vs the
   economy manager's rule, the pop cap over time, whether the order's later house steps ever complete past the 100 cap (they
   can't: they stay due) and what that costs per think. The dev's report carries the table and a proposal (trim the order's houses,
   or keep them as redundant with the rule); the Producer puts it under For your review for the owner. No `ai.json` edit.
3. `docs/factions/malazan.md` / `whirlwind.md`: a note line under the build-order table **only** if a row finds a page / file
   mismatch; else no page change.
4. **Files:** `sim/Rts.Sim.Tests/QA/Content/AiContentQaTests.cs` (new), `QA/Content/AiHouseRuleReportTests.cs` (new),
   `docs/factions/**` notes only. `Content/AiContentTests.cs` only if a D15 row proves a pin wrong (say why). **No** `game/data/**`,
   `sim/Rts.Sim/**`, `sim/Rts.Sim.Tests/Replays/**`, `DataContentHashTests.cs`.

**Out:** any `ai.json` edit (next session, after M5-3's hash change; inbox answers come first then), balance passes, M7-M9 data.

**Acceptance criteria.**
1. Rows a-i exist for both factions; every mutation fails by name (field path, or step number + page); the shipped files pass every row.
2. Row (h) recomputes 1,544 and 861 from `units.json` and the Age I composition; a mutated unit cost fails it with the recomputed number.
3. The report row runs on `main`'s AI (the flat scene, no scouting needed) and prints the counts; the report has the table + proposal.
4. Build 0 warnings; the new rows green beside the full non-Perf suite; no sim-track pin moved (tests only).
5. The branch touches nothing under `game/data/`, `sim/Rts.Sim/`, `Replays/`, or `DataContentHashTests.cs`; the line count reported.

**Design references.** docs/03 "For the data track (M5-1)" (every loader refusal, the exact messages); docs/factions/malazan.md +
whirlwind.md "AI build order (Normal)", the composition tables, the wave / retreat lines; docs/02 "AI opponent";
docs/03 "Implementation (M5-2)" Economy manager (the house rule) and Executor semantics (a due step stays due).

**Tests required.** As scope 1-2. Sim-track tests that pin values D15 changes: **none** (D15 changes no value).

**Constraints.** Tests only; no row may read `ContentHash`, `rules.json` `ai`, or depend on `ProductionManager`'s count-vs-pop
semantics (M5-3 changes both this session); no C# outside `sim/Rts.Sim.Tests/Content/` + `QA/Content/`; ~400 lines incl. QA.

**QA focus (light).** The one-line-edit rows fail by name on both sides (file and page); no row reads a count or hash that M5-3 moves;
each loader-refusal row asserts the field path, not just "an error".

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md`: each track appends only its own rows.
- **Only the sim moves the golden and `DataContentHashTests.cs` this session**; the data branch touches no `ai.json`; the view's
  branch expects `rules.json` to change under it (sim-owned) and never edits it.
- The view's M6-V2 reads `SimConfig.Seats` / `PlayerSeat`; the sim's M5-3 must not change their shape.
- Merge order sim → view → data.
