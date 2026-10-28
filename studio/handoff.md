# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-27-2050** (session 2 / 8 on 2026-10-27; base `424a5e8` = `origin/main`; a
manual resume of the paused session 0658, whose branches carry the recovered work). Sim **GO (finish M6-S5 on
`studio/2026-10-27-0658-sim`)**, view **GO (finish M6-V16 on `studio/2026-10-27-0658-view`)**, data **STOP (cheap)**. Counters at the
start: sim 1 / 4, view 0 / 4, data 1 / 4. **Open S1 / S2: BUG-1045 (S2, against M6-S5, fixed by this session's sim brief).** `main` green
at the 0158 ACCEPT (7,608 / 0 / 12); 424a5e8 is docs-only on top of it.

## Where we are

- **M5 4 / 9, M6 3 / 10** (criterion 1's sim half is in: the team; the view's column is on the view branch). Golden `data-hash`
  **E108AB2A7C66C58D** will move this session if the sim adds `ai.gatherReach` to `rules.json` (regen once, same commit, say why).
  **Save format 3**, **replay format 7** (a free-for-all still writes 6). `World.AreAllied` / `IsEnemy` / `TeamOf` / `HasAlly`.
- **On the sim branch (d6f597d, built and committed, not yet accepted):** shared vision (`FogStore.Teams.cs`: a per-team OR after
  `Update`, so `Visibility(local)` already holds the union; the view's fog needs no change), the BUG-1042 scout fix, the nobody's-kill
  rule; QA's partial output (47049c4): `QA/SharedVisionQaTests` (569 lines, one `Skip` on BUG-1045), `QA/SharedVisionScaleQaPerfTests`
  (**does not compile**: missing `using Rts.Sim.Ai;`), BUG-1045 (S2), BUG-1046 (S4).
- **On the view branch (7450aef, builds 0 warnings, smoke PASS, ViewApi suites 1,128 / 0):** every M6-V16 item is in code; the dev
  was stopped before verifying the scenes and reporting.
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. **Rule 36** (frozen save / replay API) applies to both tracks. **Rule 31** (re-print the faction pages when the AI's play
moves): the BUG-1045 fix may move 1v1 pairing cells; the sim reports, the data track re-prints next session.

---

## Sim track

### Current session plan (2026-10-27-2050)

**M6-S5 (resumed): fix the red build, BUG-1045 (S2) and BUG-1046, then QA · feature (counter 1 / 4 → 2 / 4) · QA full · bug ids
from 1047.** Work on `studio/2026-10-27-0658-sim`; keep QA's two files (fix, don't rewrite). Full brief in the PLAN output; the short form:

1. **Build:** `sim/Rts.Sim.Tests/QA/SharedVisionScaleQaPerfTests.cs` gets `using Rts.Sim.Ai;` (one line). Nothing else in QA's files
   unless it fails to compile.
2. **BUG-1045, Producer's direction (minimal, data-driven):**
   - New `rules.json` `ai.gatherReach` (m) → `AiRulesDef.GatherReach` (+ `DataValidationTests`: positive). Default: the smallest round
     value (try 64) that covers each start's nearest forest **and** two mines on the menu map seeds 1-10 and the 256-cell 4-start map;
     print the measured distances in the report.
   - `ScoutManager.FoundEnough`: a forest **and** a mine within `gatherReach` of the own hall (`TryHallPoint`), plus an enemy building
     known (as now). A node known only through an ally or a raider far away is not "found".
   - `EconomyManager.NearestNode`: while `Board.ScoutPhase != 2` a node farther than `gatherReach` from the nearest own drop-off counts
     as unknown (the kind's "no node" path, as a seat that knows nothing waits today); once the scout is done (phase 2, released or
     dead) the nearest known node is taken as before (no regression after a near forest is exhausted).
   - Un-`Skip` `QA/SharedVisionQaTests.MirrorAllies_EverySeat_GathersWoodNearHome`; `TeamAiSoakStressTests`' "starved" exemption on
     the `adjacent` rows: list which seats still use it and why (a raid, with its tick), or tighten it.
   - **A/B the 27 pairing runs** (`AiOpeningPairsReportTests`): if any cell moves, say which and do **not** edit `docs/factions/**`
     (rule 31: data re-prints next session). FFA hash pins (`SharedVisionFfaPinTests`, `ReplayGoldenTests`): an AI-seat scene may move
     legitimately (the economy changed); update the pin only with the reason in the test.
3. **BUG-1046:** item 1 a test that fails when `ExploreAllForTests`' `MergeTeams()` is removed; item 2 **Producer decision: no code**:
   docs/02 "Vision and fog of war" gets one sentence (an ally that is out lends its eyes until the next fog update, within 4 ticks, as a
   dead unit's circle does); item 3 fix the two difficulty labels in BUG-1042's fix note. Close BUG-1046 `fixed`.
4. Docs: docs/02 "AI opponent" (the scout's "found enough" and the gather reach), docs/03 "Implementation (M6-S5)" addendum, `rules.json`
   knob table, `studio/bugs/README.md` rows, coverage.
5. OUT: everything the 0658 brief excluded (Request 43, formations, resource / tech sharing, AI cooperation, a map format, `ViewApi/**`,
   `game/**` except `game/data/common/rules.json`), scouting the whole head ring before release (changes every seat's timing: not now).

### Next task candidates (in order)

1. **Request 43 (starting resources):** `rules.json`-driven presets, `SimConfig`, save format 4 / replay format 8 (bytes at the end), the
   AI's openings at "high" (a pairs-report row).
2. **A map file format** for M6 criterion 7's three hand-made maps (`game/data/maps/`, hashed in the header).
3. M5-4c slice 2; **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last units); BUG-1010's lever; D14 c; M5-5. Sim
   hardening (M6-SH1) at counter 4 / 4: BUG-1044's rest, BUG-0960's throw, the movement S3s.

### Watch-outs (sim)

- QA's `Diag1042Tests` / `Diag1042cTests` were scratch (not committed); BUG-1045's table came from them. The repro is
  `AiTeamTests.MirrorAllies(1)`, seat 3, ticks 250 / 500 / 1,000 / 2,000.
- `EconomyManager` line 169 / 175 already have a per-kind "no node" flag (`noGold` / `noWood`): the reach rule should feed that path,
  not add a second idle state. No allocation per think (a distance compare per node, no list).
- A new `rules.json` field moves `data-hash` (E108AB2A7C66C58D): regen the golden once in the same commit and say why; the owner's
  recordings made since 2026-10-19 will refuse to play after this (list it for the For your review entry).
- `SaveHeader.Write` is read by the view's `ViewApi/SaveSummary` up to the tick and now the team bytes after it: don't touch the header.
- `TeamArchitectureTests` scans `Ai/`: the reach check compares distances, not owners; keep `IsAlly` as is.
- `VisionGatePerfTests` fresh-process row 3.50-3.90 ms against 4 ms; QA's shared-vision A/B numbers are in d6f597d's docs/03 section.

---

## View track

### Current session plan (2026-10-27-2050)

**M6-V16 (resumed): verify, fix, report · feature (counter 0 / 4 → 1 / 4) · QA standard · bug ids from 1061.** Work on
`studio/2026-10-27-0658-view` (7450aef, "recovered work": every item of the 0658 brief is in code; the build is 0 warnings, the smoke
PASS, the ViewApi suites 1,128 / 0). The dev finishes what was cut off:

1. Run the scenes: `TeamsTest`, `TeamFilesTest`, `ShellTest`, `FreeForAllTest`, `SaveLoadTest`, `QaM6V5Test`, `QaM6V14Test`, `QaM6VH4Test`
   (`scene-loop.ps1 -Filter`), then the whole loop once (73 + 2 scenes); fix what fails, inside the brief's scope.
2. Windowed: the setup with four slots on Teams 1 / 1 / 2 / 2, a 2v2 to the banner, the stats screen grouped under "Team 1" / "Team 2",
   Load Game's "(T1)" cells; **screenshots, looked at**, saved under `docs/images/` only if docs/03 names them.
3. Re-check the four readers against the 0658 brief (format-7 five-token and format-6 four-token seat lines; `SaveSummary`'s team bytes
   after the tick with 9 / 255 refused; `ResultFor` silent while the ally fights on; UnitPicker / MinimapRaster through `EnemyTable`).
4. docs/02 "Game flow" 2 / 4, docs/03 "Implementation (M6-V16)", docs/01 row (all on the branch: proofread), `ui.json` text quoted in the
   report for the owner's review; BUG-1060 items 2-3 as recorded (`wontfix` word, "from the second run is enough"); then one commit with a
   real message and the game-dev report.
5. OUT: team colour tints, starting resources, any fog change (the sim's union is in `Visibility(local)` already), any `sim/**` file
   outside `ViewApi/`.

### Next task candidates

Every other M6 item still waits on something: starting resources on 43, the three maps on a map format, the art pass on the owner's
look call, music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. After M6-V16:
the starting-resources list when 43 lands, then (e) export, (f) criterion 6; view hardening (M6-VH5) at counter 4 / 4.

### Watch-outs (view)

- **Merge conflict expected with the sim branch** on docs/02 "Game flow" 2 (both rewrote the same paragraph) and docs/03's team table
  (the sim adds two rows after `Ai/ScoutManager.cs`; the view rewrites the `UnitPicker` / `MinimapRaster` rows). Resolution: keep both
  (the view's paragraph with the sim's clause "and allies share vision (M6-S5, 'Vision and fog of war')"; all four table rows).
- `ViewApi/SaveSummary` hard-codes the header's shape: the team bytes are **after the tick** (format 3).
- `Match.ResultFor` in a 2v2: the local player can be defeated while `MatchOver` is false; no Defeat banner then.
- `scene-loop.ps1 -Filter` is a file glob; the loop is 75 scenes now, ~23 min.

---

## Data track

### Current session plan (2026-10-27-2050)

**STOP (cheap, counter stays 1 / 4).** No inbox answer (D21 / D19 / Shock / `goldShare` / BUG-0780); no schema the data track needs is new
on `main`. **Next session, likely:** the rule-31 re-print if the sim's BUG-1045 fix moves pairing cells (the sim's report says which).

### Next task candidates

1. The owner's answers above, each a small page + `ai.json` / data task with a review table (golden regen once).
2. The rule-31 re-print after M6-S5 (if cells move); **BUG-1080 (S4):** two page wordings, folded into it.

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`.

---

## Watch-outs (all tracks)

- Both resumed branches are **behind nothing**: `origin/main` is 424a5e8, their base. The conductor merges sim then view; see the view's
  conflict note.
- Two tracks this session: two test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Bug ids next: **sim from 1047, view from 1061, data from 1081**.
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields); a third field lands this session.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase.
