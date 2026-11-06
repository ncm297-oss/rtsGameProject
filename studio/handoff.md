# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-11-06-0926** (session 2 / 8 on 2026-11-06). Base `6826e9d` = `origin/main`.
**PLAN checks:** D24 landed on `main` with BUG-1103's fix (7d30559 is the data branch's merge; `D24MapPinQaTests.cs:49` reads
`TestSim.Data.Maps.Length + 2`; BUG-1103 `fixed` in file and index). Build 0 warnings, smoke PASS, the non-Perf suite in the session
log. **Open S1 / S2: none.** BUG-1102 closed in full by the Producer; roadmap drift fixed (M5 criterion 3's note said expansion was
"left": it is in as the build order's `expand` step). Counters before this session: **sim 1 / 4 (feature), view 4 / 4 (hardening),
data 4 / 4 (hardening).** Bug ids next: **sim from 1070, view from 1094, data from 1104.**

## Standing rules (1-39)

Unchanged. 38: the Producer runs every suite on a scratch `git archive` export, never in a worktree. 39: a size budget "excluding
generated map content" means terrain / forest rows, the tool's source quarters and QA's suites do not count.

---

## Sim track

### Current session plan: M5-4d slice 1, "finish the match": the AI hunts the last known enemy units · feature · QA full

**Goal.** An AI whose enemy has no known building left should finish the match instead of sitting at its rally point: today a wave
targets buildings only (`MilitaryManager.TryStart` / `Wave` call `NearestEnemyBuilding`; a wave with no building known `End`s), so on
Pale Hills seeds 3 / 4 / 5 / 6 and the generated map's seed 6 the loser is razed to its last unit out in the woods and the match never
ends (BUG-1068; the owner's win rule M5-4e needs the units gone too). Roadmap M5 criterion 3's "hunting the last units".

**Scope.**
- **Enemy-unit memory, fog-filtered:** the AI remembers where it last saw an enemy unit (through `PlayerView.NextUnit`, visible
  enemies only; never the world): at least one point + tick per enemy player or one overall (dev's call, say which), updated each think,
  kept in the `Blackboard`, written and read by `Blackboard.Save` (the save format moves: bump `SaveFile`'s version the way M6-S3 /
  M6-S7 did, older saves refused with the same message; the replay format stays unless a checkpoint changes). No allocation per think.
- **The hunt:** when the AI knows **no** enemy building (`Board.KnownEnemyBuildings == 0`), the match is not over, the base is not
  being defended and it has started at least one wave before (`Board.WavesStarted > 0`), its army hunts with whatever it has (the
  opening's wave threshold does not apply): target = the most recent remembered enemy-unit point not older than a new data knob
  `rules.json` `ai.huntMemory` (seconds; propose 120; add it to the AI data hash and `DataValidationTests` like the other `ai.*` knobs,
  minimum entries only); when none is fresh, a **sweep**: the army attack-moves through the scout's waypoint grid (`Blackboard.ScoutPoints`
  geometry, as `ScoutManager.Waypoints` lays it: reuse the math, not the scout's own `ScoutVisited` state) from the point nearest the
  army, a point counted visited when a member comes within `ai.rallyReach`, farthest-from-home points first is fine; any sighting
  re-targets to the sighting. A hunt ends like a wave ends (retreat fraction, the idle-cooldown rule) and the normal wave rule takes over
  the moment a building is known again. Same command API as the player (`AttackMove` / `Attack`), one order per unit per think at most,
  the `MaxTries` guard kept.
- **Match end:** the enemy's last unit dies → the win rule fires as today; nothing new in `World`.
- **BUG-1067 rider, only if a few lines:** a seat whose scouts all died (cap spent, reach lifted) knows no forest: let gatherers search
  the hall's `ai.gatherReach` for a node once the reach lifts, or the hall's own sight reach its woodlot (data). If it is more than a few
  lines, leave it and say so.
- **Docs:** docs/02 "AI opponent" one bullet (the hunt, the knob, the sweep), docs/03 "Implementation (M5-4d)" (the memory, the
  state saved, the rule, measurements), the `rules.json` `ai` knob table wherever docs/03 keeps it.
- **OUT:** rebuilding razed buildings, the high-ground / ramp scouting before attacking up (slice 2), expansion changes (the
  `expand` step exists), the Hard knobs (M5-4c slice 2), any view or `game/data/factions` change, retuning Pale Hills' layout.

**Acceptance criteria.**
1. `dotnet build` 0 warnings; non-Perf green; Perf alone green (`AiThink` 1 ms gate rows included); smoke PASS.
2. `Stress/MapSlice2StressTests.NormalVersusNormal_KnownStalls_EndWithin72000` un-skipped: Pale Hills seeds **3, 4, 5, 6** and the
   generated map's seed **6** reach a result inside 72,000 ticks (print the tick and the winner). Seed 2 (both sides hold 2 halls at 60:00, a
   true stalemate) may stay listed as a known case with its own `Skip` or allowance that names BUG-1068.
3. A unit test on a built scene: an AI seat that knows no enemy building but saw an enemy unit within `ai.huntMemory` sends its army
   toward that point (through the command log); a second test: with no fresh sighting the army visits sweep points in order and re-targets
   on a sighting; a third: a known enemy building ends the hunt and the wave rule targets the building.
4. Fog: the hunt target is never a unit the AI cannot see at the moment it is remembered (`Ai/PlayerViewArchitectureTests`-style oracle
   row: the remembered point equals a position the fog granted that tick).
5. Save / load: a match saved mid-hunt and loaded continues with the same commands (save → load → same per-tick hashes for 1,000 ticks);
   the format bump refuses an older save cleanly; a recording made before this change replays as before when no hunt occurs in it.
6. Determinism: two processes, the same seed, Pale Hills seed 3 to the end: equal per-tick hashes (`AiDeterminismTests` row).
7. The 20 + 5 pins and the 27 pairings: unchanged unless a pinned match reaches a hunt (then regenerate / re-pin in the same commit and
   say which and why); the golden `k` lines unchanged for the same reason. BUG-1068 `fixed` (or narrowed to seed 2) with the test names;
   BUG-1067 `fixed` if the rider is taken.
8. Think cost: the 1 ms think gate holds (`Perf` rows), 0 B per think (a `GC.GetAllocatedBytesForCurrentThread` row).

**Design references:** docs/02 "AI opponent" (behaviors in priority order: defend → build order → produce → expand → scout → waves →
rebuild; the AI sees only its own fog view), docs/02 "Game flow" / M5-4e's win rule (out = no unit and no finished unit-producing
building), docs/03 "Implementation (M5-3)" (waves), "(M5-4b)", "(M5-4e)", "(M6-S3)" (the Blackboard in the save), roadmap M5 criterion 3.

**Tests required:** the rows in criteria 2-6 and 8; `DataValidationTests` for the knob (missing / negative refused); the un-skipped stress row.

**Constraints (CLAUDE.md most at risk):** no Godot in `Rts.Sim`; no `Dictionary` iteration; no allocation per think; the knob in data
(never a C# literal); the AI reads only `PlayerView`; the save format bumped, not silently changed; docs in the same commit.

**Size:** ~800-1,000 hand-written lines (a new AI behavior: uncertain design, so one slice), QA's suites excluded.

**Watch-outs (sim):** `GameData.Maps` order `pale_hills`, `raraku`, `vathar_crossing`: look up by id. A map file draws nothing from the map
stream: seeds 3 / 4 / 6 on Pale Hills are one match (openings 0 / 2); seed 5 the mirror; pick seeds by opening pair when you want
distinct matches. `MapSlice2StressTests` adds ~10 min; two 256 matches to the end ~20 s each under `Stress`. `VisionGatePerfTests`
flakes beside another host: rerun alone.

### Next (sim)
- Then: M5-4d slice 2 (rebuilding + the high-ground scouting), M5-4c slice 2 (the Hard knobs), BUG-1010's lever, D14 c's schema, M5-5's
  20-minute test. At the sim hardening (counter 4 / 4): BUG-1058, BUG-1059, BUG-1044's rest, the movement S3s, BUG-1067 if not a rider.

---

## View track

### Current session plan: M6-VH5, the view's hardening (counter 4 / 4 → 0 / 4) · hardening · QA standard

**Goal.** Pay the view's debt now that three maps are on `main`: the first full scene loop on the merged tree, two S3 / S4 bugs, a
CLAUDE.md rule-8 leftover, and doc nits. Nothing new for the player beyond a readable tooltip.

**Scope, most valuable first (batch what fits ~1,500 lines; say what was not reached):**
1. **The 82-scene loop on the merged `main` with three maps** (`tools/qa/scene-loop.ps1`): fix what fails. Then by hand: Skirmish → Map →
   Pale Hills → Add player twice (the `setup.map_players` line "has room for 4 players" inside its 300 px holder), Start: a 256 map with
   ~3,800 tree views (measure the view's frame cost at match start and the minimap at 256: say the numbers; file an S3 if the first
   second drops under 30 FPS on this PC), Vathar Crossing with 2 and 4 slots; screenshots at 1152 × 648.
2. **BUG-1093 (S3):** `TeamsTest`'s one-off GodotSharp "Handle is not initialized" inside `GD.Load` in `Main.StartMatch` after a match
   ends and the next starts. Find the cause (a freed `Match` still referenced from C#: an event handler, a static, a cached
   `PackedScene`'s script instance) or make the row robust (await the previous match's free before the next `StartSkirmish`); run the
   loop twice. Say which.
3. **BUG-1081 item 2 (S4):** the 150-180-character map / preset descriptions as one tooltip line. Producer's pick: **pre-wrap in the view**:
   a small helper (`UiText.Wrap(text, ~60)` or the like, word boundaries, no hyphenation) applied where `TooltipText` / item tooltips are
   set for the Map and Starting resources lists; the tests compare through the helper; no data change (the data stays one sentence).
4. **BUG-0581 item 1 (S4, CLAUDE.md rule 8):** `SkirmishSetup.Build`'s `$"{size} × {size}"` → a `ui.json` `setup.map_size_item` format;
   close items 2 (M5-4a's start locations settled it) and 3 (process) in the bug file.
5. **The `FreeForAllTest` startup-crash watch** (0xC06D007F once beside three hosts): two loops; file an S3 if it repeats, else note it.
6. **Doc nits:** docs/02 ~l.488 and docs/03 ~l.6825 past the ~120-column wrap (M6-V19 QA); docs/03's M6-VH5 implementation note.
- **OUT:** `tools/export.ps1` (owner templates), music, the art pass, any sim change (`ViewApi` read-only additions only if the loop needs
  one, say so), any `game/data/**` change.

**Acceptance criteria.**
1. Build 0 warnings; smoke PASS; the loop **82 / 82** twice on the merged tree (a wall-clock flake reruns alone and is named).
2. Pale Hills and Vathar Crossing start from the menu with 2 and 4 slots (a `MapListTest` row each: four halls, the right map id in the
   `Match started` line); the frame-cost numbers at match start on a 256 map in the report and docs/03.
3. BUG-1093: the cause named and fixed with a regression row, or the row made robust and the loop green twice; the bug file says which.
4. BUG-1081 item 2: a 180-character description shows as 3-4 lines in the tooltip (a test through the helper: no line past ~60
   characters, words whole, the joined text equal to the data's); screenshot.
5. BUG-0581 item 1: no player-facing literal left in `SkirmishSetup` (the `ui.json` key; a test reads the item text from `ui.json`).
6. Bug files + index rows set for BUG-1093, BUG-1081 (item 2), BUG-0581; docs nits fixed; the two doc lines wrapped.

**Design references:** docs/02 "Game flow" 2 (the skirmish setup), docs/03 "Implementation (M6-V17)" / "(M6-V18)" / "(M6-V19)" (the
lists and their tooltips), CLAUDE.md rules 7-8, docs/07 (the scene loop).

**Tests required:** the rows in criteria 2, 4, 5; the regression row for BUG-1093 if a cause is found.

**Constraints:** views hold no gameplay state; player-facing text only in `ui.json`; no sim change; tests scenes quit on their own.

**Watch-outs (view):** `MapListTest` / `QaM6V18Test` / `QaM6V19Test` look maps up by id; the merged tree is the first run with three. The
setup's greyed counts widen the SpinBox range for a map past 64 forests (Pale Hills 60: inside). Three test hosts at once: scene rows
flake under load, rerun alone before filing.

### Next (view)
- (e) `tools/export.ps1` once the templates are downloaded, (f) criterion 6's music, the sound re-cuts when the owner listens; the art pass
  on the owner's call. Every other M6 view item waits on the owner.

---

## Data track

### Current session plan: D-H7, the data's hardening, with the two new maps' final text as its content debt (counter 4 / 4 → 0 / 4) · hardening · QA light

**Goal.** Clear the data track's small debt on today's `main` and replace the two provisional map sentences the sim wrote (content the
data track owns, left as placeholders by M6-S8) with final ones, so the owner can review the three maps' text together.

**Scope (files: `game/data/maps/pale_hills.json` and `vathar_crossing.json` `description` keys only (the named exception: the sim owns
the rest of a map file), `docs/factions/malazan.md`, `docs/factions/whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`).**
1. **The allowances:** `D21OpeningsQaTests.KnownBeatenAlive` {(1, 1, 19, 0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} re-measured on
   today's `main` (the AI knobs `scoutReplacements` / `spendDownQueue` and the reach rule moved since they were set): a case the run says
   "now passes the rule" comes out; a case still failing stays, with the measured numbers in its comment (no rule loosened).
2. **The pages' AI sections against docs/02 "AI opponent"** as it stands after M6-S5 / S7 / SH1 (the reach rule, the head ring, the scout
   cap `ai.scoutReplacements` 2, the spend-down queue at High): fix any page sentence that contradicts it (wording only, no number
   changes; a sentence that quotes a measurement keeps its pinned row). A sweep for lines the three map files make stale ("the generated
   map" said as the only map): fix or say none.
3. **Final descriptions for Pale Hills and Vathar Crossing** (`description` only; `displayName` stays): one or two sentences each, ≤ 200
   characters, describe-don't-count (no digit, no ratio word "twice" / "double" / "triple" / "half", no "placeholder", no faction word,
   American spelling: the `MapTextContentTests.Problems` checker), true to the layouts in docs/03 "Implementation (M6-S8)" (Pale Hills:
   four camps in hedgerow hill woods, narrow corridors, a knoll a quarter, timber everywhere, gold scarce beyond the camps; Vathar
   Crossing: open steppe cut by a sunken river crossed only at four fords, the banks see the bed, the bed cannot see the banks). The
   provisional sentences may stand as final if judged best: then say so and pin them. Pin both by id in `MapTextContentTests.Pinned`.
4. **A review table for the owner** in the dev report: map, field, old → new, one line why; both sentences quoted.
- **OUT:** `rules.json`, any other key of a map file, any C# outside the data's test folders, the golden (decoupled: no regen), the
  faction pages' numbers.

**Acceptance criteria.**
1. Build 0 warnings; the data filter (`Content|DataValidation|DataContentHash|MapFile`) green; non-Perf green; smoke PASS.
2. Each `KnownBeatenAlive` entry either removed with the run's "now passes" line quoted, or kept with this session's measurement in its
   comment; the two `Stress` rows green.
3. The pages' AI sections contradict nothing in docs/02 "AI opponent" (list each sentence changed, or "none"); no page line names the
   generated map as the only map.
4. `git diff -- game/data` touches only the two `description` lines; both pass `MapTextContentTests.EveryShippedMapText_HasNoProblem`;
   `Pinned` holds all three maps by id and `PinProblems` is empty; `DataContentHashTests` updated for the moved hash (the recordings made
   before refuse: expected, say so).
5. The review table in the report, both sentences quoted.

**Design references:** docs/02 "Map and terrain" (the Shipped maps row), docs/03 "Implementation (M6-S8)" (each layout), docs/02 "AI
opponent", the faction pages' "AI build order (Normal)" sections, `MapTextContentTests` (the rules), BUG-1102 (American spelling).

**Tests required:** the pin rows, the checker over every shipped map, the two allowance rows, `DataContentHashTests`.

**Constraints:** no C# outside `Content/` + `QA/Content/`; the `description` keys are the only map-file edit; player text only in data.

**Watch-outs (data):** the pin is id-based: pin only ids you list; the checker runs over every shipped map. The data hash moves once (two
descriptions); no golden regen.

### Next (data)
- The owner's answers if any land (the D21 / D22 / D23 / D24 / this session's tables; the Shock pass; `goldShare`; BUG-0780); D14 c's
  content when the sim ships its schema; M7's rosters.

---

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase.
