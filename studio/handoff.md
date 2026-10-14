# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-14-0946** (a one-off run on the owner's review answers and first playtest; not
part of a chain; first full session of 2026-10-14, 0 / 8 before it). All three tracks **GO**: sim **M5-4e** (the new win condition),
view **M6-V7** (edge-pan on a second monitor, control groups for buildings, BUG-0710, the match-over scenes re-staged), data **D18**
(four reports, no number changed). Counters before this session: **sim 2 / 4, view 2 / 4, data 2 / 4** (each → 3 / 4 at ACCEPT). Bug ids:
**sim from BUG-0703, view from BUG-0711, data from BUG-0721.** The owner's assessment is the first entry of STATE "For your review"; the
decisions are rows in docs/01 (2026-10-14) and three new M5 criteria in docs/05.

## Where we are

- `main` = 2e1d3f7 (the owner's two inbox commits on top of the 1616 integration). Build 0 warnings at this PLAN; the non-Perf suite
  and the smoke run are in the Checks of the PLAN output. Golden format 6, `data-hash` **C6FC120A55AE4A0B**. **No open S1 / S2.**
- Owner decisions this morning (docs/01): the win condition (below), Easy casts too, ×1.5 back, the hp / armor / cost pass, D15 C,
  the proportional defense (designed in STATE), D14 feasible and staged, the Ages to be dropped later (design task after M5's AI work).

## Standing rules (1-21)

1-21 as in the 1616 handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`;
per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track
test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent sim
tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is reported; no
process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop when the sim
touches the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door; a QA scene row for a bug
left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a Perf gate measures the shipped data).
22. **Only one track moves the golden header per session** (a `rules.json` / `ai.json` / faction-data change); two regens can't be
    merged by the conductor. This session nobody moves it (M5-4e adds no knob; D18 edits no data). M5-4f (three new knobs) and D19
    (the owner's numbers) both move it, so **they may not run in the same session**: D19 first if D18's tables are in, else M5-4f
    first; the other waits one session. The PLAN decides.

---

## Sim track

### Current session plan: M5-4e · the new win condition (feature, QA full)

**Goal.** Owner decision 2026-10-14 (playtest note 9, docs/01): a player is out once it has **no live unit and no finished
unit-producing building**; houses, camps, the Forge, towers and unfinished sites don't need to fall. Today (M5-4b) a player is out when
its last building (sites included) goes. The view's banner, the stats and the AI's silence key off the same flags, so only the rule and
its tests move.

**Scope.**
- `World.Defeat.cs` / `PlayerLedger`: defeated = `EverBuilt` (unchanged: set when any building of the player's is placed; a player that
  never had a building is never defeated, so the sandboxes keep working) **and** `LiveUnits[p] == 0` **and** `ProductionBuildings[p] == 0`,
  where a production building is a **finished** building whose type trains at least one unit (`GameData.UnitsTrainedAt(type)` non-empty:
  with today's data the Town Hall and the five halls; a site never counts; a summoned / temporary unit counts as a unit while it lives).
  Both counts maintained at spawn / finish / free (derived from the stores, not hashed, like `LiveBuildings`; an oracle test recounts).
  Evaluated once a tick in phase 11b as today, in player order, one `DefeatEvent` each; `MatchOver` latched as today.
- New read-only: `World.LiveUnits(p)`, `World.ProductionBuildings(p)` (STATE Requests 40 for the view).
- The door, `PlacementError.Defeated`, the AI of a defeated seat, the hash bits 34-36: unchanged in meaning (34 = "has built, nothing that
  keeps it alive left" may be dropped or kept; say which in docs/03). **The golden's `k` lines and header may not move** (the cross-map
  match has no building; no data change).
- docs/02 "Game flow" 4 rewritten with the rule; docs/03 "Implementation (M5-4b)" → a new "(M5-4e)" section with the counts, what a
  "production building" is, the never-built exception and the hunt caveat; docs/01 row exists.
- `ViewApi/MatchOver.cs` is view-owned and dead (BUG-0710 item 2: the view deletes it this session); don't touch it. If it fails to
  compile against the new counts, leave it: the view's branch removes it before the merge (merge order sim → view).
- Run the whole scene loop (`tools/qa/scene-loop.ps1`) on your branch and **list in the report every view scene that goes red and why**
  (expected: `MatchOverViewTest`, `QaM6V4Test`, `AiSeatTest` stage defeats with a worker alive or a House as the last building); the view
  track re-stages them this session (Requests 40); you don't edit `game/`.
- OUT: hunting the last units (M5-4d), teams (Requests 38), the proportional defense (M5-4f), any data change, `ui.json`.

**Acceptance criteria.**
1. `MatchOverTests` (extended, content-independent): (a) a player whose last hall falls while a worker lives is **not** defeated; the worker
   dies → defeated that tick, one event; (b) a player with units and only a House / Camp / Forge is not defeated; the units die → defeated
   (the House still standing); (c) a player with no unit and an unfinished hall site is defeated; (d) a razed hall with a second hall
   standing: not defeated; (e) a never-built player with units that all die: never defeated, `MatchOver` unaffected; (f) two players
   falling on one tick: both events, player order, `MatchOver` with nobody standing; (g) a summoned unit alone keeps its side alive until
   it expires, then defeat.
2. The door and the AI: every command of a defeated player dropped where it applies (the existing `CommandDoorFuzzStressTests` rows hold
   with the new rule, re-staged so the loser really is out); the defeated seat's AI silent; `AiVersusAiTests.AHallAt1Hp_*` re-staged (the
   loser's units gone or the test says why it is still standing).
3. Oracle: a fuzz row recounts `LiveUnits` / `ProductionBuildings` from the stores every tick for 3 seeds x 2,000 ticks x 4 players with
   spawns, deaths, construction, cancels, razing and expiring summons: equal every tick.
4. Determinism: `StateHashTests` covers the flags; a recorded match that ends by the new rule plays back past `MatchOver`; the golden
   `k` lines and header unchanged (`ReplayGoldenTests` green without a regen).
5. `AiPerfTests` / `EmptyTick` gates unchanged (the counts are O(1) per event).
6. Build 0 warnings; full suite green (Perf alone once); docs/02 / docs/03 as above; the count of changed lines stated; the list of red
   view scenes in the report.

**Design references.** docs/02 "Game flow" 4 (the owner's new wording); docs/03 "Implementation (M5-4b)" (what is replaced);
`GameData.UnitsTrainedAt`; docs/01 2026-10-14 row.

**Constraints.** No Godot; no wall clock; no allocation per tick (the counts are plain int arrays); no player-facing text in C#; the
golden may not move; one system (the defeat rule).

**Budget.** ~500 dev lines (clear design). Hard stop at 800: ship the rule and the tests, report the rest.

### Next task candidates (after this session)

1. **M5-4f proportional defense + D15 C + BUG-0702 item 1** (the design in STATE "For your review" 2026-10-14; knobs `ai.defendRatio`
   1.5 (0.1-10), `ai.defendMinValue` 150 (0-5,000, gold + wood), `ai.recallHallHp` 0.5 (0-1); the golden header moves once; **may not run
   in the same session as D19** (rule 22)). QA full, ~900 lines.
2. Pathing slice 1 "surround a building", slice 2 "flank through your own line" (STATE sim queue 24 b-c). Then M5-4c slice 2 (knobs),
   Requests 39 (+ 38), D14 b (strategies schema, age-agnostic), M5-4d (+ hunting), D14 c, M5-5, M5-6.

### Watch-outs (sim)
- `AiSeatTest.MatchOverLine` and `MatchOverViewTest` stage defeats with a live worker or a House: red on the sim branch by design; the
  view re-stages them. The Producer's merged-tree check runs the loop.
- `ViewApi/MatchOver.cs` reads `IsDefeated` only; should compile either way.
- BUG-0701 (the fog cursor's per-storm cost) and standing rule 21 wait for the sim hardening (counter 3 / 4 after this session).

---

## View track

### Current session plan: M6-V7 · edge-pan on a second monitor, control groups for buildings, BUG-0710, the match-over scenes re-staged (feature, QA standard)

**Goal.** Two owner playtest notes (1 and 7) and two small debts: edge panning stopped on the side where the owner's second monitor is
(the cursor leaves the window, so `RtsCamera.EdgeDirection` sees it "outside"), control groups should hold buildings too, BUG-0710's two
items, and the three match-over scenes must survive the sim's new win rule landing this session (STATE Requests 40).

**Scope.**
1. **BUG-0710**: (1) the HUD's K / L synced when the banner opens (a `Sync()` from `Main.OnMatchEnded`, or the `ResourceBar` text `Always`);
   (2) delete `sim/Rts.Sim/ViewApi/MatchOver.cs`, `ViewApi/MatchOverTests` and QA's `QA/ViewApi/MatchOverQaTests` (QA agreed at 1616).
2. **Cursor confinement** (note 1): while a match runs unpaused and the window has focus, `Input.MouseMode = Confined` (the cursor can't
   leave the window, so the edge band is reachable on every side, both monitors); `Visible` again on pause, the banner, any menu or
   settings screen, Main menu, and when the window loses focus (Alt-Tab works). A Settings field **"Confine cursor to window"** (On / Off,
   default On) in `ui.json` `settings` + `settings.cfg` + `GameSettings` / `SettingsApplier`, applied live. Also: the last pixel column /
   row counts as inside the edge band (`m.X >= size.X - 1`), so a cursor pinned at the border pans. `--no-confine` dev flag for the
   scripted scenes (or confinement off in headless / test runs: say which). The rule "when confined" lives in a ViewApi helper
   (`ViewApi/CursorConfine` or in `CameraLimits`) so it is unit-testable without Godot.
3. **Control groups hold a building** (note 7): Ctrl + digit with a building selected stores that building (slot + generation) in the
   group, replacing its units; Shift + digit with a building does the same (a group is units or one building, never both: the selection
   model holds one building, M3-V2); the plain digit selects the building (card and panel follow), double-tap centres the camera on it; a
   dead or replaced building prunes like a dead unit. `ViewApi/ControlGroups` extended (view-owned ViewApi; it holds handles only, reads
   the stores' `Alive` / `Generation` spans it is given). docs/02 "Controls" row updated ("Assign control group (units, or a building)");
   docs/03 "Implementation (M2-3)" note.
4. **The match-over scenes re-staged** (Requests 40): `MatchOverViewTest`, `QaM6V4Test` and `AiSeatTest.MatchOverLine` must pass under
   **both** rules (today's: last building of any kind; the new: no unit and no finished unit-producing building): stage every loser with
   **no live unit** when its last building falls, and make that last building a **Town Hall or a training hall** (never a House); a
   Draw = two such players on one tick; `AiSeatTest` round 2 (a dropped command of a defeated player) uses a dev `SpawnBuilding` or
   `SpawnUnit` instead of a `Build` from a worker. Don't loosen a `Check`; say in each scene's comment why it is staged that way.
5. OUT: the resource-remaining click (next session), keybinds, slots, any `sim/` change outside `ViewApi/`, `game/data/` beyond
   `ui.json`'s `settings` keys.

**Acceptance criteria.**
1. The loop green on the branch (`tools/qa/scene-loop.ps1`); `AiSeatTest` prints no `K / L` lag row; the three `MatchOver*` files gone and
   the solution builds.
2. `SettingsTest` rows: the confine key round-trips, default On, a bad value falls back; a ViewApi test for "when confined" (match
   running + unpaused + focused + setting on → confined; each of the four false → visible) and for the last-pixel edge rule
   (`PanVector` / `EdgeDirection`'s helper).
3. A windowed check by the dev: with the setting on, the cursor stops at the window edge and the camera pans; Esc releases it; Alt-Tab
   releases it; the setting off restores today's behaviour. Screenshot not needed; the log line of the mode changes is.
4. `ViewApi/ControlGroupsTests`: assign a building, recall selects it, Ctrl + digit with units replaces the building and vice versa, a
   razed building prunes, double-tap centres (`TryMean` on the building's footprint centre); a scene row (`OrderKeysTest` or a new
   `QaM6V7Test`) drives Ctrl + 1 on a Billet, 1, and the command card showing the Billet's buttons.
5. The three match-over scenes pass on the branch and are staged per item 4 (the Producer reads the staging).
6. 0 warnings, smoke PASS both passes, `git diff --stat -- sim/` shows only `ViewApi/` and its tests; docs/02 / docs/03 / docs/01 row;
   the count stated.

**Design references.** docs/02 "Controls and camera" (Ctrl / Shift / digit rows, the camera's pan rule), docs/02 "Game flow" 7
(settings), docs/03 "Implementation (M2-3)" (groups), "(M6-V6)" (settings file), "(M6-V4)" (the banner path), STATE Requests 40.

**Constraints.** Views hold presentation state only; text from `ui.json`; no sim mutation from `ViewApi/`; per-process `user://` paths in
tests; `Main.BeforeStart` stays a test seam.

**Budget.** ~900 lines. Hard stop at 1,200: ship items 1, 2 and 4 first (the loop must be green at the merge), then 3.

### Next task candidates (after this session)
1. The resource-remaining click (note 8, Requests 41) + keybinds (criterion 4's rest). 2. M6-V5's slot part. 3. Save / load + replay
   playback, the look test, `tools/export.ps1` with Requests 33.

### Watch-outs (view)
- Confinement and the scripted scenes: a confined cursor in a headless / windowed test run is harmless but say how tests opt out.
- The sim's branch merges first; the merged tree runs the loop under the new rule: item 4 is what keeps `main` green.

---

## Data track

### Current session plan: D18 · four reports and one wording fix, no number changed (feature, QA light)

**Goal.** Measure before changing: the owner asked for quicker building kills, the hp / armor / cost pass (with ×1.5 back), a re-measure of
the AI's wood shortage on a real map, and wondered whether the default seed is short of trees. D18 produces the tables; D19 (next session)
applies the numbers with one golden regen.

**Scope** (files: `docs/factions/malazan.md`, `docs/factions/whirlwind.md` (BUG-0720 item 2 only), `sim/Rts.Sim.Tests/Content/**`,
`sim/Rts.Sim.Tests/QA/Content/**`; **no `game/data/` change, no golden change**).
1. **BUG-0720 item 2**: both pages' "a Depot / Cache more than 16 m from it" → "...from the threat" (the rule: the nearest finished
   drop-off among those more than 16 m from the threat, else the nearest).
2. **The spending report on a generated map**: `AiSpendingReportTests` gains rows on `AiScenes.Generated` seeds 1 and 7, both factions,
   Normal, 20 min, the other side idle; the same columns (gold / wood in hand at 5 / 10 / 15 / 20 min, army pop, Age II?, waves, halls'
   queue empty %), the flat row kept; a dated conclusion line (wood still the limit or not).
3. **Trees near the bases** (playtest note 2): for seeds 1-7, 128 map, the defaults, per start location: trees within 30 m and 60 m and
   the nearest tree's distance; the owner's default-seed match is the menu's (`SkirmishSetup`'s default seed and size: read them from
   `game/data/common/ui.json` / `LaunchOptions` defaults and say which seed that is). One table.
4. **Building kill times** (note 6): a scenario harness (the `CounterTriangleScene` pattern) runs, combat on, flat ground, each attacker
   group against each building type with no defenders: 20 Heavy Infantry, 20 Crossbowmen, 10 Raiders, 2 Catapults, 2 Sappers (as
   shipped) vs a House, an Infantry Hall (Billet / Raider Camp class), a Town Hall; report seconds to raze and the share of attackers
   that struck within 10 s. Then the same under **candidate levers** applied to edited in-memory copies of the data: (A) Structure
   multipliers melee 0.4 → 0.7, pierce 0.2 → 0.35, magic 0.4 → 0.6, siege 3.0 kept; (B) every building's armor halved (round down); (C)
   A + B. **Band** (Producer default): 20 Age I line units raze a House in ≤ 20 s, a production hall in ≤ 60 s, a Town Hall in ≤ 2 min;
   siege at least 2x the line units' damage per cost against buildings. Propose the lever set that meets the band with the smallest
   change, as a table (unit × building: today / proposed), for the owner.
5. **Line v Shock hp / armor / cost sweep** (D13): with ×1.5 restored in memory (both line units' `bonusVs.mounted`), sweep Line hp
   (100-140 step 10), armor (1-3), cost gold (50-80 step 10) one at a time and the best pairs, through the shared harness
   (`PageTables` / the D8 harness), target the 40-65 % band (winner keeps 40-65 % of its cost) for Line v Shock in all four match-ups
   without breaking Line v Ranged / Shock v Ranged out of their bands; print today's and the proposed values and every band cell. If no
   set lands in the band, print the nearest and say which lever moves it most.
6. OUT: any value change, golden regen, `ai.json`, docs/02 (D19).

**Acceptance criteria.**
1. BUG-0720 item 2 fixed on both pages; the D17 pins (`AiContentTests`, QA's `NormalMicroPhraseQaTests`) green.
2. Report 2 prints both seeds × both factions with the dated conclusion; preconditions only asserted; under 30 s total.
3. Report 3 prints the per-start table for seeds 1-7 and names the menu's default seed.
4. Report 4 prints today's and (A) / (B) / (C) tables with the band check per cell and the proposed set; under 60 s total.
5. Report 5 prints the sweep's best set and the band cells; a sentence on whether ×1.5 alone changed anything.
6. `git diff --stat -- game/data` empty; golden unchanged; the Content + QA/Content + hash + golden slice green; the count stated.

**Design references.** docs/02 "Combat" (the damage formula and the Structure column), "Buildings" (hp / armor), "Faction template" (the
band's origin: D6's proposal, the winner keeps 40-65 % of its cost), "Economy"; docs/factions/*.md "Balance baseline"; docs/03
"Implementation (M5-4b)" → Defense (the flee wording).

**Constraints.** Data-track files only; reports are tests that print, with preconditions only; edited data copies in memory, never the
files; every number dated in the report; QA light.

**Budget.** ~700 lines. Hard stop at 900: ship items 1-4, report 5 next session with D19.

### Next task candidates (after this session)
1. **D19** (STATE data queue 25): the owner's numbers applied with one golden regen (rule 22: not in a session where the sim moves the
   header). 2. The D14 b openings once the schema is on `main`. 3. The house notes after D15 C.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order sim → view → data. **The merged tree must run the scene loop** (the sim changes the defeat rule; the view re-stages the
  scenes): the Producer's ACCEPT check does it; a red scene there is a conductor-dispatched view round, not a sim REJECT.
- Nobody moves the golden this session (rule 22).
