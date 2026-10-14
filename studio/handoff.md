# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-14-0946** (first full session of 2026-10-14; 1 / 8 after it). All three
tracks **ACCEPT**: sim **M5-4e** (the new win condition; 1 fix round: BUG-0705 S2 + BUG-0706 S3 fixed), view **M6-V7** (cursor confined
during a match, control groups hold a building, BUG-0710, the match-over scenes re-staged; 0 fix rounds), data **D18** (four reports,
no number changed; 0 fix rounds). Counters after this session: **sim 3 / 4, view 3 / 4, data 3 / 4** (every track's hardening is the
session after next). Bug ids: **sim from BUG-0707, view from BUG-0712, data from BUG-0722.** Integration sim → view → data; the
conductor's conflict resolutions are in the session log ("Merged tree"). **BUG-0711 is handled at the merge: `AbilityViewTest` and two
`QA/ViewApi/M6V7QaTests` rows were staged under the old win rule (a view fix round before the view's merge; if none could be
dispatched the conductor held the sim branch instead and the sim's first step next session is to re-land it; see the session log
"Decisions").**

## Where we are

- `main` after this integration = the sim's af1b37a + the view's ebc609d + the data's 907becf. Build 0 warnings; the defeat / golden /
  hash / door / ViewApi / content slices and the smoke run on the merged tree are in the session log. Golden format 6, `data-hash`
  **C6FC120A55AE4A0B** (nobody moved it this session). **No open S1 / S2.**
- The win rule is the owner's (docs/02 "Game flow" 4): out = has built, no live unit, no finished unit-producing building. **Producer
  decision (BUG-0703, docs/01 2026-10-14): a defeated player's remaining buildings collapse on its defeat tick** (built at the sim's
  M5-H2 hardening; until then they stand and its towers keep shooting).
- D18's numbers (STATE "For your review", data entry): the trees are fine on every seed (the wood runs out later, not at the start);
  the AI is still wood-starved on a real map; buildings take 3-5 min to raze today, lever set (C) brings every band cell in; the
  Line v Shock band is unreachable by the line unit's hp / armor / cost (the Shock side is the lever).

## Standing rules (1-23)

1-22 as in the 0946 handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`;
per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track
test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent sim
tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is reported; no
process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop when the sim
touches the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door / the defeat rule; a QA
scene row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a Perf gate measures the shipped data;
**22. only one track moves the golden header per session**).
23. **A QA verdict without its report file is no verdict.** QA commits `studio/qa/<session>-<task>.md` on its branch before it
    reports PASS (D18's QA this session filed BUG-0721 and the coverage row but no report file; the Producer re-ran the slice itself).
24. **A sim scene that stages a building before any unit of that player** (a House, a depot, a Forge, a tower, a site) must call
    `TestSim.KeepIn(sim, player)` or spawn a unit first, else that player is out on the next tick and its later commands are dropped
    silently (BUG-0705 / 0706). The same for a Godot scene (`game/tests`): spawn a unit of that seat first (BUG-0711).

---

## Sim track

### Current session plan: pathing slice 1 · "surround a building" (feature, QA full, counter 3 / 4 → 4 / 4)

**Why this and not M5-4f:** rule 22. D19 moves the golden header next session (the owner's balance numbers); M5-4f's three knobs
would move it too, so M5-4f runs the session after (with the gather-rate change and the seed-21 re-record, one regen). The owner's
playtest note 5 (infantry stuck behind crossbowmen; units stuck behind each other at a building) is the next sim item that moves
nothing in the data.

**Goal.** Attackers ordered at a building queue up behind the friends already striking it, so half of 20 attackers stand idle (D18
report 4: 9-11 of 20 line units strike within 10 s). Each attacker should take a free spot round the footprint instead.

**Scope.**
- `CombatSystem` (the building-target branch) / `MovementSystem`: an attacker whose approach cell to its building target is held by
  an own unit already in reach picks the nearest free cell adjacent to the footprint (ring order, deterministic by cell index), and
  re-picks when that cell is taken; no new `Command`; no data change; no knob unless the design needs one (then `rules.json` and the
  header moves: say so in the report, and it is this session's one header move only if D19 is not running; it is, so **no knob**).
- A measured before / after: of 20 Heavy Infantry (and 20 Raiders) ordered at a House / hall / Town Hall on flat ground, how many
  have struck within 10 s (today 9-11 / 20, from D18's harness: reuse `CounterTriangleScene`'s block and `BuildingKillTimeReportTests`'
  staging, content-independent in the sim's own test); target ≥ 16 / 20 for the House and hall.
- OUT: flanking through your own line (slice 2), unit-vs-unit crowding, the AI's wave logic, the defense.

**Acceptance criteria.**
1. `SurroundTests`: (a) 20 line units at a 2x2 and a 3x3 building: ≥ 16 strike within 10 s (before: the dev prints today's count
   first); (b) no two attackers claim one cell; (c) a cell freed by a death is taken by a waiting attacker; (d) a building with one
   free side (the others walled by cliffs / buildings) fills that side and the rest wait without a give-up.
2. Determinism: hash twins over the scene; `ReplayGoldenTests` green (regen allowed only if the cross-map match's `k` lines move
   because movement changed: say why; the header may not move).
3. Perf: `CrowdPerfTests` / `BrawlPerf` gates unchanged (rerun alone); no per-tick allocation.
4. Existing `AttackOrderTests`, `AttackMoveRepick`, `FightReissue`, the QA combat rows green.
5. Build 0 warnings; full suite (Perf alone once); docs/03 "Implementation (pathing slice 1)" + docs/01 row; the count stated.

**Design references.** docs/02 "Combat" (melee reach, buildings), docs/03 "Movement" and "Implementation (M4-2a)" (the attack
order on buildings), BUG-0028 / 0032 / 0046 (the crowd-routing family; not fixed here), D18 report 4's staging.

**Constraints.** No Godot; no wall clock; deterministic cell order (never a `Dictionary` walk); no allocation per tick; one system.

**Budget.** ~800 lines (uncertain: a new approach rule). Hard stop at 1,000: ship (a)-(c) and report (d).

### Next task candidates (after this session)
1. **M5-4f proportional defense + D15 C + BUG-0702 item 1 + `gatherRate` gold 0.85 / wood 0.75 (owner) with the seed-21 re-record
   (`GatherWedgeQaTests.RecordedDataHash`, `BUG-0146-seed21-wood-wedge.replay` re-recorded from `M3PlayableTest -- --seed 21 --break 19`;
   docs/02 Economy)**: one header move (rule 22: D19 must already be on `main`). QA full, ~900 lines.
2. **M5-H2 sim hardening** (counter 4 / 4 after this session): BUG-0703 (the collapse rule, decision (c) in docs/01; flip QA's
   `ADefeatedPlayersTower_KeepsShooting_Measured`), BUG-0701 + standing rule 21, BUG-0704 items 3-4 + the `TestSim.cs` space nit,
   BUG-0702 items 2-3, BUG-0675 c, BUG-0651, BUG-0650, a second start forest per base (`MapGenParams`) if the owner wants more wood
   near the base (D18 says the start is fine; the forests run dry by 15-20 min).
3. Then pathing slice 2, M5-4c slice 2 (knobs), Requests 39 (+ 38), D14 b, M5-4d (+ hunting), D14 c, M5-5.

### Watch-outs (sim)
- `TestSim.KeepIn` for any new scene that places a building before a unit (rule 24).
- **If the conductor held the sim branch at the 0946 integration** (no view fix round for BUG-0711 could be dispatched): the sim's first
  step is `git merge origin/studio/2026-10-14-0946-sim` onto the session branch (no code change; the conflicts are the same unions as
  the session log's "Merged tree"), after the view's BUG-0711 fix is on `main`; then this brief. STATE's sim rows already describe
  M5-4e as on `main`.

---

## View track

### Current session plan: M6-V8 · the resource-remaining click + keybinds (feature, QA standard, counter 3 / 4 → 4 / 4)

**First, if BUG-0711 is still open on `main`:** (1) `AbilityViewTest.ProphecyBarRow` stages a Whirlwind Smithy for seat 1 before seat 1
has any unit, so under M5-4e seat 1 is out on the next tick and its `Research` is dropped ("Zealot max 63"). Fix: spawn one Whirlwind
unit for seat 1 (a Camp Follower on a far passable cell, out of the row's spots and the mages' sight) 3 ticks before the Smithy; no
`Check` loosened; the loop green. (2) `QA/ViewApi/M6V7QaTests.ViewApiDriven_*` places a House (named "Billet") for player 0 before any
unit: `TestSim.KeepIn(a, 0)` / `KeepIn(bsim, 0)` before the first `GatherMaps.Building` (both twins). Both verified on a tree with the
sim's M5-4e in it.

**Goal.** Owner playtest note 8: left-click a gold mine or a tree and the selection panel says what it is and how much is left. Then
M6 criterion 4's rest: rebindable keys.

**Scope.**
1. **Resource click** (Requests 41): a left-click on a visible mine / tree selects it alone (the unit selection cleared; the card
   empty); the panel shows its `displayName` and "`<amount>` `<unit>` left" from `World.Resources.Remaining[slot]` **while visible**;
   for an explored-but-unseen node the view's own per-node cache shows the last seen amount with a "last seen" prefix (`ui.json`
   `hud.resource_left` / `hud.last_seen`); a felled / emptied node deselects. `ViewApi/SeenResources` (view-owned) keeps the cache.
2. **Keybinds**: every HUD hotkey (orders A / S / H / M / P, Tab, the grid keys, B / V, digits, Esc stays fixed) read through one map
   (`KeyMap`, view state) with the defaults as today; a Keybinds tab on the settings screen (a row per action: name from `ui.json`
   `keys.*`, the current key, click-to-rebind, conflicts refused with a line from `ui.json`); saved in `settings.cfg` `[keys]`; the
   `InputMap` actions re-registered on apply; a bad or missing key = the default.
3. OUT: mouse-button rebinding, the camera keys (arrows stay), slots / teams, any `sim/` change outside `ViewApi/`.

**Acceptance criteria.**
1. `game/tests/ResourceClickTest.tscn`: click a mine → panel name + amount = the sim's; a worker gathers 60 ticks → the amount follows;
   the camera moves so the node is unseen → "last seen" with the old amount; the node felled while unseen → still the old amount; seen
   again → gone and deselected; 0 commands sent by any of it; the hash twin.
2. `SettingsTest` rows: `[keys]` round trip, a conflict refused, a junk key falls back, defaults restore; a scene row rebinds A to
   another key and an attack-move goes through with the new key and not the old.
3. `ShellTest` names every new `ui.json` key; no literal in `game/scripts`.
4. 0 warnings, smoke PASS both passes, the loop green, docs/02 "Controls" (a "rebindable" note is already there: say how), docs/03
   "Implementation (M6-V8)", docs/01 row; the count stated.

**Design references.** docs/02 "Controls and camera" ("All keys are rebindable"), "Game flow" 7 (keybinds), STATE Requests 41,
docs/03 "Implementation (M6-V6)" (the settings file).

**Constraints.** Views hold presentation state only; text from `ui.json`; `ViewApi/` reads only; per-process `user://` paths.

**Budget.** ~1,000 lines. Hard stop at 1,300: ship BUG-0711 (if open) and item 1 first, then item 2.

### Next task candidates (after this session)
1. **M6-VH2 view hardening** (counter 4 / 4): BUG-0684 items, BUG-0622 item 1, the AiSeat / MatchOver scenes' leftover notes, the
   `ResourceBar` "Always" alternative if the K / L sync shows a gap. 2. M6-V5's slot part (2-4 slots; teams need Requests 38). 3. Save /
   load + replay playback, the look test, `tools/export.ps1`.

### Watch-outs (view)
- The owner has not yet tried the real-cursor confinement on the two-monitor desk (criterion 3 of M6-V7 was verified headless and by
  code read only); if the inbox reports a problem, it comes first.

---

## Data track

### Current session plan: D19 · the owner's numbers applied, one golden regen (feature, QA standard, counter 3 / 4 → 4 / 4)

**Goal.** Apply what the owner decided on 2026-10-14 and what D18 measured, with one fingerprint update and a compact review table.
**This is the session's one golden header move (rule 22): the sim track moves nothing.**

**Scope** (files: `game/data/factions/malazan/units.json`, `.../whirlwind/units.json`, both `buildings.json`, both `ai.json`,
`game/data/common/damage_table.json` (by exception: the sim track is off `common/` this session), `docs/factions/*.md`, docs/02
"Combat" (the Structure column) + "AI opponent" (the Easy line), `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`,
the golden replay header, **and by named exception `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.SameGameDataHashes`** (add the new hash
with the reason "D19: ×1.5, Easy casts, building damage: no line unit hits a mounted one, no AI seat, no building is attacked in
the seed-21 match"; `CheckpointPrefixTicks` must still pass, which proves it)):
1. `bonusVs.mounted` ×1.1 → **×1.5** on both line units (D13 reversed; both pages' rows and `PageTables` pins).
2. Easy `useAbilities` **true** in both `ai.json`; docs/02's Easy line loses "doesn't use abilities"; `AiContentTests` J-K,
   `AiDifficultyFileSideQaTests`, `NormalMicroPhraseQaTests` as they pin it (name them in the report).
3. **Building damage, lever set (C)** unless the inbox says otherwise: `damage_table.json` Structure multipliers melee 0.4 → **0.7**,
   pierce 0.2 → **0.35**, magic 0.4 → **0.6** (siege 3.0 kept); every building's `armor` halved, rounded down, in both `buildings.json`;
   docs/02 "Combat" Structure column + "Buildings" armor; both pages' building tables; the D18 kill-time report re-run and its
   today / after table printed (the band check must read 12 / 12).
4. **Not in D19:** `gatherRate` (sim-owned `common/rules.json` and it changes the seed-21 recorded match: the sim's M5-4f session
   does it with the re-record); the Line v Shock hp / armor / cost (D18: no set reaches the band; the Shock side is the lever: a
   proposal for the owner, not a change); `goldShare` (a proposal in the review entry; apply only if the inbox says so).
5. OUT: any other number, the Teblor, `rules.json`.

**Acceptance criteria.**
1. `DataValidationTests` green; `DataContentHashTests` on the new hash; the golden header regenerated once with the reason in the
   commit; every `k` line unchanged (the cross-map match has no building and no line-v-mounted hit: if a `k` line moves, stop and say why).
2. Every `Content` / `QA/Content` pin green on the new values (the PageTables cells, the D8 harness, J-K, the Structure column parser).
3. `GatherWedgeQaTests` green with the new hash listed (the named exception), `CheckpointPrefixTicks` 12,131 still matching.
4. The kill-time report re-run: 12 / 12 band cells; the Line v Shock harness re-run with ×1.5 (today's table re-printed on both pages).
5. A compact review table (unit / building / tech, field, old → new, one-line why) in the report for the Producer's For your review
   entry; the count stated; `git diff --stat` shows only the files above.

**Design references.** docs/02 "Combat" (the damage formula; the 1-damage floor: Crossbowmen stay slow against halls, by design
unless the owner says otherwise), "Buildings", "Faction template" (the band), "AI opponent" (Easy), docs/01 2026-10-14 rows.

**Constraints.** Data-track files + the named exception; one regen; every number dated; QA standard (the Perf gates measure the
shipped data: `AiPerfTests` / `BrawlPerf` rerun alone after the change).

**Budget.** ~600 lines. Hard stop at 800: items 1-3 in that order; report the rest.

### Next task candidates (after this session)
1. **D-H5 data hardening** (counter 4 / 4): BUG-0721 (the siege ratio against both line units, the post-win marks, the 8-mine row),
   the reports re-run on D19's numbers, the house notes after D15 C. 2. The D14 b openings once the strategies schema is on `main`.
3. A Shock pass (Horse Raider / Wickan Lancer) if the owner asks for one after D18's table.

### Watch-outs (data)
- Rule 22: if the PLAN finds the sim must move the header (an S1 / S2 fix with a knob), D19 waits one session.
- The owner may answer D18 in the inbox (a different lever set, "Crossbowmen should hurt buildings", "add goldShare"): inbox first.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order sim → view → data. The merged tree runs the scene loop when the sim touches movement (pathing slice 1 does: the
  Producer's ACCEPT check runs it).
- Only the data track moves the golden header next session (D19).
