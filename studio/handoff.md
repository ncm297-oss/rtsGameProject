# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-13-0916** (third full session of 2026-10-13, 3 / 8; base `main` a8ebff7).
Counters before it: **sim 0 / 4, view 0 / 4, data 1 / 4**: a **feature session on sim and view**; the **data track lands D16** (resumed
branch) and takes no new task. Bug ids this session: **sim from BUG-0670, view from BUG-0680, data from BUG-0690.** Inbox: empty.

## Where we are

- `main` a8ebff7 = sim M5-H1 + view M6-VH1 (session 0527). **D16 is accepted but not on `main`:** the 0527 integration died at the
  data step. Its branch `studio/2026-10-13-0527-data` @ 16c6da7 already has `main` a8ebff7 merged in (clean; the diff against
  `origin/main` is exactly the D16 files: docs/01 row, docs/02 Easy line, both pages' Difficulty tables, `Content/AiContentTests` J-K,
  `QA/Content/AiDifficultyFileSideQaTests`, `AiHouseRuleReportTests`, BUG-0541 / 0641 / README, `qa/2026-10-13-0527-D16.md`, coverage).
  **Conductor:** merge `origin/main` once more (a no-op unless the sim or view branch of 0916 lands first: then union docs/01,
  `bugs/README.md`, `qa/coverage.md` as at 0527), build, run `Content` + non-Perf, push. No dev / QA work on the data track this session.
- Build on `main` at the PLAN: 0 warnings; non-Perf suite in the Producer's checks (the result is in the session log at ACCEPT).
- **One open S2 on `main`: BUG-0661** (view: `QaH2Test` seeds 1 / 6 and `QaM27Test`, 19.3 m marches against a 20 m line, 11.3 m
  against 15 m). Test staging on the new start geometry. **The view's first item.** No open S1.
- **The game from the menu:** Skirmish → Start opens with a Town Hall and 5 workers a side on mirrored start locations on one level;
  the AI gathers, scouts, builds, waves and retreats. Missing for a real match: the sim's defeat rule (this session), the AI's defense
  (this session), its abilities / difficulty knobs / expansion / rebuilding (M5-4c), victory / defeat on screen (M6-V4, next session).

## Standing rules (1-18)

1-16 as in the 1930 / 0146 / 0527 PLAN handoffs (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; **one system per brief**, the
dev's count excludes QA's files and is reported; **no process killing by image name**).
17. **A dev never loosens a QA row.** A failing QA row is a real finding (fix the code) or a wrong test (say so in the report; QA changes it).
18. **The Producer's merged-tree check runs the whole scene loop** (`tools/qa/scene-loop.ps1 -SkipPlayable`) whenever the sim branch
    touches the map generator, `ResourcePlacer`, `ViewApi/StartBase`, or (new) `World.Buildings` liveness / the command door.

---

## Sim track

### Current session plan: M5-4b slice 1 — the defeat rule (Requests 37) and the AI's defense · feature · QA full

**Goal.** Give the match an end and the AI a reason to come home. Today nothing in the rules says a player has lost (the view prints a
dev line from its own `ViewApi/MatchOver` watch), and the AI ignores an enemy in its base (docs/02 "AI opponent": "defend the base when
attacked" is the first behaviour by priority, and the faction pages' Defense lines are unimplemented). Both are needed before M5-5's
"one side wins" test and the view's M6-V4 banner.

**Scope.**
- **System 1, the defeat rule** (docs/02 "Game flow" 4): `World.IsDefeated(p)` = the player has no live building (construction sites
  included) and had at least one before; a player that never had a building is never defeated. Both facts (`EverBuilt`, `Defeated`) are
  sticky per-player state, **hashed**. Evaluated once per tick after building destruction / site cancel have applied (the end of the
  tick's building phase; say where in docs/03). `World.DefeatEvents` (`DefeatEvent { Player, Tick }`), a one-tick list like
  `AbilityEvents` / `Deaths` (read between ticks, emptied at the next tick's start). `Simulation.MatchOver` (true when at most one
  undefeated player remains among those that ever built; never true with 0-1 players that ever built). **A defeated player's commands
  are dropped at the command door** (every kind; same tick-order rules as a dropped command today) and **the AI of a defeated seat
  stops thinking** (sends nothing; its blackboard is left as is). Its live units stay (they keep fighting back and finishing orders
  already applied: docs/02 says only "no buildings left"; Producer default, docs/01 row). Replay playback: derived from state, no header
  field, format unchanged. **The drop happens at apply time, not at `Enqueue`** (`Enqueue` keeps accepting and the recorder keeps
  logging a defeated player's commands, like a `Move` of a dead unit today; the door drops them when they apply, on playback too, so
  hashes match and the view never gets an exception). State this in docs/03 and test it.
- **System 2, defense** (`DefenseManager`, before the executor in the think order: docs/02's priority). **Trigger:** since the last
  think an own *finished or unfinished* building within `ai.defendRadius` of the hall's centre lost hp (the census compares each own
  building's hp to the one kept from the last think; a building seen for the first time doesn't trigger), or an enemy non-worker unit
  is seen within `defendRadius` of the hall. **Response, while the threat holds** (`ai.defendFor` seconds after the last trigger):
  (a) the wave ends like a retreat (members home, the cooldown) and no wave starts; (b) every free army unit (not `Casting`, no AI
  command pending) more than `ai.rallyReach` from the **defense point** gets one `AttackMove` to it: the last seen enemy army centroid
  if it is within `defendRadius` of the hall, else the most recently damaged building's footprint centre; the point is re-issued only
  when it moves by more than `rallyReach` (a per-unit "sent to this version" like the rally rule); (c) own workers within
  `ai.workerFleeRadius` of the defense point get one `Move` to the nearest own finished drop-off / hall (the faction pages: "Laborers
  flee to the Garrison Keep"), and the economy manager leaves them alone until the threat ends (then they are idle and get gather
  orders as today). **Knobs** in `common/rules.json` `ai` (generic rules, sim-owned): `defendRadius` 48 m (8-256), `defendFor` 20 s
  (0-600, to ticks), `workerFleeRadius` 16 m (1-64), each a `DataValidationTests` row; the minimum entries ship with this task.
  `focusFire` / `pullBackWounded` stay unread (M5-4c).
- **Out of scope:** teams (Requests 38), the post-match stats (M6-V4 reads `World.Kills` / `Losses` etc.), expansion, rebuilding, the
  ability manager, the difficulty knobs, periodic re-scouting, high-ground scouting, any view change (the view keys off
  `DefeatEvents` / `MatchOver` next session; do not touch `ViewApi/MatchOver`), any `ai.json` field.
- **Size:** up to 1,200 dev lines (QA's files excluded). If the defeat rule plus its tests passes 500 lines, ship it alone and say so;
  defense then becomes slice 2.

**Acceptance criteria.**
1. `MatchOverTests`: (a) a player whose last building (a finished one; separately, a site that is cancelled; separately, a site razed)
   goes loses `IsDefeated` false → true on that tick, one `DefeatEvent` with the tick, `MatchOver` true in a 2-player match; (b) a
   player with units and no building from tick 0 is never defeated and never ends the match; (c) in a 3-player match `MatchOver`
   stays false after one defeat and turns true after the second; (d) the defeated player's `Move` / `Build` / `Train` are dropped
   (no state change, hash equal to a run with them never sent) while another player's identical commands apply; (e) the AI of a
   defeated seat issues no command for 2,000 ticks after its defeat (`ICommandSink` counter).
2. Hash and replay: `StateHashTests` shows a flipped `Defeated` / `EverBuilt` changes the hash; `AiReplayTests` / `ReplayGoldenTests`
   green; a recorded match that ends in a defeat replays to the same hashes past `MatchOver` (the door drops the same commands). The
   golden `k` lines may move **once** (the new hashed fields); say why in the commit, `data-hash` unchanged.
3. `AiVersusAiTests`: an AI-vs-AI match on `AiScenes.Generated` with the early-wave data where one hall is pre-damaged to 1 hp and razed
   by the other seat's wave reaches `MatchOver` and the loser's AI goes silent; the 20-minute "one side wins" assertion is **not**
   required yet (M5-5).
4. `Ai/DefenseManagerTests` (flat scene, combat off where possible): (a) an enemy army seen 30 m from the hall pulls every free army
   unit (one `AttackMove` each, to the centroid), ends an in-progress wave (members get `Move` home, cooldown set) and starts none while
   the threat holds; (b) a building hit (hp lowered between thinks) without any enemy seen sends the army to that building's centre;
   (c) workers within `workerFleeRadius` get one `Move` to the hall and no `Gather` until `defendFor` after the last trigger, then
   `Gather` again; (d) a threat outside `defendRadius` triggers nothing; (e) the point re-issues only when it moves past `rallyReach`;
   (f) the trigger reads only `PlayerView` (own buildings' hp are own state; the enemy through `SeenUnit`): `PlayerViewArchitectureTests`
   green, the source scan unchanged.
5. Determinism: `AiDeterminismTests` with a defended base (two runs, equal hashes); `CommandDoorFuzzStressTests` gains a row where a
   random player is defeated mid-run and every later command of theirs is dropped (hash equal to the filtered stream).
6. Cost: `AiPerfTests` stays under its 1 ms-a-think gate with the defense pass in every think (eight Hard seats, each under attack);
   `AllocationProbe` 0 B; the per-slot memory for the kept building hp (one int per building slot per seat) itemised in docs/03.
7. Build 0 warnings; the whole suite green, Perf alone; smoke PASS; docs/03 "Implementation (M5-4b)" (both systems, the think order, the
   door rule, the hash fields, the knobs, memory, "for the view (M6-V4)": `DefeatEvents`, `MatchOver`, `IsDefeated`); docs/01 rows for
   the Producer defaults (units of a defeated player stay; commands dropped; the defense trigger and point rules); the dev's line count.

**Design references.** docs/02 "Game flow" 4 (defeat = no buildings left; last standing wins), "AI opponent" (behaviours in priority
order: defend first), `docs/factions/malazan.md` + `whirlwind.md` "Defense" lines; docs/03 "AI architecture", "Implementation (M5-3)"
(the think order, the enemy army memory `Blackboard.EnemyArmyCentroid` / `Count` / `Tick`, the rally rule's per-version sending, the
wave's retreat path), "Implementation (M5-H1)" (`WaveBusyTick`); STATE "Requests for the sim track" 37.

**Tests required.** `MatchOverTests` (new), `Ai/DefenseManagerTests` (new), rows in `StateHashTests`, `AiReplayTests`,
`AiVersusAiTests`, `AiDeterminismTests`, `CommandDoorFuzzStressTests`, `AiPerfTests`, `DataValidationTests` (three knobs),
`PlayerViewArchitectureTests` unchanged and green.

**Constraints.** No `Command` kind is added (if one is, rule 1's fuzz row). No Godot, no wall clock, no `System.Random`; nothing
allocates per tick or per think (the kept-hp array sized at construction). Knobs in `rules.json`, never literals (the think-order and
"re-issue past `rallyReach`" guards are structure, not numbers). `MatchOver` reads sim state only; never the view's watch. Player-facing
text: none. Tests content-independent (the scene builds its own buildings; don't pin shipped `ai.json` numbers).

**QA focus (full).** Attack the door: a defeated player's queued / shift-queued orders already applied keep running (expected) but no new
one applies (any kind, including `Cancel` / `CancelTrain` / `SetRally`); the same stream replayed from a recording hashes equal past the
defeat. Defeat on the exact tick: a site cancelled the same tick a building finishes; two players losing their last building on one
tick (both events, `MatchOver` true, order by player); a summoned / expired unit never counts as a building. Three and four seats
(`MapGenParams.StartLocations` 3-4) with mixed defeats. Defense: a threat that flickers (seen one think, gone the next) must not
re-issue orders each think; an enemy wave parked at `defendRadius` ± 1 m; the army pulled home while a wave is mid-march, then the
wave rule restarting after `defendFor`; workers fleeing from a mine inside the radius while carrying (no cargo lost beyond the
sim's drop rules); `defendFor` 0 and `defendRadius` 256 as valid data. Scale: 8 Hard seats all under attack, 1 ms a think, 0 B.
Perf alone before filing. Golden: `k` lines move at most once; `data-hash` must not move (no data value changes).

---

## View track

### Current session plan: BUG-0661 (S2) first, BUG-0662 if a few lines, then M6-V6, the settings screen · feature · QA standard

**Goal.** Make `main`'s scene loop green again (BUG-0661: two scenes staged on the old start geometry), then ship the settings screen,
the one M6 shell criterion that needs nothing from the sim (criterion 4; docs/02 "Game flow" 7). Victory / defeat on screen (M6-V4)
waits for the sim's defeat rule, which is being built this session, so it is next session's view task.

**Scope.**
- **BUG-0661:** `QaH2Test` seeds 1 and 6, `QaM27Test`: re-derive each march threshold from what the scene proves (a 10 s march at the
  unit's speed from the staged block; seed 6's 11.3 m is terrain in the way: move the block or the goal onto open ground), the reason
  written in the test; no threshold loosened without one. Whole loop green.
- **BUG-0662 (S3):** only if a few lines: `GroundDiscPool.Reclaim` takes a node whose key is lower than the one being taken (callers take
  keys ascending), else grows; a Check that stable keys keep their nodes. Else leave it with a note.
- **M6-V6, settings:** a Settings screen reachable from the main menu (docs/02 "Game flow" 1: Skirmish, Load Game, Settings, Quit; Load
  Game stays absent until save / load) and from the pause menu. Fields: **resolution** (a list of common 16:9 / 16:10 sizes plus the
  current one), **window mode** (windowed / borderless / fullscreen), **VSync** (on / off), **quality** (Low / Medium / High: a preset
  table the dev defines in docs/03, e.g. shadows, MSAA, render scale), **master / music / SFX volume** (0-100; add `Music` and `Sfx`
  buses under `Master` in `game/default_bus_layout.tres` if absent; the existing select / command sounds go through `Sfx`), **edge-pan
  speed** (a multiplier on the camera's edge pan, 0.5-2.0). **Persisted** in `user://settings.cfg` (Godot `ConfigFile`, text), written on
  Apply / Back, read and applied at boot before the menu shows; a missing or bad file = defaults, logged, never an error. **Apply now**
  (window / vsync / buses / camera) and **Back**; **Defaults** resets. Every word from `ui.json`: a new `settings` section (named
  exception: the view edits `game/data/common/ui.json` this session; the sim does not) read like `menu` / `setup` / `pause`
  (`SettingsText` enum; a missing key is one load error naming it). Keybinds: **out** (a later slice, its own criterion item). The
  existing `--vsync` / `--mute` flags keep working and override the file for that run.
- **Out of scope:** keybinds, save / load, the victory banner (M6-V4), the 2-4 slot setup, any `sim/` change (nothing is needed), any
  real-art or font work.
- **Size:** up to 1,200 dev lines (QA's files excluded).

**Acceptance criteria.**
1. BUG-0661: `QaH2Test` (seeds 1 and 6) and `QaM27Test` PASS on `main` + this branch with thresholds derived in-test (the comment says
   from what: unit speed × seconds × a stated slack, or the staged distance); the whole loop (`tools/qa/scene-loop.ps1 -SkipPlayable`)
   green; `M4PlayableTest` seeds 1 and 6 PASS; BUG-0661 proposed fixed.
2. Headless `SettingsTest.tscn` (in the loop): from the menu, Settings shows every field with its `ui.json` text and the current values;
   change resolution, window mode, VSync, quality, the three volumes and edge-pan; Back: `user://settings.cfg` holds them (read back
   through `ConfigFile`), the buses' volumes equal the picks (`AudioServer.GetBusVolumeDb` through the dev's dB mapping), the camera's
   edge-pan multiplier equals the pick; the screen reopened shows the picks; Defaults restores the defaults and the file; a corrupt
   file at boot gives the defaults and one logged warning, no error line. Window and vsync calls are skipped headless and asserted
   through the stored state (the Check says so). From the pause menu the same screen opens and Back returns to the pause menu with the
   tree still paused.
3. Windowed (`-- --shots <dir>`): `settings-menu.png` saved from the screen; the dev looks at it (definition of done 3) and the
   screenshot is attached to the report; a window-mode change applies and reverts without the game losing the match.
4. `ShellTest` rows: the new `ui.json` keys present, each missing one an error naming it; `--vsync off` with a file saying on gives off
   for the run and the file unchanged.
5. Build 0 warnings; smoke PASS both passes; the sim suite non-Perf green (no sim change); docs/03 "Implementation (M6-V6)" (the file
   format with every key and default, the quality presets table, the dB mapping, the boot order), docs/02 "Game flow" 7 unchanged or
   amended with the preset names, docs/01 row for any Producer default the dev takes; the dev's line count.

**Design references.** docs/02 "Game flow" 1 and 7, "Controls and camera" (edge pan); docs/03 "Implementation (M6-V1)" (the shell's
screens, `ShellUi`, the boot rule, the pause's `Always` processing), "Implementation (M2-7)" (`--vsync`), "Audio (M2-6)" (`Sfx`,
`MasterBus`, `--mute`), "Build and export" (`user://` is export-safe); CLAUDE.md rule 8.

**Tests required.** `SettingsTest.tscn` (new, in the loop), `ShellTest` rows, the re-derived `QaH2Test` / `QaM27Test`; the loop green.

**Constraints.** Views hold no gameplay state; the settings store is view state (never in `sim/`). Text only from `ui.json`. No new
NuGet / plugins. `game/data/common/ui.json` is the only sim-owned file touched (named here). `GroundDiscPool` change allocation-free.

**QA focus (standard).** The persisted file: every key missing, a wrong type, a value out of range (resolution 1×1, volume 999, quality
"ultra"), a file with extra keys; a read-only `user://` (log, don't crash). Volume 0 → the bus muted or -80 dB, never NaN; `--mute`
plus a saved volume; `--vsync` precedence. Window-mode cycling windowed → fullscreen → borderless → windowed in one run under a running
match (no lost input, the HUD still laid out: `--screenshot` after each). Edge-pan 0.5 and 2.0 measured against the camera's speed in
the test (`CameraRig` edge pan over N frames). The pause path: Settings from pause, Esc, Esc: the tree state and the pause menu
stack. BUG-0661: the re-derived thresholds under load (run the two scenes 5× beside the sim suite); the reasons stated in the test.

---

## Data track

### Current session plan: land D16 (resumed branch); no new task

The conductor finishes the 0527 integration: `studio/2026-10-13-0527-data` @ 16c6da7 (D16 + `main` a8ebff7). Merge `origin/main` again
(unions for docs/01 / `bugs/README.md` / `qa/coverage.md` if the sim or view of 0916 lands first), build (0 warnings), `dotnet test
--filter "FullyQualifiedName~Content"` (expect 933 / 0), non-Perf, push. The Producer's ACCEPT of D16 (session 0527) stands: the diff was
re-read at this PLAN. Next session's data task is D17 (STATE's data queue 23) unless inbox answers arrive.

Watch-outs (data): `rules.json` is sim-owned (the sim's M5-4b adds three `ai` knobs there this session: `defendRadius`, `defendFor`,
`workerFleeRadius`; a data task never edits it); no `ai.json` field is added this session; the Teblor's `ai.json` stays with M7.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge).
- **Merge order sim → view → data (the resumed D16 branch last).** The sim's M5-4b may move the golden's `k` lines once (the hashed
  defeat fields); nobody else touches `Replays/`. The view's `ui.json` `settings` section and the sim's `rules.json` knobs are
  different files: no conflict.
- Rules 16-18 above.
