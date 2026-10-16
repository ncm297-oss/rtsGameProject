# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-16-0847** (the first full session of 2026-10-16; 1 / 8 after it). A
**hardening session on all three tracks, all three ACCEPT**: sim **M5-H2** (1 fix round), view **M6-VH2** (0 rounds), data **D-H5**
(1 round for the sim-found BUG-0761). **Every counter is 0 / 4: the next session is a feature session** for the sim and the view; the
data track STOPs cheaply unless the inbox has an answer. No golden move this session (`data-hash` **AB811AD47F68D0AE**, every `k`
line unchanged). Integration sim → view → data (the data branch already contains the sim branch up to ac80818; the sim's re-check
commit 12131c8 adds QA rows + bug files only). Bug ids next session: **sim from BUG-0790, view from BUG-0800, data from BUG-0810**.

## Where we are

- `main` after this integration = 8c39c08 + sim 12131c8 + view cdb4858 + data 4420985 (+ the Producer's studio files). Expected
  conflicts, all unions: `studio/bugs/README.md` (three sides), `studio/qa/coverage.md`, `studio/bugs/BUG-0745-*.md` (the Producer's
  status line in the sim worktree wins: "fixed (Producer, 2026-10-16-0847 ACCEPT ...)"; keep both verification sections).
- **M5 2 / 9, M6 1 / 10** unchanged (a clean-up session). Open bugs: **S1 0, S2 0**, S3 13, S4 15. New: **BUG-0764 (S3, sim):** 17-20 Perf
  rows red on an idle machine on base and head alike; **BUG-0770 (S4, view):** two dev scenes read the player's `settings.cfg`;
  **BUG-0780 (S3, data, the owner's call):** Catapults 1.7× / 1.6× the Raider per cost on a hall / Town Hall.
- The owner has not answered D19's review table, the Shock pass, `goldShare` or (new) BUG-0780; the 2026-10-14 assessment entry has a
  pointer at the top of For your review now (the owner asked where it was).
- `CLAUDE.md` still says "Current milestone: M1": the Producer doesn't edit CLAUDE.md; **the sim dev's first one-line item next session**
  ("M5 (sim) / M6 (view); M3 data Done"), or the owner.

## Standing rules (1-26)

1-25 as before (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`; per-slot memory
itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track test that pins
a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent sim tests; `git
checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is reported; no process
killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop when the sim touches
movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door / the defeat rule;
a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a Perf gate measures the shipped
data; 22. only one track moves the golden header per session; 23. a QA verdict without its committed report file is no verdict; 24. a
scene that stages a building before any unit of that player calls `TestSim.KeepIn` / spawns a unit first; 25. a sim change that can move
a measured data-page cell names those tests in the sim brief, the data track re-prints after, not the same session).
**26 (new, BUG-0761):** a measured report that stages a lone non-producing building places its keeper **after** the attackers (so the
unit slots, and the measured cells, don't shift) and fails on any fall that isn't the attackers' kill (`DeathEvent.Collapsed`, another
killer, full hp on the last tick, nobody struck). `BuildingKillTimeReportTests.Raze` is the pattern.
**Perf note (BUG-0764):** until the gates are re-baselined, a Perf row counts as a regression only when it is red on head and green on
base in a paired run on the same machine state; "red on both" is BUG-0764, not a finding against the task.

---

## Sim track

### Next task candidate: M5-4f · proportional defense + D15 C + BUG-0702 item 1 + `gatherRate` with the seed-21 re-record (feature, QA full, ~900-1,000 lines, counter 0 / 4 → 1 / 4)

**Goal.** The owner's 2026-10-14 answer: a small raid must not recall a whole wave. The design is under STATE "For your review"
(Producer assessment, 2026-10-14) and docs/01's row: the response is sized by the raiders' worth (`ai.defendRatio` × their cost, at
least `ai.defendMinValue`), taken from the home army first, then from the wave's members nearest home as long as the wave keeps its
start threshold, the whole wave only when the threat is worth more than that or the hall itself is low and under attack
(`ai.recallHallHp`). Plus three small items that touch the same files or the same golden move.

**Items, in order:**
0. `CLAUDE.md` line 5: "Current milestone: M5 (sim) / M6 (view); M3 data Done." (one line; the rest of CLAUDE.md untouched).
1. **Proportional defense** (`Ai/DefenseManager`): the three knobs in `rules.json` `ai` (required, validated with ranges, in
   `ContentHash`), the threat's worth from `PlayerView`'s seen enemy units (cost by type, fog-limited), the home army first, the wave
   split by distance to the hall, the whole-wave recall only above the threshold or on `recallHallHp`. A lone raider never recalls a
   wave (the owner's sentence, roadmap criterion 7); docs/02 "AI opponent" + docs/03 "Implementation (M5-4f)" + docs/01 row "built".
2. **D15 C**: `BuildOrderExecutor` skips a house step once the population cap is reached (the owner's decision 2026-10-14; docs/01 row
   "built"); `Content/AiHouseRuleReportTests` re-run (the data track re-prints its house notes next session).
3. **BUG-0702 item 1**: the cast group is seeded from the enemy nearest the caster (or the densest), so two groups either side of a
   mage get a cast; un-skip `QA/AbilityManagerQaTests.TwoGroupsEitherSide_OneIsCastOn`.
4. **`gatherRate` gold 0.7 → 0.85, wood 0.6 → 0.75** (`rules.json`, sim-owned; the owner's playtest note 3): this changes the recorded
   seed-21 match, so **the golden is re-recorded whole** (header + every `k` line; `CheckpointPrefixTicks` re-pinned; the reason in the
   `SameGameDataHashes` comment and the commit body; the old hash listed). **The only header move this session (rule 22).**
5. OUT: pathing slice 2, M5-4c slice 2's knobs, D14 b / c, M5-4d, any `game/data/factions/**` edit, the data track's test folders.

**Tests that pin values this task moves (rule 25 / the data brief's rule):** `Content/AiSpendingReportTests` (printed conclusions +
both pages' "Spending on the menu's map (D-H5)" paragraphs: they go stale with `gatherRate` and the defense; **the data track re-prints
them next session**, no test pins the seconds), `Content/AiHouseRuleReportTests` (D15 C), `QA/Content/AiContentQaTests` (the build-order
rows: content unchanged), `AiVersusAiTests` (defense re-staged), `DefenseQaTests` (the knobs at their edges; the whole-map radius row
stays a turtle), `GatherWedgeQaTests` / `Scenario/GatherTests` (gather numbers), `ReplayGoldenTests` (the re-record).
**Acceptance.** Criterion 7's sentence shown by a test (1 raider vs a 12-pop wave: the wave keeps going, 1-2 home units answer; 8
raiders: part of the wave; the hall at `recallHallHp` under attack: the whole wave); AI-v-AI seeds 1-3 still end with a winner; the
knobs validated; the golden re-recorded once with `k` lines regenerated and the reason stated; full suite incl. Perf alone once (BUG-0764's
rule); build 0 warnings; docs; the line count stated.
**QA focus (full):** the split under the door fuzz and a 4-seat match; a raid that grows mid-response; the wave's threshold kept after a
split; hash twins through a split; the re-record's `k` lines vs a second recording; the AI spending report's figures printed for the
data track.

### After M5-4f
Pathing slice 2 (flank through your own line; the House row's far corners), M5-4c slice 2 (knobs → criterion 4), Requests 39 (+ 38),
D14 b schema, M5-4d (+ hunting the last units → criterion 3), D14 c, M5-5, M5-6. **M5-H3** after four feature sessions: BUG-0764 first,
then BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden next session; the data track edits no `game/data/**` file.
- `SurroundClaim` / `SurroundHolder` (now one layer per player) are save state outside the hash (BUG-0733): any M6 save work carries them.
- The Perf gates: BUG-0764. A/B on the same machine state, or no claim.

---

## View track

### Next task candidate: M6-V5 slot part · 2-4 player slots in the skirmish setup, free-for-all (feature, QA standard, ~700-900 lines, counter 0 / 4 → 1 / 4)

**Goal.** M6 criterion 1 ("Main menu, skirmish setup (map, 2-4 slots, faction, difficulty, color, team)") minus the team column
(Requests 38, sim). The sim already supports it: `MapGenParams.StartLocations` 2-4 (quarter turns for 3-4, M5-4a), `SimConfig.Seats`
of any count = `PlayerCount`, `Simulation.MatchOver` = at most one undefeated player, 8 colours unique per match.

**Scope.** The setup's slot rows (add / remove a slot, 2-4; each with faction, human / AI + difficulty, colour), `LaunchOptions` /
`SkirmishSetup.MatchArgs` carrying 3-4 seats (`--players`, `--faction` / `--ai` / `--colour` per slot), `Match.PickSeats` / `SimRunner.
BuildSeats` for N seats, `StartBase.Plan` for N players (read-only; it takes `factions[]` already), the minimap / colours / match-over
banner with 3-4 seats (the banner's "Victory" when the human is the last standing; "Defeat" when out while others fight on, and the
stats table rows per seat), the smoke's `--skirmish` pass unchanged, a 4-seat `ShellTest` row + a QA scene. Fold in **BUG-0770**'s
`ShellTest` / `AiSeatTest` per-process `SettingsPath` if the task touches those files (it does: `ShellTest`). **OUT:** teams (Requests
38), save / load, the look test, export, any `sim/` change outside `ViewApi/` (read-only).
**Acceptance.** A 4-seat match from the menu (1 human + 3 AI) starts with four bases on the generator's four start locations, each
seeing a mine; a 3-seat match too; `--skirmish` still 2 seats; the banner and stats correct when the human wins last and when it falls
first (`DefeatStaging`'s pattern); smoke PASS both passes; the loop green; docs/02 "Game flow" 2 / docs/03 "Implementation (M6-V5)".
**QA focus (standard):** every slot count × faction × controller through the UI and the flags; colours unique with 4 slots; the
match-over paths with 3-4 seats (the human second out; a draw of the last two); the twin with 4 seats; the minimap dots per seat.

### After M6-V5
Save / load + replay playback (criterion 3; carries `SurroundClaim` / `SurroundHolder`, the flow-field cache, the fog bits: docs/03
"Save/load and replays"), the look test (the owner's packs are in `asset-sources/`), `tools/export.ps1` with Requests 33, the stats
columns as Requests 39 lands. **M6-VH3** after four feature sessions: BUG-0770 if still open, BUG-0550 item 2, BUG-0281 item 2.

### Watch-outs (view)
- The owner has not yet tried the real-cursor confinement or the Keybinds tab on the desk; an inbox report comes first.
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- `SimRunner.FixedFrameDelta` is a scripted-run seam (null in the game): don't let a production path set it.

---

## Data track

### Next session: STOP cheaply unless the inbox has an answer

**First: any owner answer** on D19's review table, the Shock pass, `goldShare` 0.6 → 0.5, or **BUG-0780** ("accept" closes it with a
dated line on both pages; "Catapult pass" = a sweep first, as D18 did, then one header move the session the sim moves none). If an
answer arrives, that is the task (QA light; name every sim-track test that pins a value it changes; grep the suite for the old literal).

**Otherwise STOP:** no schema the data track needs is on `main` yet (D14 b's strategies schema is the sim's, after M5-4f), and the
sim's M5-4f moves the figures the D-H5 paragraphs quote: **the session after M5-4f lands**, the data task is "re-print the D-H5 spending
paragraphs on both pages + docs/03 (`Content/AiSpendingReportTests` on the new `gatherRate` and defense) and the house notes after D15 C
(`Content/AiHouseRuleReportTests`)", QA light, ~150 lines, no data change.

### After that
The D14 b openings (2-3 per faction) once the strategies schema is on `main`; the owner's tweaks; the M7-M9 rosters when their
milestones open (the Teblor's `ai.json` is not pulled ahead of M7).

### Watch-outs (data)
- Rule 26 (above) for any new measured report.
- The data track edits no C# outside `sim/Rts.Sim.Tests/Content/` and `sim/Rts.Sim.Tests/QA/Content/`.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on both trees on this PC; an owner fresh-boot run is wanted (Waiting on you) before the sim's M5-H3
  re-baselines. Until then, paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order sim → view → data, then the push. The Producer's merged-tree check runs the scene loop whenever the sim touches movement
  (M5-4f doesn't; the generator / `StartBase` are the view's next task's: run it then).
- Only the sim moves the golden next session (the seed-21 re-record), and whole: header + `k` lines.
