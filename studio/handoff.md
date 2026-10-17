# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-16-1654** (the second full session of 2026-10-16; 2 / 8 after it). Base
commit **e48cb0e** (= `main`). **A feature session on the sim and the view; the data track STOPs cheaply** (no inbox answer, no schema
waiting; its next task is the re-print after M5-4f lands). Counters before: sim 0 / 4, view 0 / 4, data 0 / 4 → after: sim 1 / 4,
view 1 / 4, data 0 / 4. Bug ids this session: **sim from BUG-0790, view from BUG-0800, data from BUG-0810**.
Checks at PLAN: build 0 warnings (19.5 s); the non-Perf suite on e48cb0e **6,417 passed, 0 failed, 8 skipped (29 m 24 s)** (the 8 = the
known-open rows, same as the 0847 merged tree); S1 0, S2 0, S3 / S4 28 open;
golden `data-hash` **AB811AD47F68D0AE** (only the sim moves it this session, whole: header + `k` lines).

## Where we are

- `main` = e48cb0e: the 0847 integration (M5-H2, M6-VH2, D-H5). **M5 2 / 9, M6 1 / 10.** No open S1 / S2. New since 0847: nothing.
- The owner has not answered D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, or BUG-0780; the 2026-10-14 assessment entry
  stays under For your review with its pointer.
- `CLAUDE.md` line 6 still says "Current milestone: M1": **the sim dev's item 0** this session (one line).

## Standing rules (1-26)

1-26 as in the 0847 handoff (unchanged): full suite incl. Perf alone for sim rule changes (BUG-0764's paired A/B rule: a Perf row counts
as a regression only when red on head and green on base in a paired run on the same machine state); a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data
brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the
rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes
QA's files and is reported; no process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the
whole scene loop when the sim touches movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness /
the command door / the defeat rule; a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a
Perf gate measures the shipped data; 22. only one track moves the golden header per session; 23. a QA verdict without its committed
report file is no verdict; 24. a scene that stages a building before any unit of that player calls `TestSim.KeepIn` / spawns a unit
first; 25. a sim change that can move a measured data-page cell names those tests in the sim brief, the data track re-prints after, not
the same session; 26. a measured report that stages a lone non-producing building places its keeper after the attackers and fails on any
fall that isn't the attackers' kill.

---

## Sim track

### Current session plan: M5-4f · proportional defense + D15 C + BUG-0702 item 1 + `gatherRate` with the seed-21 re-record (feature, QA full, ~900-1,000 lines)

**Goal.** The owner's 2026-10-14 answer: "pulling the whole army home on any raid looks exploitable". Today `DefenseManager` ends any
wave and sends every free army unit home on one hit or one intruder within 48 m of the hall. Replace it with the response sized by the
threat (STATE "For your review", "Producer assessment, 2026-10-14", section "Design: proportional defense"; roadmap M5 criterion 7).
Three small items ride along because they touch the same files or the same golden header move (every `rules.json` `ai` field is in
`ContentHash`, so the new knobs move the header anyway; `gatherRate` moves the `k` lines too, so the re-record is whole, once).

**Items, in order:**
0. `CLAUDE.md` line 6: "**Current milestone: M5 (sim) / M6 (view); M3 data Done.**" One line; nothing else in CLAUDE.md.
1. **Proportional defense** (`Ai/DefenseManager`, `Ai/Blackboard`, `Ai/MilitaryManager` as needed, `Data/AiRulesDef` + loader validation):
   - Knobs in `common/rules.json` `ai`, required, validated with ranges, in `ContentHash`, `DataValidationTests` rows: `defendRatio` 1.5
     (0.5-10), `defendMinValue` 150 (0-5000, gold + wood), `recallHallHp` 0.5 (0-1).
   - **Threat worth** per think: the sum of (gold + wood cost by `SeenUnit.TypeId`) of the enemy non-worker units seen within
     `defendRadius` of the hall (fog-limited: only `PlayerView`'s seen units). A hit on an own building with no intruder seen counts as
     `defendMinValue`. The answer's worth = max(`defendRatio` × threat, `defendMinValue`).
   - **Who goes.** (a) The **home army** first: free army units that are not wave members (same filters as today), nearest the point
     first, until their summed cost reaches the answer. (b) If short, **part of the wave**: members nearest the hall first, as long as the
     members left in the wave keep at least the wave's start threshold (the size / value rule `MilitaryManager` used to send it; keep the
     value it was sent at on the `Blackboard`); a wave that would fall under it comes home whole (today's `End`). (c) The **whole wave**
     when the threat's worth exceeds what (a) + (b) can answer, or when the hall itself lost hp this think and is below `recallHallHp` of
     its max hp. Units from the halls keep joining while the threat holds (today's per-version rule). Workers' flee, the `defendFor` hold,
     the point / version rule (BUG-0673) and the `Move`-then-`AttackMove` rule (BUG-0670) stay as they are.
   - Members pulled from a wave are flagged out of it (`WaveFlag` 0 / a new flag) so the wave's own rule doesn't re-send them; the wave
     stays `WaveActive` with the rest. The whole-wave recall keeps the `waveCooldown` as today.
   - Deterministic ordering: slot order with distance ties broken by slot; no allocation (fixed `Blackboard` arrays sized at creation,
     itemised in docs/03 "Memory").
   - docs/02 "AI opponent" (one sentence on the sized response), docs/03 "Implementation (M5-4f)" (the rule, the knobs, memory), docs/01's
     D14 a row → "built", both faction pages' "Defense" lines are the **data track's** to re-word next session (name it in the report).
2. **D15 C** (`Ai/BuildOrderExecutor`): a `build` step whose target is the faction's House (`ai.HouseType`) is marked done without a
   command when the player's `HalfPopCap >= Rules.HalfPopCap` (the owner's decision 2026-10-14; docs/01 row 190 → "built"; docs/03 one
   bullet). `Content/AiHouseRuleReportTests` re-run green or its printed figures reported for the data track (do not edit the pages).
3. **BUG-0702 item 1** (`Ai/AbilityManager`): the cast group is seeded from the enemy nearest the caster (then grown by the ability's
   radius), not the centroid of every enemy in reach, so two groups either side of a mage get one cast. Un-skip
   `QA/AbilityManagerQaTests.TwoGroupsEitherSide_OneIsCastOn`; `AbilityManagerFuzzStressTests` stays green.
4. **`gatherRate` gold 0.7 → 0.85, wood 0.6 → 0.75** in `common/rules.json` (the owner's playtest note 3; docs/02 Economy table). This
   moves the seed-21 recorded match: **re-record the golden whole** (header + every `k` line; `CheckpointPrefixTicks` re-pinned); the
   reason in the `GatherWedgeQaTests.SameGameDataHashes` comment and the commit body, the old hash AB811AD47F68D0AE listed. The **only**
   header move this session (rule 22).
5. **OUT:** pathing slice 2, M5-4c slice 2's knobs (`gatherBonus` / `focusFire` / `pullBackWounded`), D14 b / c, M5-4d, any
   `game/data/factions/**` edit, the data track's test folders, `sim/Rts.Sim/ViewApi/**`, per-difficulty ratios.

**Acceptance criteria.**
1. `Ai/DefenseManagerTests` (or a new `ProportionalDefenseTests`) shows criterion 7's sentence: with a 12-pop wave out, **1 raider** →
   the wave keeps going (`WaveActive` true, its target unchanged) and ≤ 2 home units answer; **8 raiders** (worth more than the home
   army) → part of the wave comes home, the rest keeps the start threshold; **the hall hit and under `recallHallHp`** → the whole wave
   comes home; **a hit with nobody seen** → the minimum answer only. Each row asserts through commands sent (the door), never sim state
   written by the AI.
2. The three knobs load, validate (one row per range edge, in `DataValidationTests`), and are in `ContentHash` (`DataContentHashTests`).
3. `AiVersusAiTests` seeds 1-3 still end with a winner; `DefenseQaTests`' existing rows pass or are re-staged with the reason stated
   (the whole-map-radius row stays a turtle; no QA row loosened).
4. D15 C: a test with a build order carrying a House step at a pop where the cap is already at `Rules.HalfPopCap` → the step is done,
   no `Build` sent; below the cap the step still builds.
5. BUG-0702 item 1: `TwoGroupsEitherSide_OneIsCastOn` un-skipped and green; the cast-targeting rows in `Ai/AbilityManagerTests` and the
   fuzz green.
6. The golden re-recorded once: `ReplayGoldenTests` 2 / 2, `GatherWedgeQaTests` green with the new hash and the reason; `k` lines
   regenerated (QA re-records a second time and diffs).
7. `dotnet build` 0 warnings; the full non-Perf suite green; the Perf category run **alone once** with a paired base / head A/B (BUG-0764's
   rule; "red on both" is not a finding); `AiPerfTests`' think-cost rows within the 1 ms gate on the shipped data.
8. Docs (02 / 03 / 01) updated in the same commits; the production line count stated (excludes QA's files).

**Design references.** docs/02 "AI opponent" (defend first; the AI sees only its fog view); STATE's "Design: proportional defense" steps
1-5 (the numbers: 1.5, 150, 0.5, the 48 m radius unchanged); docs/03 "Implementation (M5-4b)" (today's trigger / point / hold / BUG-0670 /
BUG-0673 rules, all kept); docs/01 rows 2026-10-14 (D14 a, D15 C, the gather rates); docs/02 Economy table (gather rates).
**Tests that pin values this task moves (rule 25; the data track re-prints next session, not now):** `Content/AiSpendingReportTests`
(both pages' "Spending on the menu's map (D-H5)" paragraphs go stale with `gatherRate` and the defense), `Content/AiHouseRuleReportTests`
(D15 C), `QA/Content/AiContentQaTests` (content unchanged: must stay green), `Scenario/GatherTests` / `GatherWedgeQaTests` (gather
numbers), `ReplayGoldenTests` (the re-record), `DefenseQaTests`, `AiVersusAiTests`.
**Constraints most at risk.** No per-tick allocation in the AI (fixed arrays; no LINQ / sorting with allocations: an insertion sort over
the army's slot array in place); no `Dictionary` iteration order; costs read from `GameData.Units` (never a literal); the AI reads only
`PlayerView`; the knobs are data (`rules.json`), validated; no `game/data/factions/**` edit; one golden header move; the new `Blackboard`
state stays out of the hash (as today's defense marks do) and is listed in docs/03 as save state.

### After M5-4f
Pathing slice 2 (flank through your own line; the House row's far corners), M5-4c slice 2 (knobs → criterion 4), Requests 39 (+ 38),
D14 b schema, M5-4d (+ hunting the last units → criterion 3), D14 c, M5-5, M5-6. **M5-H3** after four feature sessions (1 / 4 after
this one): BUG-0764 first, then BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden this session; whole (header + `k` lines), once.
- `SurroundClaim` / `SurroundHolder` and the defense marks are save state outside the hash (BUG-0733): M6 save work carries them.
- The Perf gates: BUG-0764. A/B on the same machine state, or no claim.

---

## View track

### Current session plan: M6-V5 slot part · 2-4 player slots in the skirmish setup, free-for-all (feature, QA standard, ~700-900 lines)

**Goal.** M6 criterion 1 ("Main menu, skirmish setup (map, 2-4 slots, faction, difficulty, color, team)") minus the team column
(Requests 38, sim). The sim already supports it: `MapGenParams.StartLocations` 2-4 (quarter turns for 3-4, M5-4a), `SimConfig.Seats` of
any count = `PlayerCount`, `Simulation.MatchOver` = at most one undefeated player, 8 colours unique per match, `StartBase.Plan` for N
players (read-only, takes `factions[]`). Today `LaunchOptions.SlotCount = 2`, `Match.PickSeats` / `SimRunner.BuildSeats` take one
`Player1Ai`, and `Match.cs:145` / `:293` cap the start blocks at 2.

**Scope.**
- The setup's slot rows: add / remove a slot (2-4; slot 1 is always the human), each with faction, controller (human only for slot 1; AI
  with difficulty for 2-4), colour (unique per match: a pick another slot holds is refused / swapped as today).
- `LaunchOptions`: `--players 2..4`, `--faction <slot> <id>`, `--ai <slot> easy|normal|hard` (keep today's one-value `--ai` meaning slot 2
  for every existing scene and the smoke's `--skirmish`), `--colour <slot> <n>` for slots 1-4; `SkirmishSetup.MatchArgs` carries N seats.
- `Match.PickSeats` / `SimRunner.BuildSeats` for N seats (one `AiDifficulty?` per slot); the start blocks / `StartBase.Plan` for N
  players on the generator's N start locations (each seeing a mine, as M5-4a / M6-VH1's checks demand).
- Minimap dots and unit colours per seat; the match-over banner with 3-4 seats: "Victory" when the human is the last standing,
  "Defeat" when the human is out while others fight on (and the match continues to watch, "Continue watching" as today), "Draw" as
  today; `MatchStats` rows per seat (today's five columns).
- **BUG-0770 folded in** (`ShellTest.Flow` / `SkirmishBoot` and `AiSeatTest` get a per-process `SettingsPath`): the task touches
  `ShellTest` anyway.
- A 4-seat `ShellTest` row (menu → 4 slots → Start → four halls, four worker sets, each base sees a mine → a defeat path) + a QA scene.
- docs/02 "Game flow" 2 (already says 2-4; confirm the human-slot rule), docs/03 "Implementation (M6-V5)"; `ui.json` for every new label.
- **OUT:** teams (Requests 38), save / load, the look test, export, any `sim/` change outside `ViewApi/` (read-only; nothing expected:
  `StartBase.Plan` already takes N), `game/data/factions/**`.

**Acceptance criteria.**
1. From the menu, 1 human + 3 AI (any factions / difficulties / colours) starts a 4-seat match: four bases on the generator's four start
   locations, each hall with a mine in sight (the M6-VH1 check extended to N seats), four distinct colours; a 3-seat match too.
2. `--skirmish` unchanged (2 seats, slot 2 AI Normal; the smoke's `--skirmish` pass prints the same seats line); every existing scene
   green without edits except the two BUG-0770 ones.
3. The banner reads "Victory" when the human is the last undefeated of 4, "Defeat" when the human falls first while two AIs fight on
   (the match keeps running; `DefeatStaging`'s pattern), and the stats table lists every seat with its defeat time.
4. Colours unique with 4 slots through the UI and the flags (a collision warned about and moved to the first free colour, as M6-VH1).
5. BUG-0770: `ShellTest` / `AiSeatTest` run with a per-process `SettingsPath`, the player's `settings.cfg` untouched (md5 before / after).
6. Smoke PASS both passes; the scene loop green (rerun alone any wall-clock flake under three Godot runs); 0 warnings; docs/03; the count
   stated; `git diff --stat -- sim/` empty or `ViewApi/` read-only only.

**Design references.** docs/02 "Game flow" 2 (2-4 slots, free-for-all default, unique colours) and 4 (the win rule with N players: the
last standing wins); docs/03 "Implementation (M6-V1 / V2 / V3 / V4)" (the setup, `LaunchOptions`, colours, the banner) and "Implementation
(M5-4a)" (start locations 2-4); `ui.json` for all text.
**Constraints most at risk.** Views hold no gameplay state; player-facing text only in `ui.json`; `ViewApi/` read-only; no `sim/` change
outside it; `SimRunner.FixedFrameDelta` stays a scripted-run seam (null in play).

### After M6-V5
Save / load + replay playback (criterion 3; carries `SurroundClaim` / `SurroundHolder`, the defense marks, the flow-field cache, the fog
bits: docs/03 "Save/load and replays"), the look test (the owner's packs are in `asset-sources/`), `tools/export.ps1` with Requests 33,
the stats columns as Requests 39 lands, teams when Requests 38 lands. **M6-VH3** after four feature sessions (1 / 4 after this one):
BUG-0550 item 2, BUG-0281 item 2, BUG-0663.

### Watch-outs (view)
- The owner has not yet tried the real-cursor confinement or the Keybinds tab on the desk; an inbox report comes first.
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- The sim's M5-4f re-records the golden and changes `gatherRate`: a view scene that pins a gathered amount at a tick (the resource click's
  "last seen" rows) may move on the merged tree; re-stage with the reason, don't loosen.

---

## Data track

### Current session: STOP (cheap)

No owner answer in the inbox (D19's table, the Shock pass, `goldShare`, BUG-0780) and no schema the data track needs is on `main` yet
(D14 b's strategies schema is the sim's, after M5-4f). The sim's M5-4f moves the figures the D-H5 paragraphs quote, so the data task
waits one session on purpose.

### Next session (after M5-4f lands): re-print + re-word, QA light, ~200 lines, no number changed
- Re-run `Content/AiSpendingReportTests` on the new `gatherRate` and defense; re-print both pages' "Spending on the menu's map"
  paragraphs + docs/03's; `Content/AiHouseRuleReportTests` after D15 C and the house notes on both pages.
- Both faction pages' "Defense" lines re-worded for the proportional response (the knobs' values quoted from `rules.json`).
- **First, any owner answer** that arrives: D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 ("accept" closes it with a dated
  line on both pages; "Catapult pass" = a sweep first, then one header move the session the sim moves none).

### After that
The D14 b openings (2-3 per faction) once the strategies schema is on `main`; the owner's tweaks; the M7-M9 rosters when their
milestones open (the Teblor's `ai.json` is not pulled ahead of M7).

### Watch-outs (data)
- Rule 26 for any new measured report. The data track edits no C# outside `sim/Rts.Sim.Tests/Content/` and `sim/Rts.Sim.Tests/QA/Content/`.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on both trees on this PC; an owner fresh-boot run is wanted (Waiting on you) before the sim's M5-H3
  re-baselines. Until then, paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order sim → view, then the push (no data branch this session). The Producer's merged-tree check runs the scene loop: the view
  touches `StartBase` callers and the sim moves the golden / `gatherRate`.
- Only the sim moves the golden this session (the seed-21 re-record), whole: header + `k` lines.
