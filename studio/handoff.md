# Handoff: brief for the current / next session

Written by the Producer at the PLAN of session **2026-10-18-0516** (the first full session of 2026-10-18; cap 8). Base commit
**b789376** (= `origin/main`, all three worktrees). Inbox **New** empty. Build 0 warnings; smoke PASS in the view worktree; the
non-Perf suite's result is in the session log. Counters before: **sim 1 / 4, view 1 / 4, data 1 / 4** → all three run **feature**
sessions. Bug ids: **sim from BUG-0960, view from BUG-0970, data from BUG-0980** (highest used: 0955). Open S1 / S2: none (by file
status: S3 16, S4 18; four stale `open` index rows for fixed bugs 0790 / 0791 / 0850 / 0900 removed at this PLAN). One red scene-loop
row on `main`: `QaM6V11Test` (BUG-0953; the view's first item).

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5, 8 advanced). Criterion 8 (D14 b) has its sim half (M5-8a); **D21 writes the openings
  this session** and moves the golden `data-hash` (now 1CBDE4DBA7D17AE1) once.
- **M6 2 / 10** (criteria 2, 4 ticked; 1, 3, 5, 6, 7 advanced). Criterion 3's save half starts this session (M6-S2, slice 1: human-seat
  matches); slice 2 (AI seats) next session, with the view's Save / Load screen built against slice 1's frozen API.
- Still wanting the owner (not blocking): the art call, listening to the SFX, the Shock pass, `goldShare`, BUG-0780, "densest cluster".

## Standing rules (1-36)

Unchanged 1-35 (the 2344 ACCEPT handoff in git history has 35 and points to the long forms). New this session:

- **36 (save API freeze).** Slice 1 fixes `Simulation.TrySave(Stream, out SaveError)` / `Simulation.TryLoad(GameData, Stream, out
  Simulation?, out SaveError)` and the `SaveError` codes. Slice 2 adds sections and lifts `AiSeatsUnsupported` without changing the
  signatures or the existing codes, so the view can build its Save / Load screen against slice 1 in the same session slice 2 lands.

## Checks to run at the next PLAN

- Inbox **New**; `git status` / `git log`; build; the non-Perf suite on the merged head; smoke in the view worktree.
- Trust-but-verify candidates: `sim/Rts.Sim/Save/SaveFormat.cs` exists with `CurrentVersion` 1 and `TryLoad` never throws on a
  flipped byte (`SaveRefusalTests`); `ViewApi/AiOpeningLookup.Pick` equals `Simulation.AiOpening` (`AiOpeningLookupTests`); both
  `ai.json` hold 3 openings and the golden's `data-hash` moved once from 1CBDE4DBA7D17AE1; `QaM6V11Test` green in the loop.
- Sessions today: count `studio/sessions/2026-10-18-*.md`.

---

## Sim track

### Current session plan: M6-S2 — the save format and `Simulation` save / load, slice 1 (human-seat matches; Requests 42)

**Type** feature (counter 1 → 2 / 4) · **QA** full · **Size** ≤ 1,500 lines (the design is in docs/03 "Save/load and replays"; the
slice leaves the AI seats out) · **Bug ids** from BUG-0960 · **Files** `sim/Rts.Sim/Save/**` (new: `SaveFormat`, `SaveHeader`,
`SaveError`, `SaveWriter`, `SaveReader`), `Simulation.cs` (`TrySave` / `TryLoad`), internal `WriteTo` / `ReadFrom` on the stores
(`Entities/**`, `Map/**`, `Pathfinding/FlowFieldCache`, `Spatial/SpatialHash`, `Vision/FogStore`, `Abilities/ZoneStore`, `Combat/**`,
`Economy/MatchStats`, `World*.cs`, the ledger, techs), `sim/Rts.Sim.Tests/Save/**`, docs/03 "Save/load and replays". **Not**
`game/**`, not `game/data/**`, not `Ai/**` (slice 2), not `Replays/**` (the replay stays a text file with its own parser).

**Goal.** M6 criterion 3's save half: a save made between ticks restores a match exactly. The loaded sim's `StateHash` equals the
unsaved run's at every later tick, and the state outside the hash that decides the next tick or the post-match screen is restored too
(docs/03 "Save/load and replays" lists it). Slice 1 covers matches whose seats are all human; `TrySave` refuses an AI seat with
`SaveError.AiSeatsUnsupported` until slice 2 (next session) stores the Blackboard, the opening pick and the AI RNG.

**Scope.**
- **Format:** binary, little-endian, fixed field order, `BinaryWriter` / `BinaryReader` over a `Stream`. Header: magic, format
  version 1, `SimInfo.Version` (informational), `GameData.ContentHash()`, seed, player count, seats (faction string id, controller,
  difficulty), the capacities (unit, command, resource, building, projectile, zone), every `MapGenParams` field, `Combat`,
  `CastWalkInBlockerRange` / `CastWalkInPatienceSeconds`, `StatsSampleTicks` / `StatsSampleCapacity`, tick; then one section per
  store; last an FNV-1a 64 checksum over every byte before it (as the replay). The reader never throws on bad bytes: `SaveError`
  codes `BadMagic`, `FormatVersionMismatch`, `DataMismatch`, `InvalidHeader`, `AiSeatsUnsupported`, `Truncated`, `ChecksumMismatch`,
  `Malformed`.
- **Body, every store whole:** Units (every array incl. `Flank`, `PrevPosition` / `PrevFacing`, order queues, cargo, combat state),
  Buildings (queues, rally, `UnderConstruction` work, `BuildingGhost.Site` memory), Resources, Projectiles, Zones, Fog (explored /
  visible bits, reveals, `NodeMemory`, the last-known lists), NavGrid (cells, flags, `Version`, the movement pass's last seen block
  version), Heightmap (stored, not re-generated), SpatialHash whole, FlowFieldCache whole (every used slot's key, direction bytes,
  costs, version and LRU stamps: docs/03's Producer decision 2026-10-07, BUG-0081: store, don't rebuild), every RNG stream's `State` +
  `Increment` (the never-drawn AI streams too), the ledger (gold, wood, half-pop, caps, kills, losses), techs per player,
  `SurroundClaim` / `SurroundHolder`, the defeat flags + `EverBuilt` + the match-over latch, the stealth detection mask,
  `MatchStats` whole, the pending command queue with each player's next sequence number, and the per-tick event spans (`Deaths`,
  `Impacts`, `DefeatEvents`) unless a test proves `Tick()` clears them before anything reads them.
- **Derived structures** (SpatialHash bucket order, the stealth mask, the flow-field cache): save whole. A rebuild is allowed only
  with a test that proves it tick-for-tick identical (hash every tick for 2,000 ticks and the direct compares) on every round-trip
  scenario.
- **Entry points:** `bool Simulation.TrySave(Stream, out SaveError)` (between ticks; refuses with `AiSeatsUnsupported` when
  `Config.HasAiSeat`); `static bool Simulation.TryLoad(GameData, Stream, out Simulation?, out SaveError)` builds `SimConfig` from the
  header and a `World` from the stored map (no `MapGenerator` / `ResourcePlacer` run: a load draws from no stream).
- **OUT:** AI seats (slice 2); the replay log so far (the view's Save screen writes the recording beside the save; noted in Requests 42);
  any `game/**` code or `ui.json` text (the view adds `save.*` keys with its screen); compression; a save mid-tick; hand-made maps.

**Acceptance criteria.**
1. `TrySave` / `TryLoad` with the signatures above; `SaveFormat.CurrentVersion` = 1; docs/03 "Save/load and replays" gets the format
   table (header fields in order, each section, the checksum), the refusal codes and the slice-1 limit.
2. **Round trip, hash.** For every scenario in S = { `CrossMapScenario` seed 1 driven by `cross_map_seed1.replay`'s command log; the
   menu's map with 2 human seats (seeds 1-5) driven by a scripted command stream that gathers, builds, trains, researches, attack-moves
   and casts; a 4-seat human-only 256 map; a brawl with a surround ring; a cast resolving after the save and a live Sandstorm zone; a
   stealthed unit beside a detector; a building under construction and a 5-deep queue holding a tech; a felled tree and a razed
   building; a seat already defeated (M5-4e) }: save at T ∈ {1, 137, 1,000}, load into a fresh `Simulation`, feed both the same
   remaining commands, tick both to T + 2,000: `StateHash` equal **every tick**.
3. **Round trip, direct compares** at T, T + 1 and T + 2,000: `SurroundClaim` / `SurroundHolder`, `BuildingGhost.Site`,
   `UnitStore.Flank`, `MatchStats` (every span, the sample count, each tech's tick), `Fog.NodeMemory` + the last-known lists, the
   flow-field cache's slot keys and stamps, the pending queue (count, order, every field), each player's next sequence number,
   `Deaths` / `Impacts` / `DefeatEvents` (or the clearing proof).
4. **Idempotence:** save → load → save is byte-identical to the first save, on every scenario in S.
5. **Refusals:** a flipped byte at every offset of one small save, every truncation length of it, another format version, another data
   hash (a display-name edit through the test loader), an AI seat, an unknown faction id, player count 0 / 17, a 1-byte stream, an
   empty stream: each returns its `SaveError`, `sim == null`, nothing thrown.
6. **Architecture:** no `DateTime`, no reflection, no `System.Random`, no `Dictionary` iteration under `Save/**`
   (`ArchitectureTests` extended); `Tick()` and the systems' `Run` methods unchanged (`git diff` shows no hunk in them); paired A/B of
   the storms fuzz and `EmptyTick` Perf rows within noise.
7. **Size and time** (one `[Trait("Category","Perf")]` row): the menu's match (128 map, 2 seats) at tick 12,000 saves ≤ 100 ms, loads
   ≤ 100 ms, file ≤ 8 MB; the 4-seat 256 map's size and times printed (no gate; a follow-up shrinks the flow-field section if > 32 MB).
8. A loaded sim's `TickNumber`, `MatchOver`, `PendingCommandCount` equal the saved sim's; `Simulation.AiOpening(p)` is −1 for every
   seat (human); a `ReplayRecorder` can attach to a loaded sim only if it has not ticked (document the rule; no change to the recorder).

**Tests required.** `Save/SaveRoundTripTests` (2-4, 8), `Save/SaveRefusalTests` (5), `Save/SaveFormatTests` (header order, version,
checksum), `Save/SavePerfTests` (7), `ArchitectureTests` rows for `Save/**` (6).

**Constraints at risk.** Rule 2 (no wall clock in the sim; the Perf row measures from the test); rule 4 (a load draws from no stream;
every stream's words restored); rule 5 (`Tick()` untouched); docs updated with the behaviour; one public type per file (the stores'
`WriteTo` / `ReadFrom` are internal methods in their own files).

**QA focus.** Anything restored "equivalently" rather than identically (SpatialHash bucket order, the stealth mask, flow-field LRU
stamps, sequence numbers) shows up as a later divergence: run two scenarios to T + 6,000; fuzz T over 50 random ticks on the menu
match; a save at tick 0 and at the banner tick; load the same stream twice; the refusal sweep over every byte; the Perf row alone and
under load; a 256-map 4-seat save's size reported.

### Candidates after (in order)
Slice 2 (AI seats: `Blackboard` whole incl. `StepNoSiteUntil`, the defense marks, the casts, `WaveMinHalfPop` / `WaveMinCost`;
`AiPlayer.OpeningIndex` + the AI RNG after the pick: a loaded seat never draws again; round trip on AI-v-AI seeds 1-5, 2-4 seats, every
difficulty, mid-wave / mid-cast / in a no-site wait; lifts `AiSeatsUnsupported`; the view's screen lands the same session); Requests 38
(teams); M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting,
hunting the last units: BUG-0953's sim half, seed 11); D14 c (counter-composition); M5-5 (the 20-minute winner test); M5-6.

### Debt (next sim hardening, counter 2 / 4 after this session)
BUG-0912, BUG-0911, BUG-0889, BUG-0144, BUG-0941, BUG-0940, BUG-0914 + BUG-0915 (S4 flakes), BUG-0913 (7 nits), BUG-0113 item 2,
BUG-0704 item 3.

### Watch-outs (sim)
- The save must not change the hash composition: no `k` line of the golden moves this session.
- `World(SimConfig, Heightmap?)` already takes a map: extend that path for the load rather than a second constructor.
- BUG-0790's stand points: untouched here (no gather / build change).
- Seed 11's four-seat match reaches Age II at 14:10: M5-5 should expect minutes of spread.

---

## View track

### Current session plan: M6-V13 — BUG-0953's view half first, then "the AI opened with …" on the stats screen + BUG-0954 item 1

**Type** feature (counter 1 → 2 / 4) · **QA** standard · **Size** ≤ 800 lines · **Bug ids** from BUG-0970 · **Files**
`game/tests/QaM6V11Test.cs`, `game/scripts/MatchStats.cs` / `MatchOverText.cs` (+ the scene if a row is needed),
`game/data/common/ui.json` `match_over.opening` + `match_over.opening_human` (rule 32), **new** `sim/Rts.Sim/ViewApi/AiOpeningLookup.cs`
(read-only: `static int Pick(ulong seed, int player, AiDifficulty difficulty, int openingCount)` =
`AiPlayer.PickOpening(count, difficulty, ref new SimRng(seed, (ulong)RngStream.Ai(player)))`, the construction `Simulation.cs:54`
makes; `PickOpening` is `internal static`, reachable from `ViewApi/`) + `sim/Rts.Sim.Tests/ViewApi/AiOpeningLookupTests.cs`,
`sim/Rts.Sim/ViewApi/AttackAlerts.cs` + its tests (BUG-0954 item 1), `game/scripts/AlertHud.cs`, docs/03 "Implementation (M6-V13)",
docs/02 "Game flow" 5. **Not** `sim/**` outside `ViewApi/` and its tests; **not** `game/data/factions/**` (D21 edits both `ai.json`
this session: test the line with a 3-opening fixture under `game/tests/` or the sim tests' fixtures, never the shipped files).

**Part 0 — BUG-0953 (S3, the red loop row on `main`).** `QaM6V11Test`'s banner row waits for seed 11's idle human to fall; since
M5-H3 the AIs never hunt its workers. Stage the defeat with `DefeatStaging` (as `FreeForAllTest` does) so the row stops tracking AI
changes; keep every other check; `scene-loop.ps1` green. Say in the report that the AI-side gap stays (M5-4d).

**Part 1 — "opened with".** The stats screen gets one line per AI seat: "<player's colour or faction name> opened with
<displayName>" from `Simulation.AiOpening(p)` and `GameData.Ai[faction].Openings[i].DisplayName` (the description as a tooltip or a
second line is optional); a human seat shows nothing (or `match_over.opening_human` if the layout needs a cell). Text keys in
`ui.json`; the 1280 × 720 layout still fits four seats (`StatsScreenCheck` row). A watched replay shows the same line: playback runs
every seat human, so the view computes the pick with `AiOpeningLookup.Pick` from the replay header's seed and seats (the difficulty
is in `ReplaySeat`). With today's data every AI says "Standard"; test with a 3-opening fixture.

**Part 2 — BUG-0954 item 1** (S4, same code as the alerts): a building hit that joins a `Units` alert within 30 m / 10 s upgrades its
`Kind` to `Base`; `AlertHud` re-shows the text if the alert was off screen and the kind changed; never a downgrade. xUnit rows beside
`AttackAlertsTests`, red on head-before, green after.

**Acceptance criteria.**
1. `QaM6V11Test` PASS with the staged defeat; the loop green (68 / 68, or n / n with the new scene); the report names the AI-side gap.
2. The line for 1-3 AI seats at 1280 × 720, `ShellTest` on the two keys, a screenshot looked at; a human seat shows no opening.
3. `AiOpeningLookupTests`: `Pick` equals `Simulation.AiOpening(p)` for seeds 1-200 × 3 difficulties × 2-4 seats on a 3-opening
   fixture; `ReplayPlaybackTest` checks the line on a watched replay with a Normal AI seat.
4. BUG-0954 item 1's rows red before, green after; `AlertTest` still PASS.
5. Smoke PASS, build 0 warnings, `sim/` diff only under `ViewApi/` + its tests; text only in `ui.json`; `AiOpeningLookup` writes nothing.
6. docs/03 "Implementation (M6-V13)"; docs/02 "Game flow" 5 lists the line.

**QA focus.** Replay parity on seeds where Normal picks opening 2 vs 0 (the fixture) and on a 4-seat replay with every difficulty; a
missing `ui.json` key (`UiText.ForwardKey`); the layout with 3 AI lines above ten columns and a graph; the alert upgrade at the 30 m /
10 s edges and when the first hit is already a building; the loop twice.

### Candidates after (in order)
The Save / Load screen against slice 1's frozen API (rule 36: Save in the pause menu to `user://saves/`, Load Game lists saves beside
replays, `ui.json` `save.*` text for every `SaveError`; AI-seat saves start working when slice 2 merges the same session; the recording so
far written beside the save); the owner's sound re-cuts when the listening note arrives; teams when 38 lands; `tools/export.ps1` when the
owner's templates arrive; criterion 7's generator options in the setup (`MapGenParams` knobs: plateaus, forests, mines) and the
hand-made maps when a map format exists (Requests); the art pass when the owner answers; criterion 6's music when the track arrives.

### Debt (next view hardening, counter 2 / 4 after this session)
BUG-0955 (S3: `smoke.ps1` adopts `scene-loop.ps1`'s `-cmatch` + frame skip), BUG-0954 item 4, BUG-0922 (S4), BUG-0550 item 2 (the art
pass), BUG-0521 items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).

### Watch-outs (view)
- `scene-loop.ps1 -Filter` is a **file glob**, not a regex; `-TimeoutSec` default 600. `QaM6V11Test` ~93 s; `M3PlayableTest` ~2 min.
- A clone without `git lfs pull` fails `LookTest` / `SfxTest` clip rows and smoke (BUG-0955); the game boots on tones.
- `Sfx` pool: 8 world + 6 UI players; a new UI event must be `World = false` in `Sfx.Table`.
- Space is reserved (`KeyMap.Reserved`).

---

## Data track

### Current session plan: D21 — three named openings per faction

**Type** feature (counter 1 → 2 / 4) · **QA** standard (scenario numbers) · **Size** ≤ 1,200 lines incl. the faction-page re-prints ·
**Bug ids** from BUG-0980 · **Files** `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json` (no other track
touches them), `docs/factions/malazan.md` / `whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, the
golden `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` (header `data-hash` + `checksum` only; regen once, BUG-0750's rule) and
`QA/GatherWedgeQaTests.SameGameDataHashes` with the reason. **Not** `rules.json`, not `units.json` / `buildings.json` / `techs.json`,
no C# outside the two data test folders.

**Design refs.** docs/02 "AI opponent" (Easy plays the first opening; Normal / Hard pick by seed); docs/03 "Implementation (M5-8a)"
(1-8 openings, `id` snake_case unique, `displayName` 1-40, `description` ≤ 200, each with `buildOrder` ≤ 256 steps, `compositions` one
per age, `attack.minArmyPop` / `minArmyCost`), "Implementation (M5-1)" (step rules), "Implementation (M5-2)" (the executor: steps run
side by side, one command per step per think, an earlier step gets the gold first; the house skip at the hard cap, D15 C);
`docs/factions/<id>.md` "AI build order (Normal)", "Houses at the cap", "Spending on the menu's map".

**Content.** Opening 0 **`standard`** keeps today's order (a real `description`; `displayName` may stay "Standard" or become a name
the player reads: the data dev's call, quoted). Two new openings per faction, e.g. Malazan **`crossbow_push`** ("Crossbow push":
Range before Barracks, two Ranges, a lower `minArmyCost`, Age I mix ~60 % Crossbowman / 40 % Heavy Infantry) and **`cadre_rush`**
("Fast Age II": `age_ii` at ~300 s, Cadre Tower early, mages heavy in the Age II mix); Whirlwind **`raider_rush`** ("Raider rush":
two Raider Camps, `minArmyPop` ~10, Raiders + Horse Raiders) and **`holy_camp`** ("Holy camp": Shrine and Zealots early in Age II,
a slower first attack). Each ≤ 40 steps, houses through the cap, `expand` once, compositions of the faction's own units of that age,
`attack` thresholds consistent with the mix (a `minArmyCost` the mix can reach). **Fold in the parked D15 C finding:** the 12th house
step (Malazan step 18 `pop 81`, Whirlwind the `pop 79` tent) is due at cap 90 with 6 of 8 pop unused: drop or move it in `standard`
(and don't repeat it in the new openings); the "Houses at the cap" report decides and is re-printed per opening.

**Measure.** `AiSpendingReportTests` per opening on Normal against an idle seat: the pick is by seed, so the report names the smallest
seed in 1-50 whose Normal pick is that opening (Easy for opening 0 too), the 5-minute marks and the idle base's fall time, printed on
the faction pages under a per-opening heading. `AiContentTests` pins each opening's step table both ways; `AiContentFileSideQaTests` /
`AiContentQaTests` / `AiHouseRuleReportTests` / `D19QaTests` extended per opening (never reverted to `openings[0]` only);
`D20PinQaTests` re-pinned with the reason; `CounterTriangleMarginsTests` untouched (no unit number moves); `BuildingKillTimeReportTests`
12 / 12.

**Acceptance criteria.**
1. Both files load with exactly 3 openings; `DataValidationTests` green; every id resolves; every `build` / `research` step's
   requirement stands earlier in the same order (or at the start); the golden regenerated once with the reason; `data-hash` moved once.
2. A 20-minute Normal AI-v-AI headless match for each of the 9 Malazan × Whirlwind opening pairs (seeds scanned so each pair occurs)
   ends with both sides having sent a wave and no dead-end step (every step done or legitimately waiting at 20:00, said which).
3. The spending report and "Houses at the cap" printed per opening on both pages; the 12th-house decision measured.
4. The review table: opening / field / old → new / why; every `displayName` and `description` quoted; Easy keeps opening 0 (said on
   the pages).
5. No C# outside the data test folders; `rules.json`, the unit / building / tech files untouched.

**QA focus.** Dead-end orders (a trigger that never fires; a Shrine before Age II in `holy_camp`); a rush capping at 20 pop with no
house; composition ids of the wrong faction or age; `minArmyCost` above what the mix can reach (no wave ever); the Easy path (opening 0
only); the hash moving exactly once; every old test row extended, not reverted; the 9 pairings' 20-minute runs (say the runtime).

### Watch-outs (data)
- The golden moves: old owner recordings refuse to play again; say so in the review entry.
- A Normal / Hard AI picks by seed: every report names the opening it measured (`Simulation.AiOpening`).
- The view tests the "opened with" line with its own 3-opening fixture; it never reads the shipped files' content in a test.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's
  first hardening item.
- The data track edits both `ai.json` files (D21): the sim and view tracks must not touch them.
