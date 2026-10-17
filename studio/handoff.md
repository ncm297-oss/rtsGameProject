# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-17-0056** (the first full session of 2026-10-17, 1 / 8 done). After the
integration (sim → view → data) `main` holds M6-S1, M6-V10 and D20. Golden `data-hash` on `main` **C059AD620DE581F1** (unmoved this
session). Counters: **sim 3 / 4, view 3 / 4, data 1 / 4** (the next feature session on sim / view is the last before M5-H3 / M6-VH3).
Bug ids next session: **sim from BUG-0880, view from BUG-0890, data from BUG-0900.** No open S1 / S2; S3 15, S4 17.

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5 advanced). **M6 1 / 10** (criterion 4 ticked; criterion 2 waits only on the view's columns
  now that M6-S1's counters are on `main`; criterion 3's save half on Requests 42; criterion 1 on team, Requests 38; criterion 5's look
  test done, its art pass on the owner's call).
- **Waiting on the owner:** the art call from the look test (docs/04 "Look test (M6-V10)", the two screenshots); D19's table, the Shock
  pass, `goldShare` 0.6 → 0.5, BUG-0780, "densest cluster"; the Perf fresh-boot run (BUG-0764); the fonts / music / export templates.

## Standing rules (1-29)

Unchanged (see the 2036 handoff in git history for the long form; 28: `game/data/common/ui.json` is view-track text; 29: the art
pipeline's tooling (`tools/asset-manifest.json`, `tools/import-assets.ps1`) is view-track work when it comes). **New 30 (0056):** a
Producer-fixed docs drift found by another track's QA keeps its bug file on that track's branch; the next session's PLAN sets the file's
status (BUG-0871: fixed in docs/02 at the 0056 ACCEPT, status to set).

---

## Sim track

### Next task candidates (feature, counter 3 / 4 → 4 / 4; then M5-H3)

1. **Pathing slice 2** (the owner's playtest note 5, continued; docs/03 "Implementation (pathing slice 1)" "Not yet (slice 2)"): a melee
   unit blocked by its own standing ranged units routes round them; unit-v-unit crowding away from buildings; smarter fill order (far
   faces first for the units that arrive last; the House row's far-corner jam 17-18 / 20). QA full (movement). ~800-1,000 lines; the
   golden's `k` lines may move (movement): re-record once, say why. **BUG-0790 (S3)** lives in the same code (`EconomySystem.StandPoint` /
   `WalkToFootprint`, `MovementSystem`'s crowd-arrival rule: a worker stands out of reach beside a free ring-cell spot): fold it in only if
   the fix is a few lines, then un-skip `GatherPocketStressTests (MineOneSide, 40)` and `Bug0790FaceProbeQaTests`' regression row; else M5-H3.
2. **M5-4c slice 2** (criterion 4): `gatherBonus` (Hard's 25 %), `focusFire`, `pullBackWounded` applied; D16's pins already name the numbers.
3. **Requests 38** (teams: a `team` per seat in `SimConfig`, allied fog / targeting / defeat), then **Requests 42** (the save format: the
   Blackboard whole incl. M5-4f's marks, the surround claims, the flow-field cache, the fog bits, **and the M6-S1 stats** (`MatchStats`
   whole: the eight counters, the tech ticks, the samples and their count); a round-trip test that compares the unhashed state directly).
4. D14 b schema (named openings per faction, picked by the seeded RNG; Easy the first), M5-4d (expansion, rebuilding, high-ground scouting,
   hunting the last units → criterion 3), D14 c, M5-5 (the 20-minute winner test → criterion 5), M5-6.

**M5-H3** (after one more feature session): BUG-0764 first (an owner fresh-boot Perf run wanted), BUG-0790 if still open, BUG-0791,
BUG-0850 (S4: the dev rows' gold site refund + a cast summon; a few lines), BUG-0144, BUG-0113 item 2, BUG-0704 items 2-3.

### Watch-outs (sim)
- Only the sim moves the golden. `SurroundClaim` / `SurroundHolder`, the Blackboard, and now `MatchStats` are save state outside the hash:
  Requests 42 carries them (docs/03 "Save/load and replays").
- M6-S1's getters for the view's brief: `World.GatheredGold` / `GatheredWood` / `UnitsTrained` / `UnitKills` / `BuildingKills` /
  `UnitLosses` / `BuildingLosses` / `BuildingsLost` / `BuildingsBuilt` (per-player spans), `TechResearchedTick(player, techId)`,
  `ArmyValueSamples(player)`, `StatsSampleTicks`, `StatsSampleCapacity`. Summons count in the army value (Producer decision).
- Any `rules.json` / data change moves `ContentHash` and makes every recorded replay in the owner's `user://replays` refuse to play
  (`load.refused`): by design, but say so in the For your review entry of the task that does it.

---

## View track

### Next task candidates (feature, counter 3 / 4 → 4 / 4; then M6-VH3)

1. **The stats columns (M6 criterion 2's last half):** `MatchStats.Columns` + `ui.json` labels for gathered gold / wood, units trained,
   kills and losses split (units / buildings), buildings built / lost, **time to Age II** (`TechResearchedTick(p, age_ii)` × 50 ms, "—"
   before), and **the army-value graph** from `ArmyValueSamples(p)` (one line per player in the player's colour, x = `StatsSampleTicks`
   × k; a `Line2D` or a `Control._Draw`, no new node per sample), on the match-over stats screen and the pause menu's Stats; the replay
   watcher's stats too (the counters are not in the replay, they're recomputed by playback). Text from `ui.json` only. `MatchOverViewTest`
   rows = the sim's spans; a windowed screenshot looked at. QA standard (the graph reads sim spans only; hash twin). ~600-900 lines.
   **Ticks criterion 2** when done (the banner, the table and the sim's detection already exist).
2. Save / load (criterion 3's other half) once Requests 42 lands. 3. `tools/export.ps1` when the owner's templates arrive. 4. Teams
   (Requests 38). 5. Criterion 6's playable run against the AI. 6. **The art pass** once the owner answers the look call (the manifest +
   `tools/import-assets.ps1` per standing rule 29; decimation to the 3k budget; textures to 512²; the -Z facing fix; delete
   `assets/models/looktest/` or exclude it in the preset, BUG-0861).

**M6-VH3** (after one more): BUG-0860 first (S3: null-check every `GD.Load` in `LookTest` through `Fail` + a per-scene timeout in
`scene-loop.ps1`), BUG-0830, BUG-0550 item 2, BUG-0281 item 2, BUG-0663, the `--colour` collision warning's wording, BUG-0861 if the art
pass hasn't.

### Watch-outs (view)
- Three Godot runs share the machine: wall-clock scene rows flake under load; rerun alone before filing.
- A clone on the owner's second machine needs `git lfs pull` before `LookTest` runs (BUG-0860 until fixed).

---

## Data track

### Next task: an inbox answer first, else STOP cheaply

- **First, any owner answer in the inbox:** D19's table, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780 ("accept" closes it with a dated
  line on both pages; "Catapult pass" = a sweep first, then one header move), "densest cluster" (sim), the look call (view).
- **Parked, to batch with the next hash-moving data change:** the 12th-house step (Malazan step 18, Whirlwind step 19) never reaches the
  D15 C skip (due at cap 98) and buys 2 pop for a house's cost; dropping it (or moving it after the expansion) is one `ai.json` line per
  faction, but moves the golden and expires the owner's recordings. Not alone.
- Otherwise **STOP**: the D14 b openings need the strategies schema (not on `main`); the M7-M9 rosters wait for their milestones.

---

## Watch-outs (all tracks)

- **BUG-0764:** Perf rows red on every tree on this PC; an owner fresh-boot run is wanted before M5-H3 re-baselines. Paired A/B only.
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- **BUG-0871's file** (`studio/bugs/BUG-0871-docs02-ai-defense-says-wave-keeps-sent-size.md`, on `main` after the data merge): set status
  `fixed` (docs/02 edit at the 0056 ACCEPT, the Producer) at the next PLAN; the README row too.
