# Handoff: brief for the next session

**PLAN 2026-10-16-0847 (Producer, scheduled; base 53718f4 = origin/main):** GO on all three tracks as a **hardening session** (every
counter 4 / 4). Checks: inbox empty under New (the 2026-10-14 notes were processed at 0946); build 0 warnings; the reprint's claim verified
(`CounterTriangleMarginsTests` + `DataContentHashTests` + `ReplayGoldenTests` 59 / 59 on `main`, BUG-0734 fixed); open S1 / S2 **0**; sessions
today 0 / 8; weekly usage 75 % (stop 95 %) so budgets are trimmed: **sim ~1,000, view ~700, data ~350 lines**. Four refinements to the briefs
below: (1) BUG-0703's open question is decided: a collapse is **neither a kill nor a loss** (the stats count only what the enemy destroyed;
docs/03 says so); (2) **BUG-0750 item 4** (`Stress/AbilityManagerFuzzStressTests`' off-seat share) moves from the data track to the sim's item 5
(it is a sim-owned file and the sim works `AbilityManager` for BUG-0702 anyway): the data track edits **no** C# outside its folders this session;
(3) the sim's item 5 also fixes CLAUDE.md's stale "Current milestone: M1" line (→ "M5 (sim) / M6 (view); M3 data Done"); (4) bug ids: sim
from BUG-0760, view from BUG-0770, data from BUG-0780. Everything else in the briefs stands as written.

Written by the Producer at the ACCEPT of session **2026-10-15-2325** (the first full session of 2026-10-15; 1 / 8 after it; 2026-10-16
has 0 / 8). All three tracks **ACCEPT**: sim **M5-P1** (surround a building, 1 fix round), view **M6-V8** (resource click + keybinds, 1 fix
round), data **D19** (×1.5, Easy casts, building damage set (C), one golden header move → **AB811AD47F68D0AE**). **Every counter is
4 / 4: the next session is a hardening session for all three tracks (M5-H2, M6-VH2, D-H5).** Bug ids next session: **sim from
BUG-0760, view from BUG-0770, data from BUG-0780**. Integration sim → view → data **plus a merged-tree reprint** (below): until the
conductor reports it done, check `git log origin/main` for a commit "D19: re-print the siege and kill-time cells on the merged tree"
and `CounterTriangleMarginsTests` green on `main` before planning anything.

## Where we are

- `main` after this integration = 53e69d7 + sim 94a0c1a + view b6c33e4 + data cc3e170 + the reprint commit. Golden format 6, `data-hash`
  **AB811AD47F68D0AE** (D19; every `k` line unchanged). `GatherWedgeQaTests.SameGameDataHashes` lists it (the named exception).
- **The merged-tree reprint (REQUIRED at this integration, data-track fix-mode dev on the merged tree, docs + tests only):** the sim's
  surround rule makes line units raze buildings faster, so D19's freshly printed siege cells (Catapult on a Tent 36.2 s / 47 %, Ram on
  a Billet 40.0 s / 34 %) and its kill-time seconds are stale the moment both land: 8 `CounterTriangleMarginsTests` rows red (BUG-0734,
  S2). The dev re-prints both pages' siege tables + prose, re-runs `BuildingKillTimeReportTests` (12 / 12 must hold; if a cell fails:
  **do not loosen silently**: pin the measured count with a dated reason, file an S3 for D-H5, report), updates docs/03 "Implementation
  (pathing slice 1)"'s last bullet to the merged-tree numbers, sets BUG-0734 fixed. No `game/data/**` change (the header stays).
  **If the reprint cannot be dispatched:** push sim + view, hold the data branch (its own tree is green), and the data track's first
  item next session is "land D19 + the reprint" with the sim's rule already on `main`.
- The owner's 2026-10-14 answers are all built now (notes 1, 5, 6, 7, 8, 9 + ×1.5 + Easy casts); nothing new in the inbox since.
  The owner's D19 review table and the two proposals (a Shock pass; `goldShare` 0.6 → 0.5) are under For your review: **an inbox
  answer on either is the data track's first item** when it arrives (inbox requests before roadmap work).

## Standing rules (1-25)

1-24 as before (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`; per-slot
memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track test
that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent sim
tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is reported; no
process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop when the sim
touches movement / the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door / the defeat
rule; a QA scene row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop; a Perf gate measures the
shipped data; 22. only one track moves the golden header per session; 23. a QA verdict without its committed report file is no
verdict; 24. a scene that stages a building before any unit of that player calls `TestSim.KeepIn` / spawns a unit first).
**25 (new, BUG-0734):** a sim change to movement or combat that can move a *measured* data-page cell (`CounterTriangleMarginsTests`'
siege rows, `BuildingKillTimeReportTests`, the Line v Shock rows) names those tests in the sim brief; the data track does not re-print
those cells the same session; the integration ends with a merged-tree reprint by the data track before the push.

---

## Sim track

### Current session plan: M5-H2 · sim hardening (hardening, QA full, counter 4 / 4 → 0 / 4)

**Goal.** Work the sim debt, most valuable first; no feature. **No golden header move** (the data track moves nothing either; a later
M5-4f session carries the `gatherRate` + seed-21 re-record).

**Items, in order (batch as many as fit ~1,200 lines; stop at the budget and report the rest):**
1. **BUG-0703 (S3), the collapse rule, Producer decision (c) in docs/01 2026-10-14:** when a player is out (`UpdateDefeats`), every
   remaining building of its (finished, site, tower, Forge, House) is freed like a razed one on that tick: a `DeathEvent` per
   building (`IsBuilding`, killer owner -1, **no kill credited and no loss counted** (Producer decision at the 0847 PLAN: the stats count
   only what the enemy destroyed; the view's rubble still comes from the event), say so in docs/03), cells
   reopened, ghosts updated as for a razing, its research / queues gone with the buildings. Flip QA's
   `WinConditionQaTests.ADefeatedPlayersTower_KeepsShooting_Measured` and `...CommandsOnItsStandingBuildings_*(defeated)` to the new
   behaviour; the AI-v-AI rows and `MatchOverTests` (a)-(g) green; the golden `k` lines unmoved (no defeat in either recorded match: if
   one moves, stop). docs/02 "Game flow" 4 + docs/03 "Implementation (M5-4e)" + docs/01 row updated to "built".
2. **BUG-0732 (S4, both items, a few lines each):** `SurroundRule.HeldByOther` compares `Owner` (a rival's claims are not "held");
   the pick skips a spot whose cell the unit's cached field can't reach (`ReachableOnFoot`'s test at the spot's cell) so a sealed
   pocket's spots are never claimed first. Flip QA's `ASealedPocketOnTheNearFace_Measured` / `TwoOwnersOnOneHouse_BothStrike` to
   asserts. **Report, don't fix:** the House row at 17-18 / 20 (the far-corner jam; slice 2).
3. **BUG-0745 (S3, view-filed, possibly movement; now two data points):** replay `%APPDATA%/Godot/app_userdata/RtsGame/m4playable-seed1-fail-tick2497.replay`
   in a bare-sim probe (as BUG-0463 / 0603): does the Sapper's Move at tick ~2124-2497 leave its cell? **Second point (Producer's
   merged-tree loop, 2325):** seed 6 step 6 failed under load ("a kill and a loss: not by tick 5500") and passed alone (49.3 s): the scene
   is timing-fragile on more than one step; if the probe shows the sim moving the units correctly, the whole bug is the view's (its
   M6-VH2 item 4: stage each step from sim state, not frame counts). If the sim never moves it, fix
   it here (a unit boxed in by idle own units must push out: the BUG-0028 / 0032 family: say which); if the sim moves it, write "scene
   fault" in the bug and hand it to the view's M6-VH2 (its item 4).
4. **BUG-0701 (S3) + standing rule 21:** `FogStore.NextSeenUnit` tests only units near a blocking zone; `AiPerfTests` gets a
   shipped-data row with 8 storms.
5. **BUG-0704 items 3-4 + the `TestSim.cs` space nit; BUG-0702 items 2-3; BUG-0750 item 4** (`Stress/AbilityManagerFuzzStressTests`: a
   share of scenes whose Easy block has `useAbilities` false in a data copy, so the "an off seat sends nothing" assert can fail again;
   mutation: flip it and the assert fires); **CLAUDE.md's "Current milestone" line** (one line: M5 sim / M6 view, M3 data Done);
   **BUG-0675 c (a docs/03 line); BUG-0651; BUG-0650; BUG-0144 (`ChasersUnderPlacementChurn_*`); BUG-0113 item 2.** Each: the skipped QA
   row un-skipped. Stop at the budget (~1,000 lines) and report the rest by bug id.
6. OUT: M5-4f, pathing slice 2, any knob, any `game/data/**` edit, `rules.json`, the Ages → tech-tree design, the data track's test
   folders (`Content/`, `QA/Content/`).

**Files.** `sim/**` except the data track's folders and `ViewApi/**`; docs/02 / 03 / 01; `studio/bugs/*` statuses.
**Acceptance.** Every fixed bug's regression test red-then-green named; `ReplayGoldenTests` / `GatherWedgeQaTests` green without a regen;
full suite incl. Perf alone once; build 0 warnings; docs updated; the line count stated.
**QA focus (full):** the collapse rule under the door fuzz and a 4-seat AI match (no out player's building acts after its tick; the
stats' losses; a site collapsing while a builder works it; a tower mid-shot), hash twins through a collapse, `k` lines; the surround
pick's reachability filter against the cliff / corner / pocket scenes (no give-up added); the BUG-0745 replay probe's verdict checked.
**Budget.** ~1,200 lines (clear design: every item has a direction in its bug file).

### Next task candidates (after M5-H2)
1. **M5-4f proportional defense + D15 C + BUG-0702 item 1 + `gatherRate` gold 0.85 / wood 0.75 with the seed-21 re-record** (one header
   move; QA full, ~900 lines; `rules.json` `ai.defendRatio` / `defendMinValue` / `recallHallHp`).
2. Pathing slice 2 (flank through your own line; the House's far-corner jam), M5-4c slice 2 (knobs → criterion 4), Requests 39 (+ 38),
   D14 b schema, M5-4d (+ hunting the last units → criterion 3), D14 c, M5-5, M5-6.

### Watch-outs (sim)
- The reprint commit lands before your branch: pull `main` first; `CounterTriangleMarginsTests` must be green on your base.
- `SurroundClaim` / `SurroundHolder` are save state outside the hash (BUG-0733, Producer decision): any M6 save work carries them.

---

## View track

### Current session plan: M6-VH2 · view hardening (hardening, QA standard, counter 4 / 4 → 0 / 4)

**Items, in order:**
1. **BUG-0684 (S4) items 1-5:** per-process `user://` paths in `SettingsTest` / `ShellTest` (as `QaM6V6Test`); the screen shows the
   effective VSync under `--vsync`; the dead `ShowMenu()` branch; `GroundDiscPool.Reclaim`'s scan; the bench's half-on-screen column.
2. **BUG-0744 (S4) items 1-2:** `KeyMap.Read` refuses a value whose `OS.GetKeycodeString(key)` doesn't match the trimmed input
   (case-insensitive) so "Meta+K" / "Kp+K" are one problem + the default; `IsPlainKey` requires the name to round-trip. Item 3 stays
   documented (docs/03 "The file").
3. **BUG-0743 item 3:** call `SeenResources.UpdateAmounts` from `SimRunner.Ticked` (per tick, not per frame) if it is a few lines; else
   wontfix with the reason in the file.
4. **BUG-0745 (S3) scene half** if the sim's probe says "scene fault": pull the army off the Sapper's cell before step 7 (or stage the
   Sapper apart), and look at step 6 too (seed 6 failed it once under load on the Producer's merged-tree loop: "a kill and a loss: not by
   tick 5500", PASS alone); seeds 1 and 6 both PASS 3 / 3 under load.
5. **BUG-0622 item 1** (`StartBase.Plan` west-edge wrap; the sim may take it by name if it prefers), BUG-0550 item 1 note, BUG-0521
   items 2-3 (close as unreachable with reasons), the AiSeat / MatchOver scenes' leftover notes, the `ResourceBar` "Always" alternative
   only if the K / L sync shows a gap.
6. OUT: M6-V5 slots, save / load, the look test, export, anything under `game/data/` but `common/ui.json` text keys, any `sim/` change
   outside `ViewApi/` (read-only).

**Acceptance.** Each fixed bug's test red-then-green; 0 warnings; smoke PASS both passes; **scene loop green on the branch (58 scenes +
the playables)**; docs/03 sections touched updated; the line count stated.
**QA focus (standard):** the keys file with hostile values after item 2 (the 44 hand edits of `QaM6V8R1Test` + "Meta+K" family refused);
`SettingsTest` / `ShellTest` run twice in parallel (per-process paths); the loop under load twice.
**Budget.** ~700 lines (items 1-4 first; item 5 as it fits).

### Next task candidates (after M6-VH2)
1. **M6-V5's slot part** (2-4 slots; the team column needs Requests 38). 2. Save / load + replay playback (criterion 3; carries
   `SurroundClaim` / `SurroundHolder`, the flow-field cache, the fog bits: docs/03 "Save/load and replays"). 3. The look test (the
   owner's packs are in `asset-sources/`), `tools/export.ps1` with Requests 33, the stats columns as Requests 39 lands.

### Watch-outs (view)
- The owner has not yet tried the real-cursor confinement or the Keybinds tab on the desk; an inbox report comes first.
- Three Godot runs share the machine: wall-clock scene rows flake under load (BUG-0745 was found that way); rerun alone before filing.

---

## Data track

### Current session plan: D-H5 · data hardening (hardening, QA light, counter 4 / 4 → 0 / 4)

**First: any owner answer in the inbox on D19's review table, the Shock pass or `goldShare`** (apply it, one header move allowed for
the data track this session since the sim moves nothing: say so in the report and add the hash to `SameGameDataHashes` with the reason).

**Then, in order:**
1. **BUG-0750 (S4), items 3 and 5 only** (items 1-2 were fixed by the merged-tree reprint b616086; **item 4 is the sim track's this
   session**, no exception needed): `QA/Content/D18ReportPreconditionQaTests`' lever edit at values off the shipped ones (e.g. 0.5 / 0.25 /
   0.5 and 1.4; or dropped, with QA's `D19QaTests.BeforeD19Copy_*` named as the cover in the file); the regen-reason convention written into
   `studio/bugs/README.md`'s head or docs/07 (one line: "the reason lives in the `SameGameDataHashes` comment and the commit body").
2. **BUG-0721 (S4):** the siege ratio checked against both line rows; the spending conclusion stops at the idle seat's defeat; the
   generated rows at the menu's 8 mines (or the 6 stated beside the table).
3. **The D18 reports re-run on D19 + the surround rule** (`AiSpendingReportTests.GeneratedNormal_*`, the kill-time report's today column
   already re-printed by the merged-tree reprint: confirm, don't redo): dated conclusions on both pages / docs/02 where quoted.
4. Leftovers of the merged-tree reprint if the conductor's log lists any.
5. OUT: any number not in the inbox; the Teblor / Shadow / Andii data; `rules.json`; **any C# outside `sim/Rts.Sim.Tests/Content/` and
   `sim/Rts.Sim.Tests/QA/Content/`** (no exception this session; `Stress/AbilityManagerFuzzStressTests` is the sim's).

**Acceptance.** `DataValidationTests` / `DataContentHashTests` green; the golden untouched unless an owner tweak landed (then one regen,
`k` lines equal, the hash listed); every Content / QA/Content pin green; the line count stated; `git diff --stat` only the named files.
**QA focus (light):** the fuzz's new off-seat share really has `UseAbilities` false (mutation: flip it and the assert fires); the
precondition test fails on a mistyped key again.
**Budget.** ~350 lines.

### Next task candidates (after D-H5)
1. The owner's D19 tweaks / the Shock pass (Horse Raider and Wickan Lancer hp / cost upward; a sweep first, as D18 did) if asked.
2. The D14 b openings once the strategies schema is on `main`. 3. The house notes after D15 C (once M5-4f lands). Otherwise STOP cheaply.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing (this session: 18-25 Perf rows red under load
  in every track's full run, all green or equal-to-base alone / paired).
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them (three
  conflicts at this integration, all unions).
- Merge order sim → view → data, then the reprint, then the push. The Producer's merged-tree check runs the scene loop whenever the
  sim touches movement.
- Nobody moves the golden header next session unless an owner tweak arrives for the data track (rule 22).
