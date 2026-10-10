# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-10-0624** (base `bbb7b88` = `origin/main` after the 2026-10-10-0215
integration). All three tracks GO: sim **M4-H2** (hardening), view **M4-VH2** (hardening), data **D10c** (feature). The ACCEPT of
this session replaces this file.

## Where we are

- M0-M3 Done. **M4: 6 / 10.** Criterion 6 (abilities, statuses, zones) has M4-4a + M4-4b-1 + M4-4b-2 (zones, Blinded, Sandstorm)
  landed, and on screen M4-V6a + M4-V6b (Burning / Slowed markers, the resolve flash); owed: summons / self-aura / autocast /
  passives (M4-4b-3) and zone discs + the Blinded marker (M4-V6c). Criterion 8: Telas Fire, the Cusser and Sandstorm work; the
  Zealot passives need M4-4b-3. Then 7 stealth (M4-5), 10 the fog-on sandbox Playable.
- `main` at `bbb7b88`: build 0 warnings; the Producer's PLAN checks are in the session log. **No open S1 / S2.** Index: S3 20,
  S4 19 open (partly-fixed multi-item nits counted).
- **Hardening counters at this PLAN: sim 4 / 4, view 4 / 4, data 2 / 4.** This session is the sim's and the view's hardening;
  the data track runs D10c (feature; counter → 3 / 4 after).
- Golden `data-hash` is **5896D3E7C9FD36AD** (in `GatherWedgeQaTests.SameGameDataHashes`, with the three older ones).
- Bug ids this session: sim from **BUG-0390**, view from **BUG-0400**, data from **BUG-0410**.
- Standing rules: (1) a sim task that changes a placement, vision, combat or data-loading rule runs the full suite incl.
  Perf once (alone) and the merged scene loop before it reports; (2) a sim task that adds a `UnitState` ships the `ui.json`
  `states.*` line itself; (3) whoever moves `data-hash` adds the new hash to `GatherWedgeQaTests.SameGameDataHashes` in
  the same commit when the seed-21 match is unaffected, else re-records; (4) when the view needs a `ui.json` key, the
  brief says so and the sim stays off the file that session.
- **File ownership this session:** the sim hardening is code + docs only (no `game/data/**` edit, no `data-hash` move, no edit
  to `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` unless a hashed field forces a regen, see the sim brief); the view
  hardening touches `game/**` and `sim/Rts.Sim/ViewApi/**` (+ its xUnit tests under `ViewApi/` and `QA/ViewApi/`); the data
  track may edit `game/data/common/statuses.json`, `game/data/factions/malazan/abilities.json` and
  `game/data/factions/whirlwind/abilities.json` **text fields only** (`description`), `docs/factions/whirlwind.md`, the
  docs/02 "Status effects" Blinded row (text only), `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, and, as
  a named exception for the golden regen, `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` + the `SameGameDataHashes` line
  in `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`. docs/02: the sim edits "Zones" and "Splash and friendly fire" only; the
  data edits the Blinded row only. docs/01 change-log rows are appended by each track.

## Sim track

### Current session plan: M4-H2 · sim hardening · QA full · batch to ~1,200 lines

**Goal.** Work the sim's debt backlog (STATE "Debt backlog: sim track") most valuable first, so the zone / fog / combat rules
that M4-4b-3 and M4-5 build on are consistent and every skipped QA row that pins a known bug is un-skipped. Stop when the
budget is used; report what was not reached.

**Scope, in order:**
1. **BUG-0360 (S3):** the vision blocker hides by cell centre, the statuses by unit centre. Option (a): `VisionSystem.ZoneHides`
   and `FogStore.CanSeeUnit` hide a *unit* by its own centre (within the radius, the edge counts, the same test `ZoneSystem`
   uses); cells keep the cell rule for ground. Un-skip `QA/ZoneQaTests.TheBlockerAndTheStatuses_AgreeOnWhoIsInside`, retire
   `TheRadiusEdge_CurrentBehaviour_StatusesByCenter_BlockerByCell` (or flip it to the new rule); docs/02 "Zones" and docs/03
   "Implementation (M4-4b-2)" say the same thing. Re-derive `Stress/ZoneFogOracleFuzzStressTests`' oracle with the new rule.
2. **BUG-0361 (S3):** `ReplayRecorder` refuses a non-default `SimConfig.ZoneCapacity` (one guard beside the building / projectile
   ones); un-skip `ZoneQaTests.TheRecorder_RefusesANonDefaultZoneCapacity`.
3. **BUG-0363 (S4) items 1-2:** (1) a cheap "any blocker near the scanner" test so far storms don't turn off the one-level scan
   shortcut (`Stress/ZoneScalePerfTests`' far-blocker ratio drops; keep `EmptyTick(2500)` under 500 µs: it has ~15 µs of
   headroom, add no per-tick work in the no-zone case); (2) the up-to-3-tick acquire window after a storm appears: fix if a few
   lines, else document it in the `ZoneHides` summary + docs/03 known limits. Item 3 (a tower's wind-up before the storm) is a
   note only.
4. **BUG-0330 (S3):** size the per-tick death list `UnitCapacity + BuildingCapacity`; un-skip the `CusserQaTests` row.
5. **BUG-0311 (S3, Requests 27):** end an Attack on a gone building's ghost when `fog.SeesFootprint` (the fog's own rule) sees
   the footprint, not `UnitSeesFootprint`'s nearest-point test; regression test with the 15.7-15.96 m case from the bug file.
   Tell the view (report + STATE request line) that `QaGhostViewTest` can then run `--strict`.
6. **BUG-0241 (S3):** a chaser that takes two or three unreachable cliff-top targets in turn never gives up. Pick one of the
   bug's options (keep `ChaseBest` per remembered target, or bound the switches since the last landed hit); flip
   `QA/ChasePrevQaTests.UnreachableTargetsTakenInTurn_TheChaseStillEnds` and the `Bug0241Pin_*` row; docs/03 known limit updated.
7. **BUG-0157 (S3, Producer decision 2026-10-08-2144):** the kept-chase rule ("an AttackMove to a new point keeps a chase whose
   target is in sight", with the re-pick), accepted against a mean bound: >= 90 % mean over intervals 1-20 at 40 v 40, no
   interval under 80 %; re-state the `(10, true)` row the same way; un-skip the `(3, true)` row of `QA/AttackMoveRepickQaTests`.
8. **BUG-0270 (S3):** a tower's high-ground hit reveals the tower to its victim's owner: a per-(building slot, player) reveal
   read by `CanSeeBuilding` / `SeesBuildingCells`. **Hash it only while a reveal is live** (as zones do) so the golden replay's
   checkpoints are unchanged; if the golden must move anyway, regen once with the reason in the commit and say so in the
   report (the conductor re-records once after the data's text regen). docs/01 row (d) of the M4-3a decision updated.
9. As the budget allows: **BUG-0302 b + c** (the ~65 KB re-baseline itemised; a stopwatch seam so `AbilityPerfTests` time the
   real phases once), **BUG-0275 items 1-2** (`BuildingGhost.Site`, hashed only while set; the `ExploreAllForTests` seam marked
   or moved), **BUG-0271 / 0272** (decide: a documented limit → `wontfix` with the reason, or the fix), **BUG-0144**,
   **BUG-0142 items 1-2**, **BUG-0113 item 2**, **BUG-0094**, **BUG-0242**, and the docs/02 "Splash and friendly fire" wording
   ("allied and own units" → "own units"; alliances arrive with M5).

**OUT of scope:** M4-4b-3 features (summons, self-aura, autocast, passives), stealth / detection, any `game/data/**` edit, any
`ui.json` key, the data track's test folders, the view's files.

**Acceptance criteria:**
1. Each fixed bug's skipped row is un-skipped and green, with one regression test per fix that failed before the fix.
2. BUG-0360: a Raider 5.2 m into a 6 m storm is hidden from a Crossbowman outside (`Fog.CanSeeUnit` false, no acquisition);
   one 6.01 m out is visible; `ZoneFogOracleFuzzStressTests` 0 mismatches with the re-derived oracle; docs/02 and docs/03 agree.
3. Full suite incl. Perf green when Perf runs alone; `EmptyTick(2500)` < 500 µs and `TightBlob2500` < 4.6 ms (both thin:
   473-480 µs and 4.48-4.50 ms now); `ZoneScalePerfTests`' far-blocker ratio reported before / after.
4. Golden `cross_map_seed1.replay` unchanged unless a hashed field forced it (then regenerated once, reason in the commit, every
   `k` line equal, the new `data-hash` not moved: the sim does not touch data files).
5. Determinism: twins equal and a replay round trip for every rule touched (zones / fog / chase / towers).
6. docs/03 known limits and the per-bug sections updated; docs/01 rows for BUG-0157 and BUG-0270; every fixed bug file set
   `fixed` with the commit and test; bugs not reached listed in the report.
7. Build 0 warnings; smoke PASS; the scene loop green on the branch (the merged loop is the conductor's gate).

**Design references:** docs/02 "Zones", "Status effects" (Blinded row), "Fog of war" (the reveal rule), "Splash and friendly fire";
docs/03 "Implementation (M4-4b-2)", "Implementation (M4-3a)" / "(M4-3b)" (reveals, ghosts), "Known limits"; docs/01 rows
2026-10-08 (M4-3a decision (d)) and 2026-10-08-2144 (BUG-0157).

**Tests required:** the un-skipped rows above; `ZoneVisionTests` / `ZoneQaTests` rim cases; `ReplayRecorderTests` guard;
`CusserQaTests` death-list row; a `TowerTests` reveal test + a `StateHashTests` row for the reveal pair; `ChasePrevQaTests`;
`AttackMoveRepickQaTests` with the mean bound; golden replay tests; Perf alone.

**Constraints:** no Godot in `Rts.Sim`; no new per-tick allocation (the reveal pair and the site flag are fixed arrays); slot-order
loops only; no wall clock; every number in data (no new constant for the storm rim or the reveal); `Rts.Sim` warnings as errors.

**QA focus.** The BUG-0360 fix at the rim (5.2 m in / 6.01 m out, both for `CanSeeUnit` and `ZoneHides`; a Blinded unit inside by
centre but in an "outside" cell; a unit exactly at 6.0 m); the oracle fuzz re-derived with the new rule (cliffs, ramps,
overlapping storms, 3 players); the Perf rows alone before / after BUG-0363; the recorder guard; the death-list overflow with
2,000 units + 50 buildings dying in one tick; the kept-chase rule's interval numbers at 40 v 40 over seeds; a tower reveal's
40-tick life and its hash; replay round trips after every hashed change; `EmptyTick(2500)` paired medians vs base.

After: M4-4b-3 (summons `spawn`, `selfAura` / `targetUnit`, `autocast`, the Zealot passives), M4-5 stealth / detection
(the Revealed status, detectors), the fog-on sandbox Playable.

## View track

### Current session plan: M4-VH2 · view hardening · QA standard · batch to ~1,000 lines

**Goal.** Work the view's debt backlog (STATE "Debt backlog: view track") most valuable first: the Shift-queued cast bug that
loses a spell in normal play, the marker contrast, the shutdown crash that makes the scene loop rerun a test, and the fog-view
nits. Stop when the budget is used; report what was not reached.

**Scope, in order:**
1. **BUG-0370 (S3, first):** `AbilityCaster.PickCaster(queued: true)` treats a caster as busy when its order queue already holds a
   `UseAbility` of that ability (read-only: the queue kinds / queued type ids on `UnitStore`), and / or remembers the casters this
   armed session sent until the next tick (so two clicks in one tick pick two mages). Un-skip
   `QA/ViewApi/StatusFlashQaTests.ShiftQueuedCastQaTests.TwoShiftClicks_OnWalkingMages_GoToBothMages_BothCastsResolve`; the "two
   clicks in one tick" gap in docs/03 "Implementation (M4-V6b)" closes with it; an `AbilityViewTest` row with walking mages.
2. **BUG-0371 (S4):** (a) the Slowed marker's contrast at zoom 30 against sand (a darker / saturated blue or an outline; a taste
   default, owner may revisit; a windowed shot before / after); (b) a fog-hidden resolve decided once on its first frame (never
   drawn mid-fade later); (c) a flash first drawn late starts its age at the first draw.
3. **BUG-0251 (S3):** `EconomyViewTest.tscn` FATAL in .NET shutdown under load: drop / `Dispose()` the scene's Match references,
   `GC.Collect(); GC.WaitForPendingFinalizers();`, free the Match node and wait a frame before `Quit`; apply the same shutdown
   path to every scene that instances `Match.tscn`; 30 runs under load with 0 FATALs; the scene loop's rerun-once for it removed.
4. **BUG-0281 items 1-3 (S4):** props / the minimap resource layer relist only on fells the player can see (explored fog keeps
   the last-seen tree); new corpses / rubble not shown in explored fog unless seen (or documented as the M6 fog-look pass's);
   minimap dots vs `CommandAt` over the same captured list.
5. As the budget allows: **BUG-0250** (the collinear lob reuse rule + docs/03 wording; `TerrainHeight.Straddle` constant; the
   double-Cancel guard reset on a new `Simulation`), **BUG-0148 item 1**, **BUG-0126 items 3 / 5 / 6**, the export hygiene notes
   (docs/03 "Build and export": `game/tests/` out of the release build, `.pck`-safe data loading) as docs or a small change.

**OUT of scope:** zone discs / the Sandstorm look / the Blinded marker colour (M4-V6c, the next view feature session), stealth
visuals, any sim rule change (a sim need goes to "Requests for the sim track"), `game/data/**`, `ui.json` keys the sim would
need.

**Acceptance criteria:**
1. BUG-0370: two Shift clicks with two walking ready mages queue one cast each and both resolve (the un-skipped QA row green; an
   `AbilityViewTest` row with walking mages; two clicks in one tick pick two mages).
2. BUG-0371: the Slowed marker is readable at zoom 30 on sand in a windowed shot (`--shots`), a fog-hidden resolve never flashes
   later, a late first draw doesn't jump; `ResolveFlashesTests` rows for (b) and (c).
3. BUG-0251: `EconomyViewTest` 30 runs under load, 0 FATAL after PASS; the loop no longer reruns it.
4. Scene loop all green (39 scenes); smoke PASS; 0 B rows kept (`QaV6bTest`, `QaV6aTest`, `AbilityViewTest` steady); build 0
   warnings; no new player-facing literal (text through `ui.json`).
5. ViewApi additions read-only: the hash-twin test (`NewViewApiHelpers_DoNotChangeTheSim_*`) extended to any new helper.
6. docs/03 updated per fix; every fixed bug file set `fixed` with the commit and test; bugs not reached listed in the report.

**Design references:** docs/02 "Hotkeys" (Shift queues casts), "Fog of war" (last-seen state in explored fog), "Abilities" (one
cast per click from the nearest ready caster); docs/03 "Implementation (M4-V6a)" / "(M4-V6b)", "Implementation (M4-V4)", "Build
and export".

**Tests required:** the un-skipped QA row; `AbilityCasterTests` for the queued-busy rule and the same-tick memory;
`ResolveFlashesTests` (b) / (c); `AbilityViewTest` walking-mages row; the `EconomyViewTest` shutdown change; `FogViewTest` rows
for BUG-0281 items done; the scene loop.

**Constraints:** dumb views (no gameplay state in nodes); `ViewApi` never writes sim state, the tick, or the hash; no per-frame
allocation in the marker / flash / pick paths (keep the 0 B rows); C# only; absolute paths in test I/O.

**QA focus.** Shift-queued casts: two walking mages, two clicks in one tick, three clicks with two mages (the third waits or is
refused, never silently lost), a caster dying mid-queue, a cast popped on cooldown, Shift + right-click ending the armed state;
marker colours windowed at zooms 15 / 30 / 60; the `EconomyViewTest` shutdown 30 runs under load; the fog-view relist rules with
trees felled out of sight; hash twins for any new ViewApi helper.

After: M4-V6c zone visuals (a storm disc per live zone from `World.Zones`, drawn only where the player may see it, the Blinded
marker colour), stealth visuals (M4-5), the fog-on sandbox Playable.

## Data track

### Current session plan: D10c · Sandstorm's row + Blinded pinned, BUG-0380, the text tweaks · feature · QA light · ~400 lines

No inbox note this session, so the Producer's decisions of the 2026-10-10-0215 ACCEPT stand (owner may revisit any of them).

**Goal.** Pin the two newest pieces of content (Sandstorm, Blinded) to their design rows so a drifting number fails a test by
name, close the pin's blind spot (BUG-0380), and land the three description tweaks the owner was shown, with one golden regen.

**Scope:**
- **Pin Sandstorm:** drop it from `AbilityContentTests.PendingAbilities`; `EffectProblems` learns a `createZone` effect (its
  statuses' names and magnitudes, "Enemy units" from `affects: enemy_units`, "No effect on buildings" when nothing hits
  buildings, the Duration column = the zone's lifetime 12 s; blocks-vision claim ↔ `blocksVision`). Reword the whirlwind.md
  Sandstorm row (line 63) to the parsable form with **"Enemy units"** (Producer decision (a) of M4-4b-2: `affects` is
  `enemy_units`; "Non-Whirlwind" and "enemy" mean the same until alliances exist) and keep its numbers: 18 / 6 m / 1.2 s /
  45 s / 12 s, Blinded + Slowed 30 %, enemies outside can't see in.
- **Pin Blinded:** drop it from `PendingStatuses`; `CompareStatuses` learns the `blind` kind against docs/02's Blinded row
  ("Sight radius 2 m; can't acquire or attack targets more than 3 m away") from `StatusDef.Sight` / `Reach`; mutants on both
  sides (page 2 → 3 m, data `reach` 3 → 4) fail naming 'Blinded' and the field.
- **BUG-0380 (S3):** report every page damage claim no data effect consumed; un-skip
  `QA/Content/AbilityPinQaTests.AnExtraPageDamageClaim_WithNoDataEffect_Fails`.
- **Text tweaks (Producer decision, owner may revisit):** `slowed.description` → "Moves more slowly until it wears off. Only the
  strongest slow counts."; `cusser.description` → "Throws a Moranth munition: 120 siege damage to enemy units and buildings in
  the area. Your own units caught in the blast, the Sapper included, take half; your buildings are safe."; `telas_fire.description`
  → "Sets the ground ablaze: enemy units in the area are Burning, taking 10 magic damage a second for 4 seconds. Buildings are
  unharmed." Review `blinded` ("Can barely see: sight drops to 2 m, and it can't target anything more than 3 m away.") and
  `sandstorm` ("Calls up a sandstorm for 12 seconds: enemy units inside are Blinded and Slowed by 30%, and enemies outside can't
  see into the storm. Whirlwind units are unaffected.") the same way: adopt a rewrite only if clearly better (e.g. "Your own
  units are unaffected" fits `enemy_units` better than "Whirlwind units"), else propose in the report. Every description must
  still pass the content pins (numbers in text ↔ data).
- **Golden:** the text changes move `data-hash`: regenerate `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` **once** at the
  end (every `k` line equal, only `data-hash` + `checksum` differ) and add the new hash to `GatherWedgeQaTests.SameGameDataHashes`
  (rule 3; the seed-21 match casts nothing that changed). Run the full non-Perf suite once after the regen.
- **Files:** `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, `docs/factions/whirlwind.md`, the docs/02
  "Status effects" Blinded row (text only, if a wording nit), the three data files' `description` fields only, the golden +
  the `SameGameDataHashes` line, a docs/01 row. **No other C#, no number change, no sim or view file.**
- **For your review entry (the Producer writes it from the report):** a table of every text changed (old → new, quoted), the
  two new pins, and the BUG-0380 fix in plain words.

**OUT of scope:** any numeric balance change, `ai.json`, new units / techs, docs/02 rules text beyond the Blinded row, M4-4b-3
content (summons / auras / passives: their schemas are not on `main`).

**Acceptance criteria:**
1. Sandstorm is pinned: `PendingAbilities` is empty; a mutant per field (range 18, radius 6, cast 1.2, cooldown 45, duration 12,
   `affects`, `blocksVision`, each zone status + magnitude) fails naming 'Sandstorm' and the field, in both directions.
2. Blinded is pinned: `PendingStatuses` no longer lists it; page / data mutants on `sight` and `reach` fail by name.
3. BUG-0380's row un-skipped and green: an extra "plus 30 magic damage" claim fails naming 'Cusser' and the field; a matching
   claim still passes.
4. The three descriptions read exactly as above; `blinded` / `sandstorm` either unchanged or rewritten with the reason in the
   report; every content pin green (`--filter FullyQualifiedName~Content`).
5. Golden regenerated once; the replay diff is `data-hash` + `checksum` only; `SameGameDataHashes` carries the new hash; full
   non-Perf suite green; build 0 warnings; `git diff --stat` shows only the owned files.

**Design references:** docs/02 "Status effects" (Blinded, Slowed rows), "Abilities" (Telas Fire, Cusser), "Zones";
docs/factions/whirlwind.md Sandstorm row + Dryjhna's Prophecy; docs/factions/malazan.md Cusser / Telas Fire rows; docs/01 rows
2026-10-10 (M4-4b-2 decisions (a)-(d)); docs/03 "For the data track (M4-4b-2)" (the `createZone` / `blind` shapes).

**Tests required:** `AbilityContentTests` Sandstorm / Blinded pins + mutants; `AbilityPinQaTests` row un-skipped; the golden
replay tests; `DataValidationTests` green.

**Constraints:** player-facing text only in data; no C# outside the two test folders; numbers in descriptions must match data;
a text claim not pinned by a test is a gap to report.

**QA focus.** Sandstorm / Blinded mutants per field (both directions); the `createZone` effect text with re-ordered phrases and
"Non-Whirlwind" vs "Enemy units"; BUG-0380's claim both before and after a matching claim, and with two damage effects in memory;
the golden regen (only `data-hash` + checksum differ; the seed-21 row still passes); a scratch merge with the sim branch if one
exists (the pending-allowance tests must pass on both sides).

After: the owner's text tweaks from the inbox, the full balance pass (QA standard) once the fog-on sandbox gives numbers,
`ai.json` build orders (M5), M7-M9 faction data.

## Watch-outs (all tracks)

- **Integration gate:** the full non-Perf suite, the scene loop and smoke on each merged result; push only green states.
  Expected conflicts: `studio/bugs/README.md` (union by id), `studio/qa/coverage.md` (append all sides); docs/03 one
  subsection per track; docs/01 rows appended; docs/02 different sections (sim: "Zones" / "Splash"; data: the Blinded row).
- **Golden:** the data track moves `data-hash` this session (text fields); the sim hardening must not add a hashed field
  without regenerating in its own commit (prefer hash-while-live so it needn't), and if both move it the conductor re-records
  once on the merge (every `k` line equal is the proof).
- **Perf:** three tracks' suites at once fail wall-clock rows on base and head alike; a failure counts only alone.
  `EmptyTick(2500)` has ~15 µs of headroom (473-486 µs of 500): the sim hardening must not add per-tick work in the no-zone case.
- QA scratch directories carry the agent's name; never `rm -rf` a shared scratchpad name. Stale scratch worktrees are listed for
  the owner under "Waiting on you" as non-blocking housekeeping.
- `CLAUDE.md` still says "Current milestone: M1" (the owner's file).
