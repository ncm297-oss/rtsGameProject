# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-05-1716** (session 3 / 8 on 2026-11-05, scheduled; base `ab54ddb`). **All three
tracks accepted:** sim **M6-S7** (the map-file format slice 1 + Raraku + BUG-1047 / 1048's reach rule; counter **4 / 4 → M6-SH1 is the
next sim session**), view **M6-V17** (the setup's Starting resources list; 2 / 4), data **D22** (the presets' text; 2 / 4, **lands after a
golden merge-fix**, see below). **Open S1 / S2: none.** Golden `data-hash` **30C51BB619A7E650** once the sim merges, then once more with
D22's text. Bug ids next: **sim from 1055, view from 1067, data from 1082.**

## Where we are

- **Playable from the menu:** a 1v1 to 2v2 against AIs, Standard or **High** starting resources (new), teams, game speed, the generator's
  forests / mines; save / load / Watch; the stats screen fits every menu layout now.
- **Hand-made maps have a format** (`game/data/maps/<file>.json`, docs/03 "Implementation (M6-S7)") and one map, **Raraku** (2p, 128,
  placeholder layout and text). `SimConfig.MapId` selects it; save format 5 / replay format 9 carry it only for a map-file match (a
  generated-map match writes 4 / 6-8 byte for byte). **Nothing in the setup picks it yet: M6-V18.**
- **The AI's gather reach** lifts once its scout's head ring is seen (or the sweep ends alive); a scout lost before that is replaced. The
  decided fallback clause was dropped (docs/01 2026-11-05, second row). **New S3: BUG-1052** (unbounded replacements when every scout dies
  at the door), **BUG-1053** (no re-scout after the ring when no forest is known): M6-SH1's first items.
- **Open S3:** BUG-1052, BUG-1053 (sim), BUG-1010 (sim-side). **S4:** BUG-1049, BUG-1051 items 5 / 6, BUG-1054, BUG-1044's rest, BUG-1081 (data).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 and **D22** review tables, the Shock pass, `goldShare`,
  BUG-0780, "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Integration of this session (for the conductor; the next PLAN checks it)

1. **Sim first** (`studio/2026-11-05-1716-sim`), then **view** (`studio/2026-11-05-1716-view`): no code overlap (view's only `sim/` file
   is `ViewApi/SaveSummary.cs`, which the sim did not touch). `studio/qa/coverage.md` gets one appended row from each track at the same
   place: **keep all three rows** (sim, view, data) in that order.
2. **Data** (`studio/2026-11-05-1716-data`) **conflicts with the sim on two files** that both regenerated: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`
   (`data-hash` + `checksum` lines) and `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (`SameGameDataHashes`' second value). The right
   content is **neither side's**: it is the hash of the merged data (the sim's maps + D22's text). **Resolution = a game-dev merge-fix run
   on the data branch** (not a hand merge): merge `origin/main` (after sim + view) into the data branch taking the sim's version of both
   files, run `RTS_REGEN_GOLDEN=1 dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~ReplayGoldenTests` (rewrites the golden's
   `data-hash` / `checksum`; **every `k` line must be unchanged**), set `GatherWedgeQaTests.SameGameDataHashes`' second value to the new
   `data-hash` and append a "// D22: the presets' description text" note, run `ReplayGoldenTests` + `GatherWedgeQaTests` +
   `DataValidationTests` + `StartingResourcesContentTests` green, commit "D22: regenerate the golden on the merged tree", merge. **If no
   dev run is possible, push the data branch for later** (its ACCEPT stands) and the next session's data task starts with that merge-fix.
3. After the merges: `tools/qa/smoke.ps1` on `main` (the view's loop ran on a tree equal to `main` before M6-S7; the merged-tree checks
   that remain are the smoke and, next view session, the loop).

## Standing rules (1-37)

Unchanged.

---

## Sim track

### Current session plan

None yet (the next PLAN writes it). **Expected: M6-SH1, the sim's hardening (counter 4 / 4), QA full** (the reach rule is AI decision code).

### Next task candidates (in order)

1. **M6-SH1 (hardening):** (a) **BUG-1052 (S3):** a cap on scout replacements as a `rules.json` `ai.*` knob (e.g. `ai.scoutReplacements`
   2-3; validated, hashed, golden regen once, `k` lines unchanged); after the cap, **lift the reach as a death did in M6-S5** (Producer
   default; the brief may say otherwise); un-skip `QA/ReachRuleQaTests.EveryScoutKilledAtOnce_*` (bound ≤ 4 scouts lost, workers kept);
   the 20 standard pins, 5 FFA pins and 27 pairings must not move (no scout dies in them), else name the scene. (b) **BUG-1053 (S3):** a
   re-scout while a kind has no known node, bounded by the same cap; un-skip `ScoutKilledAfterTheRing_*`. (c) BUG-1051 items 5-6 (the
   stress row's 10:00 assertion → print only; `SpendDownQueue` → `ai.spendDownQueue` 1-5, hashed). (d) BUG-1054 (docs/02 "first minute or
   two"; a sentence on what a save's map id does). (e) BUG-1049, BUG-1044's rest, BUG-0960's throw, the movement S3s (BUG-0911 / 0889 / 0144),
   BUG-1022 / 1026 notes, as the size budget allows. Coverage rows; the bug index (the dev may edit `studio/bugs/**` at the brief's direction).
2. **Map format slice 2:** Pale Hills (4p, 256, forest: dense woods with paths) and Vathar Crossing (2-4p, steppe: a river of slopes with
   fords), a mirrored-terrain helper (the one-off script that laid Raraku out is not shipped), `AiScenes` rows on each; the data track
   writes their text after.
3. M5-4c slice 2 (the Hard knobs), M5-4d (expansion / rebuilding / hunting), BUG-1010's lever, D14 c, M5-5.

### Watch-outs (sim)

- `ScoutManager.Sweep`'s death branch: `board.ScoutPhase = ai.ReachLifted() ? 2 : 0`; the replacement count has nowhere to live yet (the
  `AIST` section's layout is unchanged since M6-S3: a new counter in the blackboard changes the save's AI section → a format bump or a
  derived counter). Think before adding state: counting dead scouts from `World` deaths is derivable.
- `Replays/premap/` and `Save/PreMapFixtures` pin ab54ddb's bytes; they move only with a deliberate format change, regenerated **on the
  base commit** with `RTS_WRITE_PREMAP_FIXTURES=1`.
- `TestDataDir.CopyOfShippedWithoutMaps` / `TestSim.DataWithoutMaps` for rows that edit a resource footprint (Raraku refuses a 1 x 1 or
  3-wide mine). `DataContentHashTests.ResourceListLengthAndFootprintEdit_ChangeTheHash` already uses it.
- `GatherWedgeQaTests.SameGameDataHashes` moves with every data field; `ReplayGoldenTests` regen with `RTS_REGEN_GOLDEN=1`.
- `VisionGatePerfTests` fresh-process row 3.66-3.69 ms against 4 ms; `PropsMeasureTests.MinimapRefresh_*` 0.34 ms: both flake beside
  another test host (rerun alone). `Stress/RarakuTwinStressTests` is slow (12,000 ticks × 2 processes).

---

## View track

### Current session plan

None yet. **Expected: M6-V18, the setup's Map list, QA standard.**

### Next task candidates

1. **M6-V18, the setup's Map list** (docs/03 "Implementation (M6-S7)" → "Requests for the view"): a **Map** list in the setup ("Generated"
   first, then `world.Data.Maps` by `DisplayName` in file order, each `Description` a tooltip); a picked map sets `SimConfig.MapId` and
   `Map = MapGenParams.Default with { Width = map.Size, Height = map.Size }` (the Forests / Gold mines / Map size rows don't apply: hide or
   disable them), caps the slots at `MapDef.Players` (Raraku: 2); `MatchArgs` adds `--map <id>` (only for a map file); `LaunchOptions`
   parses it (an unknown id warned by `Match`, generated); `SimRunner.Start` passes `MapId`, **`StartPlayback` passes `replay.MapId`**,
   `StartLoaded` takes the header's; Load Game names the map (a `ui.json` key; `SaveSummary` reads format 5's id string right after the
   preset string, read-only; `ReplayStore.TryReadHeader` accepts format 9 already but stops after `starting-resources`: read the `map`
   line too); the match log names it; `tools/qa/smoke.ps1` unchanged. Scene rows: Raraku from the setup's Start (the bases on the file's
   starts, the hall at cell (22, 64) / (105, 63)), save (format 5) → Load, Watch (format 9) to every checkpoint, the list's texts equal
   the data's, 3-4 slots refused / capped on a 2-player map, `--map` junk. **The loop on the merged tree.**
2. M6-VH5 (view hardening at 4 / 4, or earlier when nothing else is unblocked); (e) `tools/export.ps1` once the export templates are
   downloaded; (f) criterion 6's music once a track is dropped in; the sound re-cuts when the owner listens; BUG-1081 item 2 (wrap the
   long tooltip) if the owner minds.

### Watch-outs (view)

- **Run the scene loop on the merged tree** before claiming green (the 0805 lesson); M6-S7 moved the save / replay constants
  (`SaveFormat.CurrentVersion` 5, `Replay.CurrentFormatVersion` 9): a view test asserting "a generated save is `CurrentVersion`" is wrong
  now (`MaplessVersion` 4 is right; the sim's own tests were re-pinned).
- A Raraku match's `World.Config.Map` has `Width` / `Height` = 128 and every other generator field at its default: `Match`'s "placed
  fewer than asked" log check (`placed.Forests < asked.Forests`) would warn on a map file (6 forests v 12): skip it when `MapId` is set.
- `scene-loop.ps1 -Filter` is a file glob; the loop is 78 scenes now (~20-25 min). `FreeForAllTest` crashed once at startup under three
  hosts (0xC06D007F), 5 / 5 alone: rerun alone before filing.

---

## Data track

### Current session plan

None yet. **Expected: D23 (QA light), after the D22 merge-fix if it is still pending.**

### Next task candidates

1. **The D22 merge-fix first, if the conductor could not run it** (see "Integration" above): merge `origin/main`, regen the golden,
   move the `GatherWedgeQaTests` pin, `k` lines unchanged, commit.
2. **D23: Raraku's `displayName` / `description`** (`game/data/maps/raraku.json`'s two text keys only; the layout is the sim's): a name
   (keep "Raraku": a Malazan codename like every other, the rename pass is pre-release) and a one-line description in the presets'
   register (≤ 200 characters; docs/02 "Map and terrain"'s table: desert, open centre, few forests, long sightlines; no number, no faction
   word), a `Content/` pin (the map's text, as the presets'), **BUG-1081's checker row** (faction ids + the bare words of the faction
   display names in `ProperNames`, a mutant row "a Malazan quartermaster"); golden regen once (`k` lines unchanged); a review table.
   The sim's M6-SH1 may also move the golden the same session (`ai.scoutReplacements`): the same merge order (sim first, data regen on the
   merged tree) applies: say so in the PLAN.
3. The owner's answers (D21 names / descriptions / the Malazan standard wave, the D22 text, D19's table, the Shock pass, `goldShare`,
   BUG-0780); Pale Hills / Vathar Crossing's text with slice 2; D14 c; M7's rosters.

### Watch-outs (data)

- **Two tracks moving the golden in one session always conflict**; plan the regen on the merged tree as a named step (a dev run), never
  a hand merge of the hash lines.
- `rules.json` and `game/data/maps/*.json` are the sim's files; a data task touching their text keys is a named exception in the brief
  (as D22's was): list the exact keys.
- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`.

---

## Watch-outs (all tracks)

- Three tracks a session: three test hosts; wall-clock Perf and scene rows flake under load (this session: 20 Perf failures in one
  first run, 0 alone); rerun alone before filing.
- **The owner's recordings and saves made before 2026-11-05-1716 refuse to play** (the data hash moved: maps, then text); they move
  again whenever M6-SH1 adds a knob or D23 lands.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase and commits each dev's work at every phase
  boundary (`sessions/2026-10-27-0658-incident.md`).
