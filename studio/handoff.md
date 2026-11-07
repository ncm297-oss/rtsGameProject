# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-06-0926** (session 2 / 8 on 2026-11-06; all three tracks accepted: sim
M5-4d slice 1 after 2 fix rounds, view M6-VH5 and data D-H7 with 0). Next base = `origin/main` after the conductor's merge
sim → view → data **with three merge-time actions** (session log "Notes for the conductor"): the BUG-1108 patch
(`studio/qa/2026-11-06-0926-bug-1108-view-tests.patch`) after the view merge, HEAD for the `docs/factions/malazan.md` conflict at the
data merge, both sides for `studio/qa/coverage.md` / `studio/bugs/README.md`. **Open S1 / S2: none** (BUG-1107 fixed in-session,
BUG-1108 fixed by the merge patch, BUG-1110 narrowed to S3). Counters after: **sim 2 / 4 (feature), view 0 / 4, data 0 / 4.**
Bug ids next: **sim from 1116, view from 1126, data from 1136.** Save formats are **6 / 7** for any match with an AI seat (every save
the owner made before refuses, as every recording does: `ai.huntMemory` moved the data hash).

## Standing rules (1-40)

Unchanged 1-39 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map
content and QA's suites don't count toward a size budget). **New 40:** a sim task that re-prints a pinned cell on a faction page
(brief criterion "re-pin in the same commit") may edit `docs/factions/**` for those cells only, and must say so in its report; the
Producer re-prints the surrounding prose at ACCEPT if it goes stale, and the data track owns the test allowances
(`KnownBeatenAlive` and the like) at its next task.

## PLAN checks to do first
- Confirm `main` carries the merge actions: `grep -n HuntMaplessVersion game/tests/QaM6V18Test.cs` (3 hits), `grep -c "Until M5-4d"
  docs/factions/malazan.md` (1), no `<<<<<<<` anywhere. If the BUG-1108 patch did not land, the loop is red on 3 scenes: plan it as
  the view's first item and reopen BUG-1108.
- Watch-out: `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks(pale_hills)` ends at **35,569** (431 ticks of margin;
  deterministic). Any change to the hunt's sweep order moves it; see the sim brief.

---

## Sim track

### Current session plan: M5-4d slice 2, "finish the match": rebuilding + the high-ground look before attacking up; riders D25's text landing and BUG-1111 · feature · QA full

**Goal.** Roadmap M5 criterion 3's last two items: the AI rebuilds what it lost (a razed Town Hall while a worker lives; a build-order
building that was finished and then destroyed) and looks before it attacks uphill (a wave whose target sits on higher ground than its
members scouts the ramp / the high ground first instead of filing blind up a ramp). Uncertain design: one slice, ~800 lines
hand-written, QA's suites excluded; plan only what fits.

**Scope.**
- **Rebuilding** (`BuildOrderExecutor` / `Blackboard`): a finished build-order step whose building is gone (`KnownOwnBuildings` by type
  below the step's count) is due again at once; a lost hall (no hall, at least one worker) is rebuilt first, at the old hall's cell if
  free else by `placementSearchRadius`; the reserve rule applies as to any step; no new data field unless needed (then `rules.json`
  `ai.*`, hashed, validated, minimum entries).
- **The high-ground look** (`MilitaryManager`): when the wave's target cell is on a higher level than the members' centroid cell
  (`Heightmap`), the wave's first point is the nearest ramp top / the nearest high-ground cell in sight of the target (the scout
  grid's geometry or the ramps the map knows), then the target; data knob only if a number is needed. Keep it to one rule.
- **Rider, D25's text landing (named exception, the Producer's ruling 2026-11-06-0926):** the data track's final sentences for Pale
  Hills and Vathar Crossing (below) go into `game/data/maps/pale_hills.json` / `vathar_crossing.json` `description`, the sources
  `tools/Rts.Cli/maps/<id>.quad.json` line 4, `MapFileTests.TheNewMaps_Load_WithTheirFields` and **`sim/Rts.Sim.Tests/Content/
  MapTextContentTests.Pinned`** (data-owned; this one edit allowed, say so in the report), one commit; `DataContentHashTests` moves
  once; no golden regen (decoupled). The owner may veto in the inbox first: check it at PLAN.
  - Pale Hills: "Camps in hill woods, linked by paths through rows of trees. A hill stands between each camp and the open center.
    Timber is everywhere, gold scarce beyond the camps; every path invites an ambush."
  - Vathar Crossing: "A sunken riverbed cuts the steppe in two, its steep banks crossed only at a few fords. Gold waits on the banks
    beside them, and troops down in the bed cannot see up onto either side."
- **Rider, BUG-1111 (S3) only if a few lines:** on resuming a leg after a sighting, go back to the ring point before `HuntRing` (the
  leg's last point reached) so the cut edge stretch is walked; un-skip QA's `ARingLeg_InterruptedBySightings_StillSeesTheWholeBorder(-1)`.
  Then re-measure the Pale Hills seed-1 row: if it passes 36,000, raise that row's bound to 72,000 (the stress rows' budget) and re-pin
  the number in docs/03's M6-S8 table, saying why.
- **OUT:** the Hard knobs (M5-4c slice 2), expansion changes, splitting the army to sweep, any view change, `game/data/factions/**`.

**Acceptance criteria.**
1. `dotnet build` 0 warnings; non-Perf green; Perf alone green; smoke PASS; the loop's three save-format scenes green on `main`.
2. A built-scene test: an AI seat whose finished Barracks is razed rebuilds it (the command log shows the `Build`); a seat whose hall
   is razed with workers alive rebuilds a hall and goes on gathering.
3. A built-scene test on a two-level map: a wave whose target is on the upper level is first ordered to the ramp top / a high cell in
   sight of the target, then to the target; on a flat map nothing changes (the pinned matches unmoved unless a hunt / rebuild occurs:
   then regenerate in the same commit and say which).
4. Fog: every rebuild site and look point comes from `PlayerView` only (an oracle row).
5. Save / load mid-rebuild and mid-look: hash-equal for 1,000 ticks; any new Blackboard field saved (format bump only if the layout
   changes; say so); the replay format unchanged.
6. Determinism row (two processes) on a match that rebuilds.
7. D25's text: the four files changed in one commit, `MapTextContentTests` green (pin = shipped), `DataContentHashTests` re-pinned.
8. Think gate 1 ms and 0 B hold.

**Design references:** docs/02 "AI opponent" (the behaviors in order; "rebuild"), docs/03 "Implementation (M5-2)" (the executor),
"(M5-3)" (waves), "(M5-4d)" (the hunt), "(M6-S8)" (ramps and levels on the maps), roadmap M5 criterion 3.

**Constraints:** no Godot in `Rts.Sim`; no `Dictionary` iteration; no allocation per think; knobs in data; the AI reads only
`PlayerView`; docs/02 one bullet + docs/03 "Implementation (M5-4d slice 2)" in the same commit.

### Next (sim)
- M5-4c slice 2 (the Hard knobs: `gatherBonus`, `focusFire`, `pullBackWounded`; M5 criterion 4), BUG-1010's lever, D14 c's schema
  (M5 criterion 9, the data track waits on it), M5-5's 20-minute test. Hardening at 4 / 4: BUG-1058, BUG-1059, BUG-1044's rest,
  BUG-1067, BUG-1068's seed-2 stalemate, the movement S3s, the `HuntRing` load range (QA note 3), `HasMapId`'s special case.

---

## View track

### Current session plan: M6-VH6, a short hardening (the view's only unblocked work) · hardening · QA standard

Every M6 view feature left needs the owner (the art call, music / fonts / export templates, the .NET 10 SDK: all in STATE "Waiting
on you"), so the view spends this session on its debt and on verifying `main` after the integration patch. Cheap; say what was
not reached.
1. **The loop on `main`** after the BUG-1108 patch (`tools/qa/scene-loop.ps1`, 84 scenes): green once; a wall-clock flake reruns
   alone and is named.
2. **BUG-1114 (S3):** `SkirmishSetup.AddSlot` raises `_slotsBeforeMap` to `SlotCount` when it is set (or clears it as `RemoveSlot`
   does); turn `QaVh5Test.SlotGiveBack`'s `KNOWN BUG-1114` line into a check; docs/03's M6-V18 / VH5 sentence if it changes.
3. **BUG-1115 (S4) items 1-3:** the 137-character docs/03 line wrapped; one px figure (measure, keep the measured one); `Wrap` drops
   an empty first / last line a space at the width would leave (text otherwise unchanged; a test row). Item 4 (an opaque tooltip
   panel in the shell theme) if trivial, else leave it for the art pass and say so.
4. **The `FreeForAllTest` startup-crash watch:** one more loop; note or file.
- **OUT:** anything the owner's items block; any sim or data change.

**Acceptance criteria.** 1. Build 0 warnings, smoke PASS, loop green on `main`. 2. BUG-1114's row a hard check and green; the bug
`fixed`. 3. BUG-1115 items 1-3 fixed with a test row for 3; the file says which items stay. 4. Index rows set.

**If the inbox has an owner item** ("look: go" / "look: outfit first" / "look: stylized", ".NET 10 installed", templates dropped,
sound notes): that comes first instead, as a feature session; the hardening above moves to the next free slot.

### Next (view)
- (e) `tools/export.ps1` once the templates are downloaded; the .NET LTS move once the SDK is installed (one commit, sim + game
  projects, M6 criterion); (f) criterion 6's music; the sound re-cuts when the owner listens; the art pass on the owner's call.

---

## Data track

### Current session plan: D-R33, the hunt's follow-up on the content tests (BUG-1110's remainder + BUG-1104) · feature (inbox-like follow-up) · QA light

**Goal.** M5-4d finishes off two Malazan sides the content tests still list as "beaten but alive"; the allowances now print "take it
out" and would hide a regression. Also D-H7's own test hazard.
1. `D21OpeningsQaTests.KnownBeatenAlive` {(1, 1, 19, 0)} → empty (the row's "now passes" line quoted in the report), its comment
   re-written (seed 19 defeated 10:24 without a wave since M5-4d: BUG-1010's "falls without attacking" still holds);
   `DH6ThresholdQaTests.KnownBeatenAlive` {25} → empty likewise (defeated 12:34). Both `Stress` rows green.
2. **BUG-1104 (S3):** `MapTextContentTests.ThePin_NamesEveryShippedMap_RarakuFirst` keeps `Assert.Equal("raraku", Pinned[0].Id)`
   and drops the set equality (or prints the unpinned maps instead of asserting); QA's decoy row then passes as the class summary
   says.
3. The Malazan page: the seed 19 / 25 paragraph was re-printed by the Producer at the 0926 ACCEPT (reads "Until M5-4d ... Since
   M5-4d ..."); update its last sentence once the entries are out ("both entries were taken out by D-R33"). The Whirlwind page's
   twin table: check the two cells (seed 19 / 32) match the Malazan page's. Nothing else on the pages changes.
- **Files:** `sim/Rts.Sim.Tests/QA/Content/D21OpeningsQaTests.cs`, `DH6ThresholdQaTests.cs`, `Content/MapTextContentTests.cs`,
  `docs/factions/malazan.md`, `whirlwind.md`. **OUT:** `game/data/**` (D25's sentences land with the sim's rider this session: do not
  touch `MapTextContentTests.Pinned`'s text), any other C#.

**Acceptance criteria.** 1. Build 0 warnings; the data filter (`Content|DataValidation|DataContentHash|MapFile`) green; non-Perf
green; smoke PASS. 2. Both lists empty, both rows green, no "take it out" line printed. 3. BUG-1104 `fixed`, the decoy row green.
4. `git diff -- game/data` empty; the data hash unmoved by this task.

**Watch-out (data):** the sim's rider changes `MapTextContentTests.Pinned` the same session (merge order sim → data): touch only the
test method, not `Pinned`, so the merge is clean. If the sim's rider does not land, D25 stays with the sim.

### Next (data)
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780); D14 c's content
  when the sim ships its schema; M7's rosters when M7's schemas exist. With none of those, the data track STOPs (cheaply).

---

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts
  only when it fails alone.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- A developer must never kill another track's Godot or `testhost` process (one did at ~10:22 this session; the view's loop fails on
  its own without hurting the others). The scene loop's own timeout handles a hung scene.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase. This one ran about 13 h (two sim fix
  rounds with a 44-minute suite each, three tracks): plan the sim's slice small enough for one fix round.
