# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-15-2325** (first full session of 2026-10-15; 1 / 8 after it). Plan check on
`main` 350bbbd: build **0 warnings**, non-Perf suite **6,319 / 11 skipped / 0 failed (36 m 27 s alone: the PC is slow again, the 0946
check took 23 m 44 s)**, inbox **New** empty, **no open S1 / S2**, every
track's counter **3 / 4 → 4 / 4 after this session** (next session is hardening for all three: M5-H2, M6-VH2, D-H5). Bug ids this
session: **sim from BUG-0730, view from BUG-0740, data from BUG-0750** (0707-0729 are retired: the Producer's 0711 sat inside the
view's old block). Integration sim → view → data. **BUG-0711 is fixed on `main` (eb21700)**: the view's first step from the 0946
handoff is gone. Two bug files corrected at this PLAN: BUG-0711 and BUG-0720 were fixed (eb21700; ba37fa4 + c6bb467) but still read
`open`.

## Where we are

- `main` = 350bbbd (the 0946 integration: sim af1b37a + view ebc609d + data 907becf + the BUG-0711 round). Golden format 6, `data-hash`
  **C6FC120A55AE4A0B** (unmoved since D13). **This session the data track moves it once (D19); the sim and view move nothing** (rule 22).
- The owner's 2026-10-14 answers are all placed (STATE "For your review", the assessment entry): this session = sim pathing slice 1
  (note 5), view the resource click (note 8) + keybinds (M6 criterion 4), data D19 (×1.5, Easy casts, building damage (C), note 6).
- Nothing new from the owner since (inbox New empty); the two-monitor edge-pan try is still "wanted, not blocking".

## Standing rules (1-24)

1-24 as in the 0946 handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`;
per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track
test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent sim
tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is reported; no
process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop when the sim
touches the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door / the defeat rule /
movement; a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a Perf gate measures the
shipped data; **22. only one track moves the golden header per session; 23. a QA verdict without its report file
(`studio/qa/<session>-<task>.md`, committed on the branch) is no verdict; 24. a sim or Godot scene that stages a building before any
unit of that player calls `TestSim.KeepIn(sim, player)` / spawns a unit first (M5-4e puts that player out otherwise)**).

---

## Sim track

### Current session plan: pathing slice 1 · "surround a building" (feature, QA full, counter 3 / 4 → 4 / 4)

**Why this and not M5-4f:** rule 22. D19 moves the golden header this session (the owner's balance numbers); M5-4f's three knobs
(`rules.json` `ai.defendRatio` / `defendMinValue` / `recallHallHp`) would move it too, so M5-4f runs next session's hardening-adjacent
slot (see candidates). The owner's playtest note 5 ("when attacking a building, units get stuck behind other units instead of moving
round to an exposed side") is the next sim item that moves nothing in the data.

**Goal.** Attackers ordered at a building converge on one side and queue behind the friends already striking it, so half of 20
attackers stand idle (D18 report 4: 9-11 of 20 line units strike within 10 s). Each attacker should take a free spot round the footprint
instead, so the owner's armies raze buildings with every unit working.

**Where it happens today.** `Combat/CombatSystem.ChaseCell`: `TargetPoint` clamps the unit's own position onto the footprint rectangle
(so every unit arriving from one direction aims at the same face) and steps out by `Radius + CombatConstants.BuildingStandOff` (0.25 m);
`OrderSystem.ResolveTarget` turns that into a goal cell. Nothing checks whether that cell (or the reach band around it) is already
full of own units in reach of the same building.

**Scope.**
- A **standing-spot rule for building targets**: when an attacker's resolved spot is held (occupied, or already claimed this tick) by
  an own unit that is in reach of the same building, it picks the nearest free cell of the ring of passable cells adjacent to the
  footprint (plus one more ring for units whose radius needs it), walking the ring in a fixed order (by cell index, never a
  `Dictionary` / `HashSet` walk) so the choice is deterministic; it re-picks when its claimed cell is taken; a cell freed by a death or
  a retreat is taken by a waiting attacker on a later re-pick. Claims live in a per-tick scratch (an int / bool array sized at `World`
  creation; cleared by stamping, not by allocation). Every path that chases a building (ordered Attack, attack-move, retaliation,
  the AI's waves) goes through the same rule because they all go through `ChaseCell`.
- A **measured before / after** in the sim's own test: 20 Heavy Infantry and 20 Raiders ordered at a House (2x2), an infantry hall
  (3x3) and a Town Hall on flat ground; how many have struck within 10 s (200 ticks). Print today's numbers first (expect 9-11 / 20),
  then after. Target **≥ 16 / 20** for the House and the hall; report the Town Hall. Reuse the staging idea of D18's
  `Content/BuildingKillTimeReportTests` (a lone building, no defenders, flat, combat on) but write it as a content-independent sim
  test (counts, not seconds pinned to data): the test may load the shipped data but must stay green if a number changes.
- **No new `Command`; no data change; no knob.** If the design seems to need a tunable (ring depth, re-pick period), use a
  `CombatConstants` value with a `why` comment and say so in the report; **do not touch `game/data/**` or `rules.json`** this session
  (rule 22: D19 moves the header; the data track also edits `common/damage_table.json`).
- OUT: flanking through your own line (slice 2: a melee unit blocked by own standing ranged units routes round them, BUG-0028 / 0032 /
  0046 family), unit-vs-unit crowding away from buildings, the AI's wave logic, the defense rule, M5-4f, hunting the last units (M5-4d).

**Files.** `sim/Rts.Sim/Combat/**` (and `Movement/OrderSystem` only if `ResolveTarget` needs a "prefer this cell" variant), `World`
scratch arrays, `sim/Rts.Sim.Tests/Combat/SurroundTests.cs` (new), docs/03 "Implementation (pathing slice 1)", docs/01 row. **Hands
off:** `game/data/**`, `sim/Rts.Sim.Tests/Replays/*.replay` headers, `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (the data track
edits it by named exception this session), `sim/Rts.Sim.Tests/Content/**` and `QA/Content/**` (data-owned), `ViewApi/**`.

**Acceptance criteria.**
1. `Combat/SurroundTests`: (a) 20 line units ordered at a 2x2 and a 3x3 building: **≥ 16 strike within 200 ticks** (the test prints
   today's count in its output first: the dev states both numbers in the report); (b) no two attackers claim one cell on any tick
   (a fuzz over 3 seeds × 300 ticks asserts it); (c) a cell freed by a death is taken by a waiting attacker within 40 ticks; (d) a
   building with one free side (the others walled by cliffs or buildings) fills that side and the rest wait without a give-up
   (no `Idle` from a give-up while the building stands and the side is full).
2. Determinism: hash twins over the (a) scene, both groups; `ReplayGoldenTests` green **without a regen** (the cross-map match has no
   building; if a `k` line moves, stop and say why: the rule leaked into non-building movement); `GatherWedgeQaTests` green with its
   12,131 checkpoints (the seed-21 match attacks no building; the data track adds a hash to it this session, you touch nothing).
3. Perf: `CrowdPerfTests` / `BrawlPerf` gates unchanged (rerun alone); a 40-attacker scene allocates 0 B per tick (an `AllocatesNothing`
   row like the flood one); the think / tick cost of the ring walk stated (ring cells ≤ 4 × (w + h + 2) per re-pick).
4. Existing `AttackOrderTests`, `AttackMoveRepick`, `FightReissue`, `BuildingTargetTests` (ghost / remembered building paths), the QA
   combat rows green; the AI-v-AI rows (`AiVersusAiTests`) still end the same way (the waves now spread round the hall).
5. Build 0 warnings; full suite incl. Perf alone once; docs/03 "Implementation (pathing slice 1)" (the rule, the ring order, the claim
   scratch, the measured table) + docs/01 row; the line count stated (dev's files only).

**Design references.** docs/02 "Combat" (melee reach, buildings as targets), docs/03 "Movement" and "Implementation (M4-2a)" (the attack
order on buildings, `BuildingStandOff`), "Implementation (M4-H3)" (an attack order on a reachable building is never dropped: keep it),
BUG-0028 / 0032 / 0046 (the crowd-routing family; not fixed here), D18 report 4's staging (`Content/BuildingKillTimeReportTests`, read
only).

**Constraints.** No Godot; no wall clock; deterministic cell order; no per-tick allocation; one system (`CombatSystem` + its helpers);
rule 24 (`TestSim.KeepIn`) for any new scene that places a building before a unit; the shipped data untouched.

**Budget.** ~800 lines (uncertain: a new approach rule). Hard stop at 1,000: ship (a)-(c), report (d) with what is missing.

### Next task candidates (after this session)
1. **M5-H2 sim hardening** (counter 4 / 4 after this session): BUG-0703 (the collapse rule, decision (c) in docs/01; flip QA's
   `ADefeatedPlayersTower_KeepsShooting_Measured`), BUG-0701 + standing rule 21, BUG-0704 items 3-4 + the `TestSim.cs` space nit,
   BUG-0702 items 2-3, BUG-0675 c, BUG-0651, BUG-0650, any slice-1 leftovers; a second start forest per base only if the owner asks.
2. **M5-4f proportional defense + D15 C + BUG-0702 item 1 (densest cluster) + `gatherRate` gold 0.85 / wood 0.75 (owner) with the seed-21
   re-record** (`GatherWedgeQaTests.RecordedDataHash`, `BUG-0146-seed21-wood-wedge.replay` from `M3PlayableTest -- --seed 21 --break 19`;
   docs/02 Economy): one header move (rule 22: D19 on `main` first). QA full, ~900 lines.
3. Then pathing slice 2, M5-4c slice 2 (knobs), Requests 39 (+ 38), D14 b, M5-4d (+ hunting), D14 c, M5-5.

### Watch-outs (sim)
- `GatherWedgeQaTests` is edited by the data track this session (one hash added): don't touch it; if it goes red on your branch for a
  movement reason, that is a bug in the slice (the seed-21 match attacks no building).
- The merged tree runs the whole scene loop (movement changed): keep `BuildingKillTimeReportTests`' staging assumptions in mind (units
  start on one side of the building).

---

## View track

### Current session plan: M6-V8 · the resource-remaining click + keybinds (feature, QA standard, counter 3 / 4 → 4 / 4)

**Goal.** Owner playtest note 8: left-click a gold mine or a tree and the selection panel says what it is and how much is left. Then
M6 criterion 4's rest: rebindable keys. BUG-0711 is already fixed on `main` (eb21700): nothing to re-stage.

**Scope.**
1. **Resource click** (Requests 41): a left-click on a visible mine / tree (through `ViewApi/ResourcePicker`, which the right-click
   gather pick already uses) selects it alone (the unit selection cleared; the command card empty); the panel shows the node's
   `displayName` (`resources.json`: "Gold Mine" / "Tree", via `World.Resources.TypeId`) and "`<amount>` `<resource>` left" from
   `World.Resources.Remaining[slot]` **while the node is visible**; for an explored-but-unseen node the view's own cache
   (`ViewApi/SeenResources`, view-owned, extended with the last seen amount) shows the last seen amount with a "last seen" prefix; a
   felled / emptied node deselects the moment it is seen gone. New `ui.json` keys: `hud.resource_left` ("{0} {1} left"),
   `hud.last_seen` ("last seen"), `hud.gold` / `hud.wood` if the resource kind has no display text of its own. A left-click on a
   resource sends **0 commands**.
2. **Keybinds**: every HUD hotkey (orders A / S / H / M / P, Tab, the command-card grid keys, B / V build menus, the digits for control
   groups; Esc stays fixed) read through one map (`KeyMap`, view state in `game/scripts`) with today's keys as defaults; a **Keybinds
   tab** on the settings screen (a row per action: the action name from `ui.json` `keys.*`, the current key, click-to-rebind by
   pressing a key, a conflict refused with a line from `ui.json`, a Defaults button); saved in `settings.cfg` `[keys]`; the Godot
   `InputMap` actions re-registered on apply; a bad or missing key = the default.
3. OUT: mouse-button rebinding, the camera keys (arrows / middle-drag stay), 2-4 slots / teams (M6-V5), any `sim/` change outside
   `ViewApi/` (read-only, never changes a tick or the hash), any `game/data/` file but `common/ui.json`.

**Files.** `game/scripts/**`, `game/scenes/**`, `game/ui/**`, `game/tests/**`, `game/data/common/ui.json` (the view's text keys only;
the data track edits `common/damage_table.json`, the sim nothing under `common/`), `sim/Rts.Sim/ViewApi/SeenResources.cs` (+ a
`KeyMap` only if it needs no Godot: otherwise it lives in `game/scripts`), `sim/Rts.Sim.Tests/ViewApi/**`, `QA/ViewApi/**`, docs/02
"Controls and camera", docs/03 "Implementation (M6-V8)", docs/01 row.

**Acceptance criteria.**
1. `game/tests/ResourceClickTest.tscn`: click a mine → the panel's name + amount equal the sim's; a worker gathers 60 ticks → the
   amount follows; the camera / units move so the node is unseen → "last seen" with the old amount; the node emptied / felled while
   unseen → still the old amount; seen again → gone and deselected; **0 commands sent** by any click on a resource (the command
   queue's sequence counter unchanged); the hash twin (a bare sim running the same ticks hashes equal every tick).
2. `SettingsTest` rows: `[keys]` round trip, a conflict refused, a junk key falls back to the default, Defaults restores; a scene row
   rebinds A to another key and an attack-move goes through with the new key and **not** the old.
3. `ShellTest` names every new `ui.json` key (`hud.resource_left`, `hud.last_seen`, `keys.*`, the conflict line); no player-facing
   literal in `game/scripts`.
4. 0 warnings, smoke PASS both passes, the scene loop green on the branch, docs/02 "Controls" (the "All keys are rebindable" note
   now says how: Settings → Keybinds), docs/03 "Implementation (M6-V8)", docs/01 row; the line count stated.

**Design references.** docs/02 "Controls and camera" ("All keys are rebindable"), "Game flow" 7 (keybinds), STATE Requests 41,
docs/03 "Implementation (M6-V6)" (the settings file), "Implementation (M4-VH2)" (`SeenResources`).

**Constraints.** Views hold presentation state only (the seen-amount cache and the key map are presentation state); text from
`ui.json`; `ViewApi/` reads only; per-process `user://` paths; rule 24 for any new scene (spawn a unit before a building).

**Budget.** ~1,000 lines. Hard stop at 1,300: ship item 1 whole first, then item 2 (if item 2 doesn't fit, ship the `KeyMap` + the
`[keys]` file round trip and leave the tab for the hardening session, saying so).

### Next task candidates (after this session)
1. **M6-VH2 view hardening** (counter 4 / 4): BUG-0684 items, BUG-0622 item 1, the AiSeat / MatchOver scenes' leftover notes, the
   `ResourceBar` "Always" alternative if the K / L sync shows a gap, any M6-V8 leftovers. 2. M6-V5's slot part (2-4 slots; teams need
   Requests 38). 3. Save / load + replay playback, the look test, `tools/export.ps1`.

### Watch-outs (view)
- The owner has not yet tried the real-cursor confinement on the two-monitor desk (M6-V7 criterion 3 verified headless and by code read
  only); if the inbox reports a problem, it comes first next session.
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.

---

## Data track

### Current session plan: D19 · the owner's numbers applied, one golden regen (feature, QA standard, counter 3 / 4 → 4 / 4)

**Goal.** Apply what the owner decided on 2026-10-14 and what D18 measured, with one fingerprint update and a compact review table for
the owner. **This is the session's one golden header move (rule 22): the sim and view move nothing.**

**Scope** (files: `game/data/factions/malazan/units.json`, `.../whirlwind/units.json`, both `buildings.json`, both `ai.json`,
`game/data/common/damage_table.json` (by exception: the sim track is off `common/` this session), `docs/factions/*.md` (all four
pages that quote the template bonus), docs/02 (line ~51 "Template bonuses", the "Combat" Structure column, "Buildings" armor, the
"AI opponent" Easy line), one sentence in docs/03 (~6398: "the shipped Easy is off: it never casts"), docs/01 row,
`sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, the golden header `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`,
**and by named exception `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.SameGameDataHashes`** (add the new hash with the reason "D19: ×1.5,
Easy casts, building damage: no line unit hits a mounted one, no AI seat, no building is attacked in the seed-21 match";
`CheckpointPrefixTicks` 12,131 must still pass, which proves it)):
1. `bonusVs.mounted` ×1.1 → **×1.5** on both line units (Heavy Infantry, Raider; D13 reversed by the owner); both pages' rows and the
   `PageTables` pins; docs/02 "Template bonuses" back to ×1.5 (keep the D13 history in one clause); shadow.md / andii.md one-liners.
2. Easy `useAbilities` **true** in both `ai.json`; docs/02's Easy line loses "doesn't use abilities"; docs/03's sentence; both pages'
   Difficulty tables; `AiContentTests` J-K, `AiDifficultyFileSideQaTests`, `NormalMicroPhraseQaTests` as they pin it (name them in the
   report).
3. **Building damage, lever set (C)** (the inbox said nothing else): `damage_table.json` Structure multipliers melee 0.4 → **0.7**,
   pierce 0.2 → **0.35**, magic 0.4 → **0.6** (siege 3.0 kept); every building's `armor` halved, rounded down, in both `buildings.json`
   (5 → 2, 4 → 2, 3 → 1); docs/02 "Combat" Structure column + "Buildings" armor; both pages' building tables; the D18 kill-time report
   **re-based** (its "today" column is now the shipped data; drop or re-state the (A)-(C) variants so nothing is applied twice) and
   re-run: the band check must read **12 / 12** on the shipped data.
4. **Not in D19:** `gatherRate` (sim-owned `common/rules.json`; it changes the seed-21 recorded match, so the sim's M5-4f session does
   it with the re-record); the Line v Shock hp / armor / cost (D18: no set reaches the band; the Shock side is the lever: a proposal
   for the owner in the review table, not a change); `goldShare` (a proposal; apply only if the inbox says so).
5. OUT: any other number, the Teblor / Shadow / Andii data, `rules.json`, any C# outside the test folders and the named exception.

**Acceptance criteria.**
1. `DataValidationTests` green; `DataContentHashTests` on the new hash; the golden header regenerated once (as D10c did) with the reason
   in the commit; **every `k` line unchanged** (the cross-map match has no building and no line-v-mounted hit: if a `k` line moves, stop
   and say why).
2. Every `Content` / `QA/Content` pin green on the new values (the `PageTables` cells, the D8 harness rows re-printed on both pages,
   J-K, the Structure column parser).
3. `GatherWedgeQaTests` green with the new hash listed (the named exception), `CheckpointPrefixTicks` 12,131 still matching.
4. The kill-time report re-run on the shipped data: 12 / 12 band cells, the today-vs-before table printed; the Line v Shock harness
   re-run with ×1.5 (today's table re-printed on both pages; the "Winner hp left" column updated).
5. A compact review table (unit / building / tech, field, old → new, one-line why) in the report for the Producer's For your review
   entry, plus the Shock-pass and `goldShare` proposals as one line each; the line count stated; `git diff --stat` shows only the
   files above.

**Design references.** docs/02 "Combat" (the damage formula; the 1-damage floor: Crossbowmen stay slow against halls, by design unless
the owner says otherwise), "Buildings", "Faction template" (the 40-65 % band), "AI opponent" (Easy), docs/01 2026-10-14 rows (the
owner's decisions).

**Constraints.** Data-track files + the named exceptions above; one regen; every number dated in the pages; QA standard (the Perf gates
measure the shipped data: `AiPerfTests` / `BrawlPerf` rerun alone after the change; `AiPerfTests` turns Easy seats' casting on now).

**Budget.** ~600 lines. Hard stop at 800: items 1-3 in that order; report the rest.

### Next task candidates (after this session)
1. **D-H5 data hardening** (counter 4 / 4): BUG-0721 (the siege ratio against both line units, the post-win marks, the 8-mine row),
   the D18 reports re-run on D19's numbers (spending, trees), the house notes after D15 C (once M5-4f lands), the owner's D19 tweaks
   from the inbox if any. 2. The D14 b openings once the strategies schema is on `main`. 3. A Shock pass (Horse Raider / Wickan Lancer)
   if the owner asks for one after D19's table.

### Watch-outs (data)
- The owner may still answer D18 in the inbox mid-session (a different lever set, "Crossbowmen should hurt buildings", "add
  goldShare"): if the conductor relays it, apply it in the fix round; otherwise it is D-H5's first item.
- `AiPerfTests` measures the shipped data with every seat casting now (Easy included): if the 1 ms gate moves, rerun alone, then
  report the numbers (the sim owns the gate; BUG-0701 is the known cost driver).

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them.
- Merge order sim → view → data. The merged tree runs the scene loop (the sim touches movement: the Producer's ACCEPT check runs it).
- Only the data track moves the golden header this session (D19). Only the data track edits `game/data/**` except the view's
  `common/ui.json` text keys.
- Next session is a hardening session for all three tracks (counters 4 / 4): M5-H2 (BUG-0703 first), M6-VH2, D-H5.
