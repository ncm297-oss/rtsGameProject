# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-17-0816** (session 1 / 8 of 2026-11-17, started 08:16; it
ran about 26 hours: three tracks, QA full on the sim with one tests-only fix round, full-suite runs of 4-5 h under
shared load; the ACCEPT on 2026-11-18). Sim **M8-2** ACCEPT (feature, 1 fix round, QA full FAIL → PASS_WITH_ISSUES),
view **M6-V25** ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES), data **D33** ACCEPT (feature, 0, QA standard
PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 2 / 4,
view 3 / 4, data 2 / 4**. Bug ids next session: **sim 1620+, view 1630+, data 1640+ (20 ids)**. Open S1 / S2:
none. The next session is **1 / 8 of 2026-11-18**.

**Next session in one line (proposal):** sim **M8-3: the Archer's first shot from stealth and the Hounds' pack
bonus** (Request 56 G2 + G3; feature, QA full; test data only, no AI change) · view **M6-V26: the first building
models** (feature, QA standard, an uncertain slice of about 800 lines) · data **D34: owner tweaks first, then what
D33 cost** (BUG-1612 first; QA standard; the one hash move of the session).

**The default loop on `main` is green.** Expect **0 failed** (9,917 passed / 0 failed / 19 skipped of 9,936 in 42 m 23 s on the Producer's scratch clone of
sim b70604a + view bcb48e2 + data 95fd578). Any red row is a finding. **The Stress category has no expected red row
any more:** both `Bug0982_*` rows are green since D33.

**At the PLAN, first:** (1) the inbox (the owner may answer D33's review table: tweaks come before roadmap work);
(2) the default loop on `main`; (3) **D34 may move the data hash** (only if a number moves): the golden's header and
the two sim-owned data-hash pins go to the data developer by name (rule 51), the pin classes one a line (rule 71),
and **the sim's M8-3 ships test data only and adds no literal pin of the shipped hash or of a save's bytes on shipped
data (rule 73)**; (4) **M8-3 must not change Normal AI behaviour** (rule 41: D34 measures openings on `main`'s AI);
(5) the view's building slice adds LFS binaries only of types already in `.gitattributes` and no new third-party
pack (the Ultimate Fantasy RTS licensing row exists); (6) session length: this one ran 26 h. A tests-only fix round
is re-checked without Stress (rule 72); QA full on the sim runs Stress once; the data's QA runs scenario rows first
and its Stress rows alone; tell every developer again that nothing is written outside its worktree and its own
scratch folder (rule 63: a stray `fix1.patch` this session).

## Current session plan

**Session 2026-11-18-0926 (1 / 8 of 2026-11-18), planned 09:30. All three tracks GO, all feature.** Base 0317e7e.
Bug ids: sim 1620-1629, view 1630-1639, data 1640-1659. Producer decisions for M8-3 and M6-V26: docs/01
"2026-11-18 (b)". Everyone: record the PID of every `dotnet test` / Godot you start and end only those (rules 52 /
69); scratch folders are new and named `<task>-...`, and nothing is written outside your worktree and your own
scratch folder (rule 63); "build 0 / 0" means `--no-incremental` after the last edit (rule 64); run the full default
loop after the last edit or say in the first lines that you did not (rule 62); open questions go in the docs section
the task owns (rule 67). Developers cannot write `studio/**`. Smoke may fail once on stale `uid://` import state:
re-run it once before calling it a finding.

### Sim: M8-3, the first shot from stealth and the pack bonus, on test data only (feature, QA full)

Goal: build the two damage rules the Shadow roster still lacks (shadow.md gaps G2 and G3; M8 criteria 1 and 2): the
Shadow Archer's first shot from stealth deals +50 %, and a Hound of Shadow deals +10 % for each other Hound within
8 m, up to +30 %. Both are generic data hooks with their schema checks. No shipped unit carries them, so nothing in
a match changes.

Scope:
- Schema A: `units.json` `attack.fromStealthBonus` (a number above 0, at most a bound you name in `DataLimits`;
  absent or JSON `null` = none). A building's attack may not carry it (one error).
- Schema B: a fourth passive trigger `nearbyOwn` with `units` (the `onDeath` passive's rule: at least one, own
  faction, no repeats), `radius` (above 0, at most `DataLimits.MaxPassiveRadius`), `damageBonus` (above 0, bounded)
  and `maxStacks` (a whole number from 1 to a bound you name). The fields it does not use (`status`, `magnitude`,
  `duration`, `hpFraction`) are errors on it; `damageBonus` and `maxStacks` are errors on the other three triggers;
  one per trigger as today (`MaxUnitPassives` becomes 4; the trigger id is appended, existing ids keep their values).
- Rule A (docs/01 2026-11-18 (b) (a), (b)): a hit by a unit whose attack carries the field is multiplied by
  `1 + fromStealthBonus` when `World.IsStealthed(attacker)` is true (Stealthed and not Revealed) at the moment the
  hit's damage is worked out: a melee blow at the strike (`CombatSystem.QueueHit`), a shot at its landing
  (`ProjectileSystem.Strike`), exactly where `StatusSystem.DamageFactor` is read today. From any stealth source.
  The landing reveals the attacker as today, so the next hit has no bonus. A dead shooter's shot has none.
- Rule B (docs/01 2026-11-18 (b) (c)): a hit by a unit whose type has the passive is multiplied by
  `1 + min(n, maxStacks) x damageBonus`, n = the attacker's owner's other live units of the listed types whose
  centre is within `radius` of the attacker's centre (the boundary counts), counted through the spatial grid at the
  same moments as rule A. An ally's units, enemies, other types and the attacker itself do not count. A dead
  shooter's shot has none.
- The factors multiply (status bonus x first shot x pack) in one fixed order, through one helper that every place
  reading the status factor today calls (unit hits, building hits, splash), so no path forgets one (BUG-1281's
  lesson). Data without either field pays one flag test.
- **No stored state:** no new store field, save part or hash bit; save format stays 10, replay 9 / 6.
- Test data only (rule 58: a helper that forges a row tolerates shipped data already carrying it; put the copies
  beside `Vision/StillScenes`).
- Docs: docs/02 "Stealth and detection" (the first-shot rule) and the passives text (the pack rule); docs/03
  "Implementation (M8-3)" with "For the data track (M8-3)" (the two rows the Archer and the Hound will carry) and a
  "Questions for the Producer" list; and write the Producer's answers to M8-2's Questions 1-3 into docs/03's M8-2
  section (docs/01 2026-11-18: all three "leave it").
- OUT of scope: any byte under `game/data/**`; the AI (no rule, no think change, no estimate); the view and
  `ViewApi`; the 7-alive cap (G4), teleport, the Wraith row, ability `requires` (G5-G8); remembering "fired from
  hiding" on a projectile; `docs/factions/**`; any existing pin class, fixture, the golden, or any literal of the
  shipped data hash or of a save's bytes on shipped data (rule 73: the data track may move the hash this session).
  If you find a shipped rule or a pin must move, stop and say so.

Acceptance criteria:
1. Both schemas load; each bad row is refused with exactly one error naming the unit and the field (bonus zero /
   negative / over the bound; the field on a building's attack; `nearbyOwn` with a missing or bad `units`, `radius`,
   `damageBonus`, `maxStacks`; an unused field on it; `damageBonus` or `maxStacks` on another trigger; a second
   `nearbyOwn`). `null` reads as absent. New loader tests + `DataValidationTests` rows.
2. `git diff 0317e7e -- game/data` is empty; no existing pin class, fixture or golden file is in the diff; every
   existing test is green unedited (a test that pinned "three triggers" or `MaxUnitPassives` 3 may change that
   number only, named in the report).
3. Ranged: a Stealthed unit with `fromStealthBonus` 0.5 whose shot lands on a unit deals exactly the damage of
   factor 1.5 (the pinned number beside the same shot from a unit that is not Stealthed), is revealed by that
   landing, and its next shot deals the plain damage. Shown for forest stealth, stand-still stealth and a timed
   Stealthed status.
4. Melee: the same for a melee unit with the field: the first blow carries the bonus, the second does not.
5. The boundary: a shot whose shooter is no longer Stealthed at the landing tick (it lost hit points, its
   stand-still hold was let go after a move, it was Revealed by another hit of its own, or it died) deals the plain
   damage; one still Stealthed at the landing deals the bonus even when an enemy detects it. Each case one test,
   with the tick on both sides of the boundary pinned where there is one.
6. The bonus reaches a hit on a building and every victim of that shot's splash; a Stealthed unit without the
   field gets nothing.
7. Pack: with `damageBonus` 0.1 and `maxStacks` 3 the factor is 1.0 / 1.1 / 1.2 / 1.3 / 1.3 / 1.3 for 0-5 other
   listed units in reach (a table-driven test with pinned damage); a unit whose centre is exactly at `radius`
   counts, one just beyond does not; the attacker itself, another type, an enemy's, an ally's and a dead unit do not
   count.
8. All three factors multiply: one pinned row with a status damage bonus, the first shot and a pack of two.
9. Generic: each rule works on a unit of every shipped faction's test copy; on data with neither field the helper
   returns the status factor alone.
10. Determinism: twin runs agree on every tick's state hash over 4 seeds x 2,000 ticks, 3 players, random move /
    attack / stop / hold orders, with units carrying each field on every side; a save at every 50th tick, loaded
    and continued, equals the uninterrupted run. The diff adds no store field, save part or hash bit (save 10,
    replay 9 / 6).
11. No allocation per tick in a 200 v 200 fight of pack units (`[Collection(SerialCollection.Name)]`).
12. Measure and report (not pass / fail): the tick cost of that 200 v 200 fight with and without the passive
    (radius 8), alone under rule 42. No Perf threshold loosened.
13. `dotnet build RtsGame.sln --no-incremental` 0 / 0; default loop 0 failed, 19 skipped (the same 19); smoke
    PASS.
14. docs/02 and docs/03 say what the code does; M8-2's three answers are written in.

Design references: `docs/factions/shadow.md` "Unit notes", "Schema gaps (D-R38)" G2 and G3, Decisions; docs/02
"Stealth and detection" and the passives rules; docs/01 2026-11-18 (b); docs/03 "Implementation (M7-4)" (the
Frenzy's damage factor: where it is read is the model), "(M7-2)" (`onHit`: the passive loader's shape), "(M8-2)".

Tests required: loader + validation; scene tests for criteria 3-9 with pinned damage and ticks; an invariant fuzz
(every hit's damage equals the base damage times the product of the factors that hold at its moment; no hit by a
unit without a field differs from the base's); determinism and save / load twins; allocation; the report.

Constraints: no Godot in `Rts.Sim`; no stat in C# (0.5, 0.1, 3 and 8 are data); no allocation per tick; the count
goes through the spatial grid, in a fixed order; float factors multiplied in one fixed order; one public type per
file; test data only.

QA focus (full): the landing-tick boundary (reveal ordering in `HitUnit` / `HitBuilding`: the damage must be worked
out before the reveal on every path, and a second shot in flight when the first lands must not carry the bonus); a
lob and a splash shot; a shot whose shooter's slot was freed and reused by another unit (the bonus must not come
from the new occupant); the pack count at the exact radius, across spatial-grid cell borders and for the 0.75 m
radius class; units of the pack dying in the same phase 11 as the hit; team games (an ally's Hounds); rounding of
stacked factors against the damage formula (no off-by-one against a hand calculation); every path that reads the
status factor today also reads the new ones (melee, shot on unit, shot on building, splash, friendly fire); the AI's
`PlayerView` and shipped-data matches unchanged (a shipped-data twin against the base's state hashes over a full AI
match). Scale: 2,000 units with both fields, 4 seeds x 2,000 ticks. Stress once, then Perf alone (rule 42); after a
tests-only fix round no Stress re-run (rule 72).

### View: M6-V26, the first building models (feature, QA standard; a first slice of about 800 lines)

Goal: put the first real buildings on screen. Build the import path for a static building model from the
Ultimate Fantasy RTS pack already on disk and use it for two Malazan buildings, the Garrison Keep
(`malazan_garrison_keep`, 4 x 4 cells) and the Barracks (`malazan_barracks`, 3 x 3), with team colour, so the owner
can judge buildings beside the figures before the rest follow.

Scope:
- Tooling: a `buildings` list in `tools/asset-manifest.json` and a build step in `tools/art/` run by
  `tools/import-assets.ps1` (Blender 5.2; `asset-sources/quaternius-ultimate-fantasy-rts-2022-08/` of the main
  checkout, read only). Output per building: one `.glb` (one surface, one material named `team`, one atlas whose
  alpha is the team mask, as the units' since M6-V24) at
  `game/assets/models/malazan/<building id>/<building id>.glb`. Which source model (TownCenter / Barracks, First or
  Second Age, which level) and which parts take the player colour are your pick from the pictures; tint toward the
  Malazan palette (steel grey, oxblood, dull bronze), muted, grounded.
- View: a building type whose model file exists draws it when finished (found by id: docs/01 2026-11-18 (b) (e));
  scaled uniformly to fit its footprint from the data with a small margin, origin at the ground centre. A site
  keeps today's rising box; a remembered building under the fog draws the same model darkened as today's ghost box
  is. The hit-point / progress bar sits above the model's top; selection, picking, the build ghost and the outline
  work as today. Every other building type stays a box. Pointer files (no `git lfs pull`) and `--placeholder-art`
  give today's boxes with 0 `ERROR` lines.
- Scenes: one new scene for the two buildings (model shown when finished, box while a site, ghost, bar height,
  two player colours, the fallback); existing scenes that pin a building's box mesh or material either read the new
  accessor or switch models off, as the unit scenes did.
- Docs: docs/04 (a "Building models, as built" part: the manifest entry, the fit rule, the team parts, the
  licensing log row of the pack listing the source files used), docs/03 "Implementation (M6-V26)" with a
  "Questions for the Producer" list.
- OUT of scope: any file under `sim/` or `game/data/` (no `model` field for buildings; if you find you need one,
  say so in Questions); the other eight Malazan buildings, Whirlwind and Teblor buildings, construction-stage
  models, resource nodes, trees, props; the Teblor buildings' `scale` on a model (say in docs/03 how it will apply);
  a new third-party pack; a new binary type (`.glb` and `.png` only); BUG-1600 / 1601 / 1602 (the next task);
  changing unit figures.

Acceptance criteria:
1. Two manifest entries; a second run of the import leaves every model file (the units' too) sha256-identical.
2. Each building file: one surface, the `team` material, one atlas, at most 5,000 triangles (docs/04), texture at
   most 1024 x 1024.
3. In a match, a finished Garrison Keep and Barracks draw their models inside their footprints (no part beyond the
   footprint's edge by more than 0.25 m), standing on the ground; the scene checks the fitted bounds against the
   data's footprint.
4. A site of either type draws today's box and switches to the model the tick it is finished; a remembered one
   under the fog draws the model with the ghost's darkening; destroying it removes it. Scene rows for each.
5. The bar is above the model's highest point for both types; a click on the model's footprint selects it as
   today.
6. Team colour: the same building in two player colours reads apart at the default zoom; the scene checks that
   team texels take the player colour and the others do not, in four colours.
7. Pictures, looked at by you: both buildings at the default zoom and close up, in two player colours, with a
   Laborer and a Heavy Infantry figure beside each for scale; a site, and a remembered one under the fog. The
   report says what you picked and why. The sheets go in a scratch folder named in the report (the Producer copies
   them to `studio/review/2026-11-18-0926/`).
8. The bench alone (rule 42): 400 mixed figures with 60 buildings (30 of each) on screen, beside the same window's
   boxes; and rule 61's 400- and 1,000-figure rows with the capsule figure. Over 16.7 ms a frame for 400 figures
   with 60 building models fails.
9. Pointer files and `--placeholder-art`: boxes, 0 `ERROR` lines.
10. Only `.glb` and `.png` binaries added; the art pass's binaries' total size reported; the pack's licensing-log
    row names the source files used, in the same commit.
11. Nothing under `sim/` or `game/data/` in the diff.
12. Build `--no-incremental` 0 / 0; default loop 0 failed; smoke PASS; scene loop all green (117 + the new one).
13. docs/04 and docs/03 "Implementation (M6-V26)" updated.

Design references: docs/04 (style guide: grounded, muted world and saturated teams; "Scale and dimensions":
buildings fit their footprint with a small margin; "Team color"; "Buildings, nature, props"; "Directory layout and
naming"; "Import process"; "Unit models, as built" for the atlas and mask); docs/03 "Implementation (M6-V24)" and
"(M7-V4)" (the buildings' drawn `scale`); `game/data/factions/malazan/buildings.json` (ids, footprints);
`game/scripts/BuildingViews.cs`, `UnitModels.cs` (the model of the loader and the fallback).

Constraints: views hold no gameplay state; no player-facing text in C#; the model is found from the data's ids,
never a list of building names in C#; counts of model types come from the manifest, never a literal;
`tools/asset-manifest.json`, `tools/import-assets.ps1` and `tools/art/**` are the view's; nothing is downloaded.

QA focus (standard): a fresh-clone import is byte-identical; the fit on both footprints and on a map's terraces
(a model sunk into or floating over the ground); the site-to-finished switch, a building destroyed while a site and
a finished one remembered under the fog then razed out of sight; team colour leaking onto walls or missing in four
colours, Bone and Crimson included; the bar and the selection outline against the model's size at the closest and
farthest zoom; units walking beside the model (figures hidden behind it or clipping through an overhang); the
pointer-file fallback; a scene that pins a count as a literal. The bench last and alone; the rows that flake under
load re-run alone.

### Data: D34, Easy's lost upgrade and the pages after D33 (feature, QA standard; at most one data-hash move)

Goal: D33 brought the Teblor AI's whole army out but cost the Easy AI its one upgrade (the Anointing 35 -> 1 of 64
matches) and Shamans on Easy and Hard (BUG-1612). Try to win it back by the order and times of the steps alone, and
make the pages say what `main` does (BUG-1613 and the small items).

Scope (in this order; cut from the end if the budget is short and say so):
- A. BUG-1612: `game/data/factions/teblor/ai.json` `standard`, order and step times only (the Shaman's Circle and
  the Anointing ahead of the three new train steps; the Anointing's time earlier). Screen at least three and at
  most five variants on a reduced row first (Easy and Normal), then run the full 64-seat row of
  `D33TeblorRowQaStressTests`' shape for Easy / Normal / Hard on the one candidate.
- B. BUG-1613: teblor.md's "was" column printed against `main`'s row (QA's numbers are in the bug file) or the
  column relabelled, the AI section's short paragraph too; the `standard` tooltip softened so it promises only what
  most Normal matches show (or the figure it rests on measured and printed). If A ships a variant, the page and the
  tooltip follow that variant's row.
- C. Small items: `KnownBeatenAlive` seed 19 with its `DR33QaTests` pin; BUG-1510 item 1 (two long lines); BUG-1421
  items 1, 2, 5 (close each as fixed, as "waits for Request 55", or as wontfix with the reason on the page).
- D. Docs only: `docs/factions/shadow.md` says gap G1 is built (M8-2: the field and its row from docs/03 "For the
  data track (M8-2)"), keeping `Content/PagePinReadinessTests` green; the art notes on malazan.md and whirlwind.md
  say what the figures on `main` show (BUG-1602's data half; the figures are in `studio/review/2026-11-17-0816/`
  and docs/04 "Fourth slice (M6-V25)").
- Print the Hard v Normal tally if a variant ships; tune nothing for it.
- Files: `game/data/factions/teblor/ai.json` (the only data file that may change); `docs/factions/teblor.md`,
  `malazan.md`, `whirlwind.md`, `shadow.md`; `Content/**`, `QA/Content/**`. **Only if a variant ships (the hash
  moves),** handed over by name, values and "was" comments only (rules 51 / 71): the golden
  `cross_map_seed1.replay` header and checksum (the reason in the `GatherWedgeQaTests` comment);
  `Data/FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`;
  `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`; and each of these, one line each in the report with
  "re-measured: yes / moved to ... / unchanged":
  - `MatchCriterionTests` (+ `CriterionMatch`, `HalfWay`)
  - `StandardPresetPinQaTests`
  - `NonHardMatchPinQaTests`
  - `CounterCompositionTests.TheNonHardPins_*`
  - `ReserveTwinTests`
  - `SharedVisionFfaPinTests`
  - `OutcomePinM73bQaTests`
  - `HardKnobsPinTests`
  - `CliTests.Match_TheLiteralRow_*`
  - the premap AI replay, `save_ai_seed2`, `seed21_playable.replay`
  - the Hard v Normal tally, docs/03's M5-5 table and bound comments
  - Stress: `AiDeterminismTests`
  - Stress: `HuntStressTests`
  - Stress: `M6Sh2DeterminismStressTests`
  - Stress: `M6Sh2QaStressTests`
  - Stress: `M6S12DeterminismStressTests`
  - Stress: `StarvedSaveTwoProcessQaStressTests`
  - Stress: `ReservePairingsStressTests`
  - Stress: `MatchRowsM6S10Report`
  - Stress: `MatchCriterionSweepQaStressTests`
  A view scene red only because its save / replay fixture carries the old data hash: regenerate that fixture file
  and name it in the report; nothing else under `game/`.
- OUT of scope: `game/data/common/**`, the other factions' `ai.json`, units' stats and costs, any other C# (a
  missing rule goes in the report as evidence for Request 55); the Shadow roster's files; shadow.md's G2 / G3 rows
  (the sim builds them this session; next task); tuning for the Hard v Normal tally; the waves D33 set (they wait
  for the owner's answer).

Acceptance criteria:
1. The report prints, for every variant tried, Easy / Normal / Hard rows (defeated / 2nd Longhouse / Shaman /
   Breaker / Rider / War-dog / Anointing) beside `main`'s, and says which rows are the reduced screen and which the
   full 64.
2. A variant ships only if, on the full rows, Easy's Anointing is back to at least 20 of 64 while on Normal and on
   Hard each of Breakers, Riders and War-dogs stays at 8 or more, Normal's Shamans at 14 or more, its second
   Longhouse at 9 or more and its "beaten" count at 15 or less. If none reaches that, nothing under `game/data/`
   changes, the criterion is met by the table and the cause, BUG-1612 is marked "waits for Request 55" with the
   best variant named, and the hash does not move.
3. BUG-1613: every "was" figure on teblor.md is `main`'s (or the column says what it is), in the table and in the
   AI section's paragraph; the tooltip's claims hold in most Normal matches by a printed figure, or the text is
   softened; the page quotes the tooltip as the file has it.
4. The small items of C are each closed or stated with a reason; pinned figures stay pinned
   (`DR33QaTests`).
5. shadow.md lists G1 as built with its field; `PagePinReadinessTests` green; the two art-note sections match the
   figures.
6. If the hash moved: the new hash stated; the golden header and the two hash pins regenerated; every class in the
   list re-measured once (Stress classes run alone, PIDs recorded), old values kept in "was" comments; a pinned
   criterion row that stops ending moves to the nearest seed that ends (rule 45) and the report says so; no bound
   raised; the report says saves and recordings from before stop loading. If it did not move: `git diff 0317e7e --
   game/data` is empty and the report says so.
7. The pages' cells, prose and the opening's `description` match the shipped file and the measured tables; every
   `Content/**` and `QA/Content/**` class green.
8. The report carries the owner's review table (opening / field, old -> new, one-line why) and quotes any changed
   player-facing text old -> new.
9. Build `--no-incremental` 0 / 0; default loop 0 failed, 19 skipped; smoke PASS; scene loop green if anything
   under `game/` changed.

Design references: docs/02 "AI opponent" (openings, the reserve, the army mix); `docs/factions/teblor.md` (AI build
order, "The Age II army (BUG-1420, D33)", the opening's tooltip), `malazan.md` and `whirlwind.md` (art notes, the
lines of BUG-1510), `shadow.md` "Schema gaps (D-R38)"; docs/03 "For the data track (M8-2)"; BUG-1612, BUG-1613,
BUG-1510, BUG-1421, BUG-1602; Request 55 in `studio/STATE.md`.

Constraints: no C# outside the two content test folders except the pin values named above; player-facing text only
in data; at most one hash move; `teblor/ai.json` is this track's alone this session; the sim's M8-3 does not
change the AI, so openings are measured on `main`'s AI.

QA focus (standard): scenario rows first, Stress rows alone. Re-run the full Easy / Normal / Hard rows of the
shipped state independently and compare with the report; check the fallback was not taken where an order would have
done (try one order of your own); Normal and Hard must not lose what Easy regains; every "was" figure against
`main`; the tooltip against what the AI does in a match; pages against files both ways; if the hash moved, every
listed pin class really run (rule 71) and a save from `main` refused cleanly.

## Where things stand

- **`main` after this session = sim b70604a + view bcb48e2 + data 95fd578** (plus this ACCEPT's studio and docs
  files). The Producer's scratch clone `prod0816-accept` (code merged clean; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`): build `--no-incremental` 0 / 0, default loop **9,917 passed / 0 failed / 19 skipped of 9,936 in 42 m 23 s**,
  smoke PASS, scene loop **117 of 117, first pass**.
- **The data hash moved: 318E8C4256456C05** (was B74DC7AE61B4862D). Saves and recordings from before are refused
  `DataMismatch` (QA: an AI-seat save and recording from `main`, cleanly). Formats unchanged (save 10, replay 9 / 6).
  The golden `cross_map_seed1.replay`: header and checksum only.
- **Stand-still stealth (M8-2), on test data only:** `units.json` `stillStealth { status, delay }` →
  `UnitDef.StillStealthStatus` / `StillStealthDelayTicks`; `StillStealthSystem.Run` in phase 5 after
  `ForestStealthSystem`, before `StealthSystem.Run`; `StillStealthStore` (`UnitStore.Still`, null without the
  field); save part `STIL` after `REGN` / `FRST`; state-hash bit 23; `StatusSystem.Holders` (0-2) counts the rules
  holding a Stealthed entry (`TicksRemaining` = timed part + holders). Test copies: `Vision/StillScenes`. Cost: 500
  moving units +13.9 % when all can hide (the detection pass's work, not the rule's). The data track's row is in
  docs/03 "For the data track (M8-2)".
- **The AI's openings (D33):** `cadre_rush` 16 / 1,184, `holy_war` 14 / 918, the Teblor `standard` with three train
  steps ahead of the expand step (29 steps; fixture `Content/Fixtures/teblor_standard_d33.json`). Teblor 64-seat
  rows: Normal Breakers / Riders / War-dogs 12 / 22 / 39, Shamans 25, 2nd Longhouse 23, beaten 10; Hard 35 / 43 / 48;
  Easy 27 / 36 / 51 but the Anointing 35 → 1 (BUG-1612). Hard v Normal **34 / 5 / 3** (Raraku 13 / 1 / 0, Vathar
  14 / 0 / 0, Pale Hills 7 / 4 / 3).
- **Pins moved by D33 (rule 45):** `MatchCriterionTests` FFA row Vathar **seed 1** (31,991; seed 2 no longer ends:
  BUG-1610), Raraku seed 3 28,915; `M6Sh2DeterminismStressTests` **Raraku seed 5**; `ReservePairingsStressTests`
  `NoWaveSides` 1; two rows each in `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`,
  `CounterCompositionTests.TheNonHardPins_*`, `OutcomePinM73bQaTests`. Unchanged: every other class on rule 71's list.
- **The view (M6-V25):** 15 unit types are figures, 6 capsules (`ModelViewTest` names them). **117 scenes**
  (`FootModelTest`, QA's `QaM6V25Test`; `QaM6V25Shot` is a windowed tool). Bench alone: 400 figures 6.88 ms, 1,000
  figures 17.64 ms. The art pass's binaries 36.7 MB.
- **Skipped / known rows:** the default loop's skips are the standing ones less `Malazan_Bug0982Prose_*`
  (un-skipped). `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall` still skipped. `AliveWithoutAWave` is
  empty. The five `KeepCornerCureQaStressTests` rows (BUG-1463) still skipped.
- **Requests:** 58 open (dropped queued orders for the view); **55 has new evidence** (D33 cured BUG-1420 by file
  order because a waiting expand step holds the reserve against every later step; BUG-1612 is the same cause).
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's
  list; nobody touches the owner's checkout).
- **The conductor started each track's QA when that track's build finished** (not after all three). Keep it; timing
  rows are judged alone (rule 42).

## Standing rules (1-73)


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

**68 (amended 2026-11-16-2036, BUG-1552):** the content classes handed to a sim developer whose task moves Normal AI
play while the data track is stopped are seven: `Content/AiOpeningPairsReportTests`,
`Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
`QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`, with the pages' cells
they read (Age II tables, "Houses at the cap", "Spending on the menu's map", the holy war's first wave, the Teblor
opponent sentence).
**71 (new, 2026-11-16-2036):** a pin-move brief lists the pin classes one a line, and the developer's report carries
the same list with "re-measured: yes / moved to ... / unchanged" beside each, Stress pin classes included (M6-S12:
the four-seat FFA pin was on the list and never run: BUG-1559, S2, one fix round). Such a task gets 20 bug ids
(M6-S12 used all ten). When rule 45 moves a row onto a seed another row already plays, the report says so.
**72 (new, 2026-11-17-0816):** after a fix round that changed tests or docs only, QA's re-check runs the touched
classes, the default loop and the `--no-incremental` build; it does not re-run Stress or Perf (M8-2's re-check spent
4 h 46 m on a full loop for three test files). Rule 70's "no Stress re-run" holds for every tests-only round.
**73 (new, 2026-11-17-0816):** no new test pins the shipped data hash, or the bytes / digest of a save made on
shipped data, as a literal (rule 49 widened: BUG-1580, S2, one fix round). The hash is read from the golden header;
"byte for byte what `main` writes" is proven in-session against the base and kept as a differential row
(`StillStealthSaveBytesTests` is the model). A brief never states the shipped hash as a pass / fail literal in a
session where another track moves it.


## Watch-outs (all tracks)
- **New this session: a unit type may carry `stillStealth`.** No shipped unit does. A test that forges it uses
  `Vision/StillScenes` (rule 58). With the field anywhere in the data, every world allocates `UnitStore.Still`, the
  save gains a `STIL` part and a held unit's Stealthed entry shows `TicksRemaining` = timed part +
  `StatusSystem.Holders`: a test that reads a Stealthed entry's ticks on forest or stand-still data must subtract the
  holders.
- **New this session: the AI's openings changed (D33).** `cadre_rush` waves at 16 pop, `holy_war` at 14, the Teblor
  `standard` has 29 steps and trains a Breaker, a Rider and a War-dog pack before its expand step. In `ai.json` a
  step after a waiting expand step spends only above the expand's reserve (Request 55): **file order is behaviour**.
  The FFA criterion row is Vathar seed 1; the retrain twin is Raraku seed 5.
- **`ModelViewTest` pins the capsule types as an exact set of six** (the criterion's own list). A new unit type
  without a model (the Shadow roster, a test faction on shipped data) turns it red: the task that lands one hands the
  view that list (rules 54 / 55 rehearsal).
- **Session length:** 0816 ran about 26 hours (three default loops of 1-2.7 h, two Stress runs of 2.6 h and 4.8 h,
  the data's pin runs; QA started per track as builds finished). Rule 72 removes the second Stress run. Perf was
  evidence only in the first inspection (the re-check hit the PC's slow state; base failed the same rows).
- **Smoke can fail once on stale import state** (`Can't find file 'uid://...'` / `Unrecognized UID` lines in a
  worktree's `.godot` cache after another branch's assets were merged): the re-run passes. Two developers saw it this
  session; it is not a finding unless the second run fails.
- **Pinned matches after D33** are listed under "Where things stand"; the older "Pinned matches on `main` after
  M6-S12" bullet below is right except for those rows.
- **The default loop is green on `main`** and **the Stress category has no expected red row** (both `Bug0982_*`
  are green since D33). `main` builds **0 errors / 0 warnings**.
- **New this session: the AI's rally point may differ from `hall + rallyDistance`.** A test that stages an AI seat
  and expects its army at the nominal point must keep one-cell lanes (between its own buildings too) more than
  `ai.rallyReach` away, or read `Board.Rally`. A rebalance now goes to the node nearest the worker's nearest finished
  drop-off; a blind seat (phase 2, short of a resource, no live node known, an idle worker) sends a scout on the
  first think of an `ai.scoutAfter` window: a staged seat that must sit still needs a known node of both kinds or no
  idle worker.
- **A construction site is not a drop-off** (M6-SH2). A test or scene that places a drop-off building and expects
  deliveries there must finish it first; a loaded worker with only a site standing waits in state `Returning`. The AI
  retrains a worker when it has none: a test that keeps a seat worker-less must also keep it without the gold
  (`HuntTests.TheRingLeg_*` is the model).
- **Unit models are one surface now.** A scene that pinned a surface count or a team surface index reads
  `UnitModels.AtlasOf` / the one `team` material (`ModelViewTest`, `TeblorModelTest`, `QaM7V5Test`,
  `OverlayLineTest` were updated). A new model type goes through `tools/art/build_unit.py` `build_atlas`; the role
  PNGs no longer exist. Team texels are lit Burley (1-4 levels brighter than before M6-V24): a pixel pin from
  before is stale.
- **Session length:** 2036 ran about 11 hours (Stress 4 h 17 m beside the default loop; the view's default loop
  2 h 43 m under that load). The Perf category was not evidence (the PC's slow state: `ASweepRetarget` 78 us).
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
- **The data hash moved (318E8C4256456C05, D33):** every save and recording fixture from before is refused. The next
  hash move is the data's D34 (only if a number moves): never two tracks in one session.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 117 scenes** (`FootModelTest`, QA's `QaM6V25Test` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Fifteen unit types are models** (the eleven before plus Cadre Mage, Sapper, Priest, Zealot); six are capsules
  (`ModelViewTest` prints "15 models, 6 capsules" and names them: Wickan Lancer, Horse Raider, Catapult, Battering
  Ram, Destrier Rider, War-dog). A count of model types is read from `tools/asset-manifest.json`, never a literal. A
  model file holds the 1.84 m base figure; the data `scale` is applied by the view. Bars and marks sit over the figure
  as drawn (`UnitViews.OverlayLines`); robes, veils and satchels are rigid boxes on a bone (a leg shows through).
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

- **Pinned matches on `main` after M6-S12 (every one re-measured once, old values in "was" comments; docs/03
  "Implementation (M6-S12)" item 6 lists old -> new):** the Hard v Normal tally **34 / 7 / 1** (Raraku 13 / 1 / 0,
  Vathar 12 / 2 / 0, Pale Hills 9 / 4 / 1); `MatchCriterionTests` (Vathar **seed 1** 23,244 / Raraku seed 3 27,394 /
  Pale Hills seed 1 33,230 / generated **seed 1** 21,876 / FFA 38,690 / 2v2 61,686; `HalfWay` 11,622);
  `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`,
  `ReserveTwinTests`, `SharedVisionFfaPinTests`, `OutcomePinM73bQaTests`, the premap AI replay, `save_ai_seed2`,
  `CliTests.Match_TheLiteralRow_*`; unchanged: `HardKnobsPinTests`, `seed21_playable.replay`, the golden. Stress:
  `AiDeterminismTests` (`HuntSeed` 2: ends 51,218, 2 hunts; its comments still carry M6-SH2's figures),
  `HuntStressTests` (`MidHuntSeed` 2; **the four-seat FFA oracle row seed 2**, 59,951, the same match as
  `FourAiSeats_OnPaleHills_Seed2_KnownStall`: drop that twin at the next pin move), `M6Sh2DeterminismStressTests`
  (the retrain twin on **Raraku seed 3**), `M6Sh2QaStressTests` (the save sweep on **Raraku seed 2**),
  `StarvedSaveTwoProcessQaStressTests` (Raraku seed 1), `ReservePairingsStressTests` (`KnownNoAgeTwo` (2, 0, 1, 1)),
  `MatchRowsM6S10Report` (seeds 1-8 on the three maps: all end). The next sim task that changes Normal AI behaviour
  or gather movement re-measures all of them (rules 43 / 45 / 71).
- **Known stalls / skips with ids on `main`:** the four-seat Pale Hills FFA seed 1 (BUG-1068 / 1116 / 1559: the
  map's gold is gone by 30,000), seeds 3 and 4 (BUG-1536, the owner's question), seeds 6 and 7 (no end by 144,000,
  measured in M6-S12's fix round, same shape); the five `KeepCornerCureQaStressTests` rows (BUG-1463);
  `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's `QaM7V1Test`
  zoom-40 1.5 head row; `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.

---

## Sim track

### Where we are
M8-2 on `main`. Counter **2 / 4.** Fixed in-session: BUG-1580 (S2). Closed by ruling: BUG-1581 (S4, wontfix). New
debt: BUG-1582 (S3, `RampWallQaTests`' 10 s wall clock), BUG-1610 (S3, Vathar FFA seed 2 stands). Carried: BUG-1555,
1554 / 1556 (ruled, the AI follow-up), BUG-1557 (S3), BUG-1558 (S4), BUG-1463, BUG-1536 (the owner's question),
BUG-1537, BUG-1539, BUG-1490 (with the next save change), BUG-1434, 1433, 1161, 1149, 1152, 1150, 1010. The
Producer's answers to M8-2's Questions 1-3 are in docs/01 (2026-11-18): all three "leave it".

### Next task candidates
1. **M8-3: the first shot from stealth and the pack bonus** (Request 56 G2 + G3; M8 criteria 1 / 2; feature, QA
   full; test data only; no AI change, so no pin moves). G2: `units.json` `attack.fromStealthBonus` 0.5, a hit whose
   attack was started while the attacker was Stealthed deals x1.5 (decide and document "started": the swing's start
   tick, held with the pending hit, so the reveal at landing does not cancel it). G3: a passive
   `{ trigger: nearbyAllies, units, radius, damageBonus, maxStacks }`, counted when the attack lands, through the
   spatial grid. If the design of either is not clear from shadow.md, do G2 alone. Write M8-2's answers into docs/03
   (rule 67). Rule 73 in the brief. If a save part is needed it is conditional like `STIL` (format stays 10).
2. **The AI follow-up (QA full, one pin move, rules 43 / 45 / 68 / 71; only in a session where the data track does
   not measure openings):** BUG-1555 first; the saved count of blind re-scouts lost capped by `ai.scoutReplacements`
   (BUG-1554 / 1556; the save format changes, so BUG-1490's one-trip flag rides with it and the brief names the view
   scenes that pin save bytes: rule 50); **Request 55** (a waiting expand step stops holding the reserve against
   later steps, or a step may name itself exempt: D33 and BUG-1612 are its evidence); one trace of BUG-1610 (seats 2
   and 3 after 34,000); BUG-1558's three edges; `ScoutCapQaTests.KnownWorkerLosses`' label; the seed-2 twin row of
   `HuntStressTests` dropped; `AiDeterminismTests`' comments.
3. **Request 58** (dropped queued orders for the view; small).
4. Hardening items when the counter reaches 4 / 4: BUG-1582 (replace the 10 s `Task.Delay` race with a generous
   limit or `[Collection(SerialCollection.Name)]`), BUG-1557, BUG-1539.
5. Requests 54, 53 part two, 56 G4-G8. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-V25 on `main`. Counter **3 / 4** (one more feature session, then the view's hardening). 117 scenes. New debt:
BUG-1600 (S3, readability in player colours), BUG-1601 (S3, the Sapper's throw is not tied to the shot), BUG-1602
(S4, figures against the faction pages' art notes). The Producer's answers to Questions 1-5 are in docs/01
(2026-11-18): 1, 2, 5 accepted; 3 (the Priest's veil) and 4 (the Zealot's idle blade) are with the owner.

### Next task candidates
1. **M6-V26, the first building models (feature, QA standard; uncertain: about 800 lines, first part only):** the
   import path for a static building from `quaternius-ultimate-fantasy-rts-2022-08` (one surface, team colour by the
   same atlas mask, footprint and height from the data's `scale` / footprint, a construction-site look kept), and
   two Malazan buildings (the Keep and the Barracks, or whichever two the pack fits best); pictures at the default
   zoom beside the figures in two colours; the bench with 60 buildings beside 400 figures. Pointer files and
   `--placeholder-art` keep today's boxes. No file under `sim/` or `game/data/`.
2. **The view's hardening (after V26, counter 4 / 4):** BUG-1600 (a darker hood or trim for Bone, a non-crimson veil
   and wrap or a rule that props never use a setup colour, the staff head clear of the bar), BUG-1601 (one throw per
   shot: the attack clip seeked from the sim's attack progress as the cast clip is), BUG-1602 with BUG-1613's item
   for the data track (the pages' art notes say what the figures are), the load-flaky 1 ms rows.
3. The view's half of Request 58 once the sim exposes it.
4. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner).
5. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...").

---

## Data track

### Where we are
D33 on `main`. Counter **2 / 4**. Fixed: BUG-1420, BUG-1553, BUG-1524. New: BUG-1612 (S3, Easy and Hard lose the
Anointing and Shamans), BUG-1613 (S4, the Teblor page's "was" column and the tooltip's promise), BUG-1611 (S4, one
Malazan side falls without Age II), BUG-1610 (S3, the sim's). Open before: BUG-1421 S4 (items 1, 2, 5), BUG-1510
S4. The Producer's answers to D33's Questions are in docs/01 (2026-11-18): the re-stated rule (16), 14-pop holy war
and the 195-character tooltip accepted; the reserve question is Request 55.

### Next task candidates
1. **D34 (QA standard; one hash move if a number moves; the sim's M8-3 does not touch the AI):** (0) the owner's
   answers to D33's review table, if any. (1) **BUG-1612:** try order only in `teblor/ai.json` `standard` (the
   Shaman's Circle and the Anointing ahead of the three new train steps; the Anointing's time earlier) and print
   Easy / Normal / Hard in `D33TeblorRowQaStressTests`' shape; ship a variant only if Easy's Anointing is back to at
   least 20 of 64 with Normal's and Hard's Breakers / Riders / War-dogs each still at 8 or more; otherwise the table
   and "waits for Request 55". (2) BUG-1613: the page's "was" column against `main` (QA's row has the numbers), and
   either measure "takes part in a wave" or soften the tooltip. (3) `KnownBeatenAlive` seed 19 with its `DR33QaTests`
   pin, BUG-1510 item 1, BUG-1421 items 1, 2, 5. (4) BUG-1602's data half: the art notes on malazan.md and
   whirlwind.md say what the figures show (docs only). Print the Hard v Normal tally, tune nothing for it. Pin
   classes one a line (rule 71); the owner's review table in the report.
2. The Shadow roster as Request 56's schemas reach `main` (G1 is there; G2-G8 are not); the "Orders full" text once
   Request 58's key exists.
