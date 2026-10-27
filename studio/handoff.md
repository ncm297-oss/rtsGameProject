# Handoff: brief for the next session

**PLAN 2026-10-27-0158 (session 1 / 8 today):** sim **GO M6-S4** (teams slice 1, the brief under "Sim track → Current session plan"), view
**STOP** (cheap), data **STOP** (cheap). Checks at the PLAN: inbox empty; `fd0720c` build 0 warnings; the Producer decision on allied
vision (slice 2) is in docs/01's change log. The ACCEPT rewrites this file.

Written by the Producer at the ACCEPT of session **2026-10-26-2108** (written on 2026-10-27, so **2026-10-27 starts at 0 / 8**; the
2026-10-26 session was a one-off dispatched by the owner). Base for the next session: `origin/main` after the conductor merges sim
(`studio/2026-10-26-2108-sim`, cde7952 + the Producer's studio commit), then view (6f12759), then data (95d1ebc). **All three ACCEPT.**
**Counters after: sim 0 / 4, view 0 / 4, data 1 / 4** (the two hardenings done; the data re-print was a feature session).
**Open S1 / S2: none** (BUG-1027 closes with this merge; BUG-1040 S2 was found and fixed inside the session). **`main` is green again** once
all three branches are merged: the sim's four stress rows and the data's five pinned rows were the only red.

## Where we are

- **M5 4 / 9, M6 3 / 10** (hardening sessions tick nothing). Golden `data-hash` **E108AB2A7C66C58D** unmoved. Save format **2**; **new format
  bound:** `SimConfig.MaxCommandCapacity` = 65,536 (the save header, `Replay.Validate` and `SimConfig.Validate` share it; the game uses 4,096).
- **Debt closed this session:** sim BUG-0912 / 0913 (1-5) / 0914 / 0915 / 0940 / 0941 / 0990 / 1023 / 1027 (sim half) + 0960 item 2's two paths;
  view BUG-1030 / 1031 / 1032 / 1002 / 0970 / 0955 / 0954 / 0922 / 0521 (closed); data BUG-1011, BUG-1027 (data half). **Left open:** sim BUG-0960
  (one int32-sweep throw in `FlowFieldCache.Get`, offset 2525 value −1), BUG-1022 (measured, not shipped: no variant met the rule), BUG-0911 /
  0889 / 0144 / 0113 item 2 / 0704 item 3 / 0913 items 6-7 (not reached); view BUG-1060 (S4: index rows done by the Producer at this ACCEPT;
  BUG-0521's status word; a no-LFS clone's *first* smoke run still red, documented); data BUG-1080 (S4 wording), BUG-1010 (S3, sim-side).
- **Waiting on the owner** (none blocking): the art call, the sounds, the D21 review table, the Shock pass, `goldShare`, BUG-0780, "densest
  cluster"; the downloads (fonts, music, export templates); **new:** the 3-hour stale-lock window (see STATE "Wanted").

## Standing rules (1-37)

Unchanged. Confirmed at this ACCEPT under **rule 36** (the save API freeze): a forged command capacity over the bound now reads
`InvalidHeader` (was `Malformed`), the code the docs/03 refusal table already gave a capacity out of range; no new code, no new signature.
**Note (not a rule):** `ViewApi/AttackAlerts.cs` is the view's own file (M6-V12); a behaviour fix inside it is fine as long as the hash twin
holds (QA's `AttackAlertsRingQaTests.AlertsFedEveryTick_*`), so "sim/ diff empty" in a view brief means "nothing outside `ViewApi/`".

---

## Sim track

### Current session plan (PLAN 2026-10-27-0158, session 1 / 8; sim counter 0 / 4 → feature)

**TASK M6-S4 · Teams, slice 1 (Request 38): a team per seat, allies never target each other, the last team standing wins · QA full · bug ids from 1042.**

Goal: M6 criterion 1's last word ("team") and docs/02 "Game flow" 2 / 4 need the sim to know alliances. Slice 1 ships the seat field,
one alliance predicate used by every "is this an enemy" site, the per-team result, the AI treating allies as non-enemies, and the two
headers. **Shared vision between allies is slice 2 (M6-S5), not this task** (Producer decision: allies will share vision; built after
the vision cost is measured). Starting resources (Request 43) is the session after.

Scope (sim track only; `sim/**`, `tools/Rts.Cli` for an optional `--teams` flag, docs/02 + docs/03; **no `game/` change**):
- `PlayerSeat.Team` (`int`, 0 = no team: the seat is its own side; 1-8 = a named team). `SimConfig.ValidateSeats` refuses a team outside
  0-8. One predicate, precomputed at `World` construction into a flat `bool[players × players]` (no per-tick cost, no allocation):
  `World.AreAllied(a, b)` = `a == b || (Team[a] != 0 && Team[a] == Team[b])`; `IsEnemy` is its negation. Public read-only `World.TeamOf(p)`
  for the view (banner, stats) and `SimConfig.SeatOf(p).Team`.
- Every site that means *enemy* goes through the predicate; sites that mean *the same player* (groupmates, own buildings, queue and
  production ownership, movement shoving, `PlayerView.IsOwnUnit`) stay as they are. The dev lists every `Owner == / !=` site it classified
  in docs/03 "Implementation (M6-S4)" (a table: file, line's meaning, allied-as-own / allied-as-other / untouched). Known enemy sites:
  `SpatialHash.NearestEnemy` / `QueryEnemies` / `OthersThan` and the single-owner bucket skip (the hash gets the team table at construction;
  the skip must treat an allied-only bucket as skippable), `CombatSystem` acquisition / retaliation (≈546-560, 640, 796-864),
  `TowerSystem`, `ProjectileSystem` (friendly fire: allied units take the 50 % own-unit share, allied buildings none, docs/02 "Splash and
  friendly fire"), `ZoneSystem` / `AbilitySystem` / `PassiveSystem` "own" (allied units get friendly effects and are spared hostile ones),
  `StealthSystem` + `World.Stealth` (a stealthed unit hides only from enemies), `VisionSystem` / `FogStore.HidesFrom` (a Darkness / Sandstorm
  zone blocks only enemy viewers), `OrderSystem`: an explicit `Attack` on an allied unit or building is dropped as one on an own unit is,
  `AttackMove` acquires enemies only.
- The result per side: `World.HasResult` → the match is over when every undefeated player that ever built is on one team (team-0 players are
  one-player sides); `IsDefeated` / `DefeatEvents` unchanged (a player is still out on its own). A 2v2 where one player of each side falls is
  not over; where both of one side fall it is. Free-for-all (every team 0) is bit-identical to today.
- The AI: `PlayerView` "seen enemy" answers (`NextUnit` / `NextBuilding` enemy enumeration, ghosts, `Blackboard.SeenEnemy*`, the last enemy
  army) exclude allied units and buildings; `DefenseManager` triggers never on allied units near the hall nor on an ally hitting nothing;
  `MilitaryManager` wave and rally targets are the nearest known *enemy* building; `ScoutManager`'s sweep ends on an enemy building, not an
  ally's; `AbilityManager` targets enemies only. Allied units are seen only inside the AI's own vision (no sharing yet). The AI reads nothing
  of an ally's hidden state (`PlayerViewArchitectureTests` stays).
- Headers: save format **3** (one team byte per seat after the difficulty; format 2 reads `FormatVersionMismatch` as today, docs/02 "Game
  flow" 1 says "another version"); replay format **7** (the `seat` line gains a team token; formats 3-6 read as team 0; `ReplaySeat.Team`;
  `Replay.Validate` refuses a team outside 0-8). **Rule 36:** a new version, no new signature or code. The two golden replay files are
  format 6: untouched and still play (`ReplayGoldenTests` green without regenerating; the content hash is not touched: `data-hash`
  E108AB2A7C66C58D). `CliRunner --teams 1,1,2,2` (per player, default all 0) only if it is a few lines.
- OUT: shared vision (M6-S5), starting resources (43), allied unit pushing / formation rules, allied resource or tech sharing, AI
  cooperation (an AI never coordinates with its ally beyond not attacking it), the view's team column (view, next session), diplomacy
  changes mid-match, M5-4d / M5-4c slice 2.

Acceptance criteria:
1. `Ai/.. / TeamTests` (new): allied units never auto-acquire each other across 2,000 ticks of a mixed 2v2 brawl (0 allied-on-allied
   attacks in the command log and in `Target`), towers skip allies, a Catapult's splash deals 50 % to allied units and 0 to allied buildings,
   an explicit `Attack` on an ally is dropped, an `AttackMove` through an allied army walks on and strikes the enemy behind it.
2. Result per team: the three cases above (one of each side out: not over; both of one side: over; FFA unchanged) + a 3-player 2v1 and a
   4-player 1v1v2; `MatchOver` latches as before; `World.TeamOf` / `AreAllied` answers pinned for team 0 and 1-8.
3. Free-for-all unchanged: `ReplayGoldenTests` 2 / 2 green on the untouched golden files; every existing determinism / stress / AI pairing
   row green without a hash change (QA A/B against base `fd0720c` on three scenes: hash-equal every tick).
4. Save and replay: a 2v2 human + AI save at 500 loads and runs 1,000 ticks hash-equal; a replay of a 2v2 AI match plays to every
   checkpoint; format-6 replays read with team 0; a forged team 9 / −1 refused `InvalidHeader` in both; `SaveRefusalTests` sweeps extended.
5. AI in a 2v2: on seeds 1-3, 2,000+ ticks, no AI seat ever sends a wave, an `Attack`, an `AttackMove` point or a cast at an allied unit
   or building (checked through the command log and `WaveTarget`); an allied army parked beside the AI's hall for 2,000 ticks never sets
   `Defending`; `AiDeterminismTests` twin 2v2 × 3 seeds × 2,000 ticks; think and tick stay 0 B (`AiPerfTests` rows unchanged).
6. Fog / stealth with allies: a stealthed allied unit is visible to its ally inside the ally's vision and hidden from enemies as before; a
   Darkness zone of an ally does not blind the ally; the `PlayerViewOracle` learns `AreAllied` and the oracle fuzz (3 seeds × 2,000 ticks,
   4 players as 2v2) stays equal; no answer about an ally's hidden state (`PlayerViewArchitectureTests` green).
7. `SpatialHash` tests: allied skip in `NearestEnemy` / `QueryEnemies` incl. the single-owner bucket skip and `OthersThan` (allied-only map
   returns 0; a mutation that keeps the old owner skip fails them).
8. Build 0 warnings; full non-Perf suite green (Perf alone green: `VisionGatePerfTests` 4 ms row and the far-corner row unchanged within
   noise, A/B head v base); docs/02 "Game flow" 2 / 4 + "Vision and fog of war" (one line: allies do not share vision yet; planned M6-S5)
   + "Splash and friendly fire" (allies), docs/03 "Implementation (M6-S4)" with the predicate table, the save format 3 and replay format 7
   notes, docs/01 change-log row (Producer decision: shared vision is slice 2); `studio/bugs/README.md` rows for any bug the dev files.

Design references: docs/02 "Game flow" 2 (team per slot, default free-for-all) and 4 ("the last player or team standing wins"), "Splash
and friendly fire" (50 % own-unit share; buildings never), "Vision and fog of war", "AI opponent" (last line: the AI sees only its own fog
view); docs/03 "Spatial hash" (the M4-1 bucket skip), "Save format (M6-S2 / S3)", "Save/load and replays" (format 5 seat lines), standing
rule 36 (save API freeze), rule 31 (a sim change that moves the AI's play → the data track re-prints: this one must not, teams default 0).

Tests required: `TeamTests` (criteria 1-2), `SeatConfigTests` team range, `SaveRoundTripTests` + `SaveRefusalTests` + `SaveFormatTests`
format-3 rows, `ReplayFormatTests` format-7 + format-6 compatibility rows, `AiDeterminismTests` 2v2, `Ai/PlayerViewOracleFuzzTests` with
teams, `Ai/MilitaryManagerTests` / `DefenseManagerTests` / `ScoutManagerTests` allied rows, `SpatialHashTests` allied rows, `ReplayGoldenTests`
untouched and green, an `ArchitectureTests` row that every `QueryEnemies` / `NearestEnemy` caller passes a player (no raw owner compare
added in `Combat/`).

Constraints: no Godot in `Rts.Sim`; no per-tick allocation (the alliance table is built once; `SpatialHash` takes it by reference); no
`Dictionary` iteration; the state hash must not change for team-0 matches (the team is config, in the headers, not in `StateHash`); no
hard-coded stats; player-facing text none (the view names teams); rule 36; `sim/Rts.Sim/ViewApi/**` untouched except a read-only
`TeamOf` pass-through if the view needs it (it can read `World.TeamOf` directly). ~800-1,000 lines; if the AI half runs over, ship the
sim half (criteria 1-4, 6-8) and leave the AI's allied rows as criterion 5's named gap for the Producer to split.

### Next task candidates (in order)

1. **Request 43 (starting resources):** standard / high purses, `rules.json`-driven (`startingResources` presets or a multiplier), in
   `SimConfig`, the save header (format 4) and the replay header (format 8); the AI's openings must still work at "high" (a pairs-report
   row). Then **M6-S5 (teams slice 2): shared vision between allies** after measuring the vision cost (the union per team cell).
2. **A map file format for M6 criterion 7's three hand-made maps** (the view can't build them without one): a heightmap + resources + start
   locations file under `game/data/maps/`, loaded by the sim, hashed in the header. Plan after 43 unless the owner asks for maps first.
3. M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last
   units: BUG-0953's sim half, seed 11); BUG-1010's lever (D-R32 re-read the thresholds: keep 18 stands, so the lever is a sim rule such as
   "send what you have when the base is being lost", tried once as defend-then-counter and backed out: 0 / 6); D14 c; M5-5.

### Watch-outs (sim)

- `SimConfig.MaxCommandCapacity` 65,536 is now a hard bound: a test or tool asking for more throws at `Validate` (the CLI caps its queue).
- `AiSaveScenes` probes the Blackboard (rule 37); a manager change that moves a probe fails `AiMatch_SavedAtFixedTicksAndProbedMoments_*`:
  adjust the row's tick, not the probe. The AI's state is not hashed: a manager change moves no `k` line but moves the pairing reports
  (rule 31: the data track re-prints).
- `ReservePairingsStressTests.KnownNoAgeTwo` names four sides; a sim change that gets one of them to Age II prints "take it out": take it out.
- `VisionGatePerfTests.Brawl250v250_DesignBudget_FreshProcessShortRun_Under4Ms` reads 3.50-3.90 ms against 4 ms: ~10 % margin; it spawns
  `dotnet test` children (120 s timeout). A failure alone on a quiet machine is real; under another track's run it is not.

---

## View track

### Current session plan (PLAN 2026-10-27-0158)

**STOP (cheap, no counter change).** Nothing the view needs has landed: Request 38 (teams) is the sim's task *this* session (M6-S4), 43 is
next, no map format yet, and the owner's art call / listening note / downloads are not in the inbox. **Next session, if M6-S4 merges: M6-V16,
the setup's team column** (a Team list per slot, 0 = none / 1-4; the banner says Victory when the local player's *team* stands, the stats
screen groups seats by team; `--team` per slot; Load Game's header line shows the teams; QA standard). Riders: BUG-1060 items 2-3.

### Next task candidates

Every M6 item left waits on something: teams on Request 38, starting resources on 43, the three maps on a map format, the art pass on the
owner's look call, the music / fonts / export templates on the owner's downloads, the sound re-cuts on the owner's listening note. **If
none has landed at the next PLAN, the view STOPs** (a cheap STOP, no counter change). Small items that could ride along with a real task:
BUG-1060 items 2-3 (the `wontfix` word on BUG-0521; the no-LFS first-run note), BUG-0550 item 2 (the art pass).

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape; a sim change to `SaveHeader.Write` (a format bump for 43) breaks the list's tick column
  silently: `SaveSummaryQaTests` catches it; re-run `SaveLoadTest` + `QaM6V14Test` + `QaM6VH4Test` on the merged tree when `Save/` changes.
- `smoke.ps1` now matches `-cmatch` and skips `[n]` frame lines, and skips the wav importer's lines only when `.wav` files are LFS pointers
  (prints a `WARN`). A normal clone: unchanged gate. A fresh no-LFS clone still fails its first run (looktest art), passes from the second.
- `StatsScreenCheck.LineReadable` threshold 1.4:1 on the outline: set just under the shipped Violet (1.45:1). A palette change in `ui.json`
  is the owner's taste; don't raise the threshold without it.
- `scene-loop.ps1 -Filter` is a file glob; the loop is **73** scenes now (`QaM6VH4Test` added), ~22 min.

---

## Data track

### Current session plan (PLAN 2026-10-27-0158)

**STOP (cheap, counter stays 1 / 4).** The inbox is empty (no D21 / D19 / Shock / `goldShare` / BUG-0780 answers) and no schema the data
track needs is new on `main` (M6-S4's teams need no faction content; the default team 0 moves no pairing number, so no rule-31 re-print).
BUG-1080's two wordings keep waiting for the next page-touching task.

### Next task candidates

1. The owner's answers above, each a small page + `ai.json` / data task with a review table (golden regen once).
2. **BUG-1080 (S4):** two page wordings (seed 32 "still standing" at 3 pop / cap 0; whirlwind.md's unmeasured "wood runs short 0-50 from
   13:00"): fold into the next page-touching task.
3. Rule 31 re-prints whenever a sim task moves the AI's play (38 / 43 should not; M5-4c slice 2 and M5-4d will).

### Watch-outs (data)

- `D21OpeningsQaTests.KnownBeatenAlive` {(1,1,19,0)} and `DH6ThresholdQaTests.KnownBeatenAlive` {25} both print "take it out (BUG-1010)"
  when their case passes: take them out then.
- `cadre_rush` v `raider_rush` seed 18 (alive, no wave at 20:00) is shown only by the non-asserting DH6 table row; making that row strict
  needs an allowance for it (QA's note).
- Every "Age II against ..." cell on both pages is pinned by `AiOpeningPairsReportTests.AssertAgeTwoCells` from the 1st / 2nd / 3rd-seed runs.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/03: each track appends its own rows; the conductor unions them (this session all
  three touched docs/03 and the index: sim → view → data).
- Three tracks run: three test hosts; wall-clock Perf and scene rows flake under load; rerun alone before filing.
- Bug ids next: **sim from 1042, view from 1061, data from 1081** (the PLAN re-allots; QA briefs quote the allotment verbatim).
- The owner's old recordings refuse to play since E108AB2A7C66C58D (two `rules.json` fields); the seed-21 golden was regenerated then.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase (machine-local); the owner's call on the window
  itself is listed in STATE "Wanted".
