# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-11-07-0236** (session 1 / 8 on 2026-11-07; base `af7047b` = `origin/main` after the
0926 merge with its three merge-time actions, verified: the BUG-1108 patch is on `main` (the three scenes compare to
`SaveFormat.HuntMaplessVersion` / `CurrentVersion`), the Malazan paragraph reads "Until M5-4d ... Since M5-4d ...", no conflict markers).
Producer's checks on a scratch clone of `af7047b` (rule 38): build **0 warnings / 0 errors**, smoke **PASS**, the non-Perf suite in the
PLAN output. **Open S1 / S2: none.** Counters before this session: **sim 2 / 4 (feature), view 0 / 4, data 0 / 4.** Bug ids:
**sim from 1116, view from 1126, data from 1136.** Save formats **6 / 7** for a match with an AI seat. Inbox **New: empty** (no veto on
D25's two sentences: they ship with the sim's rider).

## Standing rules (1-40)

Unchanged (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content and QA's
suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit `docs/factions/**` for
those cells only, says so in its report, and never edits the surrounding prose; the data track owns the test allowances).

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only
  when it fails alone.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- **Two tracks touch the same two files this session, in disjoint regions:** `sim/Rts.Sim.Tests/Content/MapTextContentTests.cs` (sim:
  the two description strings in `Pinned`, lines ~23-40; data: the method `ThePin_NamesEveryShippedMap_RarakuFirst`, lines ~70-76) and
  `docs/factions/malazan.md` (sim: table cells only, rule 40; data: the last sentence of the "Until M5-4d ..." paragraph, lines 546-551).
  Neither edits the other's region. If git still conflicts, the conductor keeps both sides.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase. Plan the sim's slice small enough for one fix
  round (a sim fix round costs ~45 minutes of suite).
- `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)` ends at **35,569** (431 ticks of margin; deterministic).
  Any change to the hunt's sweep order, a rebuild or the high-ground look can move it; see the sim brief.

---

## Sim track

### Current session plan: M5-4d slice 2, "finish the match": rebuilding + the high-ground look before attacking up; riders D25's text landing and BUG-1111 · feature (counter 2 / 4 → 3 / 4) · QA full

**Goal.** Roadmap M5 criterion 3's last two items: the AI **rebuilds** what it lost (a razed Town Hall while a worker lives; a build-order
building that was finished and then destroyed) and **looks before it attacks uphill** (a wave whose target stands on higher ground than
its members goes first to a point from which the high ground is seen, then to the target, instead of filing blind up a ramp). Uncertain
design: one slice, **~800 hand-written lines** (QA's suites and generated content excluded); plan only what fits, and say what was left.

**Scope.**
- **Rebuilding, build-order steps** (`BuildOrderExecutor` / `Blackboard`): at each think, for every `Build` step marked `StepDone` whose
  type the seat now holds fewer of (`AiPlayer.OwnBuildingTypes` / `OwnBuildingCount`, sites included: the census the fog grants of the
  seat's own buildings) than the number of done `Build` steps of that type, the newest such done step becomes due again (`StepDone`
  false, `StepQueued` back, `StepDueTick` = now; no slack; the usual site search, reserve rule and `NoSiteRetryThinks` apply). A house
  step at the hard cap stays done (D15 C). An `Expand` step is out of scope (an expansion lost is not rebuilt this slice: say so in docs/03).
- **Rebuilding the hall** (`BuildOrderExecutor` or `AiPlayer`, one rule): with no Town Hall (finished or a site: `TownHallCell` < 0) and
  at least one worker, a `Build` of `TownHallType` comes first, before any step: at the old hall's anchor if `CanPlace`, else by the
  usual site search round that point. Where the old anchor comes from: prefer a point already saved (the `Rally`, set from the hall,
  or the nearest own building / the workers' centroid); a **new saved Blackboard field** is allowed only if nothing saved serves, and
  then it goes into `AIST` with a **save-format bump (8 / 9)**, `SaveHeader.VersionFor`, the refusal rows and the fixtures updated,
  said in the report. Gathering goes on after the hall is up (the economy manager picks the new drop-off as it does any).
- **The high-ground look** (`MilitaryManager`, where a wave's target is set, line ~524, and each wave think): when the target
  building's cell level (`Heightmap` / `NavGrid.LevelAt`: terrain is static, every player knows it; add a read-only `PlayerView`
  accessor if the AI has none) is higher than the members' centroid cell level, the wave's first point is a **look point**: the
  nearest cell to the target that is at the target's level, reachable, and within `ai.scoutGrid`-independent sight distance of the
  target's cell (use the wave's own members' sight: `unitSight` of the members' type, or the nearest ramp top: the ramp cell
  (`NavFlags.Ramp`) nearest the target whose up-neighbour is at the target's level). One rule, pick the one that is simplest to prove on
  Pale Hills' knolls and Vathar's banks; the point is `AttackMove`d to as a hunt point is (`WaveTargetPoint`), the target building
  becomes the point once a member is within `ai.rallyReach` of the look point or the target's cell is visible to the seat (the fog
  shows it); on a flat map or a same-level target nothing changes. No new data field unless a number is needed (then `rules.json`
  `ai.*`, hashed, validated, minimum entries, `DataValidationTests` rows).
- **Rider, D25's text landing (named exception, Producer ruling 2026-11-06-0926; no owner veto in the inbox):** the final sentences
  below go into `game/data/maps/pale_hills.json` / `vathar_crossing.json` `description`, the sources
  `tools/Rts.Cli/maps/pale_hills.quad.json` / `vathar_crossing.quad.json` (the `description` line), `MapFileTests.TheNewMaps_Load_WithTheirFields`
  and **`sim/Rts.Sim.Tests/Content/MapTextContentTests.Pinned`** (data-owned; this one edit allowed: only the two description strings,
  nothing else in the file; say so in the report), **one commit**; `DataContentHashTests` re-pinned once; no golden regen (decoupled).
  - Pale Hills: "Camps in hill woods, linked by paths through rows of trees. A hill stands between each camp and the open center. Timber
    is everywhere, gold scarce beyond the camps; every path invites an ambush."
  - Vathar Crossing: "A sunken riverbed cuts the steppe in two, its steep banks crossed only at a few fords. Gold waits on the banks
    beside them, and troops down in the bed cannot see up onto either side."
- **Rider, BUG-1111 (S3) only if a few lines:** on resuming a leg after a sighting, go back to the ring point *before* `HuntRing` (the
  leg's last point reached) so the cut edge stretch is walked; un-skip QA's
  `HuntSweepRound2QaStressTests.ARingLeg_InterruptedBySightings_StillSeesTheWholeBorder(-1)`. Then re-measure the Pale Hills seed-1 row.
- **The Pale Hills seed-1 row** (`MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks`, 35,569 today): if any change here
  moves it past 36,000, raise that row's bound to 72,000 (the stress rows' budget), re-pin the number in docs/03's M6-S8 match table and
  say why; a row that no longer ends is a finding, not a bound change.
- **OUT:** the Hard knobs (M5-4c slice 2), expansion changes, periodic re-scouting, splitting the army to sweep, any view change,
  `game/data/factions/**`, prose on the faction pages (table cells only, rule 40), the data track's test allowances.

**Acceptance criteria.**
1. `dotnet build` 0 warnings; non-Perf green; Perf alone green; smoke PASS; the loop's three save-format scenes (`QaM6V18Test`,
   `QaM6V18bTest`, `StartingResourcesTest`) green on the branch.
2. Built-scene tests (`Ai/RebuildTests` or in `BuildOrderExecutorTests`), by the command log: (a) an AI seat whose finished Barracks
   (or any done `Build` step's building) is razed re-issues a `Build` of that type within a few thinks and the step is done again once
   accepted; (b) a seat whose hall is razed with workers alive issues a `Build` of the hall first, at the old anchor when free, else
   nearby, and its workers gather again after it finishes; (c) a razed house at the hard cap is not rebuilt (D15 C); (d) the rebuild
   obeys the reserve rule (a reserved step is not starved by it, or the rebuild is the reserved step: say which).
3. Built-scene tests on a two-level map (a generated map with a plateau, or Pale Hills / Vathar Crossing): a wave whose target is on the
   upper level is first `AttackMove`d to the look point (at the target's level or the ramp top, within the members' sight of the target),
   then to the target; the look point is not used when the target is at the members' level; on a flat map every pinned match is
   unmoved unless a rebuild occurs (then regenerate in the same commit and say which rows and why).
4. Fog: every rebuild site and look point is derived from `PlayerView` (own buildings, own workers, static terrain, the seat's own fog
   for "the target's cell is visible") only; the fog oracle row (QA's `PlayerViewOracle`) over an AI v AI match that rebuilds and one
   that looks before attacking up: 0 leaks.
5. Save / load mid-rebuild (a due-again step pending) and mid-look (the look point as `WaveTargetPoint`): hash-equal for 1,000 ticks;
   any new Blackboard field saved (format bump only if the layout changes, said in the report); the replay format unchanged.
6. Determinism row (two processes) on a match that rebuilds (a Pale Hills or generated seed where a hall is razed and the loser lives
   on: QA's choice if the dev finds none).
7. D25's text: the four files changed in one commit, `MapTextContentTests` green (pin = shipped), `DataContentHashTests` re-pinned,
   `tools/Rts.Cli mirror-map` on each source reproduces the shipped map byte for byte (the existing mirror row).
8. Think gate 1 ms and 0 B a think hold (`AiPerfTests`); the rebuild scan is one pass over the steps per think.
9. docs/02 "AI opponent": one bullet each for rebuilding and the look; docs/03 "Implementation (M5-4d slice 2)": the rules, the
   measurements (the seed-1 row, any pinned match moved), the save-format note, what is left (expansion rebuild, re-scouting); the
   roadmap criterion 3 text is the Producer's at ACCEPT.

**Design references:** docs/02 "AI opponent" (the behaviours in order: "... send attack waves → rebuild losses"), docs/02 "High ground"
("the AI scouts ramps before attacking up them"), docs/03 "Implementation (M5-2)" (the executor, `StepDone` / `StepQueued` /
`StepDueTick`), "(M5-3)" (waves, `WaveTargetPoint`, `ai.rallyReach`), "(M5-4d)" (the hunt: "Not yet" lists this slice), "(M5-4g)" (the
reserve), "(M6-S8)" (ramps and levels on the maps), roadmap M5 criterion 3.

**Tests required:** the rows of criteria 2-3 and 5-7 as dev tests; the oracle, determinism and perf rows (4, 6, 8) may be QA's but must
exist on the branch at ACCEPT; `DataValidationTests` rows for any new knob.

**Constraints:** no Godot in `Rts.Sim`; no `Dictionary` iteration; no allocation per think (no LINQ, no closures); knobs in data; the AI
reads only `PlayerView`; no wall clock; the seat's own fog decides "visible"; docs in the same commit; the faction pages' prose untouched.

### Next (sim)
- M5-4c slice 2 (the Hard knobs: `gatherBonus`, `focusFire`, `pullBackWounded`; M5 criterion 4), then **the sim hardening at 4 / 4**
  (BUG-1058, BUG-1059, BUG-1044's rest, BUG-1067, BUG-1068's seed-2 stalemate, the movement S3s BUG-0911 / 0889 / 0144, the `HuntRing`
  load range, `HasMapId`'s special case), then BUG-1010's lever, D14 c's schema (M5 criterion 9; the data track waits on it), M5-5's
  20-minute test.

---

## View track

### Current session plan: M6-VH6, a short hardening (the view's only unblocked work) · hardening (counter 0 / 4, stays 0 / 4) · QA standard

Every M6 view feature left needs the owner (the art call, music / fonts / export templates, the .NET 10 SDK: all in STATE "Waiting on
you"; the inbox is empty), so the view spends this session on its debt and on verifying `main` after the integration patch. Cheap
(~300 lines); say what was not reached.
1. **The loop on `main`** after the BUG-1108 patch (`tools/qa/scene-loop.ps1`, 84 scenes): green once on the base; a wall-clock flake
   reruns alone and is named in the report.
2. **BUG-1114 (S3):** `SkirmishSetup.AddSlot` raises `_slotsBeforeMap` to `SlotCount` when it is set (or clears it, as `RemoveSlot`
   does: pick the one whose behaviour docs/03's M6-V18 / VH5 sentence describes, and keep that sentence true); turn
   `QaVh5Test.SlotGiveBack`'s `KNOWN BUG-1114` line into a `Check`; a `ShellTest` or `MapListTest` row for the hand sequence in the bug.
3. **BUG-1115 (S4) items 1-3:** the 137-character docs/03 line (6820) wrapped; one px figure (keep the measured ~470 px in both the code
   summary and docs/03); `TooltipWrap.Wrap` drops the empty first / last line a leading or trailing space would leave (text otherwise
   unchanged; a `TooltipWrapTest` row for each). **Item 4** (an opaque tooltip panel in the shell theme) only if it is a one-resource
   change in `game/ui` with a screenshot looked at; else leave it for the art pass and say so in the bug file.
4. **The `FreeForAllTest` startup-crash watch:** one more loop; note or file.
- **OUT:** anything the owner's items block (art, music, export, .NET); any sim or data change; `sim/Rts.Sim/ViewApi/**` (nothing needed).

**Acceptance criteria.** 1. Build 0 warnings, smoke PASS, loop green on the branch (flakes named, green alone). 2. BUG-1114's row a
hard `Check` and green; the bug `fixed` in the report with the test name; docs/03's sentence true. 3. BUG-1115 items 1-3 fixed with a
test row for item 3; the report says whether item 4 was done (screenshot) or left. 4. `sim/` diff empty; `game/data` untouched.

**If the inbox has an owner item** ("look: go" / "look: outfit first" / "look: stylized", ".NET 10 installed", templates dropped, sound
notes): that comes first instead, as a feature session; the hardening above moves to the next free slot. (None at this PLAN.)

### Next (view)
- (e) `tools/export.ps1` once the templates are downloaded; the .NET LTS move once the SDK is installed (one commit, sim + game
  projects, M6 criterion); (f) criterion 6's music; the sound re-cuts when the owner listens; the art pass on the owner's call. With
  none of those, the view STOPs (cheaply) next session: no more debt worth a session.

---

## Data track

### Current session plan: D-R33, the hunt's follow-up on the content tests (BUG-1110's remainder + BUG-1104) · feature (inbox-like follow-up; counter 0 / 4 → 1 / 4) · QA light

**Goal.** M5-4d finishes off two Malazan sides the content tests still list as "beaten but alive"; the allowances now print "take it
out" and would hide a regression. Also D-H7's own test hazard (BUG-1104). ~150 lines.
1. `D21OpeningsQaTests.KnownBeatenAlive` {(1, 1, 19, 0)} → empty (the row's "now passes" line quoted in the report), its comment
   re-written (seed 19 defeated 10:24 without a wave since M5-4d: BUG-1010's "falls without attacking" still holds);
   `DH6ThresholdQaTests.KnownBeatenAlive` {25} → empty likewise (defeated 12:34). Both `Stress` rows green, re-measured on this
   session's base (`af7047b`), the ticks quoted.
2. **BUG-1104 (S3):** `MapTextContentTests.ThePin_NamesEveryShippedMap_RarakuFirst` keeps `Assert.Equal("raraku", Pinned[0].Id)` and
   drops the set equality (print the unpinned maps instead of asserting); QA's decoy row then passes as the class summary says. **Touch
   only that method** (lines ~70-76): the sim's rider changes `Pinned`'s two description strings in the same file this session.
3. The Malazan page (`docs/factions/malazan.md` lines 546-551, the "Until M5-4d ... Since M5-4d ..." paragraph): update only its last
   sentence once the entries are out ("both test entries were taken out by D-R33"). The Whirlwind page carries no twin of the Age II
   table (checked at PLAN): nothing to do there unless the dev finds one.
- **Files:** `sim/Rts.Sim.Tests/QA/Content/D21OpeningsQaTests.cs`, `DH6ThresholdQaTests.cs`, `Content/MapTextContentTests.cs` (the one
  method), `docs/factions/malazan.md` (one sentence). **OUT:** `game/data/**` (D25's sentences land with the sim's rider this session),
  `MapTextContentTests.Pinned`, any other C#, the page tables (the sim may re-pin cells there under rule 40).

**Acceptance criteria.** 1. Build 0 warnings; the data filter (`Content|DataValidation|DataContentHash|MapFile`) green; non-Perf green;
smoke PASS. 2. Both lists empty, both rows green, no "take it out" line printed, the defeat ticks quoted. 3. BUG-1104 `fixed`, the decoy
row green. 4. `git diff -- game/data` empty; the data hash unmoved by this task.

### Next (data)
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780); D14 c's content
  when the sim ships its schema; M7's rosters when M7's schemas exist. With none of those, the data track STOPs (cheaply).
