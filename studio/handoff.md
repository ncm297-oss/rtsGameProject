# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-27-0158** (session 1 / 8 on 2026-10-27). Only the sim track ran (view and
data were cheap STOPs at the PLAN). Base for the next session: `origin/main` after the conductor merges `studio/2026-10-27-0158-sim`
(c4bd6ee + the Producer's studio commit). **Sim ACCEPT.** **Counters after: sim 1 / 4, view 0 / 4, data 1 / 4.** **Open S1 / S2: none**
(BUG-1043 S2 was found and fixed inside the session). `main` green: the full non-Perf suite on the branch (see the session log).

## Where we are

- **M5 4 / 9, M6 3 / 10** (criterion 1's sim half is in: the team; the view's column is left). Golden `data-hash` **E108AB2A7C66C58D**
  unmoved. **Save format 3** (a team byte per seat at the end of the header), **replay format 7** (a fifth seat token; a free-for-all is still
  written as format 6, byte for byte as before). `World.AreAllied` / `IsEnemy` / `TeamOf` / `HasAlly`, `PlayerView.IsEnemy` / `TeamOf` /
  `HasAlly`, `SpatialHash.SideOf`; `SimConfig.MaxCommandCapacity` 65,536 as before.
- **New debt:** BUG-1042 (S3: with allies at mirror starts an AI that only knows its ally's base never waves: **planned as M6-S5's first
  criterion**), BUG-1044 (S4: item 1 decided, a friendly-fire kill is nobody's kill, a rider of M6-S5; the untested sites, the regex blind
  spots and two nits to the next sim hardening). Older sim debt unchanged (BUG-0960 one throw, BUG-1022 / 1026 tuning, BUG-0911 / 0889 /
  0144 / 0113 item 2 / 0704 item 3 / 0913 items 6-7).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster", the 3-hour lock window; the downloads (fonts, music, export templates).

## Standing rules (1-37)

Unchanged. Confirmed at this ACCEPT under **rule 36**: two new format versions, no new signature or error code. **Note:** the recorder
writes format 7 only when a seat has a team; `Replay.TeamlessFormatVersion` = 6 is what every test that pins "the current format" of a
teamless recording must compare against (four pins moved this session: `CliTests`, `ProductionFuzzTests`, `QA/CliQaTests`, `QA/EconomyQaTests`).

---

## Sim track

### Current session plan (next PLAN writes it)

**Next task: M6-S5, teams slice 2: shared vision between allies + BUG-1042 + BUG-1044 item 1 · feature (counter 1 / 4 → 2 / 4) · QA full
· bug ids from 1045.** Why before Request 43: the view's team column (M6-V16) lands in the same session, and a human picking two slots on
one team on the 4-start map puts its AI ally at the mirror start, which is BUG-1042's exact case; shared vision is the Producer decision
already in docs/01 (2026-10-27).

Brief outline for the PLAN:
- **Measure first, then build:** the per-team vision union. Today `FogStore` holds per player a visibility byte map, explored / visible
  bits, reveals, ghosts and the node memory. Options: (a) a team-level OR of the allies' visibility at the end of `VisionSystem` (one pass
  over the map's cells per team per update: cost = cells × allies; a 256 map is 65,536 cells), (b) `CanSeeUnit` / `CanSeeBuilding` /
  `IsVisible` answering through the allies' maps (no extra pass; every read loops the allies), (c) stamping each unit's sight into every
  ally's map at update (cost grows with units × allies). The brief asks for a `VisionGatePerfTests`-style A/B row on the 250 v 250 and
  the 4-seat 256 scenes before the choice; the 4 ms design budget still rules (3.50-3.90 ms today, ~10 % margin).
- Rules (docs/02 "Vision and fog of war", the "Allies do not share vision yet" line becomes the rule): an ally's units and buildings are
  visible wherever either ally sees; explored ground is shared; ghosts: an enemy building seen by either ally is remembered by both;
  stealth / detection: a unit detected by either ally is detected for both; high-ground reveals shared; `PlayerView` answers through the
  shared vision (the AI gets its ally's eyes; the `PlayerViewOracle` learns the union rule); the hash: the fog is in `StateHash`, so a
  team match's hash moves (fine) but a free-for-all's must not (A/B on the five scenes again).
- **BUG-1042** as criterion 1 (direction in the bug file: drop a head waypoint found to be an ally's base; sweep away from the ally; with
  shared vision an enemy building the ally sees is known; the soak rows' wave check made strict). **BUG-1044 item 1** as a rider
  (`World.cs:234`: the killer credited only when `IsEnemy`; the `CusserQaTests` pin moves; docs/02 "Game flow" 4 one sentence).
- OUT: starting resources (43, the session after), allied pushing / formations, resource or tech sharing, AI cooperation, the view's
  column (M6-V16, the view track, same session), a map format.

### Next task candidates (in order)

1. **M6-S5** above.
2. **Request 43 (starting resources):** standard / high purses, `rules.json`-driven (`startingResources` presets or a multiplier), in
   `SimConfig`, the save header (format 4) and the replay header (format 8); the AI's openings must still work at "high" (a pairs-report
   row; rule 31 if the AI's play moves). `CliRunner --teams` / `--resources` if a few lines.
3. **A map file format for M6 criterion 7's three hand-made maps** (the view can't build them without one): a heightmap + resources +
   start locations file under `game/data/maps/`, loaded by the sim, hashed in the header.
4. M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the
   last units: BUG-0953's sim half, seed 11); BUG-1010's lever; D14 c; M5-5. Sim hardening (M6-SH1) at counter 4 / 4: BUG-1044's rest,
   BUG-0960's throw, the movement S3s.

### Watch-outs (sim)

- **`SaveHeader.Write` is read by the view's `ViewApi/SaveSummary` up to the tick field**; the team bytes went after the tick for that
  reason. A format-4 header for Request 43 should keep adding at the end, or the view's reader moves with it in the same session.
- `TeamArchitectureTests.NoRawOwnerCompareMeaningEnemy_InTheEnemySiteFolders` scans `Combat/`, `Abilities/`, `Vision/`, `Ai/` and the
  root `World*.cs`: a new enemy site must go through `AreAllied` / `IsEnemy` / the hash's side, a new same-player site into its allow-list
  **and** docs/03's table. QA's `QA/TeamArchitectureQaTests` pins the two lower-case aliased compares (`FogStore` NextSeenUnit, UpdateGhosts).
- The scan / tower / detection loops keep `u.Owner[j] == owner || (world.HasAlliances && world.AreAllied(...))`: the hash's sides are
  phase 1's; a slot freed and refilled since (a summon) may hold an own or allied unit. Don't "simplify" the first half away.
- `Stress/TeamAiSoakStressTests` (`Category=Soak`, 8 rows × 12,000 ticks) skips the wave check on the `adjacent: true` rows until
  BUG-1042 is fixed: make it strict in M6-S5.
- `AiSaveScenes.TwoVersusTwo` probes the Blackboard (rule 37); `VisionGatePerfTests` fresh-process row 3.50-3.90 ms against 4 ms.
- `SimConfig.MaxCommandCapacity` 65,536 is a hard bound; `ReservePairingsStressTests.KnownNoAgeTwo` prints "take it out" when a side
  reaches Age II: take it out.

---

## View track

### Current session plan (next PLAN writes it)

**Next task: M6-V16, the skirmish setup's team column + the view's team readers · feature (counter 0 / 4 → 1 / 4) · QA standard · bug ids
from 1061.** Request 38 has landed (M6-S4): the view can build its half now.
- The setup: a **Team** list per slot (None / 1-4; `PlayerSeat.Team`), default None (free-for-all); `--team <slot> <n>`; the Victory /
  Defeat banner says Victory when the local player's *side* stands (`World.MatchOver` is per side already; the local player may be
  defeated while its team wins: docs/02 "Game flow" 4 says what the screen shows: the Producer's default is "Victory" with the stats
  screen grouping seats by team); the stats screen groups rows by team (a team label from `ui.json`); Load Game's header line shows
  the teams.
- **The four readers the sim track named (view-owned files):** `ReplayStore.TryReadHeader` accepts the five-token seat line (format 7)
  and keeps reading four-token lines; `SimRunner`'s playback passes `seat.Team` into the `PlayerSeat`s it builds (a format-7 replay
  watched back must play to every checkpoint); `ViewApi/SaveSummary` reads the team bytes after the tick (format 3; a format-2 file is
  refused as another version anyway); `ViewApi/UnitPicker.ResolveEnemy` and `ViewApi/MinimapRaster.DrawnEnemyDotAt` ask `World.IsEnemy`
  (BUG-1044 item 2's view half: today a right-click on an ally's unit resolves as an Attack the sim drops, so the click does nothing
  instead of a move; the minimap should draw an ally's dot in its colour as a non-enemy). Team colours: a slot keeps its own colour
  (docs/02: colours are per slot); no team tint in this slice.
- Riders: BUG-1060 items 2-3. **Rough edge to tell the owner in the review entry:** BUG-1042 (an AI ally at the mirror start never
  attacks) is the sim's M6-S5, in the same session; until both merge, a 2v2 with the human's ally at the mirror start plays oddly.
- QA focus: a 2v2 and a 1v1v2 through the real setup; a format-6 replay list beside a format-7 one; a save from a team match loaded
  and its summary row right; a right-click on an ally = a move; the banner when the local player is out but its team wins.

### Next task candidates

Every other M6 item still waits on something: starting resources on 43, the three maps on a map format, the art pass on the owner's
look call, music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. After M6-V16:
the starting-resources list when 43 lands, then (e) export, (f) criterion 6.

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape: the team bytes are **after the tick** (format 3); `SaveSummaryQaTests` must gain a
  format-3 row; re-run `SaveLoadTest` + `QaM6V14Test` + `QaM6VH4Test` on the merged tree.
- `smoke.ps1` matches `-cmatch`, skips `[n]` frame lines and prints a WARN only when `.wav` files are LFS pointers.
- `StatsScreenCheck.LineReadable` 1.4:1 on the outline: a palette change is the owner's taste.
- `scene-loop.ps1 -Filter` is a file glob; the loop is **73** scenes, ~22 min.

---

## Data track

### Current session plan (next PLAN writes it)

**STOP (cheap, counter stays 1 / 4)** unless the inbox carries an answer (D21 / D19 / Shock / `goldShare` / BUG-0780). No schema the data
track needs is new on `main`: M6-S4's teams need no faction content, and the default team 0 moved no pairing number (no rule-31 re-print:
QA's A/B was hash-equal on five scenes).

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
- Two tracks next session (sim + view): two test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Bug ids next: **sim from 1045, view from 1061, data from 1081**.
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields).
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase; the owner's call on the window is in STATE "Wanted".
