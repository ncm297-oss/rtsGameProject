# Handoff: brief for the next session

## Current session plan (2026-11-13-1118, session 1 / 8 of 2026-11-13)

Written at the PLAN. Base 71aa082. All three tracks GO, all feature sessions (counters before: sim 2 / 4, view 3 / 4,
data 3 / 4). Inbox "look: go" processed (docs/04, docs/01 2026-11-13). Bug ids: sim 1310+, view 1320+, data 1330+.
Files per track this session: sim `sim/**` (not `Content/`, `QA/Content/`, `ViewApi/`), docs/03, docs/02; view `game/**`
(not `game/data/**`), `tools/asset-manifest.json`, `tools/import-assets.ps1`, `tools/art/**`, docs/04, docs/03 (append);
data `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, `docs/factions/teblor.md`. **Nobody edits
`game/data/**` this session.**

### Sim track: M7-6 (feature, QA full)

**Title:** Make the Blood-oil test copy tolerate shipped rows (BUG-1280), then add a per-opening reserve share (Request 52)

Goal: Two things stand between the Teblor's finished numbers and `main`. The sim's own M7-4 test copy breaks 84 tests
the moment the data ships a `teblor_frenzied` row (BUG-1280), and the AI can only save for Age II by one global number
that also moves its opponent (Request 52). Fix the first, build the second as a schema with a default that changes no
shipped match, so the data track can land the Teblor rows and the chosen opening next session.

Scope:
- **BUG-1280 first.** `sim/Rts.Sim.Tests/Abilities/BloodOilScenes.Edit` and the private copy in
  `DataContentHashTests.AnM74FieldInTheFiles_*` look the status row, the ability's effect, the Anointing's
  `statusDuration` effect and the building's `scale` up, then add or edit (QA's `QA/FrenzyRulesQaTests` `BloodOil` copy is
  the model; rule 58). Any other M7-4 helper that appends blindly gets the same treatment.
- **Request 52.** An optional `attack.reserveArmyShare` on an opening in `ai.json` (a fraction 0-1, validated exactly as
  `rules.json` `ai.reserveArmyShare` is, one error at `openings[i].attack.reserveArmyShare`). `AiPlayer.MayReserve`
  reads the seat's own opening's value when present, otherwise the `rules.json` value; the difficulty's
  `waveSizeMultiplier` applies as today. `GameData.ContentHash` adds the field only when a file carries it (M7-4's
  treatment). Schema, validation, test openings only.
- **BUG-1281 folded.** Remove `CombatSystem.DamageToUnit(World, int, int, int)` and `DamageToBuilding(World, int, int,
  int)` (no caller) or give them the status damage factor; correct docs/03 "Implementation (M7-4)": nothing draws
  `BuildingDef.Scale` yet (the view's later task).
- docs/03 "Implementation (M7-6)" ending with the exact JSON for the data track; docs/02's AI section if it describes
  the reserve.
- OUT of scope: every file under `game/data/**` (the shipped `ai.json` edit is the data track's D31); the golden replay;
  any save or replay format; `ProductionManager`'s empty-queue rule; Requests 46 / 48 / 51; `sim/Rts.Sim.Tests/Content/**`
  and `QA/Content/**` (the data track's); `sim/Rts.Sim/ViewApi/**`; `tools/**`.

Acceptance criteria:
1. With docs/03 "For the data track (M7-4)" applied exactly as written to a scratch copy of the shipped data (the four
   files of BUG-1280's repro), BUG-1280's filter passes whole (426 of 426 or today's count); on the shipped data it
   passes too. Report both command outputs.
2. A default-loop test proves the helper: `Edit` on a copy that already carries the four rows loads with exactly one
   `teblor_frenzied` row and one `statusDuration` effect of 4 s on the Anointing, and `Edit` applied twice gives the same
   `GameData.ContentHash` as applied once.
3. Loader: the field absent, 0, 0.5 and 1 load; below 0, above 1, a string and `null`-as-a-value are each one error at
   the field's path; `DataValidationTests` green on the shipped data.
4. Behaviour, on test openings: (a) a seat whose opening carries 0.5 names a reserved step at half the wave where the
   same seat without the field does not; (b) two AI seats with different values each follow their own (the opponent's
   gate does not move); (c) a field equal to the `rules.json` value plays state-hash-identical to the field absent over
   a full pinned-length match.
5. Hash and formats: the shipped data's content hash is unchanged (compare against the base commit's value computed in
   the test run, not a literal; rule 49); a test file carrying the field hashes differently; `SaveFormat.CurrentVersion`
   stays 10; a mid-match save of a seat with a per-opening share loads and continues hash-identical. If the AI's saved
   state turns out to need the value, stop and report (rule 50) instead of changing a format.
6. No shipped behaviour moves (rule 43): no shipped opening carries the field, so no M5-5 row and no pinned match may
   move. `git diff 71aa082...HEAD -- game/` is empty, no golden or pin value is edited, and the pin classes are green
   unchanged: `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, `CounterCompositionTests.TheNonHardPins_*`,
   `ReserveTwinTests`, `SharedVisionFfaPinTests`, `MatchCriterionTests`, `HardKnobsPinTests`, `GatherWedgeQaTests`, and
   in Stress `OutcomePinM73bQaTests`. A pinned row that moves or stops ending is a finding to report, never a re-pin
   (rule 45).
7. BUG-1281: the two overloads are gone or carry the factor (a test for the second choice); the docs/03 sentence is
   true.
8. No allocation: the AI think's `AllocationTests` row is green unchanged.
9. `dotnet build RtsGame.sln` 0 warnings / 0 errors; the default loop
   (`--filter "Category!=Perf&Category!=Stress&Category!=Soak"`) green; docs updated in the same commit; a proof patch for
   BUG-1280 / BUG-1281 in the scratchpad (you cannot write `studio/**`); no `testhost` left running (rule 52).

Design references: STATE "Requests for the sim track" 52; docs/03 "Implementation (M5-4g)" (the reserve) and
"Implementation (M7-4)" with "For the data track (M7-4)"; `AiPlayer.MayReserve`, `BuildOrderExecutor.Run`,
`DataLoader.Ai.cs` (the `rules.json` field's `Fraction` check), `GameData.cs` (the opening's hash block);
`studio/bugs/BUG-1280-*.md`, `BUG-1281-*.md`.

Tests required: the helper test (criterion 2); loader rows in the opening's loader tests; the three behaviour tests in
`Ai/ReserveTests` (or beside it); a `DataContentHashTests` row for the field present / absent; the save round trip; the
default loop; the Stress pin class named above run once.

Constraints: no stat in C# (the share is data); no `Dictionary` iteration, no allocation in the think; the field is read
from the seat's own opening only (the AI's fog-filtered view is untouched); warnings are errors; sizes: about 400 lines
with tests.

QA focus (full): apply docs/03's data-track rows on a scratch clone and run the M7-4 classes and this task's (rule 58).
Mutants: `MayReserve` ignoring the field; reading the opponent's opening; the hash adding the field always; the hash
never adding it; the loader accepting 1.01. Edge values: 0 (what does "reserve from the start" do to a first wave: state
it), 1, the field on one opening of three with the opening picked by seed, two seats of one faction on different
openings, Easy / Hard multipliers, a seat defending at the moment the gate would open. Save and load in the middle of a
reserve; a replay round trip; twins across three processes. All the pin classes of criterion 6 including Stress; Perf
rows alone under rule 42.

### View track: M6-V20 (feature, QA standard; planned earlier under the name M7-V4)

**Title:** Fix BUG-1290, then the art pass's first slice: the import tooling and two Malazan units as real models with team colour

Goal: The owner made the art call on 2026-11-12: keep the Quaternius characters, add outfits and team colour. This
slice proves the whole path once, from the raw packs through Blender to a moving, team-coloured figure in a match, on
two units, and measures what 400 animated figures cost before thirty more are built. The scene loop also goes back to
all green (BUG-1290, rule 57).

Scope:
- **A. BUG-1290 first (a few lines).** A unit's capsule height is never under twice its radius (such a type is drawn as
  a ball), and `UnitViews.DrawnHeight` returns that same number so the bar and markers follow. `QaM7V3cTest` green.
- **B. Tooling.** This session the view track writes `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` (Blender Python), and nothing else under `tools/`. The script runs the installed Blender in background
  mode (`$env:BLENDER`, else `C:\Program Files\Blender Foundation\Blender 5.2\blender.exe`, 5.2.2 on this PC) and stops
  with a clear message when it is absent. It reads `asset-sources/` from the main checkout when run in a worktree (as
  `tools/audio/trim-wav.ps1` does) or from `-SourceRoot`. Per model the manifest names the sources (the base character's
  head, the outfit's parts), the destination, the triangle target, the texture size, the rotation fix, which parts
  become the `team` material, palette tints, and the clip map (docs/04's required clip name → the library's clip).
- **C. Two models.** `malazan_laborer`: the Universal Base Character's head + the Male Peasant outfit.
  `malazan_crossbowman`: the head + the Male Ranger outfit with hood and pauldron. Each: one `.glb` at the path the
  data's `model` field names (`game/assets/models/<model>/<id>.glb`, docs/04 "Directory layout"), at most 3,000
  triangles, textures at most 512 x 512, about 1.8 m tall, feet at the origin, facing -Z, on the shared 65-bone rig,
  one material named `team`. A simple crossbow built from primitives in the Blender script (at most 200 triangles,
  inside the budget) on the Crossbowman's hand; a tool for the Laborer is optional. Colours muted toward the Malazan
  palette (docs/04 "Faction palettes").
- **Team colour (Producer default, owner may revisit):** the `team` material is the Ranger's hood and pauldron and the
  Peasant's tunic (the body part), drawn by one shared shader as the outfit's own shading multiplied by the player
  colour, the same per-player colours the capsules use. If a screenshot shows another part reads better, use it and say
  why.
- **Animations.** One shared library for the rig under `game/assets/animations/` holding only the clips used, under
  docs/04's names: `idle`, `walk`, `attack`, and for the Laborer `gather_gold`, `gather_wood`, `build` when Universal
  Animation Library 1 or 2 has a fitting clip (otherwise `idle` and say so). In-place locomotion, root motion off.
- **D. The view.** `UnitViews` draws a type's model when its file loads, otherwise today's capsule. The path comes from
  `UnitDef.Model`; no table of unit ids in C#. The clip follows the sim's unit state read each frame (standing, moving,
  attacking, a worker's three states); the view holds no gameplay state. Keep working on a model: the per-player colour,
  the hit flash, the worker's state tint and cargo marker, fog hiding, slot reuse. Unchanged numbers: selection and
  target rings, the health bar's height, the click hit test, the corpse disc (death animation is a later slice). A type
  with a model and a data `scale` other than 1 is drawn at that scale uniformly (no shipped model type has one; state it
  in docs/03).
- **The switch.** `UnitViews.UseModels`, on in the game; `--placeholder-art` forces capsules. An existing scene that
  pins capsule geometry or pixels may turn models off; list every scene that does. No other pin value changes.
- **A missing file or a Git LFS pointer** in place of a model: the capsule, one warning line that does not contain
  `ERROR` (BUG-0860's lesson). A clone without Blender or `asset-sources/` builds, imports and runs: the generated files
  are committed, the tooling only regenerates them.
- **E. Docs.** docs/04: "Import process" as built, the team-colour rule, a licensing row for Modular Character Outfits
  – Fantasy (Standard) with its licence file read at import time, the Universal Base Characters and Animation Library
  rows extended. docs/03 "Implementation (M6-V20)". `.gitattributes` covers every new binary type before the commit.
- OUT of scope: the other twelve Malazan and Whirlwind unit types; the Teblor; buildings and the building's drawn
  `scale` (the view's next session); trees, terrain and props; bodies as corpses; Mixamo retargeting (if no library clip
  fits an attack, report it); any performance optimisation (measure only); `game/data/**` (if a `model` value must
  change, stop and report: the file is the data track's); `sim/**` (a read-only `ViewApi` accessor only if a state
  cannot be read otherwise, and say so); downloads and installs of any kind.

Acceptance criteria:
1. BUG-1290: `QaM7V3cTest` passes; shipped units are pixel-identical with `--placeholder-art` (the fixed-camera
   `combat-seed1-brawl.png` byte for byte, as M7-V3c compared it).
2. `powershell -File tools/import-assets.ps1` rebuilds both models and the animation library from `asset-sources/`
   with no manual step; a second run gives the same file list, triangle counts and texture sizes (report whether the
   bytes are identical); with Blender's path wrong it exits non-zero with one clear line.
3. New scene `game/tests/ModelViewTest` (headless, in the scene loop): both types load a model and every other type a
   capsule; each model's triangle count read from its meshes is at most 3,000; each has a `team` material that shows
   two different player colours for two players; the library holds the clips named in the manifest; a standing, a
   moving and an attacking unit play `idle`, `walk` and `attack`; a forged data copy whose `model` points nowhere, and a
   pointer-sized file in a model's place, each give the capsule with no `ERROR` line; a slot reused by the other kind of
   view shows the right one.
4. Pictures, windowed at 1600 x 900, looked at before reporting: (a) the default zoom (40 m): ten Laborers and ten
   Crossbowmen of player 1 and of player 2 beside Heavy Infantry capsules; (b) the same at 60 m; (c) (a) in grayscale:
   say whether the two types read apart by shape alone; (d) a real skirmish start as Malazan with workers gathering.
   Say plainly what reads and what does not.
5. Cost, measure and report (rule 48), windowed, vsync off, default zoom: 400 model units (200 a side, moving then
   fighting) against the same scene with `--placeholder-art`: average and worst-1% frame time, and the node and material
   counts per unit. docs/04's target is 60 FPS; if the models miss it, say what costs (skinning, animation, draw calls)
   and recommend the next step. State what else was running (the other two tracks' suites).
6. A steady frame with models allocates nothing in the view's own code, or the report gives the measured bytes and
   their source.
7. The new files under Git LFS total at most 12 MB; nothing from `asset-sources/` is committed uncut; the licensing log
   rows are in the same commit.
8. `dotnet build RtsGame.sln` 0 / 0; `powershell -File tools/qa/smoke.ps1` PASS; the scene loop all green (99 + the
   new scene, none red); the menu's match and `--skirmish` show the models; docs updated; a proof patch for BUG-1290 in
   the scratchpad; no Godot or `testhost` of yours left running, and never end another track's (rule 52).

Design references: docs/04 "Style guide" (budgets, scale, team colour, palettes), "Coverage plan", "Directory layout
and naming", "Import process", "Look test (M6-V10)" (triangles, textures, facing, clip names, the two missing texture
names, the import settings), "Animation setup"; CLAUDE.md rule 7 (one light view node per entity, mesh +
AnimationPlayer, no physics body) and rule 6; docs/03 "Implementation (M7-V3c)"; `studio/bugs/BUG-1290-*.md`;
`asset-sources/Modular Character Outfits - Fantasy[Standard]/.../Readme.txt` (with an outfit only the head of the base
character is used).

Tests required: `ModelViewTest` (criterion 3); `QaM7V3cTest` green; the existing 99 scenes; the two import-script runs
of criterion 2 in the report.

Constraints: views hold no gameplay state; no player-facing text in C#; no stat or unit id table in C#; scenes and
resources in text formats, binaries only as `.glb` / `.png` under LFS; raw packs never enter git; size: BUG-1290 about
30 lines, the slice about 800 lines of script and C# with its scene (generated assets do not count).

QA focus (standard): the fallback paths (a pointer file, a missing file, a model without a `team` material, a library
missing a clip); four players' colours all distinct on the models; a slot reused capsule → model → capsule; fog hide and
show on a model mid-animation; the hit flash and the worker tint on a model; a forged `scale` 1.5 on a model type; a
match restarted three times (nodes and materials freed, counts flat); a save loaded and a recording watched with
models on; `--placeholder-art` giving M7-V3c's exact pixels; the scenes the developer switched to capsules (is each one
really a capsule pin?). Re-take the four pictures and re-run the 400-unit measurement alone (rule 42); check the
committed sizes and that a fresh clone with `git lfs pull` imports with 0 `ERROR`. Stress number: 400 models on screen,
and 1,000 as a report.

### Data track: D30 (feature, QA standard)

**Title:** Ready the content pins for the Teblor landing and re-measure the openings with the Frenzy rows; no shipped file

Goal: Next session the Teblor's Frenzy rows, the Anointing's +4 s, the buildings' 1.25x and the chosen AI opening land
in one data-hash move (D31). This task makes that landing boring: the content tests learn the new fields before any
row uses them, the Teblor page stops saying things that are no longer true, and the opening ranking is re-measured
with the Frenzy rows in play so the final pick rests on the game as it will ship.

Scope:
- **Pins taught the M7-4 fields** (`sim/Rts.Sim.Tests/Content/**`, `QA/Content/**` only): a `statusDuration` arm in
  `TechContentTests`; the description-number rules (D-H2) reading a status row's `damageBonus` and `onEnd`; the Teblor
  page pins ready for a Frenzy row ("+20% damage", "Slowed 20% for 4 s", "+4 s") and a buildings `Scale` column. Each
  proven on a data copy carrying docs/03 "For the data track (M7-4)"'s rows. Make that copy in your own test code: do
  **not** call `Abilities/BloodOilScenes` (the sim track is rewriting it this session, BUG-1280).
- **`docs/factions/teblor.md`:** line 27's "drawn at 1.5x scale" becomes the M7-V3c rule (a unit is drawn as wide as its
  room, `scale` is its height), and the "not in the data yet" notes become "the rules exist (M7-4); the rows land with
  D31". No table cell that a pin reads changes.
- **Re-measurement (report tests, Stress category):** openings V0, V24, V25, V14 over the 40 pairings to 15:00 with
  `TeblorOpeningHorizonReportTests`' harness, each with and without the Frenzy rows on the copy, and each again with the
  global reserve share at 0.5. Add seeds 6-8 or the generated map (seeds 2 / 5 and 3 / 4 repeat on the named maps). Also
  V24 with the Stables at the shipped 8:15. Print Frenzy casts per match for the Normal AI; if it is 0, say what
  `ai.json` would need.
- OUT of scope: every file under `game/data/**`; the golden replay; any C# outside the two test folders; any
  `docs/**` file but `docs/factions/teblor.md`; the opening's final pick (the Producer's, at D31).

Acceptance criteria:
1. On the shipped data every content test passes as today; on the copy carrying the four rows every content test
   passes, the new arms included (run both, report both counts).
2. Each new arm fails when it should, shown by a test on a wrong copy: the Anointing's effect at 5 s against the page's
   "+4 s"; a Frenzy description saying 25% against `damageBonus` 0.2; an `onEnd` of 5 s against "for 4 s"; a building at
   1.5 against the page's 1.25.
3. The page: line 27 and the "not in the data yet" notes are true for `main` as it stands after this session (rules
   built, rows not shipped); the page-pin tests are green; `git diff` shows no pinned table cell changed.
4. The re-measurement, measure and report (rule 48): one table per configuration in D29's columns (seeds met, Age II by
   10:00, a wave by 10:00, expanded, every building kind, the Anointing, defeated by 15:00, army pop at 15:00) plus
   Frenzy casts per match; then three plain sentences: does the 10-15 minute collapse move with the rows, does the
   ranking of V24 / V25 / V14 change, and which opening and share you would land.
5. The diff is `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**` and `docs/factions/teblor.md` only;
   `dotnet build RtsGame.sln` 0 / 0; the default loop green; the report tests are not in the default loop; no
   `testhost` left running (rule 52).

Design references: docs/03 "For the data track (M7-4)" (the exact rows and the id rule: after `stealthed`);
`docs/factions/teblor.md` (Blood-oil Frenzy, Blood-oil Anointing, the buildings' 1.25x); docs/02 "Status effects" (the
stacking rule); docs/03 "Implementation (M7-V3c)"; the D29 table in `studio/sessions/2026-11-12-0546.md`; the fixtures
`Content/Fixtures/teblor_standard_d29.json` and `_v4.json`.

Tests required: the positive and negative tests of criteria 1-2 in the default loop; the report tests in Stress.

Constraints: rule 41: the sim's M7-6 changes no shipped behaviour this session (its new field is absent everywhere), so
your figures hold on the merged tree; numbers on the page and in descriptions must match the data both ways; no
player-facing text in C#.

QA focus (standard): re-run one configuration (V24 with the rows, share 0.5) on the merged tree through QA's own
harness and compare; mutate each new pin's number on a copy and see it fail; check the copy differs from shipped in
exactly the four rows; check a status id sorting before `burning` is caught or at least named as a hazard in a test
comment; read the page against `main` line by line for claims that are false today. Default loop + the task's Stress
rows (rule 53).

---


Written by the Producer at the ACCEPT of session **2026-11-12-0546** (session 1 / 8 of 2026-11-12). Sim **M7-4** ACCEPT (0
fix rounds), view **M7-V3c** ACCEPT (0), data **D29** ACCEPT (1 fix round); all three QA PASS_WITH_ISSUES.
**M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 2 / 4 (criteria 1, 2).** Counters: **sim 2 / 4, view 3 / 4, data 3 / 4**.
Bug ids next session: **sim 1310+, view 1320+, data 1330+**. Open S1 / S2: none. The next session is 2 / 8 of 2026-11-12.

**Inbox: one note is waiting on `main`** (commit d5d0e00, written after this session's plan check): **"look: go"**, the
owner's art call (keep the Quaternius stack, add outfits + team colour). Process it first at the PLAN: docs/04 and
docs/01's change log, STATE's Waiting item (already reworded), M6 criterion 5's second half unblocked. Check what the
art pass needs that only the owner can do (a download, an install) before planning it; route around if so.

**Next session in one line (proposal):** sim **M7-6** (BUG-1280 first, then Request 52's per-opening reserve share,
BUG-1281 folded; feature, QA full) · view **M7-V4** (BUG-1290 first, then the building's drawn `scale`, then the art
pass's first step if unblocked; feature, QA standard) · data **D30** (the content pins for M7-4's fields, the page's
drift, a re-measurement on a copy carrying the Frenzy rows; **no shipped file**; feature, QA standard).

## Where things stand

- **`main` after this session = sim 0172c41 + view f213e1f + data 99bd60e.** The Producer's scratch clone of that tree:
  build 0 / 0; the default loop **8,998 / 0 / 21 of 9,019 in 29 m 19 s**; smoke **PASS**; the scene loop **98 of 99 (red: `QaM7V3cTest` alone, BUG-1290, by ruling)**; the M7-4 classes
  and pins **137 / 137 (the Stress fuzz, `GatherWedgeQaTests`, three pin classes, the D29 fixture tests)**.
- **No shipped data moved this session.** The golden header is still **67269D955D18D885**; save format 10; every pin as
  it was. The owner's saves from after 2026-11-11-2256 still load.
- **M7-4's rules are on `main`, unused by any shipped file:** `statuses.json` `damageBonus` (frenzied kind) and `onEnd`,
  the tech stat `statusDuration` (`effects[].statuses`), `buildings.json` `scale` (`BuildingDef.Scale`). docs/03
  "Implementation (M7-4)" ends with the exact JSON for the data track.
- **Giants are drawn right** (`UnitViews.DrawnRadius` = the collision radius; `DrawnHeight` = `(2 radius + 1) × scale`).
  `game/tests/TeblorViewTest` is the scene on the shipped Teblor. Pictures: `studio/review/2026-11-12-0546/`. 99 scenes,
  98 green (rule 57).
- **The Teblor AI opening is measured, not changed.** `Content/TeblorOpeningHorizonReportTests` (25 variants, seeds 1-5,
  to 15:00) and the hand-over fixture `Content/Fixtures/teblor_standard_d29.json` (V24). The table is in the session log.
  Provisional pick (docs/01 2026-11-12 (b)): the four-Warrior family, V24 or V25; the owner may write "teblor ai: early
  raid" for V14. Final pick at D31 with Request 52's field.
- **Not in the Teblor data yet:** Frenzy's +20% damage, the aftermath slow and the Anointing's +4 s (rules ready), the
  buildings' 1.25× (field ready), the Gatherer's 15 a trip (Request 48), the AI's later-wave rules (Requests 46 / 51).
- The held branch `studio/2026-11-11-1317-data` was never merged and can be deleted.

## Standing rules (1-58)


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
**56 (new):** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57 (new):** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58 (new):** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)

## Watch-outs (all tracks)
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **`QaM7V3cTest` is red on `main` on purpose (BUG-1290, rule 57):** the scene loop reads 98 of 99 until M7-V4's first
  item lands. Any other red row is a finding.
- **Nothing may ship a `teblor_frenzied` status row before BUG-1280's fix is on `main`** (84 sim tests go red). A Frenzy
  status id must sort after `stealthed` (docs/03 "For the data track (M7-4)"): one sorting before `burning` renumbers
  the M4 statuses' state-hash numbers and moves every pinned digest with a burn or a slow in it.
- **M7-4's fields are in the data hash only when used:** the first shipped `damageBonus` / `onEnd` / `statusDuration` /
  building `scale` moves the golden header (rule 51 applies to that data brief).
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

- **Pinned matches on `main` after M7-3b (outcomes unchanged, digests re-measured):** the Hard v Normal tally **32 / 8 / 2** (Raraku
  10 / 4 / 0, Pale Hills 11 / 1 / 2, Vathar 11 / 3 / 0); `StandardPresetPinQaTests` (8 save scenes + 7 AI rows), `NonHardMatchPinQaTests`
  8, the 8 `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests` (2 rich rows), `SharedVisionFfaPinTests` (2): all re-pinned
  with the old value in a comment; **QA's `OutcomePinM73bQaTests` (Stress, 20 id-free fingerprints equal to de591f0's)** is the
  outcome pin that must hold through any later renumbering; `HardKnobsPinTests` / `HardKnobsMatchTests`, `ScoutManagerTests` (311),
  `GatherBonusTests` (169 / 191), the premap AI replay (81 commands; its Research ids moved 4 → 3 and 7 → 5 by key);
  `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA 56,896 / 2v2 26,070);
  `MapSlice2StressTests` (49,565 / 55,485); `D21OpeningsQaTests` / `DH6ThresholdQaTests` (outcomes only); `HuntStressTests`
  (75,382 / 53,892 / 64,226); the Teblor-copy FFA hash 6C448DFB90134A32 (`FactionOrderQaTests`), the four `FactionOrderTests` copy
  pins (Stress).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's
  `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest`'s 1 ms frame row flakes under three-track load (passes on a rerun).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's order of work now:** sim M7-6 (BUG-1280, then Request 52) and data D30 (pins and a measurement,
  no shipped file) next session → data D31, the one landing (the Frenzy row, the ability's effect, the Anointing's +4 s,
  the buildings' `scale` 1.25, the chosen opening; one golden header move; M7 criterion 3 ticked there) → the balance
  pass (Requests 46 / 48 / 51 as measured) → models (the art call is answered: "look: go").

---

## Sim track

### Where we are
M7-4 on `main`. Counter **2 / 4**. Open from it: **BUG-1280 S3 (first in the next task)**, BUG-1281 S4. Debt: BUG-1220 /
1221 S4, the three `SaveFormat` alias names (BUG-1161 S4), BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010.

### Next task candidates
1. **M7-6: BUG-1280, then the per-opening reserve share (Request 52).** Feature, QA full (AI decisions).
   - **BUG-1280 first:** `Abilities/BloodOilScenes.Edit` looks the row and the effect up, then adds or edits (QA's
     `QA/FrenzyRulesQaTests` `BloodOil` copy is the model). Criterion: with docs/03's "For the data track (M7-4)" rows
     applied to a scratch copy of the shipped data, the M7-4 classes are 426 / 426; without them too (rule 58).
   - **Request 52:** an optional `attack.reserveArmyShare` on an opening in `ai.json` (validated as the `rules.json`
     field is), read by `AiPlayer.MayReserve`; absent = the `rules.json` `ai.reserveArmyShare`. Schema, validation, the
     content hash only when present (M7-4's treatment), a test opening only; no shipped `ai.json` edit (the data's D31).
   - **Rule 43:** no shipped opening carries the field, so no M5-5 row and no pinned match may move; the brief says so
     and the criterion is "every pin class identical". If the AI's saved state needs the value (it is derived from data:
     it should not), stop and report (rule 50).
   - Read before briefing: `AiPlayer.MayReserve`, `BuildOrderExecutor.Run`, docs/03 "Implementation (M5-4g)". QA's
     reading in the D29 report: the gate is closed while defending or while the army is below share × wave in both
     half-pop and worth; only a non-train step is reserved; Age II is reservable only once its two hall kinds are finished.
   - Fold BUG-1281: the two overloads without callers (remove, or pass the factor), the docs/03 sentence about the view.
   - Out of scope: `ProductionManager`'s empty-queue rule (measure first), Requests 46 / 48 / 51.
   - Size: small and clear (around 400 lines with tests).
2. Requests 46 / 48 / 51's remaining parts, each only when the balance pass's numbers ask for it.
3. Hardening at 4 / 4 (two sessions away): BUG-1220 / 1221, BUG-1161's names, BUG-1149 / 1151 / 1152.

---

## View track

### Where we are
M7-V3c on `main` (BUG-1260 fixed). Counter **3 / 4**. 99 scenes, `QaM7V3cTest` red on purpose (BUG-1290). Debt:
BUG-1230 S3, BUG-1177 item 2 (the owner's taste), BUG-1115 item 4.

### Next task candidates
1. **M7-V4: BUG-1290, the building's drawn scale, the art pass's first step.** Feature, QA standard.
   - **BUG-1290 first** (a few lines): Producer default: the capsule's height is never under twice its radius (such a
     type is drawn as a ball), and `DrawnHeight` returns that same number so the bar and markers follow. Criterion:
     `QaM7V3cTest` green, the scene loop 99 / 99, shipped units pixel-identical. The alternative (the loader bounding
     `scale` against `radius`) is the sim's and refuses valid data: not chosen.
   - **The building's `scale`:** the placeholder box (and later the model) drawn at `BuildingDef.Scale` about the
     footprint's centre, the footprint, the selection outline, the hit test and the ghost unchanged. No shipped row
     carries it yet: a forged copy with 1.25 on one faction's buildings, a screenshot looked at; rule 56's check on the
     shipped Teblor comes at D31's landing (say so in the brief). Decide and record: does the health bar / rubble box
     follow the scale (default: the rubble follows the footprint, the bar's height follows the scaled top).
   - **The art pass** if the PLAN finds its first step unblocked by "look: go" (docs/04; the look test of 2026-10-17 is
     the reference). If it needs an owner download, it goes to Waiting on you and this task is the first two items.
   - Never kill another track's Godot or `testhost` (rule 52).
2. The view's clean-up at 4 / 4 (the session after): BUG-1230 (long faction names), BUG-1177 item 2, BUG-1115 item 4.
3. An owner item (".NET 10 installed", templates, music, the sound notes) whenever one lands.

---

## Data track

### Where we are
D29 on `main` as tests and a fixture; nothing shipped moved. Counter **3 / 4**. Debt: BUG-1241 S3 (open until the
opening lands), BUG-1302 S4 (answered at ACCEPT: set its status to closed, "V25 named beside V24 in the 0546 log").

### Next task candidates
1. **D30: get ready for the one landing; no shipped file.** Feature, QA standard (scenario numbers).
   - Content tests (`Content/`, `QA/Content/`) taught the new fields before any row uses them: a `statusDuration` arm in
     `TechContentTests`; the description-number rules (D-H2) reading `damageBonus` and `onEnd`; the Teblor page pins
     ready for a Frenzy row with "+20% damage", "Slowed 20% for 4 s", "+4 s", and a buildings `Scale` column. Each
     proven on a data copy carrying docs/03's "For the data track (M7-4)" rows (the copy is made in the data's own test
     code; do not call `Abilities/BloodOilScenes`, BUG-1280).
   - `docs/factions/teblor.md` line 27 "drawn at 1.5× scale" → the M7-V3c rule (as wide as its room, `scale` the height),
     and the "not in the data yet" notes re-worded to "the rules exist (M7-4), the rows land with D31". No table cell
     that a pin reads changes.
   - **Re-measure with the Frenzy rows on the copy:** V0, V24, V25, V14 over the 40 pairings to 15:00
     (`TeblorOpeningHorizonReportTests`' harness), with and without the rows, plus the global reserve share at 0.5 on
     each: does the 10-15 minute collapse move, and does the ranking change? Measure and report (rule 48). Add seeds
     6-8 or the generated map only, since seeds 2 / 5 and 3 / 4 repeat on the named maps. Also V24 with the Stables at
     the shipped 8:15 (QA's unmeasured note).
   - Does the Normal AI cast Frenzy at all in these matches? Print casts per match; if 0, say what `ai.json` needs.
   - BUG-1302's status. Out of scope: `game/data/**`, the golden, any C# outside the two test folders.
   - Rule 41: the sim's M7-6 changes no shipped behaviour (the field is absent everywhere), so the figures hold on the
     merged tree; QA re-runs one variant there.
2. **D31: the one landing** (after BUG-1280's fix and Request 52 are on `main`): `common/statuses.json`
   (`teblor_frenzied`, the id as docs/03 says), `factions/teblor/abilities.json`, `techs.json` (+4 s),
   `buildings.json` (`scale` 1.25), `ai.json` (the chosen opening with its `reserveArmyShare`), the page and the pins
   both ways, new player-facing text quoted for the owner, the golden header once (rule 51: list the sim pin classes, run
   their Stress rows, the reason in `GatherWedgeQaTests`' comment). `common/statuses.json` is under `game/data/common/**`
   (the sim's folder): the PLAN names the file in the data brief and keeps the sim off it that session. M7 criterion 3
   is ticked at that ACCEPT; the view checks the buildings on a screenshot (rule 56).
3. The full balance pass (several seeds, both seat orders, the 0.75 m crowd figures and the aftermath-slow stacking note
   as inputs).
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, the counters, `goldShare`, BUG-0780,
   `counterStrength`).
