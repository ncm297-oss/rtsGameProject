# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-11-0634** (second full session of 2026-10-11, 2 / 8). Base `main`
6fa7944. All three tracks GO. Counters before this session: sim 1 / 4, view 1 / 4, data 2 / 4 (all feature sessions). Bug ids:
sim from **BUG-0570**, view from **BUG-0580**, data from **BUG-0590**. Integration order sim → view → data.

## Where we are (checked at this PLAN)

- `main` 6fa7944: build 0 warnings; the non-Perf suite run by the Producer (result in the session log). Golden
  `cross_map_seed1.replay` format 4, `data-hash 5760E83CB301B62E`, 1 player, no AI seat.
- `World.FactionOf(p)` is `p mod factions` "until a lobby picks them (M6)": a per-slot faction choice needs `SimConfig` (sim). The
  sim's M5-2 adds seats this session; the view's lobby wires the dropdowns next session.
- **Two Producer decisions this PLAN (docs/01 row at ACCEPT, owner may revisit):** (1) **AI commands are recorded in the replay**
  like a human's (the AI enqueues through `Simulation.Enqueue`, the one door; playback never runs the AI, so a replay survives AI
  code changes; the header carries the seats for display and the factions for the match). docs/03 "AI architecture" said "a
  replay only needs the human's commands and the seed": the dev rewords it. (2) **`ai.json` joins `ContentHash` in M5-3**, not
  M5-2, so the sim's golden regen this session is the format-5 header only and the data's D14 moves no golden (the two tracks edit
  `ai.json` and the golden in different sessions).
- Standing rules 1-11 unchanged (see the 0314 handoff in the session log: full suite incl. Perf alone for sim rule changes; a new
  `Command` kind into `CommandDoorFuzzStressTests`; per-slot memory itemised; every agent's scratch folder `<track>-<session>-<purpose>`
  with absolute paths; the dev sets the statuses of the bugs it fixes).

## Sim track

### Current session plan: M5-2, the AI's first decisions · feature · QA full · ~1,500 lines incl. QA (slice order below)

**Goal.** An AI seat that runs its faction's `ai.json` build order and a basic economy and production, reading only its
`PlayerView` and acting only through `Command`s; BUG-0540 closed first so M5 criterion 1 can be ticked; seats (faction, AI
difficulty) in `SimConfig` and the replay header so the lobby and the sandbox AI seat can follow.

**Scope, in order (slice from the bottom if the budget runs out: 6 then 5 go to M5-3; never the tests).**
1. **BUG-0540.** Per-player node memory in the fog (one last-known entry per resource slot, the `Fog.Ghosts` pattern; match the
   view's `ViewApi.SeenResources` rule: a node stays known until a footprint cell is visible again; say whether it is hashed and
   why). `PlayerView.TryGetResource` / `NextResource` answer the remembered node (`Remaining` -1) until its ground is seen;
   un-skip QA's `ANodeDepletedOutOfSight_IsStillAnswered_UntilItsGroundIsSeen`, delete the "today" row; `Ai/PlayerViewOracle`
   learns the rule; docs/03 "Lifetime" reworded. Memory itemised (bytes per slot x capacity x players) in
   `QA/FieldBuildFairnessQaTests.cs:473`'s bound comment and docs/03.
2. **Seats.** `SimConfig` gains a per-player seat (faction id; controller `Human` / `Ai`; `AiDifficulty`), default = today's
   (`p mod factions`, all human) so every existing test, scene and `Match.cs` is unchanged; `World.FactionOf` reads it; `Validate`
   refuses a faction id out of range and an AI seat without a loaded `ai.json`. Replay **format 5**: one `seat <p> <faction> <human|ai>
   <difficulty>` line per player after `players`; format 4 still reads (seats defaulted) and writes back byte for byte; `ReplayPlayer`
   builds the sim with the header's factions and **no AI seat** (the log holds the AI's commands). The golden regenerated **once**,
   header lines only: `data-hash` 5760E83CB301B62E unchanged, every `k` line equal, `SameGameDataHashes` untouched; docs/03
   "Save/load and replays" updated.
3. **`AiPlayer`** (`sim/Rts.Sim/Ai/AiPlayer.cs`, `Blackboard.cs`, `BuildOrderExecutor.cs`, `EconomyManager.cs`,
   `ProductionManager.cs`): one per AI seat, built by the sim with a kept `PlayerView` (stateless, so valid across ticks; 72 B once)
   and its own `SimRng` stream (seed + player). `Simulation.Tick()` runs every AI seat's think **at the start of the tick** (before
   phase 1) on ticks where `TickNumber % thinkTicks == 0`, in player order; each command goes through `Simulation.Enqueue`, stamped
   like a between-ticks command (so the recorder logs it and playback re-enqueues it between the same ticks). The source-scan guard
   stays green: no `Ai/` file but `PlayerView.cs` names `World`, `Simulation` or a store (hand the AI an enqueue delegate or a
   small `ICommandSink`; say which). **AI state is not in `StateHash`** (a playback without the AI must hash the same). The
   `Blackboard` in M5-2 is bookkeeping only (step progress, counts queued, retry marks); last-seen enemy memory is M5-3.
4. **`BuildOrderExecutor` semantics (fixed by this brief; the data track writes against them today):** every step whose trigger is
   reached (`time` s since tick 0; `workers` = own live workers; `pop` = own pop in whole pop) and which is not complete is worked
   **each think, in file order, at most one command per step per think** (steps are concurrent, not sequential: a 15-count laborer
   step at time 0 does not block the Billet at pop 6). `buildOrderSlack` s is the delay the AI waits after a step becomes due before
   its first command (Easy 30 s, Hard 0). `count` = how many `train` commands the step queues over its life (default 1); `build` and
   `research` are complete when the command was accepted once. A refused command is retried next think, never spammed within a think.
   `build`: a site chosen through a new **fog-safe `PlayerView.CanPlace(type, cell)`** (own buildings, enemy buildings the player sees
   or remembers as ghosts, explored ground, terrain; `World.CanPlace` counts hidden enemy buildings: docs/01 2026-10-09), searched
   outward from the own Town Hall (the `camp` near the nearest known forest), issued to an idle worker else the nearest gatherer
   through `Command.Build`. `train`: `Command.Train` at an own finished building that trains the type with queue room. `research`:
   `Command.Research`. `expand`: a Town Hall placed near the nearest known gold mine with no own or seen hall within reach, else wait
   (scouting is M5-3). Generic AI tunables in `common/rules.json` under `ai` (e.g. `goldShare` 0.6, `houseWhenPopWithin` 2,
   `placementSearchRadius`), `DataValidationTests` rows; no stat in C#.
5. **`EconomyManager`:** idle own workers to gold / wood by `goldShare` (3 : 2 as the pages say); a `house` (`Command.Build`) when
   `HalfPopCap - HalfPop` is within `houseWhenPopWithin` pop and none is under construction. **`ProductionManager`:** when the executor
   issued nothing this think and resources allow, train the composition's most under-weighted unit (weight vs own live + queued counts,
   the current age's composition) at a finished building with an empty queue.
6. **Not this session:** `ai.json` into `ContentHash` (M5-3, after D14 lands), the military / attack / scout managers (M5-3),
   difficulty tuning (M5-4), the sandbox AI seat (view, M5-V2), real build orders (data, D14 today in `ai.json` only: the sim does not
   edit `game/data/factions/**`; a schema need becomes a request). BUG-0511 / 0512 only if touched.

**Acceptance criteria.**
1. BUG-0540: the un-skipped QA row and the oracle fuzz (3 seeds x 2,000 ticks) green; a node depleted out of sight is answered until
   its ground is seen; memory itemised; docs/03 "Lifetime" matches.
2. Seats: `SimConfig` seats with defaults; replay format 5 round-trips (format 4 reads, writes back byte for byte); a replay recorded
   with an AI seat plays back **without** the AI to equal checkpoints (`Ai/AiReplayTests`); the golden regenerated once, `data-hash`
   and every `k` line equal.
3. `Ai/AiPlayerTests`: the two shipped steps run; a 10-step test build order (time / workers / pop, counts, slack) fires in the
   documented order on Easy and Hard; a refused Build (no gold, blocked ground) is retried next think, not spammed, and never stalls
   the tick; the house rule; the composition fill.
4. Determinism: two runs with 2 AI seats (3 seeds x 2,000 ticks) same hash; the hash twin with and without a recorder equal; AI
   seats added to `CommandDoorFuzzStressTests` (an AI seat under random human commands: every AI command legal).
5. Architecture: the source-scan guard green with the new files; `AiPlayer` hands out nothing; no allocation in a think at steady
   state (`AllocationProbe`); `Ai/AiPerfTests`: 8 AI seats at 2,500 units, a think under 1 ms Debug.
6. Build 0 warnings; non-Perf and Perf alone green; docs/03 "AI architecture" + a new "Implementation (M5-2)" (seats, the think
   point, the recorded-commands decision, the executor semantics); docs/01 row; bug statuses set by the dev.

**QA focus.** The AI never reads hidden state (the scan + the oracle with AI seats thinking); AI commands apply through the same
door and are dropped the same way as a human's (a `Train` at an enemy building, a `Build` on unexplored ground); the replay with an AI
seat plays back without it; impossible build orders (no gold, blocked ground, a type the faction can't train) never stall or spam;
the format-5 header with hostile seat lines (16 seats, faction 99, `ai` with no difficulty); memory itemisation; 8 seats x 2,500 units.

## View track

### Current session plan: BUG-0560 first, then M6-V1, the shell's first slice · feature · QA standard · ~1,000 lines incl. QA

1. **BUG-0560** (`game/tests/M4PlayableTest.cs` step 5 / `CastOnBlob`): accept the step on the cast's resolve event and check the
   Burning marker on a staged target as step 8 does, or fail fast with "the fight ended before Telas Fire burned anyone"; 6+ seed-runs
   under load as the proof. The dev sets the bug's status.
2. **M6-V1, the shell:** `Main.tscn` stays the main scene and prints the banner and loads data as today; with no match flag on the
   command line it shows the **main menu** (Skirmish, Quit); with any match flag (`--seed`, `--units`, `--screenshot`, `--bench`, ...)
   or a new `--skirmish` it starts the match at once (today's path: every script, the scene loop and `tools/qa/smoke.ps1` keep
   working, and smoke must PASS with no args: the menu boots headless, banner printed, no `ERROR`). **Skirmish setup:** seed (a
   number + Random), map size (128 / 192 / 256 through `MapGenParams.Width` / `Height`), 2 slots: slot 1 "You", slot 2 "AI"; the
   faction shown per slot as the fixed assignment (`World.FactionOf`: Malazan, Whirlwind) and the difficulty, both **disabled** with a
   `ui.json` "coming" text (M5-2 adds seats this session; the dropdowns are the next view slice); Start → `Match.Start` with those
   options; Back. **Pause menu:** Esc in a match (check `ui_cancel`'s current users: cancel placement / deselect win when armed) →
   Resume / Quit to menu; the sim stops ticking and input is swallowed while paused; quitting to the menu frees the match (no sim or
   view node left: assert it). Every text from `ui.json` under new sections `menu` (`title`, `skirmish`, `quit`), `setup` (`title`,
   `seed`, `random`, `map_size`, `slot_human`, `slot_ai`, `faction`, `difficulty`, `coming_soon`, `start`, `back`) and `pause`
   (`title`, `resume`, `quit_to_menu`); no other `game/data/**` file. A headless `game/tests/ShellTest.tscn` drives menu → setup
   (seed 7, 192) → match (asserts the sim's seed and map size) → pause (asserts no tick advances) → quit to menu (asserts the match
   nodes are gone) → Skirmish again. docs/03 "Implementation (M6-V1)" + "Game flow" pointers.
3. The remembered site as a foundation (`Fog.Ghosts` `Site`) if budget remains.
4. Out: victory / defeat, save / load, settings, 2-4 slots, colours and teams, the faction and difficulty wiring (M6-V2 with M5-V2's
   `--ai`), `Replays/`, `DataContentHashTests.cs`.

**Acceptance criteria.** (1) BUG-0560 fixed as above, 6+ seed-runs PASS under load. (2) Menu → setup → match → pause → menu works
windowed (a screenshot of each screen looked at) and headless (`ShellTest` green, in the loop). (3) No-arg boot shows the menu;
`smoke.ps1` PASS; every existing scene and flag unchanged (loop green). (4) All new text from `ui.json`; no sim change; views hold no
gameplay state; docs/03 updated; bug statuses set by the dev.

**QA focus.** Esc while a build ghost or a cast is armed; pause during a tick burst (no double tick, no input leak); quit to menu
mid-match 10 times in a row (node count and memory flat); the seed typed as junk; smoke and the whole loop under load.

## Data track

### Current session plan: D14, real `ai.json` build orders for both factions · feature · QA light · ~500 lines incl. QA

Files: `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json`, `docs/factions/malazan.md` + `whirlwind.md`
("AI build order" sections), `sim/Rts.Sim.Tests/Content/AiContentTests.cs` (+ `QA/Content/`). No other file; no golden change from
this branch (`ai.json` is outside `ContentHash` until M5-3). The sim's M5-2 does not edit these files.

**Executor semantics (fixed in the sim brief; write against them):** every step whose trigger is reached and which is not complete
is worked each think in file order, at most one command per step per think (steps are concurrent); `count` = how many `train`
commands the step queues over its life; `build` / `research` complete on the first accepted command; `buildOrderSlack` delays a
step's first command; `expand` = a second Town Hall near the nearest known free mine. Worker targets are **train steps** (the schema
has no worker-target field: Malazan 20 in Age I = `time 0, train laborer, count 15` on top of the 5 starting workers, 30 in Age II =
a second step at the Age II time; Whirlwind 18 / 26). Age II buildings fire by `time` (the triggers are time / workers / pop only).

**Content.** Each page's "AI build order sketch (Normal)" + docs/02 "AI opponent" → real `buildOrder` steps (the opening, the halls
by pop, the Forge then its first upgrade, a house every ~7 pop, Age II at ~6:30 Malazan / ~7:30 Whirlwind, the Age II buildings, the
expand step at `expandAfter`), compositions per age from the pages' percentages (Age I only the units Age I can train), `attack`
from the pages (Malazan first wave at 20 army pop, Whirlwind 14; cost the matching sum), `retreat` (Whirlwind 0.5 losses per its
page; Malazan the default 0.5; hp 0.3). Difficulty numbers stay (docs/02: Easy 2 s / Hard 0.5 s think, Hard gather 0.25). Every id
must exist and belong to the faction (docs/03 "For the data track (M5-1)" lists every refusal). Rewrite each page's sketch as a
table that matches the file line for line (trigger, action, id, count) and keep the composition / wave / defense lines. BUG-0561
folded in (andii.md / shadow.md ×1.5 notes, whirlwind.md's "nearest" clause, the 1.1-over-1.3 reason).

**Acceptance criteria.** (1) Both files load (`DataValidationTests` green) with the real build orders, compositions, thresholds.
(2) `Content/AiContentTests` pins each page's build-order table to the file both ways (every step; every composition weight to the
page's percentage; the wave thresholds), and every id to the faction's roster. (3) The pages' tables and docs/02 agree with the
files; `DescriptionProblems` 0. (4) Golden header unchanged on this branch (`data-hash` 5760E83CB301B62E; the sim's branch moves the
format line only). (5) A review table for the owner: every step, old → new (the two files held one step), plus the compositions.
(6) Build 0 warnings; non-Perf green; BUG-0561 status set by the dev.

**QA focus.** Ids of the other faction or of Age II units in the Age I composition; the same step twice; a `pop` that isn't a
multiple of 0.5; the pages' tables vs the files after a deliberate one-line edit (the pin must fail by name); `research` steps for
techs the faction's buildings can't research.

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows may fail under load; rerun alone before filing.
- `studio/bugs/README.md`: each track appends only its own rows.
- The sim's golden regen (format 5) and the data's `ai.json` edits are in different files: the data branch must not touch
  `sim/Rts.Sim.Tests/Replays/`; the sim branch must not touch `game/data/factions/**`.
- The view must not read `SimConfig` seats this session (they land with the sim merge); next session wires them.
