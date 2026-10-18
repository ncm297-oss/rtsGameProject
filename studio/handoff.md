# Handoff: brief for the current / next session

Written by the Producer at the ACCEPT of session **2026-10-17-2344** (the fourth full session of 2026-10-17; 2026-10-18 stands at 0 / 8).
Both active tracks ACCEPTed: sim **M5-8a** (the openings schema + the seeded pick + BUG-0910), view **M6-V12** (BUG-0920, real SFX,
attack alerts, the music hook; 1 fix round); data STOP. Merge order sim → view. Golden `data-hash` **1CBDE4DBA7D17AE1** (moved once by
the `ai.json` re-wrap; header + checksum only). Counters after: **sim 1 / 4, view 1 / 4, data 1 / 4**. Bug ids: **sim from BUG-0960,
view from BUG-0970, data from BUG-0980** (highest used: 0955). Open S1 / S2: none. One red scene-loop row on `main`: `QaM6V11Test`
(BUG-0953, pre-existing on base b30462a: the view's first item).

## Where we are

- **M5 3 / 9** (criteria 1, 2, 7 ticked; 3, 4, 5, 8 advanced). Criterion 8 (D14 b) has its sim half: `ai.json` `openings[]`, the pick
  (`AiPlayer.PickOpening`: Easy 0; Normal / Hard the AI stream's first and only draw; AI state, not hashed), `Simulation.AiOpening(player)`.
  The shipped files hold one opening `standard` each: **D21 writes the real ones.**
- **M6 2 / 10** (criteria 2, 4 ticked; 1, 3, 5, 6, 7 advanced). The SFX / alerts criterion has its SFX (16 Sonniss clips) and its alerts;
  music is the owner's download (the hook plays `assets/audio/music/*.ogg`). The owner has not heard the clips: the ask is under
  "Waiting on you" (not blocking).
- Inbox: **New** empty at the 2344 PLAN. Still wanting the owner: the art call, the Shock pass, `goldShare`, BUG-0780, "densest cluster".

## Standing rules (1-35)

Unchanged 1-34 (the 2344 PLAN handoff in git history has 34; the 1624 one 32-33; the 2036 one the long form of 1-31). New this session:

- **35 (cross-track test rewrites).** When a sim schema change breaks the data track's test files, the sim dev makes the mechanical
  rewrite in the same commit (paths and accessors only; no assertion, bound or pinned number moves), names the files in the report,
  QA reads every changed line, and the Producer confirms at ACCEPT. First case: M5-8a's six files.

## Checks to run at the next PLAN

- Inbox **New**; `git status` / `git log`; build; the non-Perf suite on the merged head; smoke in the view worktree.
- Trust-but-verify candidates: `Simulation.AiOpening` exists and `AiPlayer.PickOpening` draws once (`grep NextInt sim/Rts.Sim/Ai`);
  `game/assets/audio/sfx/**` has 16 `.wav` in `git lfs ls-files`; `KeyMap.Reserved` holds `Key.Space`; docs/03 has "Implementation
  (M5-8a)" and "Implementation (M6-V12)"; the golden's `data-hash` line is 1CBDE4DBA7D17AE1.
- Sessions today: count `studio/sessions/2026-10-18-*.md` (none yet).

---

## Sim track

### Current session plan (candidate): Requests 42 slice 1 — the save format and `Simulation` save / load, with a round trip

**Type** feature (counter 1 → 2 / 4) · **QA** full · **Size** ~800 lines (a new system: slice it; the format and the round trip first,
the view's Save / Load screen is the view's later task) · **Files** `sim/Rts.Sim/Save/**` (new), `Simulation` (entry points), the stores'
serialisation (`sim/Rts.Sim/Entities/**`, `Map/**`, `Fog/**`, `Combat/SurroundRule`, `Ai/Blackboard`, `AiPlayer`), `docs/03 "Save/load and
replays"`. Not `game/**`, not `game/data/**`, not `docs/factions/**`.

**Goal.** M6 criterion 3's save half: a save file that restores a match exactly (same `StateHash` at every later tick as the unsaved
run) including the state outside the hash that decides the next tick. The list in STATE Requests 42: every store, the fog bits, the
flow-field cache (or its rebuild), the RNG streams, the tick, the seats, `World.SurroundClaim` / `SurroundHolder`, the AI `Blackboard`
whole (incl. `StepNoSiteUntil`, the defense marks, the casts, `WaveMinHalfPop` / `WaveMinCost`), `BuildingGhost.Site`, `MatchStats`
whole, `UnitStore.Flank`, **the opening pick (`AiPlayer.OpeningIndex`) and the AI RNG state after it** (a loaded seat must not draw
again). Text (version / data mismatch) stays in `ui.json` (the view adds the keys when it builds the screen).

**Acceptance (sketch; the PLAN sharpens it).** (1) `Simulation.Save(Stream)` / `Simulation.Load(GameData, Stream)` with a versioned header
(format version, sim version, `data-hash`, seed, seats, tick). (2) Round trip: save at tick T of an AI-v-AI match (seeds 1-5, 2-4 seats,
every difficulty), load, run both to T + 2,000: `StateHash` equal at every 100th tick **and** the unhashed arrays compared directly
(the list above), `Simulation.AiOpening` per seat equal, the command sequence numbers equal. (3) A save mid-wave / mid-cast / with a
surround ring / with a pending build order step in its no-site wait / with a stealthed unit / with a storm zone. (4) A mismatched
data-hash or format version is refused with a reason, never a half-load. (5) No `DateTime`, no reflection-based serializer in the sim
(plain binary, fixed field order, little-endian). (6) Size: a 4-seat 256-map save ≤ 4 MB; save + load ≤ 100 ms (a Perf row). (7) Docs:
docs/03 "Save/load and replays" gets the format table and the round-trip rule; the Requests 42 list in STATE closed by the Producer.

**Watch-outs.** The flow-field cache can be rebuilt on load (cheaper than storing it) if the first ticks after a load produce the same
hash: prove it. `FogStore.NodeMemory` and the last-known lists are unhashed but visible: save them. The AI's `SimRng` state: expose a
`Save` / `Load` on `SimRng` (the struct's words). Don't let the replay format and the save format share a parser: the replay stays a text
file. Perf gates: paired A/B on the storms fuzz if anything touches a per-tick path (it shouldn't).

### Candidates after (in order)
Requests 42 slice 2 if slice 1 stops short; Requests 38 (teams: `SimConfig.Seats` team ids, allied vision / no friendly targeting /
shared defeat; the view's criterion-1 remainder waits on it); M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d
(expansion, rebuilding, high-ground scouting, hunting the last units: BUG-0953's sim half: on seed 11 the AIs raze the idle human's hall
and never hunt its five workers: a regression row on that seed)**; D14 c (counter-composition); M5-5 (the 20-minute AI-v-AI winner
test); M5-6.

### Debt (next sim hardening, counter 1 / 4)
BUG-0912 (the far-corner gate + a 4 ms design-budget row), BUG-0911, BUG-0889, BUG-0144, BUG-0941 (the cell-free test before `AnyBuilder`),
BUG-0940 (refuse a present `null`), BUG-0914 + BUG-0915 (S4 flakes: S3 and first item if either fails a second full run), BUG-0913 (7 nits),
BUG-0113 item 2, BUG-0704 item 3.

### Watch-outs (sim)
- BUG-0790's stand points move every diagonal footprint walk: any gather / build change re-runs `GatherWedgeQaTests` and the rule-31 cells.
- The data hash now covers the openings' ids and names: **D21 moves the golden** (regen once in D21's commit; `GatherWedgeQaTests.SameGameDataHashes` takes the new value with its reason).
- Criterion 1's HI row sits on its 8 / 10 bound (`FlankTests`); the Perf gates measure this PC's sustained clock (paired A/B shows cost).
- Seed 11's four-seat match reaches Age II at 14:10 since M5-H3 (was 17:30): M5-5 should expect minutes of spread.

---

## View track

### Current session plan (candidate): BUG-0953's view half first, then "the AI opened with …" on the stats screen + BUG-0954 item 1

**Type** feature (counter 1 → 2 / 4) · **QA** standard · **Size** ≤ 800 lines · **Files** `game/tests/QaM6V11Test.cs` (the re-stage),
`game/scripts/MatchStats.cs` / `MatchOverScreen` (or wherever the columns live) / `MatchOverText.cs`, `game/data/common/ui.json`
`match_over.opening` + `match_over.opening_human` (rule 32: named here), `sim/Rts.Sim/ViewApi/AttackAlerts.cs` (BUG-0954 item 1: upgrade a
`Units` alert to `Base` when a building hit joins it, re-show the text if it was off screen) + its tests, `AlertHud`, docs/03
"Implementation (M6-V13)", docs/02 "Game flow" 5 if the line is added to the list. Not `sim/**` outside `ViewApi/`.

**Part 0 — BUG-0953 (S3, the red loop row on `main`).** `QaM6V11Test`'s banner row waits for seed 11's idle human to fall; since M5-H3 the
AIs never hunt its workers. Stage the defeat with `DefeatStaging` (as `FreeForAllTest` does) so the row stops tracking AI changes; keep
every other check; `scene-loop.ps1` 68 / 68. Say in the report that the AI-side gap stays (M5-4d).

**Part 1 — "opened with".** The stats screen gets one line per AI seat: "<colour / faction> opened with <displayName>" from
`Simulation.AiOpening(player)` and `GameData.Ai[faction].Openings[index].DisplayName` (the description as a tooltip or a second
line is optional); a human seat shows nothing (or `match_over.opening_human` if the layout needs a cell). Text keys in `ui.json`; the
1280 × 720 layout still fits four seats (`StatsScreenCheck` row); a watched replay shows the same line (the replay carries the seats and
the seed: the pick is rebuilt by `AiPlayer.PickOpening` from the header's seed; if playback builds every seat human (it does), the view
computes the pick itself through a read-only ViewApi helper that calls `AiPlayer.PickOpening` with a copy of `RngStream.Ai(p)` — add it
in `ViewApi/` only if `AiPlayer.PickOpening` is reachable; else put "expose the pick for replays" into Requests for the sim track and show
the line in live matches only). With today's data every AI says "Standard"; D21 lands the same session, so test with a 3-opening test file.

**Part 2 — BUG-0954 item 1** (S4, same code as the alerts): a building hit that joins a `Units` alert within 30 m / 10 s upgrades its
`Kind` to `Base`; `AlertHud` re-shows the text if the alert was off screen and the kind changed. xUnit rows beside `AttackAlertsTests`.

**Acceptance (sketch).** (1) `QaM6V11Test` PASS on base-equivalent AI behaviour, loop 68 / 68. (2) The line for 1-3 AI seats at 1280 × 720,
`ShellTest` keys, a screenshot looked at. (3) Replay parity or the Requests note. (4) BUG-0954 item 1's rows red on head-before, green
after. (5) Smoke PASS, build 0 warnings, `sim/` diff only in `ViewApi/`. (6) docs/03 + docs/02.

### Candidates after (in order)
Save / Load screen when Requests 42 lands (the view's criterion-3 remainder: Save in the pause menu, Load Game lists saves beside
replays, `ui.json` text for refusals); the owner's sound re-cuts (manifest rows) when the listening note arrives; teams when 38 lands;
`tools/export.ps1` when the owner's templates arrive; criterion 7's hand-made maps when the sim ships a map file format (ask:
Requests); the art pass when the owner answers the look call; criterion 6.

### Debt (next view hardening, counter 1 / 4)
BUG-0955 (S3: `smoke.ps1` adopts `scene-loop.ps1`'s `-cmatch` + frame skip; the Producer's call, the import-pass lines stay), BUG-0954
item 4 (an integer tick clock for the alerts, or leave the docs' "200, 201 on rare ticks"), BUG-0922 (S4), BUG-0550 item 2 (the art
pass), BUG-0521 items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).

### Watch-outs (view)
- `scene-loop.ps1 -Filter` is a **file glob** (`QaM6V1*`), not a regex; `-TimeoutSec` default 600. `QaM6V11Test` runs 21,600 ticks (~93 s);
  `M3PlayableTest` ~2 min; `CombatViewTest` ~48 s.
- A clone without `git lfs pull` fails `LookTest` and `SfxTest`'s clip rows cleanly and smoke (BUG-0955); the game itself boots on tones.
- The `Sfx` pool is 8 world + 6 UI players; a new UI event must be marked `World = false` in `Sfx.Table` or the cap drops it.
- Space is reserved (`KeyMap.Reserved`); `QaM6VH2Test`'s rows moved to Insert / Home.

---

## Data track

### Current session plan (candidate): D21 — three named openings per faction

**Type** feature (counter 1 → 2 / 4) · **QA** standard (scenario numbers) · **Size** ≤ 1,200 lines incl. the faction-page re-prints ·
**Files** `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json` (the sim track touches neither this session),
`docs/factions/malazan.md` / `whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, the golden
(`sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` header + checksum: regen once, BUG-0750's rule, and
`QA/GatherWedgeQaTests.SameGameDataHashes` with the reason). **Design refs:** docs/02 "AI opponent" (openings; Easy the first), docs/03
"Implementation (M5-8a)" (the schema: `id` snake_case unique, `displayName` 1-40, `description` ≤ 200, each opening's `buildOrder` ≤ 256
steps under the M5-1 rules, `compositions` one per age, `attack.minArmyPop` / `minArmyCost`), docs/03 "Implementation (M5-2)" (the
executor's semantics), `docs/factions/<id>.md` "AI build order" / "Spending on the menu's map" / "Houses at the cap".

**Content.** Opening 0 stays **`standard`** (today's order; rename "Standard" → something the player reads, e.g. "Balanced build", and a
real description). Add two per faction, e.g. Malazan **`crossbow_push`** ("Crossbow push": Range before Barracks, 2 Ranges, attack at a
lower `minArmyCost`, composition 60 % Crossbow / 40 % HI in Age I) and **`cadre_rush`** ("Fast Age II": Age II at 300 s, Cadre Tower early,
mages in the Age II mix); Whirlwind **`raider_rush`** ("Raider rush": 2 Raider Camps, attack at `minArmyPop` 10, Raiders 60 % + Horse
Raiders) and **`holy_camp`** ("Holy camp": Shrine and Zealots early in Age II, a slower first attack). Each ≤ 40 steps, the house steps
through the cap (the parked 12th-house step batches in: D15 C's cap-98 / 90 note), `expand` once. `displayName` / `description` quoted
in the review table. **Measure:** `AiSpendingReportTests` per opening (Normal, seeds 1 / 7, the 5-minute marks, the idle base's fall time)
re-printed on the faction pages under a per-opening heading; `AiContentTests` per opening (every id resolves, every step reachable in
order, the composition ids are own units of that age); `D20PinQaTests` kept or re-pinned with the reason; `CounterTriangleMarginsTests`
untouched (no unit number moves). Easy keeps opening 0: say so on the pages.

**Acceptance (sketch).** (1) Both files load with 3 openings; `DataValidationTests` green; the golden regenerated once with the reason.
(2) Every opening's spending report printed and quoted. (3) A 20-minute Normal AI-v-AI on each opening pair ends with one side's hall
down or a wave started (no stall from a dead-end order). (4) The review table: opening / field / old → new / why, the names and
descriptions quoted. (5) No C# outside the data test folders; `rules.json` untouched.

### Watch-outs (data)
- The golden moves (D21 is the hash-moving change the 12th-house tweak waited for). Old owner recordings refuse to play again; say so in
  the review entry.
- A Normal / Hard AI on a 3-opening file picks by seed: the spending report must name the opening it measured (`Simulation.AiOpening`).
- The six test files M5-8a rewrote (`Content/AiContentTests`, `AiSpendingReportTests`, `QA/Content/AiContentFileSideQaTests`,
  `AiContentQaTests`, `AiHouseRuleReportTests`, `D19QaTests`) read `ai.Opening.*` / `openings[0]`: extend them per opening, don't revert.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's
  first item.
- The data track edits both `ai.json` files next session (D21): the sim track must not touch them; the view reads them only.
