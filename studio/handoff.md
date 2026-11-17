# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-15-2136** (session 3 / 8 of 2026-11-15, started 21:36; it
ran about 22 hours: QA full with two 4-hour Stress runs, two sim fix rounds, a slow PC; the ACCEPT in two calls on
2026-11-16, 16:05 and 19:40). Sim **M6-SH2** ACCEPT (hardening, 2 fix rounds, QA full FAIL → FAIL → PASS_WITH_ISSUES
after the Producer's rulings), view **M6-V23** ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES), data STOP at the
PLAN. **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 0 / 4, view 1 / 4, data
1 / 4**. Bug ids next session: **sim 1550+, view 1560+, data 1570+**. Open S1 / S2: none. The next session is
**1 / 8 of 2026-11-16** (or of 2026-11-17 after midnight).

**Next session in one line (proposal):** sim **M6-S12: the AI cures decided at this ACCEPT, the red Stress rows
first, one pin move** (feature, QA full) · view **M6-V24: merged surfaces + BUG-1530** (feature, QA standard) · data
**a cheap STOP** (rule 41: M6-S12 moves every opening measurement again), unless an owner answer is in the inbox;
**D33** the session after.

**The default loop on `main` is green.** Expect **0 failed, 21 skipped** (9,746 passed / 0 failed / 21 skipped of 9,767 in 46 m 55 s on the Producer's scratch clone
of sim b385866 + view 41cd358). Any red row is a finding.

**At the PLAN, first:** (1) the inbox (the owner may answer "gold gone: a / b / c", BUG-1536); (2) the default loop on
`main` is green; (3) **rule 68 for M6-S12:** sort the rows its AI changes will move into "re-pin", "the pages' cells
and prose pins" (handed to the sim developer by name while the data track is stopped: the same five classes as in
M6-SH2's brief) and "another track's"; (4) **keep the session short:** M6-SH2's Stress category takes about 4 hours on
this PC beside other suites. Tell QA to run Stress **once**, after the last behaviour change, and the developer to run
only the Stress classes it touches plus the pin classes.

## Current session plan (2026-11-16-2036, written at the PLAN; session 1 / 8 of 2026-11-16)

Inbox empty. Base 9e0ebb3, checked by the Producer on scratch clone `prod2036-plan`: build `--no-incremental`
0 errors / 0 warnings, default loop **9,746 passed / 0 failed / 21 skipped of 9,767 in 41 m 17 s**, smoke PASS. Sim **GO M6-S12** (feature, QA full) · view **GO M6-V24** (feature, QA standard) · data
**STOP** (cheap, rule 41: M6-S12 moves the AI under every opening measurement and re-prints the pages; D33 next
session). Bug ids: sim 1550+, view 1560+, data 1570+. All paths absolute, inside the track's own worktree; record
the PID of every `dotnet test` / Godot started and end only those (rules 52 / 69); scratch folders named
`m6s12-...` / `m6v24-...` (rule 63).

### Sim track: M6-S12, the AI cures decided on 2026-11-16 (one pin move)

Goal: build the three AI rules decided at the last ACCEPT (docs/01, 2026-11-16 row, item 3) and clear the Stress
rows that are red on `main` by ruling, so that after this task the only red Stress rows are the data track's two
`Bug0982_*`. Every AI match pin is re-measured once, after the last behaviour change.

Scope, in this order:
1. **BUG-1463, the five red `Stress/KeepCornerCureQaStressTests` rows at 0.5** (`KeepAgainstACliff` (28, 25),
   `TwoByTwoDepot_MineAtItsCorner` (22, 20) Gatherers, `KeepOnTheMapEdge` (1, 15), and the `HardShapes` rows
   cliff28_25 / depot22_20 / edge1_15). One bounded trace (at most about two hours of work): for each shape, what
   holds the worker (which unit or footprint, which give-up). If the trace gives one generic cure that frees all
   five and leaves `Stress/WideCrowdStressTests`' two columns and armies' separation unchanged, build it. Otherwise
   re-state each row as a known-bug row (`Skip = "BUG-1463: <one-line cause>"`, body and bound unchanged) and put the
   trace in docs/03. Either way none of the five is red afterwards. Do not repeat M6-SH2's reverted shove change.
2. **BUG-1461, the AI's rally point keeps off one-cell passages and their mouths.** In
   `MilitaryManager.RallyPoint`: when the nominal point (`ai.rallyDistance` from the hall toward the enemy) stands in
   or at the mouth of a passage too narrow for the parked army, take the nearest acceptable point stepping back
   toward the hall along the same line (the nominal point if none is acceptable). Define "acceptable" from the map's
   existing walkability / clearance data and existing `ai.*` knobs only; no new data field. If you think a number is
   needed, use the nearest existing knob and propose the new one under "Questions for the Producer". The point must
   stay stable (no flapping between thinks: `RallyVersion` rises only when the point really changes).
   `Movement/ParkedArmyGatherTests.Bug1461_*` become asserted rows on a staged AI seat, or stay report rows with the
   reason written beside them.
3. **BUG-1531, a rebalanced gatherer's node is measured from its drop-off.** When `EconomyManager` moves a gatherer
   from one resource to the other, it picks the known live node of the wanted kind nearest that worker's nearest
   finished drop-off (not nearest the worker). An idle worker's pick stays as it is unless your trace shows the same
   fault there (then say so and ask under Questions; do not change it silently). Un-skip `QA/ReachRuleQaTests` row
   (2, 80) with body and bound unchanged. Re-word "nearest the worker" in docs/02 and docs/03 where it describes a
   rebalance.
4. **BUG-1460, a blind seat scouts again.** A seat that is short of a resource its army mix needs and knows no live
   node of that resource sends one worker on the scout's sweep again, even past `ai.scoutReplacements`. At most one
   such scout alive at a time; reuse the existing scout timing knobs for when the next may go. Report Raraku Normal
   v Normal seed 1 after it (ends at which tick, or the probe's reason why not).
5. **The two other red Stress rows:** `StartingResourcesAiStressTests` (1, 0) (`crossbow_push` seed 9, BUG-1462) and
   `MapSlice2StressTests` Pale Hills 5 (BUG-1523). Run them after items 2-4. If green, done. If still red, one short
   trace each; a fix of a few lines may be built, otherwise `Skip = "BUG-...: <cause>"` with body and bound unchanged
   and the trace in the bug notes.
6. **The pin move, once, after the last behaviour change:** every class listed in the handoff under "Pinned matches on
   `main` after M6-SH2", old values kept in "was" comments; docs/03's M5-5 table and bound comments (rule 43); the
   Hard v Normal tally printed per map (BUG-1522: report only); a table of Raraku / Pale Hills / Vathar seeds 1-8,
   old → new end tick. Measure the effect of items 2, 3 and 4 apart where it is cheap (one criterion-row table per
   item on a scratch build), so the cause of each move is known.
7. **Handed to you by name while the data track is stopped (rules 40 / 68):** the re-printed cells on
   `docs/factions/malazan.md`, `whirlwind.md`, `teblor.md` and the rows of `Content/AiOpeningPairsReportTests`,
   `Content/HolyWarFirstWaveReportTests`, `QA/Content/DR35ProsePinQaTests`, `QA/Content/TeblorOpeningQaTests`,
   `QA/Content/D21OpeningsQaTests` that your re-measure moves. You may re-word a page sentence that a re-printed
   cell makes false: state what the cell now says and keep the old figure as history. Leave any conclusion about
   balance for D33.

OUT of scope: any byte under `game/data/**` (no data-hash move; the golden `cross_map_seed1.replay` must not change);
save and replay formats; BUG-1490; M8-2; Request 58; the `Bug0982_*` Stress rows, malazan.md's BUG-0982 threshold
table and `DR35ProsePinQaTests.Malazan_Bug0982Prose_*` (stay as they are: D33); BUG-1536 (the owner's question);
BUG-1537 / 1521 / 1116 / 1522 (report what moves, cure nothing); any Perf threshold; any movement rule that changes
how armies separate; `sim/Rts.Sim/ViewApi/**`; every file under `game/` (the view track's and the data track's).

Acceptance criteria:
1. None of the five `KeepCornerCureQaStressTests` rows is red: cured (green, assertion unchanged) or skipped with
   `BUG-1463` and a cause, with the per-shape trace in docs/03. If cured: `WideCrowdStressTests` prints both columns,
   unchanged within its run-to-run spread, and the fuzz rows (`FuzzSeeds57To120`, seeds 2-56) stay green.
2. Rally rule: a new test stages an AI seat whose nominal rally point lies in a one-cell passage mouth and shows the
   chosen point outside it, on the hall side, identical on two runs and unchanged across 200 thinks with nothing
   else changing; a second test shows a seat on open ground keeps today's exact point. Both fail or cannot compile
   on the base for the first, pass for the second.
3. On Raraku Normal v Normal seed 1, seat 0's rally point is no longer in the passage mouth (print old and new
   point), and the worker held from tick 5,131 is not held there (measure and report how long any worker of either
   seat is held in the first 12,000 ticks, before and after).
4. Rebalance rule: a new test (red on the base) stages a wood gatherer far from home with a mine near it and a mine
   near its drop-off and shows the rebalance picks the home mine; `ReachRuleQaTests` (2, 80) runs un-skipped and
   green with body and bound unchanged, and its other five rows stay green.
5. Re-scout rule: a new test (red on the base) stages a seat past `ai.scoutReplacements` that lacks wood, knows no
   live tree and has an idle worker, and shows exactly one worker sent on the sweep; a control seat that does know a
   live tree sends none; no second scout while the first lives.
6. Raraku seed 1: measure and report (end tick, or the probe's account of why it does not end). Not pass / fail.
7. `StartingResourcesAiStressTests` (1, 0) and `MapSlice2StressTests` Pale Hills 5 are green, or skipped with their
   bug id and a traced cause.
8. Every pin in the handoff's list re-measured once with "was" comments; no bound raised to make a row pass. A
   pinned criterion row that stops ending (rule 45): move that map's row to the next seed that ends by the
   criterion's own rule, add the seed to `MatchCriterionSweepQaStressTests.Known` with a new bug id, and report it
   as a finding at the top of your report.
9. Determinism: two runs and a save / load twin of a match that uses each new rule (the rally shift, a rebalance, a
   re-scout) give the same state hash at every checkpoint; the golden replay and the data hash
   (B74DC7AE61B4862D) are unchanged; `git diff --stat <base> -- game/data` is empty.
10. No allocation in per-tick code from the new rules (the existing allocation rows stay green; add one for the
    rally search if it runs on a think).
11. `dotnet build RtsGame.sln --no-incremental` 0 errors / 0 warnings after the last edit. The default loop
    (`Category!=Perf&Category!=Stress&Category!=Soak`) run in full after the last edit: 0 failed, skipped at most 20
    (21 today minus the un-skipped reach row; any other new skip is named with its bug id). The Stress classes you
    touched plus the Stress pin classes: red only where criterion 1 / 7 / 8 name it, and `Bug0982_*` x2. Do not run
    the whole Stress category (about 4 hours here); QA runs it once.
12. `powershell -File tools/qa/smoke.ps1` prints PASS.
13. Docs in the same commit: docs/02 (the AI's rally, rebalance and scouting sentences), docs/03 "Implementation
    (M6-S12)" with the traces, the old → new tables and a "Questions for the Producer" section (rule 67), the
    faction pages' moved cells. Bug-file updates as a patch in your scratchpad (you cannot write `studio/**`).

Design references: docs/01 change log, 2026-11-16 row, items 3 and 4; docs/02 "AI" (rally at `ai.rallyDistance`,
scouting and `ai.scoutReplacements` near lines 505 and 543, the rebalance); docs/03 "Implementation (M6-SH2)" items
D and E, "Implementation (M6-S7)" (the reach rule); `studio/bugs/BUG-1460`, `-1461`, `-1463`, `-1531`, `-1462`,
`-1523`.

Tests required: `Ai/` unit tests for the three rules (each red on the base), the Raraku seed 1 probe, determinism
twins, the un-skipped reach row, every re-measured pin class.

Constraints: no stat in C# (existing `ai.*` knobs only); the AI reads the world only through its fog-filtered view
(the re-scout must not read unseen nodes; the rally search may read terrain walkability only as far as the AI
already may); no `Dictionary` / `HashSet` iteration, no LINQ or closures on a think; `SimMath` for trig; no wall
clock. A test helper that forges data tolerates the shipped rows (rule 58).

QA focus (full): attack the rally rule on all three hand-made maps and generated maps of 2-4 seats (does any seat's
point flap, land in water / on a cliff / inside a building footprint, or end so near the hall that the parked army
blocks its own workers or drop-offs: measure held workers before and after on 8 seeds a map); armies that never
reach a shifted point (the `ai.rallyReach` "rallied" check); the rebalance with no finished drop-off, with the
drop-off razed mid-walk, with every home node mined out (falls back to the far node, never to none); the re-scout
as a drain (a camping enemy killing each scout: workers lost in 20 minutes, bounded?), with zero idle workers, in
team games, and that it never reads through fog; save / load mid-sweep; a fuzz of 8 seeds with conservation.
Verify each skip is honest (assertion unchanged, cause traced). Run the default loop on the commit holding your own
suites, then the Stress category **once**, after the developer's last behaviour change (expected red: `Bug0982_*`
x2 only), then Perf alone under rule 42 (BUG-1539's row is known flaky). End with a `--no-incremental` build
(rule 65).

### View track: M6-V24, cheaper figures (merged surfaces) and the cast gesture (BUG-1530)

Goal: every unit model draws as one surface with one material and one texture sheet instead of about five, which
docs/03 "Implementation (M6-V21)" estimates is worth 4-5 ms a frame at 1,000 figures. Nothing about how a figure
looks may change. With it, the Shaman's cast gesture starts at the start of the cast and plays once (BUG-1530).

Scope:
- `tools/art/build_unit.py`, `tools/import-assets.ps1`, `tools/asset-manifest.json` (the view track's): bake each
  model's role textures (outfit, skin, team, props) into one atlas and one material; keep which texels are team
  colour in a mask (for example the atlas alpha or a second small texture; your choice, say why). Re-import all
  eleven model types; the outputs are Git LFS.
- `game/shaders/team_colour.gdshader`, `game/scripts/UnitModels.cs`, `UnitViews.cs`: the one surface is drawn by the
  team shader, which tints masked texels only, at today's brightness; one material per model type and player colour
  as today; fog, hidden / remembered states and `--placeholder-art` behave as today.
- BUG-1530: a cast clip starts at phase 0 on the tick the unit starts casting and spans the cast once (speed =
  clip length / cast time, or no loop); other clips keep the crowd phase. If the cast's length is not readable from
  the view today, add a read-only accessor in `sim/Rts.Sim/ViewApi/` (it may not name `World` or `UnitStore`).
- Existing scenes that pin a surface count or a team surface index: update the row and list each in the report
  (the oracle must not get weaker).
- The bench, last and alone: 400 and 1,000 figures, with the capsule figure from the same window.
OUT of scope: a shared skeleton or baked animation; new model types (the Rider, the War-dog, buildings, the javelin
prop); any `game/data/**` file; anything under `sim/` except a read-only `ViewApi` accessor; Request 58; any scene
that pins an AI match's outcome (the sim's M6-S12 moves them this session); `tools/**` other than the three paths
above; installing or downloading anything (Blender 5.2.2 and `asset-sources/` in the main checkout are there: read
them, write nothing into the main checkout).

Acceptance criteria:
1. Every model type in `tools/asset-manifest.json` (count read from the file, not a literal) imports to exactly one
   mesh surface with one material; a scene row checks it for every type on the shipped files.
2. A second run of `tools/import-assets.ps1` gives byte-identical outputs (hashes of every `.glb` printed twice).
3. Looks the same: for each of the eleven types, a windowed picture before (the base) and after, same camera, in two
   player colours; you look at each pair and say so. A scene row samples rendered pixels (or the atlas and mask) and
   shows: a team texel takes the player colour, a non-team texel (skin, a prop) does not change between two player
   colours. No team colour on skin or weapons that did not have it before; none lost (the Heavy Infantry's shield).
4. Draw calls for the 400-model bench drop to at most half of the base's figure measured in the same window (today
   about 5,200); report both.
5. Frame time, measure and report (alone, rule 42, last): 400 and 1,000 mixed figures, average / while 90 %+ alive /
   worst 1 %, base v this branch in the same window, plus capsules. A 400-figure result more than 0.5 ms worse than
   the base in the same window is a finding to explain, not to hide.
6. BUG-1530: a scene row shows a casting figure's clip position is 0 (within one frame) when the cast starts and
   that the clip plays exactly once across casts of 0.5 s, 0.8 s and 1.2 s (forged cast times); a walking crowd
   still starts its clips at different phases.
7. `--placeholder-art` and an LFS pointer file still give capsules with no `ERROR` line; `ModelViewTest` still
   prints "11 models, 10 capsules".
8. If `ViewApi` changed: read-only, its unit test, the state hash of a 600-tick match identical with and without
   the accessor being called, `TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls` green.
9. `dotnet build RtsGame.sln --no-incremental` 0 / 0; the default loop 0 failed in full after the last edit; smoke
   PASS; the scene loop all green (113 + yours; the rows known to flake under load re-run alone).
10. Docs in the same commit: docs/03 "Implementation (M6-V24)" (the atlas layout, the mask, the bench tables,
    "Questions for the Producer"), docs/04 where it describes materials per model, and the size of the art pass's
    binaries before and after. Pictures copied for the owner are named in the report (the Producer files them).

Design references: docs/03 "Implementation (M6-V21)" (the 1,000-figure measurement and what merged surfaces are
worth), "Implementation (M6-V20)" (the team shader, `teamShade`), "Implementation (M6-V23)" (the cast clip);
docs/04 (the import pipeline, team colour); `studio/bugs/BUG-1530`; CLAUDE.md rule 7 (dumb views).

Tests required: a scene for criteria 1 / 3 / 7, a scene row for criterion 6, the existing model scenes green, the
bench output.

Constraints: views hold no gameplay state; no player-facing text in C#; scenes and resources stay text formats;
binaries only through Git LFS types already in `.gitattributes`; no new third-party asset (if one is added, the
licensing log in the same commit); do not kill another track's Godot or `testhost`.

QA focus (standard): compare before / after pictures yourself at the default zoom and zoomed in, in at least three
player colours including a dark and a very light one, and under fog (remembered) and at mip distance (does atlas
bleeding put team colour on a neighbouring texel, or a seam line on a figure when zoomed out?). Check the 1.5×
Teblor and the Heavy Infantry's shield. Re-run the import on a fresh clone for byte identity. Cast gesture at game
speeds 0.5× / 2×, paused, and at a replay's end; a cast interrupted by death or a Stop. The default loop, the
scene loop, smoke; run the bench and the timing rows last and alone, and say what else was running. End with a
`--no-incremental` build on the commit holding your own files.

### Data track: STOP (cheap)

No owner answer in the inbox; rule 41 (M6-S12 moves the AI under every opening measurement and re-prints cells on
all three faction pages, which are handed to the sim developer this session). Counter stays 1 / 4. D33 next session.

## Where things stand

- **`main` after this session = sim b385866 + view 41cd358** (plus this ACCEPT's studio and docs files). The
  Producer's scratch clones: `prod2136-merge` (sim 63a6a2a + view: build 0 / 0, default loop 9,744 / 4 failed / 19
  skipped, exactly the four rows then for ruling, smoke PASS, scene loop **113 of 113, first pass**) and
  `prod2136-final` (sim b385866 + view: build `--no-incremental` 0 / 0, default loop **9,746 passed / 0 failed / 21 skipped of 9,767 in 46 m 55 s**, smoke PASS).
  Round 2 changed tests and docs only (`git diff 63a6a2a b385866 -- sim/Rts.Sim game tools` is empty), so the scene
  loop was not run again.
- **No data byte moved.** Data hash **B74DC7AE61B4862D**; formats unchanged (save 10, replay 9 / 6); the golden
  `cross_map_seed1.replay` unchanged. Saves and recordings from the morning of 2026-11-15 on still load.
  `seed21_playable.replay` and the premap AI replay were re-recorded.
- **BUG-1520 (the owner's) is fixed:** `EconomySystem.InReachOfDropOff` / `NearestDropOff` skip `UnderConstruction`;
  `StartReturn` with none finished but an own site standing stands `Returning` with the cargo and re-checks every
  `RetryTicks`; a walker whose goal is beside a site re-picks (`WalksToSite`, over `World.DropOffSites`, a per-tick
  scratch list: +0.02 ms with 400 loaded walkers); `EndsForQueue` reads the finished rule. `FarExpansion.OwnDropOffNear`
  counts sites on purpose.
- **The AI:** `BuildOrderExecutor.Retrain` (no live worker and none queued: a Train of a worker type, first thing in
  the executor's think, also while defending and while stamped starved: BUG-1534's ruling);
  `EconomyManager.NoteDroppedRebalance` (BUG-1151: a rebalance Gather that was dropped marks the gone node). The hall
  rebuild already worked. **Not built: the wood rule** (Raraku seed 1's winner knows no tree; rule (1) was already
  firing, rule (2) had no node to act on).
- **Decided at this ACCEPT, not built (docs/01 2026-11-16 item 3; M6-S12):** a rebalance's pick measured from the
  worker's nearest finished drop-off (BUG-1531); a re-scout past `ai.scoutReplacements` for a seat that knows no node
  of a resource its army mix needs (BUG-1460); the rally point off one-cell passages (BUG-1461); the five
  `KeepCornerCureQaStressTests` rows cured or re-stated as known-bug rows (BUG-1463: no fourth red session).
- **Stress rows red on `main` by ruling (rule 57), 9:** `KeepCornerCureQaStressTests` x5 at 0.5 (`HardShapes`
  cliff28_25 / depot22_20 / edge1_15, `TwoByTwoDepot` (22, 20) Gatherers, `KeepOnTheMapEdge` (1, 15)),
  `StartingResourcesAiStressTests` (1, 0) (`crossbow_push` seed 9), `MapSlice2StressTests` Pale Hills 5 (BUG-1523):
  **the sim's M6-S12**; `Bug0982_*` x2: **the data's D33**. Perf alone: `Perf_UnitsConvergingOnOnePoint (500)` is flaky
  on its worst tick, on the base too (BUG-1539).
- **Skipped by ruling in the default loop (2):** `ReachRuleQaTests` (2, 80) (BUG-1531; M6-S12 un-skips it) and
  `DR35ProsePinQaTests.Malazan_Bug0982Prose_*` (BUG-1524 item 3; D33 re-measures malazan.md's threshold table and
  un-skips it). `MatchCriterionSweepQaStressTests.Known`: Raraku 1 (BUG-1460), Raraku 2 (BUG-1521), Vathar 2
  (BUG-1537), plus the older entries; a `Known` entry excuses its row from every oracle (QA's note).
- **The view (M6-V23):** `ViewApi/OrderPath.QueuesBehind` is true on a gather loop; every unit order goes through
  `SelectionController.SendUnitOrder`, which counts queued orders not yet applied (`PendingQueuedOrders`: two batches,
  since a command applies in the second tick after it is sent); `OrderPathView.AddRing` nests rings per point across
  the selection; `UnitModels.Clip.Cast` = `attack_cast`, `idle` when a library lacks it. **113 scenes**
  (`CastClipTest`, QA's `QaM6V23Test`). BUG-1530 S4: the cast gesture starts at the crowd phase and would loop.
- **Requests:** 59 done. 58 open (dropped queued orders for the view; small).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's
  list). The M6-SH2 developer wrote one file into the owner's checkout by a relative path and reverted it at once
  (conductor confirmed the checkout unchanged): briefs keep saying "absolute paths inside your worktree only".

## Standing rules (1-70)


Unchanged 1-51 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle **and with no
non-studio load on the machine** (QA says what else was running); 43: every sim task that can change Normal AI behaviour names which
M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table in the same commit, and a row that stops
ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on a scratch clone and integrated first, a new
task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says what to do when a pinned criterion row stops
ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so no branch lives a fourth session; 47: QA's
re-check runs the default loop on the commit that includes QA's own suites, and a QA file that measures allocation declares
`[Collection(SerialCollection.Name)]`; 48: at PLAN the Producer checks every acceptance criterion against the brief's own out-of-scope
list; a figure the scope cannot move is "measure and report", never pass / fail; 49: no test pins the golden replay's header
`data-hash` as a literal (`GatherWedgeQaTests.SameGameDataHashes` is the model); 50: a sim task that changes a save or replay format
names the view scenes that pin those bytes and the view track re-checks them; 51: a data brief that re-pins sim-owned digests lists
the sim test folders and pin classes and says the `Stress` pin classes must be run; 52: a developer's report ends every test process
the developer started; a `testhost` or Godot left running in a worktree after the report is a finding; 53: the QA tier bounds the
suites QA runs: **light** = the task's filters + the default loop (`Category!=Perf&Category!=Stress&Category!=Soak`), never the full
suite; **standard** = the default loop + the Stress rows the task touches + the scene loop (view); **full** = the default loop with
Stress, then Perf alone under rule 42).
**54:** before a task adds a faction, a data type or a default that every test reads, the Producer applies it on a scratch clone
and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that. **55:** that rehearsal uses a
copy that exercises every hook the new content will use (a passive, a scale, a pack, an aura, a new radius), or the content
branch itself when it exists; a Malazan copy alone missed 28 rows this session.
**56:** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57:** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58:** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59:** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61:** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62:** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63:** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64:** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
**65 (new):** QA's re-check ends with `dotnet build RtsGame.sln --no-incremental` on the commit that holds QA's own
files, and its report says so (2026-11-14-1246: QA's new `QaM6V21Test` carried a CS8600 warning that the developer's
0 / 0 could not have seen: BUG-1412). A warning in a QA file with the developer's commit clean is S3, fixed first in
that track's next task; in the developer's own files it stays S2 (BUG-1380).
**61 (amended 2026-11-14-1246):** the measurement is done (M6-V21: 1,000 mixed figures 26.9 ms alone; merged surfaces
are worth about 4-5 ms, a shared skeleton or baked animation up to about 8 ms, both together about 14-15 ms). The owner
asked for the Teblor figures first (inbox 2026-11-14), so the checkpoint moves: **merged surfaces are the view task
right after the Teblor humanoids**, and every art brief still reports the 400- and 1,000-figure bench alone.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)
**66 (new, 2026-11-14-2156):** at PLAN every invariant criterion ("no worker ...", "every pair ...", "within N s") is
checked two ways before it is written as pass / fail: **does it hold today with the feature off** (if not, or if the
out-of-scope list forbids what would make it hold, it is written "measure and report, against off": rule 48), and
**can it hold together with every other criterion** (M6-S9: criterion 5's Stop clause against criterion 4, criterion
7's 60 s window against "off is identical"; both cost a fix round and an ACCEPT by ruling). A criterion QA shows to be
the brief's error is corrected in the session log by name, never passed silently.
**67 (new):** a developer's open questions for the Producer are written where QA can read them (a "Questions for the
Producer" section of the page or docs section the task owns, each with the recommendation); the Producer's answers
go to docs/01 and the next task writes them in (D-R38: Q1-Q11 lived only in the returned report).
**61 (amended again 2026-11-14-2156):** the order after the Teblor humanoids is the owner's Shift-queue request, the
view's hardening (4 / 4), then merged surfaces; 400 mixed figures of eleven types are at 11.1-11.4 ms alone (worst 1 % 15.4-15.7).
**68 (new, 2026-11-15-0506):** a brief never writes "the default loop green" beside rows it forbids the developer to
fix. When a task re-prints cells that another track's prose pins read, the brief hands those pins and sentences over
by name, or writes the criterion as "red only in these named rows, ruled at ACCEPT". At PLAN the rehearsal's red list
is sorted into "re-pin", "re-stage" and "another track's" before the criteria are written (M6-S10: criterion 14
against 10 and 12; five default-loop rows reached `main` red by ruling). A fix round with nothing to act on but a
Producer ruling may be skipped by the conductor; the session log says so.
**69 (new, 2026-11-15-1536):** a developer or QA agent ends only the processes it started itself, by the process ids it
recorded when it started them; never by matching command lines or names (M6-S11's developer force-stopped 34 processes
whose command line matched "Total tests" / "Passed!|Failed!", some possibly another track's; nothing was lost, by luck).
A brief that runs long suites says "record the PID of every `dotnet test` / Godot you start".
**70 (new, 2026-11-15-2136):** when a QA FAIL leaves nothing a developer can act on without a Producer ruling, the
conductor calls the Producer, who answers with rulings and a fix brief instead of a verdict; the last fix round applies
them (tests and docs only when possible, so no Stress re-run is needed). `main` takes no red default-loop row by ruling:
a row the ruling cannot make green is skipped with its bug id in the repo's `Skip = "BUG-..."` form, its cure first in
the next brief. An owner bug that changes gather or AI behaviour moves every AI pin through divergence (BUG-1520 did,
alone): plan it only where a pin move is planned, and tell the developer to measure its effect apart from the rest.



## Watch-outs (all tracks)
- **The default loop is green on `main`** (21 skipped). A "default loop with Stress" run expects the 9 Stress rows
  listed under "Where things stand" red, until M6-S12 and D33 land. `main` builds **0 errors / 0 warnings**.
- **New this session: a construction site is not a drop-off.** A test or scene that places a drop-off building and
  expects deliveries there must finish it first; a loaded worker with only a site standing waits in state `Returning`.
  The AI retrains a worker when it has none: a test that keeps a seat worker-less must also keep it without the gold
  (`HuntTests.TheRingLeg_*` is the model).
- **Session length:** 2136 ran about 22 hours (the Stress category 3.8 h and 4.0 h beside the default loop; the
  default loop 43 m to 2.5 h under load). M6-S12 is QA full with a pin move again: see "At the PLAN" item 4.
- **New this session: a gather loop ends for a queue** (`EconomySystem.EndsForQueue`, only when `QueueCount > 0`). A
  test that parks a worker on a loop with a queued order must put it in reach of its node with a drop-off standing
  (`ViewApi/OrderPathTests.QueuesBehind_*` is the model), or the queue starts at once. The AI never queues.
- **`EndGameHardeningTests.PaleHillsSeed2_FourSeats_*_TwoProcesses_*` has a 1,200 s wall limit** and failed under a
  contended Stress run (3 m 11 s alone): a Stress red there counts only when it fails alone.
- **Scratch clones left behind (permission prompt denied):** `qa1536\m6vh8-qa-clone` and `m6s11-qa-clone` in session
  e9f70028's scratchpad; harmless, on the owner's housekeeping list.
- **`gatherOverlap` 0.5 is in the shipped `rules.json`.** A test that means "the rule off" removes the field from a
  copy (`GatherOverlapScenes` writes or removes it itself; rule 58); "the base's data" in a fixture means the field
  taken out (`D32PreFixtureTests`). Any value above 0 turns the gather floor on.
- **The data hash moved (B74DC7AE61B4862D):** every save and recording fixture from before is refused. The next
  hash move is the data's (D-R39 only if a number must move, else D33) or the sim's hardening: never both in one
  session.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 113 scenes** (`CastClipTest`, QA's `QaM6V23Test` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Eleven unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer; the
  Teblor Gatherer, Warrior, Javelin Thrower, Shaman, Breaker); ten are capsules (`ModelViewTest` prints "11 models,
  10 capsules; teblor capsules: [teblor_destrier_rider teblor_war_dog]"). A count of model types is read from
  `tools/asset-manifest.json`, never a literal (`QaM6V21Test` row 1). A model file holds the 1.84 m base figure; the
  data `scale` is applied by the view. Bars and marks sit over the figure as drawn since M6-VH8 (`UnitViews.OverlayLines`).
- **Session length:** 2026-11-14-2156 ran about 7 hours (builders and QA about 6; one sim fix round; the lock was
  refreshed four times). QA full on the sim beside a standard and a light pass still gave default loops of 1 h 4 m
  to 1 h 15 m against 22 m alone. Next session the sim is on QA full again with pin re-measures: keep the data task
  light and tell the view's QA to run its bench last.
- **The third faction's order of work now:** M6-S10 (the wedge + the gather value; every opening measurement is
  redone after it) -> data **D33** (BUG-1420) as the start of the balance pass -> the sim reads the Frenzy report
  again (Request 53 part two). The Teblor humanoids are done; the Rider and the dog wait on the owner's packs. M7
  criterion 4 is ticked after the balance pass; the end-of-M7 hardening follows it.
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 111 scenes** (`OverlayLineTest` and QA's `QaM6VH8Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **The Teblor rows are on `main`.** A Frenzy status id sorts after `stealthed`, and `teblor_frenzied` is the last row
  of `common/statuses.json`: a brief that adds a status says where it sorts (`StatusLoaderTests.Shipped_Regenerating_*`
  pins the sorted order on purpose). The two golden data-hash pins
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
  are sim-owned: a data brief that moves the hash is handed them by name (rule 51).
- **Two tracks must not both move the data hash in one session** (each regenerates the golden's header and checksum:
  a certain conflict). A sim task that ships a value in `game/data/common/**` and a data task that ships a faction
  file are planned in different sessions, or one of them lands test data only.
- **Every data change moves the golden header now** (the Teblor use `damageBonus`, `onEnd`, `statusDuration`, a
  building `scale`, `carry` and a per-opening share): batch data changes into one hash move a session, and say in the
  report that older saves and recordings stop loading.
- **`QaM7V3aTest` fails on purpose at six shipped factions** ("no room for three clones under 8"). `ThreeFactionTest` and
  `RegenCueTest` never show the real Teblor (the forge cuts them away): M7-V3c adds a scene that does.
- **`tools/qa/third-faction-copy.ps1`'s display name "<id> (Malazan copy)" trips BUG-1230** at 1152 x 648 with five
  factions (5 setup scenes red from name width): use short ids or expect them.
- **A QA scratch scene left a data copy** at `%APPDATA%\Godot\app_userdata\RtsGame\qa-teblor-shot\` (could not be
  deleted: permissions). Harmless. Scratch scenes should write under the scratchpad, not `user://`.
- **A dead session loses the developers' reports and bug patches** (they live in the conversation / scratchpad). When a session
  is resumed, QA writes the proof lines and the Producer applies them at ACCEPT.
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rules 42 / 53). The default loop is ~25-30 min alone,
  ~45-90 min beside other suites. **BUG-1170 is closed as machine state:** a Perf run is evidence only when `ASweepRetarget`
  reads near 16 us (85 us = the PC's slow state; do not chase it).
- Scene rows that flake under load and pass alone: `DebugOverlayTest` and `QaM25Test` (1 ms rows), `MinimapTest` (0.5 ms),
  `BenchTest` (7 s), `QaH1Test` (600 s timeout).
- Merge conflicts to expect: `studio/bugs/README.md`, `studio/qa/coverage.md` (both sides kept), docs/03 when two tracks
  append an "Implementation" section (keep both, the sim's first).
- A developer must never kill another track's Godot or `testhost`; a developer ends its own (rule 52). **The developer cannot
  write `studio/**`:** bug-file updates come as a patch in the scratchpad; the Producer applies it.
- `tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` makes a Malazan copy; a Whirlwind-shaped copy is made
  by hand (`Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy` is the recipe); a Teblor-shaped one is the `teblor`
  folder under another id (QA's `barghast`).

- **Pinned matches on `main` after M6-SH2 (every one re-measured once, old values in comments; docs/03
  "Implementation (M6-SH2)" item E lists old → new):** the Hard v Normal tally **28 / 10 / 4** (Raraku 13 / 1 / 0,
  Vathar 11 / 3 / 0, Pale Hills 4 / 6 / 4: BUG-1522); `MatchCriterionTests` (Vathar seed 7 19,912 / Raraku **seed 3**
  25,083 / Pale Hills seed 1 27,498 / generated seed 2 22,280 / FFA 39,102 / 2v2 44,858; `HalfWay` 9,956);
  `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`,
  `ReserveTwinTests`, `SharedVisionFfaPinTests`, `HardKnobsPinTests`, `OutcomePinM73bQaTests`, the premap AI replay,
  `seed21_playable.replay` (7,754 ticks; `Seed21ResearchTests`), `CliTests.Match_TheLiteralRow_*`; re-staged:
  `AiDeterminismTests` (`HuntSeed` 2, waves seed 2), `HuntStressTests` (`MidHuntSeed` 2, the four-seat FFA row seed 1),
  `StarvedSaveTwoProcessQaStressTests` (Raraku seed 1), `ReservePairingsStressTests` (`KnownNoAgeTwo`, `NoWaveSides`
  2). The next sim task that changes Normal AI behaviour or gather movement re-measures all of them again (rules
  43 / 45): M6-S12 does, once.
- **Known stalls / skips with ids on `main`:** Raraku Normal v Normal seed 1 (BUG-1460), seed 2 late (BUG-1521),
  Vathar 2 late (BUG-1537), Pale Hills 5 no result (BUG-1523), the four-seat Pale Hills FFA seeds 3 and 4 (BUG-1536:
  the map's gold is gone by 30,000), Pale Hills FFA seed 1 (BUG-1068 / 1116), `EndGameHardeningTests.ALoneIdleWorker_*`
  (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150),
  `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's three rows,
  `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the
  view's `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.

---

## Sim track

### Where we are
M6-SH2 (the hardening) on `main`. Counter **0 / 4.** Fixed: BUG-1520 (the owner's), BUG-1465, BUG-1151, BUG-1440
(Request 59), BUG-1464 item 1; in-session BUG-1532 / 1533 / 1535 / 1538; BUG-1534 closed by ruling. Open and first:
BUG-1463 / 1461 (five Stress rows red a third session), BUG-1531 (row skipped), BUG-1460 (Raraku 1). New debt:
BUG-1521, 1523, 1536, 1537, 1522 (balance), 1539 (a flaky Perf row). Older: BUG-1490 (with the next save change),
BUG-1434, 1433, 1161, 1149, 1152, 1150, 1010. Criteria of M6-SH2 the Producer ruled: 2 was the brief's error (the
rebuild already worked), 4 not built for cause, 5 not met (carried).

### Next task candidates
1. **M6-S12: the AI cures decided at the 2136 ACCEPT (feature, QA full; one pin move; rules 43 / 45 / 57 / 68).**
   In this order:
   - **The five `KeepCornerCureQaStressTests` rows** (rule 57: first). One bounded trace of the synthetic shapes
     (they are not Raraku's passage: say what holds the worker in each). Cure generically if the trace gives a cure
     that frees all five without moving armies' separation; otherwise re-state each as a known-bug row
     (`Skip = "BUG-1463: ..."`, the assertion unchanged) with the trace in the bug file. Either way they are not red
     after this task.
   - **BUG-1461:** the AI's rally point keeps off one-cell passages and their mouths (an AI rule, docs/02's rally;
     existing knobs only; if a number is needed, propose the knob under "Questions for the Producer").
     `Movement/ParkedArmyGatherTests.Bug1461_*` become asserted rows on a staged AI seat, or stay report rows with
     the reason.
   - **BUG-1531:** `EconomyManager`'s rebalance picks the known node of the wanted kind nearest the worker's nearest
     finished drop-off (idle workers' picks unchanged unless the trace says they share the fault); un-skip
     `ReachRuleQaTests` (2, 80); docs/02 / docs/03 "nearest the worker" re-worded.
   - **BUG-1460:** a seat short of a resource its army mix needs that knows no live node of it sends one idle worker
     on the scout's sweep again, past `ai.scoutReplacements`; Raraku seed 1 ends or its probe says why not.
   - As the budget allows, each traced first: BUG-1537 / 1521 (a seat with soldiers and gold reserved for a hall
     sends nothing for minutes), BUG-1523 with BUG-1116 (the hunt of a last unit), `StartingResourcesAi` (1, 0).
   - Every match pin re-measured once after the last behaviour change; the Hard v Normal tally printed per map
     (BUG-1522: measure and report). Raraku / Pale Hills / Vathar seeds 1-8 table old → new.
   - Handed by name while the data track is stopped: the faction pages' re-printed cells and
     `Content/AiOpeningPairsReportTests`, `Content/HolyWarFirstWaveReportTests`, `QA/Content/DR35ProsePinQaTests`,
     `QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`. **This time the developer may re-word a
     sentence a re-print makes false** (state what the cell says, keep the old figure as history, build outcome
     clauses from the row as `TheOpponentSentence_*` now does); only a *conclusion* about balance is left for D33.
   - OUT: `game/data/**` (no hash move), save / replay formats, BUG-1490, M8-2, Request 58, `Bug0982_*` and
     malazan.md's BUG-0982 threshold table (D33's), BUG-1536 (the owner's question), any Perf threshold.
   - Criteria written under rule 66 / 68: the default loop 0 failed with no exception list; Stress red only in
     `Bug0982_*` x2.
2. **M8-2: the Shadow Archer's stand-still stealth** (Request 56 G1; schema first, test data only; QA full). If it
   changes the save format, BUG-1490's flag rides with it.
3. **Request 58** (dropped queued orders for the view; small).
4. Requests 54 / 55, 53 part two, 56 G3-G8.

---

## View track

### Where we are
M6-V23 on `main`. Counter **1 / 4.** 113 scenes. Fixed: BUG-1500, BUG-1501, BUG-1441 item 4. New: BUG-1530 S4. The
owner's Shift-queue request is complete. 400 figures 9.3 ms a frame alone (worst 1 % 13.95 ms).

### Next task candidates
1. **M6-V24, merged surfaces (rule 61; feature, QA standard):** one material and one atlas a model; the 400- and
   1,000-figure bench alone, last, with the capsule figure from the same window; the import's outputs byte-checked
   for the models it does not touch. With it **BUG-1530** (the cast gesture starts at phase 0 when the cast starts
   and does not loop within a cast). `tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**` are
   the view's. The sim's M6-S12 changes AI matches: no new scene pins an AI match's outcome.
2. The view's half of Request 58 once the sim exposes it (the Refused sound + the `placement` text over the unit).
3. The Destrier Rider and the War-dog once a horse / dog pack is on disk (owner); Teblor buildings; the javelin prop.
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D-R39 on `main`; stopped this session. Counter **1 / 4**. The sim's M6-SH2 re-printed cells on all three pages and
re-worded three sentences by the Producer's ruling (BUG-1524). Open: BUG-1420 S3, BUG-1524 items 3-4, BUG-1522
(balance), BUG-1421 S4 (items 1, 2, 5), BUG-1510 S4.

### Next task candidates
1. **Next session: STOP (cheap), unless the inbox holds an owner answer** (rule 41: M6-S12 moves the AI under every
   opening measurement and re-prints the pages).
2. **D33 (the session after M6-S12 lands; QA standard; one hash move):** first **BUG-1524 item 3** (re-measure
   malazan.md's BUG-0982 threshold table on the landed sim, rewrite its prose, un-skip
   `DR35ProsePinQaTests.Malazan_Bug0982Prose_*`) with the two `Bug0982_*` Stress rows (settle 14 v 18 pop, or
   re-state the rule), item 4 (a stale comment in `AiOpeningPairsReportTests`); then BUG-1420, `holy_war`'s 20-pop
   wave (it no longer falls on any of nine seeds), `KnownBeatenAlive` seed 19 with its `DR33QaTests` pin, **BUG-1522**
   (Hard on Pale Hills: re-tune Hard's knobs if the tally after M6-S12 still trails), BUG-1510 item 1, BUG-1421
   items 1, 2, 5. The golden's header and the two sim-owned data-hash pins handed over by name (rule 51).
3. The Shadow roster as Request 56's schemas reach `main`; the "Orders full" text once Request 58's key exists.
