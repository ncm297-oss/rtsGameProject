# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-11-06-0326** (session 1 / 8 on 2026-11-06; base `0cd8031` = `origin/main`). All three
tracks **GO**: sim **M6-S8** (map slice 2: Pale Hills + Vathar Crossing + the mirror helper + BUG-1057), view **M6-V19** (BUG-1092 + the map's
own counts + `--map-size` warned), data **D24** (step 0 the merge of `origin/studio/2026-11-05-2148-data`, then BUG-1102 items 1-2 and the
id-based map-text pin). **Open S1 / S2: none.** `main` 0cd8031: build 0 warnings; non-Perf and smoke rerun by the Producer at this PLAN (see
the session log). Golden header **30C51BB619A7E650** (informational since M6-SH1; the data branch's 742D3E95CF629116 must not land).
Bug ids next: **sim from 1060, view from 1094, data from 1103.** Counters before: **sim 0 / 4, view 3 / 4, data 3 / 4** (after this session
1 / 4, 4 / 4, 4 / 4: **view and data hardenings next session**).

## Where we are

- Playable from the menu: 1v1 to 2v2 against AIs, Standard / High, teams, speed, the Map list (Generated / Raraku). On `main` Raraku's and the
  presets' tooltips still read the placeholders (D22 + D23 land with D24's step 0).
- **M6 4 / 10.** Criterion 7 needs Pale Hills + Vathar Crossing (this session's M6-S8), their final text (D25 next session) and the mirror
  tooling (M6-S8). The rest of M6 waits on the owner (art call, music / fonts / export templates) or the export script.
- **Cross-track rule this session (the Producer's, both briefs carry it):** the data branch's `MapTextContentTests` pins the map count and
  reads `Data.Maps[0]` as Raraku; `pale_hills.json` sorts before `raraku.json`, so after the sim merges the data's tests would go red. D24
  makes the pin **id-based** (pin only the ids it lists, look Raraku up by id, keep the checker over every map); M6-S8 ships the two new maps
  with **provisional descriptions that pass the checker** (the exact sentences are in the sim brief). The conductor merges sim → view → data.

## Standing rules (1-38)

Unchanged (38: the Producer runs every suite on a scratch `git archive` export, never in a worktree).

---

## Sim track

### Current session plan

**TASK_ID M6-S8 · Map format slice 2: Pale Hills, Vathar Crossing, the mirror helper, BUG-1057 · feature · QA full · counter 0 / 4 → 1 / 4**

Goal: ship the two remaining hand-made maps of M6 criterion 7 as `game/data/maps/pale_hills.json` (4 players, 256, forest) and
`game/data/maps/vathar_crossing.json` (4 starts for 2-4 players, 256, steppe), produced by a mirror helper in `tools/Rts.Cli` so their symmetry is
by construction, each proven playable by a Normal v Normal match to a result; and fix BUG-1057 so the scout cap survives a hall whose centre
is a scout grid centre (a 256 map's quarter points), which these maps can reach.

Scope:
- `tools/Rts.Cli` command `mirror-map <in.json> <out.json> --symmetry point|quad`: reads a map file whose `terrain`, `ramps`, `mines`, `forests`
  and `starts` describe one half (point: the north-west half mirrored through the centre, x → size−1−x, y → size−1−y, as Raraku) or one
  quarter (quad: the north-west quarter mirrored across both axes, 4 starts), writes the full file (ramps' `up` flipped with the mirror), then
  loads it through `DataLoader` and prints its errors (exit 1 on any). The helper is a dev tool, not shipped game code; its source quarter /
  half files live under `tools/Rts.Cli/maps/` (or a sibling folder the dev names), not under `game/data/`.
- **Pale Hills** (`id` `pale_hills`, `displayName` "Pale Hills", `players` 4, `size` 256, quad symmetry): docs/02 "Map and terrain" row:
  "Dense woods with paths between; favors Shadow ambushes" — forest biome: most of the map is tree rows (`T`) with corridors of open ground
  between the camps, hills of level 1-2 with ramps along some paths; each start with its two mines and woodlot within the start contract;
  gold scarce beyond the starts (one expansion pair per quarter), wood everywhere. Starts in the four quarters (ordinal order so starts 0 and 1
  are **diagonally opposite** and 2 and 3 the other diagonal: a 2-player match on it is fair). Pin: at least half the passable cells on open
  ground (`.`), so armies can move; the flood proves every passable cell reachable.
- **Vathar Crossing** (`id` `vathar_crossing`, `displayName` "Vathar Crossing", `players` 4, `size` 256, quad or point symmetry, the dev's
  call): "A river of slopes with fords as chokepoints" — the Producer's reading: the ground is level 1, a **river band of level 0** runs
  across the map (winding is fine), crossed only at **fords**: ramps down one bank and up the other (2-4 fords in all), so a crossing is a
  chokepoint and low ground can't see the banks (docs/02 "High ground"). Starts: two per side of the river, ordered so 0 and 1 face each
  other **across** the river. Each start with its mines and woodlot on its own bank; expansions near the fords.
- Provisional `description`s (the data track replaces them next session; **use these exactly, they pass the data's checker**: ≤ 200
  characters, no digit, no "placeholder", none of the words Malazan / Empire / Imperial / Drill / Whirlwind / Apocalypse, ASCII, a capital
  first letter, a full stop last, no double space):
  - Pale Hills: `Four camps in dense hill woods, joined by narrow paths between the trees. Timber is everywhere, gold is scarce beyond the camps, and every path is a place for an ambush.`
  - Vathar Crossing: `A sunken river of slopes cuts the steppe in two, crossed only at its fords. Hold a ford and the far bank is yours; lose it and the enemy pours through.`
- **BUG-1057 rider:** make `ScoutManager.SentAgain`'s mark readable when the hall's centre is exactly a grid centre (e.g. lay the mark a quarter
  cell off the centre when it would coincide, `HomeGridPoint` / `SentAgain` agreeing; or another saved fact with no new byte). No save format
  change; QA's skipped row `ScoutSentAgainQaTests.EveryScoutKilledAtTheDoor_TheCapHolds_WhenTheHallStandsOnAGridCentre` un-skipped and green
  (3 scouts, not 9); the save rows `ASaveBetweenTwoScoutDeaths_*` / `ASaveWhileAScoutSentAgainIsOut_*` green.
- `AiScenes` / `MapFileTests` rows: Normal v Normal (seed 1, Malazan v Whirlwind) on each new map plays to a result within 36,000 ticks
  (if not, retune the layout as Raraku's first draft was; say what moved); a 2-player match on Vathar Crossing uses starts 0 and 1.
- Docs: docs/03 "Implementation (M6-S8)" (the helper, each map's layout in two sentences with start cells, the Normal v Normal result, the
  BUG-1057 fix); docs/02 "Map and terrain": the paragraph at line ~343 says all three maps ship and that Raraku's **layout** is the
  placeholder (BUG-1102 item 3's wording; the data track does not touch docs/02 or docs/03 this session); docs/03's M6-S7 Raraku paragraph
  "placeholder text" → "layout a placeholder" (one word each).
- OUT of scope: any change to the map file schema, `SimConfig`, the save / replay formats, the generator, `MapGenParams.Default`, the start
  contract; the maps' final text; the view (the list picks the files up by itself); water cells; BUG-1058 / 1059 (next hardening).

Acceptance criteria:
1. Build 0 warnings; non-Perf green; Perf alone green; smoke PASS. The 20 `StandardPresetPinQaTests` + 5 `SharedVisionFfaPinTests` + 27
   pairings unmoved; `Replays/premap/` + `Save/PreMapFixtures` byte-for-byte rows green; the golden **byte-identical** (no `k` line moves:
   a generated-map match is untouched).
2. `game/data/maps/pale_hills.json` and `vathar_crossing.json` load with no error, both `size` 256, `players` 4, and a test proves each is
   the mirror of its source (point / quad) cell for cell, ramp for ramp, node for node, and that `mirror-map` on the checked-in source
   reproduces the checked-in file byte for byte.
3. `MapFileTests`: every shipped map keeps the start contract (the existing row now covers three); each new map's Normal v Normal seed-1
   match ends with a result ≤ 36,000 ticks and the winner, tick and gathered totals are in the docs; same seed twice equal; a save at 1,000
   (format 5) and a recording (format 9) on each new map round-trip hash-equal 1,000 ticks / every checkpoint.
4. Vathar Crossing: a 2-player match uses starts 0 and 1 on opposite banks; at least one ford exists per pair of banks and the flood passes;
   a unit ordered across walks through a ford (a row that counts the river-band cells its path crosses are ramp or band cells).
5. Pale Hills: a 2-player match uses starts 0 and 1 diagonally opposite (≥ half the side apart); open cells ≥ 50 % of passable cells.
6. BUG-1057: the QA row un-skipped and green; the three save rows green; `ai.scoutReplacements` rows green; no byte added to `AIST` /
   `Blackboard.Save.cs`; bug file `fixed` with the test names.
7. Descriptions exactly as given above; `DataContentHashTests` cover the new files (hash moves: expected, note it in the report);
   `git diff --stat -- game/` lists only `game/data/maps/*.json`; nothing under `sim/Rts.Sim/ViewApi`, `game/data/factions`, the data's test
   folders; docs as in scope.

Design references: docs/02 "Map and terrain" (the table: Layout symmetric, Gold 2 per start + 2 per expansion, 2-3 expansions a player;
the Shipped maps rows; High ground); docs/03 "Implementation (M6-S7)" (every field and check, the start contract numbers 10 / 8 / 18 m / 24 m,
ramp length ≥ 3, levels 0-2); docs/02 "AI opponent" (the cap); `studio/bugs/BUG-1057-*.md`.

Tests required: `Map/MapFileTests` rows above (+ the mirror proof, which may live in `tools/`' test project if one exists, else
`sim/Rts.Sim.Tests/Map/`); `DataValidationTests` unchanged rows green; `DataContentHashTests` per new file; `Ai/ScoutReplacementTests`
rows for the grid-centre hall (a 64 × 64 flat scene with the hall at (30, 30), as the bug file); QA's row un-skipped; `AiDeterminismTests`
two-process row on one new map.

Constraints: no Godot / wall clock / `System.Random` in the sim; the map build runs once at construction (nothing per tick); no allocation in
`SentAgain`; player text only in the map files; the tool writes files only under the paths it is given; keep the size budget to 1,500 changed
lines **excluding the map files' terrain rows** (each 256 rows); `Stress/RarakuTwinStressTests`-style rows for the new maps get
`[Trait("Category","Stress")]` if they run over a minute each, and say so.

### Watch-outs (sim)
- `MapFileTests.RarakuDef` looks Raraku up by id (fine); `DataContentHashTests` line 511 reads `d.Maps[0]`: that becomes Pale Hills (ordinal
  file order) — re-read by id. Any other `Maps[0]` in sim tests likewise.
- The 256 map's quarter points (64, 64) cells = (128, 128) m are grid centres with `ai.scoutGrid` 56 m: BUG-1057's case. Put the starts there
  or not, the fix must hold either way.
- Two 256-map Normal v Normal matches to the end are slow (Raraku's 29,001 ticks on 128); run them once, cache nothing, and budget the suite.
- `VisionGatePerfTests` 3.66-3.69 ms against 4 ms flake beside another host: rerun alone.

---

## View track

### Current session plan

**TASK_ID M6-V19 · BUG-1092, the picked map's own counts, `--map-size` on a map file · feature · QA standard · counter 3 / 4 → 4 / 4 (M6-VH5 next)**

Goal: close the two rough edges M6-V18 left: the command-line case that starts a match that can never end (BUG-1092), and the greyed Forests /
Gold mines rows showing the generator's 12 / 8 instead of the picked map's own counts (Raraku has 6 forests / 12 mines).

Scope:
- BUG-1092: after `Match` caps the players at a map file's starts, run BUG-1066's one-team check over the capped seats: warn and clear the
  teams (free-for-all) as `LaunchOptions.OneTeamWarning` does; QA's `QaM6V18bTest` row `KNOWN BUG-1092` un-flagged and asserting
  `IsEnemy(0, 1)`. Keep the setup's behaviour (Start disabled with `setup.one_team`) as it is.
- The greyed counts: with a map file picked, Forests / Gold mines show the map's own numbers (count `MapDef.Forests` / `Mines`; read from
  `GameData`, which the view already holds; no ViewApi change needed); Generated restores the spinners' values. `MapListTest` / `QaM6V18Test`
  rows for Raraku 6 / 12 and the restore.
- `--map-size <n>` together with `--map <id>`: warned and ignored like `--players` over the cap (`MapPlayersWarning`'s pattern); a
  `LaunchOptions` row.
- Optional rider, only if a few lines: BUG-1081 item 2, the long tooltips (Raraku's 180 characters) wrapped to a readable width.
- The 81-scene loop on the branch (which carries `main` 03b45ed + M6-SH1 + M6-V18 together for the first time): `TeamsTest` / `FreeForAllTest`
  rerun alone before filing (BUG-1093).
- OUT of scope: Pale Hills / Vathar Crossing (the sim ships them this session; the list picks them up by itself; the conductor's merged-tree
  loop is the first look), BUG-1093's fix (M6-VH5), the export script, music, the art pass.

Acceptance criteria:
1. Build 0 warnings; smoke PASS; the loop green on the branch (a flake rerun alone 5 / 5 noted); non-Perf green.
2. `--players 4 --map raraku --team 1 1 --team 2 1 --team 3 2 --skirmish` starts a 2-player free-for-all with a warning line; the QA row
   un-flagged; the setup's one-team refusal unchanged (`TeamsTest.OneTeamStart` green).
3. Raraku picked: Forests shows 6, Gold mines 12, both greyed; Generated again: the spinners' previous values back; a test row each.
4. `--map-size 256 --map raraku` warns and plays Raraku at 128 (the QA row at line ~192 asserts it; extend it to read the warning).
5. `git diff --stat -- sim/` empty (or `ViewApi` read-only only, with its test); `ui.json` keys for any new line; docs/03 "Implementation
   (M6-V19)" (short) and docs/02 "Game flow" 2 if the wording changes; BUG-1092 `fixed` with the test names.

Design references: docs/02 "Game flow" 2 (the setup); docs/03 "Implementation (M6-V17)" (BUG-1066's one-team rule), "(M6-V18)" (the map cap,
`MapPlayersWarning`); `studio/bugs/BUG-1092-*.md`, `BUG-1081-*.md` item 2.

Tests required: `MapListTest` / `QaM6V18Test` rows above; `QaM6V18bTest` un-flagged; a `LaunchOptions` row for `--map-size` with `--map`;
`ShellTest` key counts if `ui.json` grows.

Constraints: views hold no gameplay state; text from `ui.json`; the sim validates (no view-side rule duplication beyond the warning);
stay out of `game/data/**` except `ui.json`.

### Watch-outs (view)
- The merged tree will hold three maps in the list (Pale Hills, Raraku, Vathar Crossing, ordinal file order): any row that assumes the list
  is "Generated + Raraku" or that index 1 is Raraku will go red on `main`; look maps up by id (`_data.Maps.First(m => m.Key == "raraku")`).
- Four slots must still fit 648 px; the Map row shares a grid row with Map size.

---

## Data track

### Current session plan

**TASK_ID D24 · Step 0: merge `origin/studio/2026-11-05-2148-data` (D22 + D23) onto `main`; then BUG-1102 items 1-2 and the id-based map-text pin · feature · QA light · counter 3 / 4 → 4 / 4 (D-H7 next)**

Goal: land the accepted D22 + D23 content (the two preset sentences, both pages' High bullet, Raraku's final sentence) on `main` with the
mechanical merge the Producer rehearsed, then fix the D23 nits, and make the map-text pin tolerant of the two maps the sim adds this session
so the conductor's data merge is clean.

Scope:
- **Step 0 (the first commit, verbatim):** on the session branch (cut from `origin/main`), `git merge origin/studio/2026-11-05-2148-data`, then:
  - `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` → **`main`'s** (`git checkout origin/main -- sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`).
  - `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` → **`main`'s** (same command, **after** the merge even if git reports no conflict: it
    auto-takes the branch's regen `742D3E95CF629116`; `main`'s header is `30C51BB619A7E650`).
  - `studio/qa/coverage.md`: both sides. `studio/bugs/README.md`: both appended blocks, **one** BUG-1081 row, the branch's ("open (item 1
    fixed D23 ...)"), the BUG-1102 row from the branch; `studio/bugs/BUG-1102-*.md` is already on `main` byte-identical.
  - Check: `git diff --stat origin/main -- sim/` lists only `sim/Rts.Sim.Tests/Content/**` and `sim/Rts.Sim.Tests/QA/Content/**`; `game/data/`
    diff = `rules.json`'s two `description`s + `raraku.json`'s `description`; `docs/factions/*.md` the High bullet.
  - Run `--filter "FullyQualifiedName~ReplayGoldenTests|GoldenDecouplingQaTests|GatherWedgeQaTests|DescriptionWordingQaTests|DataValidationTests|DataContentHashTests|MapTextContentTests|StartingResourcesContentTests|D22PresetTextQaTests|D23MapTextQaTests"`
    (the Producer's rehearsal: 646 / 646 with the save rows). Commit as "D24: merge D22 + D23 onto main".
- **Item 1 (BUG-1102):** `raraku.json` `description` "centre" → "center" (Producer default: American, as the rest of the data; the owner may
  answer "spelling: British" later) + the pin; the For your review table row.
- **Item 2 (BUG-1102):** the ratio words ("twice", "double", "triple", "half") rejected by `MapTextContentTests.Problems`, sharing the
  presets' `NumberWords`; a mutant row; D23's QA row `NumberWords_AreAllowed_*` re-cut (number words like "two" stay allowed; ratio words
  do not).
- **The id-based pin (the Producer's cross-track rule):** `TheMapsText_IsThePinnedText_InOrder` must no longer assert `Pinned.Length ==
  Data.Maps.Length` or read `Data.Maps[0]` as Raraku: pin by id (every pinned id must exist with that name and text; a shipped map not in
  the pin is allowed, the checker still runs over **every** map in `EveryShippedMapText_HasNoProblem`), and `AMutantDescription_IsCaught`,
  `D23MapTextQaTests.Check` / `TheChecker_RejectsAnEmptyOrDigitDisplayName` look Raraku up by id. Why: the sim's M6-S8 adds `pale_hills.json`
  (sorts first) and `vathar_crossing.json` this session with provisional descriptions that pass the checker; D25 pins their final text.
- No golden regen (the first data task under the decoupled golden: say so in the report). BUG-1102 item 3 (the docs' "placeholder") is
  the sim's this session: **do not edit docs/02 or docs/03.**
- OUT of scope: Pale Hills / Vathar Crossing's text (D25, once M6-S8 is on `main`), any number, the faction pages beyond the merge.

Acceptance criteria:
1. Step 0's commit exists with the resolutions above; the golden's header on the branch is `30C51BB619A7E650` and the file is byte-identical to
   `main`'s; `GatherWedgeQaTests.cs` byte-identical to `main`'s; the filter green; `git diff --stat origin/main -- sim/` = the data's test
   folders only.
2. `rules.json`'s two preset descriptions are D22's, both faction pages carry the High bullet, `raraku.json`'s description is D23's with
   "center" (one word moved against the branch: JSON diff).
3. `MapTextContentTests` pin by id; a test row proves a shipped map absent from the pin passes (a temp map in a copied data dir, or the
   checker called on a made-up id), and that a pinned id missing from the data fails.
4. The ratio-word mutant rows fail as asked ("twice the forests" caught); "two mesas" still passes.
5. Build 0 warnings; the data filter green; non-Perf green; smoke PASS; BUG-1102 items 1-2 `fixed` with the test names, item 3 left to the sim
   (say so in the bug file).
6. The report carries the review table (Raraku: "centre" → "center", why).

Design references: docs/02 "Map and terrain" (the Shipped maps row: "Open center"); `studio/bugs/BUG-1102-*.md`; docs/03 "Testing strategy"
(regenerate only when a `k` line moves); the Producer's recipe above.

Tests required: `Content/MapTextContentTests` (id-based pin + ratio mutants), `QA/Content/D23MapTextQaTests` re-cut, the data filter above.

Constraints: no C# outside `sim/Rts.Sim.Tests/Content/` and `sim/Rts.Sim.Tests/QA/Content/` past step 0's resolutions (which take `main`'s
files); `rules.json` and `maps/raraku.json` are the sim's files, touched only on their `description` keys (the named exception); no golden
regen; no docs/02 / docs/03 edits.

### Watch-outs (data)
- The sim track adds two map files this session; your branch does not have them. Write the pin so it passes with 1 map on your branch and
  3 on the merged tree.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25}: take out when their case passes.

---

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **Merge order sim → view → data.** After the sim lands, the owner's recordings and saves refuse again (the data hash moves: two new map
  files); after the data lands, once more (the text).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase.
