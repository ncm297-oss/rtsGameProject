# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-11-05-0805** (session 1 / 8 on 2026-11-05; base `424a5e8` = `origin/main`; the
third resume of the paused session 2026-10-27-0658, whose branches carry the recovered work; 0658, its first resume 2050 and the scheduled resume 0749 (died at its PLAN, ~07:55; its plan is adopted here unchanged) all died
without an ACCEPT: `studio/sessions/2026-10-27-0658-incident.md`). Sim **GO (finish M6-S5 on `studio/2026-10-27-0658-sim`)**, view
**GO (finish M6-V16 on `studio/2026-10-27-0658-view`)**, data **STOP (cheap)**. Counters at the start: sim 1 / 4, view 0 / 4, data
1 / 4. **Open S1 / S2: BUG-1045 (S2, against M6-S5, on the sim branch; its fix is mostly in 5dff5d8, finished by this session's sim
brief).** `main` green at the 0158 ACCEPT (7,608 / 0 / 12); 424a5e8 is docs-only on top of it. **Verified at the 0805 PLAN:** both branches build 0 warnings; the sim branch's targeted run (golden, data validation / hash, `AiTeamTests`, `SharedVisionQaTests`, `TeamArchitectureTests`, `GatherWedgeQaTests`) 294 / 2 / 1: the two reds are the expected golden + the `data-hash` pin, the skip BUG-1045's test.

## Where we are

- **M5 4 / 9, M6 3 / 10** (criterion 1's sim half is in: the team; the view's column is on the view branch). Golden `data-hash`
  **E108AB2A7C66C58D moves this session**: `rules.json` gained `ai.gatherReach` on the sim branch (regen once, same commit, say why).
  **Save format 3**, **replay format 7** (a free-for-all still writes 6). `World.AreAllied` / `IsEnemy` / `TeamOf` / `HasAlly`.
- **On the sim branch (5dff5d8, builds 0 warnings, not yet accepted):** d6f597d shared vision (`FogStore.Teams.cs`: a per-team OR after
  `Update`, so `Visibility(local)` already holds the union; the view's fog needs no change), the BUG-1042 scout fix, the nobody's-kill
  rule; 47049c4 QA's partial output (`QA/SharedVisionQaTests` 569 lines with one `Skip` on BUG-1045, `QA/SharedVisionScaleQaPerfTests`,
  BUG-1045 S2, BUG-1046 S4); 5dff5d8 the 2050 dev's unreported BUG-1045 fix (`ai.gatherReach` 64 in `rules.json` → `AiRulesDef.GatherReach`
  (1-512 m, `DataLimits.MaxAiGatherReach`), `AiPlayer.InGatherReach` + `KnowsNode`, `ScoutManager.FoundEnough` within reach of the hall,
  `EconomyManager.NearestNode` within reach, `DataValidationTests` + `DataContentHashTests` rows, the `using` fix, and a scratch
  `sim/Rts.Sim.Tests/ZzScratchReachTests.cs` that prints each start's nearest forest / two mines).
- **On the view branch (7450aef, builds 0 warnings, smoke PASS, ViewApi suites 1,128 / 0):** every M6-V16 item is in code; the dev was
  stopped before verifying the scenes and reporting. `ui.json` new text: `setup.team` "Team", `setup.team_none` "None", `setup.team_item`
  "Team {0}", `match_over.team` "Team {0}", `load.seat_team` "{0} (T{1})".
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. **Rule 36** (frozen save / replay API) applies to both tracks. **Rule 31** (re-print the faction pages when the AI's play
moves): the BUG-1045 fix may move 1v1 pairing cells; the sim reports, the data track re-prints next session.

---

## Sim track

### Current session plan (2026-11-05-0805, the 0749 plan adopted)

**M6-S5 (resumed, second time): finish BUG-1045 (S2), regen the golden, A/B, BUG-1046, docs, report · feature (counter 1 / 4 → 2 / 4)
· QA full · bug ids from 1047.** Work on `studio/2026-10-27-0658-sim` at 5dff5d8 (do not rebase; `main` is its base). Keep QA's two
files (fix, don't rewrite). The dev has no memory of the earlier sessions: read d6f597d's docs/03 "Implementation (M6-S5)" section,
`studio/bugs/BUG-1045-*.md` and `BUG-1046-*.md` on the branch first.

**Goal.** Allies share vision (built), and an AI seat that sees a far forest through its ally's eyes still gathers near home and still
scouts its own surroundings (BUG-1045: mostly built, unverified). Land it green with the paperwork a merge needs.

**Scope (what is left; everything named is on the branch unless said):**
1. **Verify the recovered fix.** Run the full non-Perf suite on the branch. Expect `ReplayGoldenTests` and every `data-hash` pin to be
   red (the new field). Fix what else is red inside the brief's scope; report anything outside it as a bug.
2. **Golden regen, once:** `RTS_REGEN_GOLDEN=1 dotnet test --filter ReplayGoldenTests`, then without; add the new hash to
   `QA/GatherWedgeQaTests.SameGameDataHashes` the way the earlier entries did ("M6-S5: rules.json ai.gatherReach joined the hash, no AI
   seat in this match"); grep the tests for `E108AB2A7C66C58D` and move every pin. The reason goes in the commit message.
3. **The reach value:** run `ZzScratchReachTests.Reach` once, copy its table (per start: nearest forest, nearest two mines; menu map
   seeds 1-10 as 2 seats, the 256-cell 4-start map seeds 1-10 as 4 seats) into docs/03 "Implementation (M6-S5)" and the report; keep 64
   if it covers every start's nearest forest **and** two mines, else the smallest round value that does (48 / 64 / 80 / 96) and say so.
   Then **delete `ZzScratchReachTests.cs`** (no scratch file in the commit).
4. **Un-`Skip`** `QA/SharedVisionQaTests.MirrorAllies_EverySeat_GathersWoodNearHome`; it must pass. `Stress/TeamAiSoakStressTests`'
   "starved" exemption on the `adjacent` rows: list which seats still use it and why (a raid, with its tick), or tighten it.
5. **A/B the 27 pairing runs** (`AiOpeningPairsReportTests`) head v 424a5e8: if any cell moves, name which and do **not** edit
   `docs/factions/**` (rule 31: the data track re-prints next session).
6. **FFA pins** (`SharedVisionFfaPinTests`, `ReplayGoldenTests`, the five A/B scenes × 3,000 ticks from the 0158 session): a **human-only**
   scene must be hash-equal to 424a5e8; an **AI-seat** scene may move only through the gather choice (the reach rule): name the first
   diverging tick and seat, and update the pin with the reason in the test.
7. **BUG-1046:** item 1 a test that fails when `FogStore.ExploreAllForTests`' `MergeTeams()` is removed; item 2 **Producer decision, no
   code**: docs/02 "Vision and fog of war" gets one sentence (an ally that is out lends its eyes until the next fog update, within 4
   ticks, as a dead unit's circle does); item 3 fix the two difficulty labels in BUG-1042's fix note. Close BUG-1046 `fixed`.
8. **Docs:** docs/02 "AI opponent" (the scout's "found enough" within reach and the gather reach), docs/03 "Implementation (M6-S5)"
   addendum (the reach table, the shared-vision A/B rows if not already there, the pairing A/B), docs/03's `rules.json` `ai.*` knob table
   (`gatherReach`), `studio/bugs/README.md` rows (BUG-1042 fixed, BUG-1044 item 1 fixed, BUG-1045 fixed, BUG-1046 fixed),
   `studio/qa/coverage.md` (the M6-S5 row).
9. One commit with a real message (not "recovered work"); the game-dev report listing every criterion with its evidence.
10. **OUT:** Request 43, formations, resource / tech sharing, AI cooperation, a map format, `ViewApi/**`, `game/**` except
    `game/data/common/rules.json`, scouting the whole head ring before release (changes every seat's timing), any change to the
    `SaveHeader` shape (the view's `SaveSummary` reads it).

**Acceptance criteria:**
1. Build 0 warnings; the full non-Perf suite green on the branch; the Perf suite green when run alone.
2. `MirrorAllies_EverySeat_GathersWoodNearHome` runs (no `Skip`) and passes; in `AiTeamTests.MirrorAllies(1)` seat 3 knows a forest within
   `gatherReach` of its hall by tick 1,000 and has gathered wood by tick 4,000 (the BUG-1045 repro).
3. `ai.gatherReach` loaded, validated 1-512 m (`DataValidationTests` rows), hashed (`DataContentHashTests` row), in docs/03's knob
   table; the golden regenerated once with the reason in the commit; every `E108AB2A7C66C58D` pin moved.
4. The reach table in docs/03 and the chosen value justified by it; no scratch file in the commit.
5. Human-only FFA scenes hash-equal to 424a5e8; AI-seat FFA pins equal or moved with the first diverging tick / seat and the reason.
6. The 27-pairing A/B reported; `docs/factions/**` untouched.
7. BUG-1046 items 1-3 done; the four bug statuses in `studio/bugs/README.md`.
8. The 0658 criteria still hold: `VisionGatePerfTests` fresh-process row under 4 ms; `TeamArchitectureTests` green; `InGatherReach` and
   `FoundEnough` allocate nothing (an `AiThinkCostQaTests`-style row or the existing allocation test covers them).
9. No `ViewApi/**` or `game/**` change outside `rules.json`; no Godot, wall clock, or unseeded randomness in `Rts.Sim`.

**Design references:** docs/02 "Vision and fog of war" (shared vision rule, d6f597d), docs/02 "AI opponent" (scout, economy), docs/03
"Implementation (M6-S5)" and the `rules.json` knob table, docs/03 "Testing strategy" (golden). **Tests required:** the un-skipped QA
test; BUG-1046 item 1's test; the validation / hash rows (in); the golden; the FFA pins.

**Constraints most at risk:** no allocation per think (one distance compare per own building, no list); `TeamArchitectureTests` scans
`Ai/` for raw owner compares (the reach check compares distances, keep `IsAlly` as is); the golden regen must be in the same commit as
the field with its reason; no `DateTime` / `System.Random`.

### Next task candidates (in order)

1. **Request 43 (starting resources):** `rules.json`-driven presets, `SimConfig`, save format 4 / replay format 8 (bytes at the end), the
   AI's openings at "high" (a pairs-report row).
2. **A map file format** for M6 criterion 7's three hand-made maps (`game/data/maps/`, hashed in the header).
3. M5-4c slice 2; **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last units); BUG-1010's lever; D14 c; M5-5. Sim
   hardening (M6-SH1) at counter 4 / 4: BUG-1044's rest, BUG-0960's throw, the movement S3s.

### Watch-outs (sim)

- QA's `Diag1042Tests` / `Diag1042cTests` were scratch (not committed); BUG-1045's table came from them. The repro is
  `AiTeamTests.MirrorAllies(1)`, seat 3, ticks 250 / 500 / 1,000 / 2,000.
- `EconomyManager` has a per-kind "no node" flag (`noGold` / `noWood`): the reach rule feeds that path (as 5dff5d8 does), not a second
  idle state. `InGatherReach` returns true with no drop-off and once `ScoutPhase == 2`: a seat whose scout dies early falls back to any
  known node (intended; say so in docs/02).
- The owner's recordings made since 2026-10-19 will refuse to play after the regen (list it for the For your review entry).
- `SaveHeader.Write` is read by the view's `ViewApi/SaveSummary` up to the tick and the team bytes after it: don't touch the header.
- `VisionGatePerfTests` fresh-process row 3.50-3.90 ms against 4 ms; QA's shared-vision A/B numbers are in d6f597d's docs/03 section.
- Two tracks this session: two test hosts; wall-clock Perf rows flake under load; rerun alone before filing.

---

## View track

### Current session plan (2026-11-05-0805, the 0749 plan adopted)

**M6-V16 (resumed, second time): verify, fix, report · feature (counter 0 / 4 → 1 / 4) · QA standard · bug ids from 1061.** Work on
`studio/2026-10-27-0658-view` at 7450aef ("recovered work": every item of the 0658 brief is in code; builds 0 warnings, smoke PASS,
the ViewApi suites 1,128 / 0; do not rebase). The dev has no memory of the earlier session: read `git show 7450aef --stat`, docs/03
"Implementation (M6-V16)" on the branch and docs/02 "Game flow" 2 / 4 first.

**Goal.** The skirmish setup's Team column (M6 criterion 1's last view item) and the four view-owned readers the sim's teams need, so
the owner can play a 2v2 against two AIs with an AI ally. The code is in; this session proves it, fixes what fails, and reports.

**Scope:**
1. Run the scenes: `TeamsTest`, `TeamFilesTest`, `ShellTest`, `FreeForAllTest`, `SaveLoadTest`, `QaM6V5Test`, `QaM6V14Test`,
   `QaM6VH4Test` (`tools/qa/scene-loop.ps1 -Filter`), then the whole loop once (75 scenes, ~23 min); fix what fails inside the brief's
   scope; `powershell -File tools/qa/smoke.ps1` PASS.
2. Windowed: the setup with four slots on Teams 1 / 1 / 2 / 2, a 2v2 played to the banner (`--team` flags are fine), the stats screen
   grouped under "Team 1" / "Team 2", Load Game's "(T1)" cells; **screenshots, looked at**, saved under `docs/images/` only if docs/03
   names them.
3. Re-check the four readers against the 0658 brief: `ReplayStore.TryReadHeader` accepts five-token seat lines (format 7) and still reads
   four-token ones (format 6); `SimRunner.StartPlayback` passes `seat.Team` into both `PlayerSeat` lists (a format-7 replay plays to every
   checkpoint; the two fixtures under `game/tests/fixtures/teams/`); `ViewApi/SaveSummary.TryRead` reads the team bytes after the tick
   (format 3) and refuses 9 / 255; `Match.ResultFor` per side (Victory when the local team stands at the end, Defeat when the whole side
   is out, Draw when nobody stands; **silent while the ally fights on** after the local player is out); `ViewApi/UnitPicker.ResolveEnemy` +
   `MinimapRaster.DrawnEnemyDotAt` through `EnemyTable` / `World.IsEnemy` (a right-click on an ally's unit = a move; an ally's dot is not
   an enemy dot).
4. Docs: docs/02 "Game flow" 2 / 4, docs/03 "Implementation (M6-V16)", docs/01 row (all on the branch: proofread against what the code
   does); the five `ui.json` strings quoted in the report for the owner; BUG-1060 items 2-3 as recorded (`wontfix` word on BUG-0521;
   "from the second run is enough"); `studio/qa/coverage.md` row. Then one commit with a real message and the game-dev report.
5. **OUT:** team colour tints, starting resources, any fog change (the sim's union is in `Visibility(local)` already; the view's fog
   shader needs nothing), any `sim/**` file outside `ViewApi/`, any `game/data/` file but `ui.json`.

**Acceptance criteria:**
1. Build 0 warnings, smoke PASS, the named scenes PASS, the whole loop green (a flaky wall-clock row counts only if it fails alone).
2. Setup: a Team list per slot (None / 1-4), default None; `--team <slot> <n>` emitted only for a slot with a team; the seats reach the
   sim with `PlayerSeat.Team`.
3. Banner and stats: a 2v2 reaches Victory / Defeat / Draw by side; no Defeat banner while the ally fights on; the stats screen grouped
   under "Team 1" / "Team 2" with none-team seats ungrouped; Load Game shows "(T1)".
4. Readers: format-7 and format-6 replays both list and play; a format-3 save's team bytes read and shown; 9 / 255 refused.
5. Picker / minimap: a right-click on an ally's unit is a move; an ally's dot is not drawn as an enemy dot (the ViewApi tests on the branch).
6. Screenshots looked at and described in the report; docs/02 / 03 / 01 match the code; every player-facing string in `ui.json`.
7. Nothing in `sim/**` outside `ViewApi/`; `ViewApi` changes read-only (no tick, no hash change: `ViewApiAllocationTests` green).

**Design references:** docs/02 "Game flow" 2 (setup) and 4 (result per side), docs/03 "Implementation (M6-S4)" (formats 3 / 7) and
"Implementation (M6-V16)", docs/03's ViewApi rule. **Tests required:** `TeamsTest`, `TeamFilesTest` scenes; `ViewApi/SaveSummaryTests`,
`UnitPickerTests`, `MinimapRasterTests`, `ViewApiAllocationTests` rows (in).

**Constraints most at risk:** views hold no gameplay state; `ViewApi` never mutates; text only in `ui.json`; the save / replay API frozen
(rule 36: read, never write a new shape).

### Next task candidates

Every other M6 item still waits on something: starting resources on Request 43, the three maps on a map format, the art pass on the
owner's look call, music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. After
M6-V16: the starting-resources list when 43 lands, then (e) export, (f) criterion 6; view hardening (M6-VH5) at counter 4 / 4.

### Watch-outs (view)

- **Merge conflict expected with the sim branch** on docs/02 "Game flow" 2 (both rewrote the same paragraph) and docs/03's team table
  (the sim adds two rows after `Ai/ScoutManager.cs`; the view rewrites the `UnitPicker` / `MinimapRaster` rows). Resolution: keep both
  (the view's paragraph with the sim's clause "and allies share vision (M6-S5, 'Vision and fog of war')"; all four table rows).
- `ViewApi/SaveSummary` hard-codes the header's shape: the team bytes are **after the tick** (format 3).
- `Match.ResultFor` in a 2v2: the local player can be defeated while `MatchOver` is false; no Defeat banner then.
- `scene-loop.ps1 -Filter` is a file glob; the loop is 75 scenes now, ~23 min.

---

## Data track

### Current session plan (2026-11-05-0805, the 0749 plan adopted)

**STOP (cheap, counter stays 1 / 4).** The inbox's **New** is empty (no D21 / D19 / Shock / `goldShare` / BUG-0780 answer); no schema
the data track needs is new on `main` (`ai.gatherReach` is a sim-owned `rules.json` knob, on the sim branch only). **Next session,
likely:** the rule-31 re-print if the sim's BUG-1045 fix moves pairing cells (the sim's report says which), with BUG-1080's two wordings
folded in.

### Next task candidates

1. The owner's answers above, each a small page + `ai.json` / data task with a review table (golden regen once).
2. The rule-31 re-print after M6-S5 (if cells move); **BUG-1080 (S4):** two page wordings, folded into it.

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`.

---

## Watch-outs (all tracks)

- Both resumed branches are based on `origin/main` = 424a5e8 (nothing to merge in). The conductor merges sim then view; see the view's
  conflict note.
- Two tracks this session: two test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Bug ids next: **sim from 1047, view from 1061, data from 1081**.
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields); a third field lands this session.
- **The diff base for both resumed devs is `424a5e8`** (`git diff 424a5e8...HEAD`); the sim dev goes first (unfinished work), the view dev goes first too (verification + report), then each track's QA.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase; commit each dev's work at every phase boundary
  (the 2050 fix sat uncommitted for nine days).
