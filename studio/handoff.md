# Handoff: brief for the next session

Written by the Producer at the PLAN of session **2026-10-27-0658** (session 2 / 8 on 2026-10-27; base `e5c4265` = `origin/main`).
Sim **GO (M6-S5)**, view **GO (M6-V16)**, data **STOP (cheap)**. Counters at the start: sim 1 / 4, view 0 / 4, data 1 / 4. **Open S1 / S2:
none.** Build on `main` 0 warnings; the full non-Perf suite result is in the session's PLAN checks (STATE "Build" row at the ACCEPT).

## Where we are

- **M5 4 / 9, M6 3 / 10** (criterion 1's sim half is in: the team; the view's column is this session). Golden `data-hash` **E108AB2A7C66C58D**
  unmoved. **Save format 3** (a team byte per seat at the end of the header), **replay format 7** (a fifth seat token; a free-for-all is still
  written as format 6, byte for byte as before). `World.AreAllied` / `IsEnemy` / `TeamOf` / `HasAlly`, `PlayerView.IsEnemy` / `TeamOf` /
  `HasAlly`, `SpatialHash.SideOf`; `SimConfig.MaxCommandCapacity` 65,536 as before.
- **Producer defaults recorded at this PLAN** (docs/02 "Game flow" 4, docs/01 2026-10-27): the banner is about the local player's *side*
  (Victory when its team stands at the end even if the local player was out earlier; Defeat when the whole side is out); the stats screen
  groups rows by team; allies share vision (M6-S5).
- **Debt:** BUG-1042 (S3, M6-S5's criterion 1), BUG-1044 (S4: item 1 a rider of M6-S5; the rest the next sim hardening). Older sim debt
  unchanged (BUG-0960 one throw, BUG-1022 / 1026 tuning, BUG-0911 / 0889 / 0144 / 0113 item 2 / 0704 item 3 / 0913 items 6-7).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. **Rule 36** (frozen save / replay API: no new signature or error code) applies to both tracks this session. The recorder writes
format 7 only when a seat has a team; `Replay.TeamlessFormatVersion` = 6 is what a teamless recording pins against.

---

## Sim track

### Current session plan (2026-10-27-0658)

**M6-S5: teams slice 2: shared vision between allies + BUG-1042 + BUG-1044 item 1 · feature (counter 1 / 4 → 2 / 4) · QA full · bug ids
from 1045.** Full brief in the PLAN output of session 2026-10-27-0658 (the conductor passes it to the dev); the short form:

1. **Measure before choosing** the per-team vision union: (a) a team-level OR pass over the allies' visibility maps at the end of
   `VisionSystem.Run`, (b) read-side answers through the allies' maps in `FogStore.IsVisible` / `IsExplored` / `CanSeeUnit` /
   `CanSeeBuilding`, (c) stamping each unit's sight into every ally's map. Write a `VisionGatePerfTests`-style A/B row (250 v 250 brawl
   and the 4-seat 256 map as 2v2) into the report; the 4 ms design budget rules (3.50-3.90 ms today, ~10 % margin). The free-for-all
   cost must not move (the same scenes, team 0).
2. **The rule** (docs/02 "Vision and fog of war": the "Allies do not share vision yet" line becomes the shared-vision rule): a player sees
   whatever any ally sees (units, buildings, cells); explored ground is shared; an enemy building seen by either ally is a ghost for both;
   a stealthed enemy detected by either ally is detected for both; high-ground reveals shared; an ally's own stealthed unit stays visible
   to its allies (M6-S4). `PlayerView` answers through the shared vision (the AI gets its ally's eyes; `PlayerViewOracle` learns the
   union). The fog is in `StateHash`: a team match's hash moves (fine); a free-for-all's must not (A/B on the five scenes).
3. **BUG-1042** (criterion 1 in the brief): a scout head waypoint whose base is an ally's is dropped and the sweep continues from the
   unexplored grid points farthest from the ally's known buildings; with shared vision an enemy building the ally sees is known, so a wave
   can start on it; `Stress/TeamAiSoakStressTests`' skipped wave check on the `adjacent: true` rows becomes strict.
4. **BUG-1044 item 1** (rider): `World.cs:234` credits the killer only when `IsEnemy(attackerOwner, victimOwner)`; the
   `QA/CusserQaTests.Cusser_OnItsOwnPosition_*` pin moves to Kills[0] = 0; docs/02 "Game flow" 4 gets one sentence.
5. OUT: starting resources (Request 43, next), allied pushing / formations, resource or tech sharing, AI cooperation, the view's column
   (M6-V16, view track, same session), a map format, any `ViewApi/**` or `game/**` change.

### Next task candidates (in order)

1. **Request 43 (starting resources):** standard / high purses, `rules.json`-driven (`startingResources` presets or a multiplier), in
   `SimConfig`, the save header (format 4, bytes appended at the end) and the replay header (format 8); the AI's openings must still work
   at "high" (a pairs-report row; rule 31 if the AI's play moves). `CliRunner --teams` / `--resources` if a few lines.
2. **A map file format for M6 criterion 7's three hand-made maps** (the view can't build them without one): a heightmap + resources +
   start locations file under `game/data/maps/`, loaded by the sim, hashed in the header.
3. M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the
   last units: BUG-0953's sim half, seed 11); BUG-1010's lever; D14 c; M5-5. Sim hardening (M6-SH1) at counter 4 / 4: BUG-1044's rest,
   BUG-0960's throw, the movement S3s.

### Watch-outs (sim)

- **`SaveHeader.Write` is read by the view's `ViewApi/SaveSummary` up to the tick field**; the team bytes went after the tick for that
  reason. A format-4 header for Request 43 should keep adding at the end, or the view's reader moves with it in the same session.
- **This session the view edits `ViewApi/SaveSummary`, `ViewApi/UnitPicker`, `ViewApi/MinimapRaster`** (M6-V16): the sim must not touch
  `ViewApi/**`. The sim's `World.IsEnemy` is what those readers call; keep its signature.
- `TeamArchitectureTests.NoRawOwnerCompareMeaningEnemy_InTheEnemySiteFolders` scans `Combat/`, `Abilities/`, `Vision/`, `Ai/` and the
  root `World*.cs`: a new enemy site must go through `AreAllied` / `IsEnemy` / the hash's side, a new same-player site into its allow-list
  **and** docs/03's table. QA's `QA/TeamArchitectureQaTests` pins the two lower-case aliased compares (`FogStore` NextSeenUnit, UpdateGhosts):
  if shared vision rewrites those lines, move the pin with them.
- The scan / tower / detection loops keep `u.Owner[j] == owner || (world.HasAlliances && world.AreAllied(...))`: the hash's sides are
  phase 1's; a slot freed and refilled since (a summon) may hold an own or allied unit. Don't "simplify" the first half away.
- `FogStore` is saved (`SpatialHash.Save`-style) and hashed: a team-level map, if (a) is chosen, is derived state: rebuild it after a load
  rather than saving it, or the save format moves (rule 36: no new signature; a format bump is the brief's call, prefer none).
- `AiSaveScenes.TwoVersusTwo` probes the Blackboard (rule 37); `VisionGatePerfTests` fresh-process row 3.50-3.90 ms against 4 ms.
- `SimConfig.MaxCommandCapacity` 65,536 is a hard bound; `ReservePairingsStressTests.KnownNoAgeTwo` prints "take it out" when a side
  reaches Age II: take it out.

---

## View track

### Current session plan (2026-10-27-0658)

**M6-V16: the skirmish setup's Team column + the view's four team readers · feature (counter 0 / 4 → 1 / 4) · QA standard · bug ids
from 1061.** Full brief in the PLAN output; the short form:

1. **Setup:** a **Team** list per slot (None / 1-4 in `ui.json` text: `setup.team`, `setup.team_none`, `setup.team_item`), default None;
   `--team <slot> <n>` in `LaunchOptions` (`SkirmishSetup.MatchArgs` emits it only for a slot with a team); `SimRunner` seats carry
   `PlayerSeat.Team`.
2. **Banner and stats:** `Match.ResultFor` per side (docs/02 "Game flow" 4's new sentence: Victory when the local team stands at the end,
   Defeat when the whole side is out, Draw when nobody stands); `MatchStats` groups rows by team with a team label row
   (`match_over.team` "Team {0}", none-team seats ungrouped); Load Game's players cell shows the team (`load.seat_team`).
3. **The four readers (view-owned files):** `ReplayStore.TryReadHeader` accepts five-token seat lines (format 7) and still reads four-token
   ones; `SimRunner.StartPlayback` passes `seat.Team` into both `PlayerSeat` lists (a format-7 replay plays to every checkpoint);
   `ViewApi/SaveSummary.TryRead` reads the team bytes after the tick (format 3) and exposes them; `ViewApi/UnitPicker.ResolveEnemy` and
   `ViewApi/MinimapRaster.DrawnEnemyDotAt` ask `World.IsEnemy` (a right-click on an ally's unit = a move; an ally's dot is not an enemy dot).
4. Riders: BUG-1060 items 2-3 (the `wontfix` word on BUG-0521; the no-LFS first-run smoke: fix if it's a few lines in `smoke.ps1`, else
   record the decision "from the second run is enough" in the bug and close it).
5. OUT: team colour tints (colours stay per slot), starting resources (43), shared-vision rendering changes (the fog shader reads
   `FogStore.Visibility(local)`; if M6-S5 chooses the read-side option the view's fog must call the union accessor **next session**, not this
   one: the two branches merge separately), any `sim/**` file outside `ViewApi/`.

### Next task candidates

Every other M6 item still waits on something: starting resources on 43, the three maps on a map format, the art pass on the owner's
look call, music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. After M6-V16:
the fog's shared-vision read if M6-S5 needs one (small), the starting-resources list when 43 lands, then (e) export, (f) criterion 6.

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape: the team bytes are **after the tick** (format 3); `SaveSummaryQaTests` must gain a
  format-3 row; re-run `SaveLoadTest` + `QaM6V14Test` + `QaM6VH4Test` on the merged tree.
- `ReplayStore.TryReadHeader` today rejects any seat line that isn't four tokens (`f.Length == 4`): a format-7 file is listed as
  unreadable until this task; a format-6 line must keep reading as team 0.
- `Match.ResultFor` reads `w.IsDefeated(local)` first: in a 2v2 the local player can be defeated while `MatchOver` is false (the ally
  fights on); the banner must not fire Defeat then (the match isn't over for the side). Watching on after your own defeat is the
  existing "Continue watching" path.
- `smoke.ps1` matches `-cmatch`, skips `[n]` frame lines and prints a WARN only when `.wav` files are LFS pointers.
- `StatsScreenCheck.LineReadable` 1.4:1 on the outline: a palette change is the owner's taste.
- `scene-loop.ps1 -Filter` is a file glob; the loop is **73** scenes, ~22 min.

---

## Data track

### Current session plan (2026-10-27-0658)

**STOP (cheap, counter stays 1 / 4).** No inbox answer (D21 / D19 / Shock / `goldShare` / BUG-0780); no schema the data track needs is new
on `main` (M6-S4's teams need no faction content; M6-S5's shared vision and M6-V16's column need none either).

### Next task candidates

1. The owner's answers above, each a small page + `ai.json` / data task with a review table (golden regen once).
2. **BUG-1080 (S4):** two page wordings; fold into the next page-touching task.
3. Rule 31 re-prints whenever a sim task moves the AI's play (M6-S5 should not for free-for-all matches: the pairing runs are 1v1; 43 may
   at "high"; M5-4c slice 2 and M5-4d will).

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} print "take it out (BUG-1010)" when
  their case passes: take them out then.
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells`.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/03: each track appends its own rows; the conductor unions them.
- Two tracks this session (sim + view): two test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- **Shared-file collision to avoid this session:** docs/02 "Game flow" 4 and "Vision and fog of war" (sim edits vision + the kill sentence;
  view edits nothing in docs/02 but docs/03 "Implementation (M6-V16)"); docs/01's change log is append-only for both.
- Bug ids next: **sim from 1045, view from 1061, data from 1081**.
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase; the owner's call on the window is in STATE "Wanted".
