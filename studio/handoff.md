# Handoff: brief for the next session

**PLAN 2026-10-18-1236 (the second full session of 2026-10-18, 2 / 8): GO on all three tracks.** Checks: inbox New empty; `main` 114b32e
builds 0 warnings (24 s); smoke PASS in the view worktree; the non-Perf suite on `main` **7,250 passed / 0 failed / 14 skipped of 7,264 in
34 m 35 s**; `SaveFormat.CurrentVersion` 1,
both `ai.json` 3 openings, golden 554A3832241A0896 and `ui.json` `match_over.opening` verified; one docs drift fixed (docs/02 "Game flow" 5
quoted the retired name "Standard"). Open S1 / S2: none (S3 18, S4 20 incl. two flakes). Tasks: **sim M6-S3** (save slice 2, the AI seats +
BUG-0960 item 1; feature, QA full), **view M6-V14** (the Save / Load screen; feature, QA standard), **data D-H6** (an early data hardening:
BUG-0982, the pairs report's wait length and a third seed; QA standard). Counters after: sim 3 / 4, view 3 / 4, data 0 / 4. Bug ids: sim from
BUG-0990, view from BUG-1000, data from BUG-1010. The "Current session plan" under each track is the brief; the rest of this file is the
0516 ACCEPT handoff it was planned from.

Written by the Producer at the ACCEPT of session **2026-10-18-0516** (the first full session of 2026-10-18; cap 8, so **1 / 8 done**).
**All three tracks ACCEPT**, merge order sim → view → data (the conductor unions `studio/bugs/README.md`, `studio/qa/coverage.md`,
docs/02 and docs/03: every track appended in its own region). Golden `data-hash` moves once at the data merge: **1CBDE4DBA7D17AE1 →
554A3832241A0896** (D21; header + checksum only, no `k` line). Counters after: **sim 2 / 4, view 2 / 4, data 2 / 4** → every track runs a
**feature** session next. Bug ids: highest used **0982**; next **sim from BUG-0990, view from BUG-1000, data from BUG-1010**. Open S1 / S2:
**none** (BUG-0980 S1 fixed in D21's round 1). New open: BUG-0960 (S4 sim), BUG-0970 (S4 view), BUG-0981 (S3, sim-side AI), BUG-0982 (S3,
data). The loop is green on `main` again once the view merges (BUG-0953's view half; 69 / 69).

## Where we are

- **M5 4 / 9** (criteria 1, 2, 7, **8** ticked; 3, 4, 5 advanced). **Criterion 8 ticked this session**: D21's three openings per faction
  on top of M5-8a's schema, shown on the stats screen by M6-V13. Left: M5-4d (expansion, rebuilding, high-ground scouting, hunting the
  last units), M5-4c slice 2 (the Hard knobs), M5-5 (the 20-minute winner test), criterion 6 (the owner's play), D14 c.
- **M6 2 / 10** (criteria 2, 4 ticked; 1, 3, 5, 6, 7 advanced). **Criterion 3's save half, slice 1, is in** (M6-S2: the format +
  `TrySave` / `TryLoad`, human seats). Slice 2 (AI seats) and the view's Save / Load screen are the next session's pair.
- Still wanting the owner (not blocking): the art call, listening to the SFX, the Shock pass, `goldShare`, BUG-0780, "densest cluster",
  and now the **D21 review table** (names, descriptions, the Malazan `standard` wave 20 → 16).

## Standing rules (1-37)

Unchanged 1-36 (the 2344 ACCEPT handoff in git history has 35 and points to the long forms; 36 = the save API freeze, below). New:

- **36 (save API freeze, from the 0516 PLAN).** `Simulation.TrySave(Stream, out SaveError)` / `Simulation.TryLoad(GameData, Stream, out
  Simulation?, out SaveError)` and the `SaveError` codes are frozen. Slice 2 adds sections and lifts `AiSeatsUnsupported` without changing
  the signatures or the existing codes. **Producer call for slice 2:** bump `SaveFormat.CurrentVersion` to 2 (no reader for version 1:
  nothing shipped a save before the view's screen, which lands the same session); the view shows `save.version_mismatch` for it.
- **37 (cross-track fixture fix, the mirror of 35; ratified at the 0516 ACCEPT for BUG-0980).** When a data content change breaks sim-owned
  test *fixtures* that assumed a content shape (one opening, one tech, …), the data dev may fix those fixtures only, in a separate commit
  named in the report, with no assertion loosened unless a stricter test still holds the pin; QA reads every changed line, the Producer
  confirms at ACCEPT, and the owner is told. And the other way: a sim brief that ships a new content list must make its fixtures
  content-independent (cut the shipped list to its first entry before appending), so the data track can fill the list without touching
  sim tests.

## Checks to run at the next PLAN

- Inbox **New**; `git status` / `git log`; build; the non-Perf suite on the merged head (expect ~7,300 tests; `AiOpeningsQaTests`,
  `AiOpeningPickTests`, `DataValidationTests` green with three shipped openings); smoke in the view worktree; the loop row `QaM6V11Test`.
- Trust-but-verify candidates: `sim/Rts.Sim/Save/SaveFormat.cs` `CurrentVersion` 1 and `TrySave` refusing an AI seat
  (`SaveRefusalTests`); both `ai.json` hold 3 openings and the golden's header says `554A3832241A0896`; `game/data/common/ui.json` has
  `match_over.opening` and the stats screen prints "<faction> opened with <name>" in a windowed run against a Normal AI.
- Sessions today: count `studio/sessions/2026-10-18-*.md` (1 after this one).
- BUG-0981 is sim-side AI behaviour found by the data track (no reserve for a due step: four openings never reach Age II against an active
  opponent). It is S3 by QA's call but it shapes what the owner meets in a match; see the sim candidates.

---

## Sim track

### Current session plan (2026-10-18-1236): M6-S3 — save slice 2, the AI seats (+ BUG-0960 item 1)

Feature, QA full, ≤ 1,500 lines. Files: `sim/Rts.Sim/Save/**`, new `sim/Rts.Sim/Ai/*.Save.cs` parts (`AiPlayer`, `Blackboard`), the
`Simulation.cs` save / load path, `sim/Rts.Sim.Tests/Save/**`, docs/03 "Save format (M6-S2)" (+ a slice-2 paragraph). Not `game/**`, not
`game/data/**`, not the data track's test folders. **Goal:** a match with AI seats saves and loads hash-equal; lift `AiSeatsUnsupported`.
- Store per AI seat (a new section tag, after the human sections): `OpeningIndex` (checked against the pick the loaded seat's fresh
  construction makes from the seed: a mismatch is `Malformed`), the AI RNG's full state after construction (restored over the fresh one;
  the loaded seat draws the pick exactly once, at construction), `_queueFull`, `NoSiteFound`, `SiteSearches`, `Thinks`, `CommandsSent`,
  and the `Blackboard` whole: every array and scalar, scratch included (`SeenEnemies`, `DefenseOrder`, `DefenseKey`, `SiteBuiltAt`: cheaper
  to store than to prove unread), the retry marks (`_badType` / `_badCell` / `_badNext`), `HousePending` / `ProductionPending` /
  `StepPending`, the recent casts. `PlayerView`: store whatever it holds outside `FogStore` (check; nothing expected).
- Rule 36: the two signatures and the 8 codes unchanged; `SaveFormat.CurrentVersion` 1 → 2, no reader for 1 (a version-1 file reads
  `VersionMismatch`). `Tick()` and every `Run` untouched; no `k` line of the golden moves; no per-tick allocation.
- **BUG-0960 item 1 folded:** a minimum-length pre-check from the header's capacities and map size before `World.ForLoad`: the forged
  max-capacity header (`SaveForgeryQaTests.AForgedHeader_WithEveryCapacityAtTheFormatMaximum_*`) refuses `Malformed` in microseconds, well
  under 1 MB allocated.
- **Content-independent fixtures (rule 37):** the round-trip scenes find "mid-wave / mid-cast / in a no-site wait / during a defense" by
  probing the Blackboard (`WaveActive`, `RecentCastCount > 0`, `StepNoSiteUntil[k] > now`, `Defending`) and saving at the first such tick,
  never at a tick literal pinned to the shipped `ai.json` (the data track may move `cadre_rush`'s threshold this session).
Acceptance: (1) `SaveRoundTripTests` AI scenes: AI-v-AI seeds 1-5, 2-4 seats, every difficulty, Malazan and Whirlwind, saved at {1, the
first think, 137, the four probed moments, 1,000}, hash-equal every tick to T + 2,000 and `AiOpening(p)` + every Blackboard field equal at
T / T + 1 / T + 2,000; (2) slice 1's 13 human scenes still green, save → load → save byte-identical; (3) a human + AI (the menu's match)
saves and loads: `TrySave` true, hash-equal to T + 2,000; (4) `SaveRefusalTests` on a small AI save: every flipped byte and truncation
refused with its code, nothing thrown, `OpeningIndex` forged → `Malformed`, version 1 → `VersionMismatch`; (5) the BUG-0960 item 1 row
green with the allocation bound; (6) `SavePerfTests`: the menu match's size and times printed (no gate loosened; < 8 MB / 100 ms alone);
(7) `ArchitectureTests.SaveSource_*` cover the new parts; (8) docs/03 says what the AI section holds and the version bump.

### Next task candidates (in order)

1. **M6-S3 — save slice 2: the AI seats** (feature, counter 2 → 3 / 4; QA full; ≤ 1,500 lines; files `sim/Rts.Sim/Save/**`,
   `Ai/**.Save.cs` parts, `Simulation.cs` load path, `sim/Rts.Sim.Tests/Save/**`, docs/03 "Save format (M6-S2)" → a slice-2 paragraph;
   **not** `game/**`, not `game/data/**`). Store per AI seat: `AiPlayer.OpeningIndex` and the AI RNG stream's state after the pick (a
   loaded seat never draws the pick again: QA's `ThePick_IsTheOnlyDrawOnTheAisStream` is the oracle), the `Blackboard` whole
   (`StepNoSiteUntil`, `SiteSearches`, the defense marks `DefenderGeneration` / `DefendAll` / `DefendAnswer`, the casts, `WaveMinHalfPop` /
   `WaveMinCost`, the wave / retreat / rally state, the scout state), the `PlayerView` memory if any lives outside `FogStore`. Lift
   `AiSeatsUnsupported`; `CurrentVersion` → 2 (rule 36). Round trip (hash every tick to T + 2,000 and the direct compares incl.
   `Simulation.AiOpening` per seat and every Blackboard field) on AI-v-AI seeds 1-5, 2-4 seats, every difficulty, saved mid-wave,
   mid-cast, in a no-site wait, during a defense; the human-seat scenes of slice 1 still pass; idempotence; the refusal sweep on a
   small AI save. **Fold in BUG-0960 item 1** (a minimum-length pre-check from the header's capacities before `World.ForLoad`: a forged
   header must refuse `Malformed` in microseconds, not after 1.4 GB; a few lines in `SaveHeader.TryRead` / `Simulation.Load`).
2. **M5-4g — a reserve for the due step (BUG-0981)** (feature, QA full): the economy / production managers keep the earliest due
   `build` / `research` step's cost in reserve (and weight the gather split toward what it lacks) so Age II comes in within ~2 minutes
   of its step on every opening against an active opponent; re-run `AiOpeningPairsReportTests` (the "Age II at" lines) and the
   spending reports (rule 31: the data track re-prints the cells). Alternatively fold into M5-4d.
3. Requests 38 (teams); M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground
   scouting, hunting the last units: BUG-0953's sim half, seed 11); D14 c (counter-composition); M5-5 (the 20-minute winner test); M5-6.

### Debt (next sim hardening, counter 2 / 4 after this session)
BUG-0960 items 2-3 (S4: forged saves that load can throw on a tick; the save Perf row under contention), BUG-0912, BUG-0911, BUG-0889,
BUG-0144, BUG-0941, BUG-0940, BUG-0914 + BUG-0915 (S4 flakes), BUG-0913 (7 nits), BUG-0113 item 2, BUG-0704 item 3.

### Watch-outs (sim)
- The save must not change the hash composition: no `k` line of the golden moves.
- Slice 1's size: ~2,000 production + ~1,500 test lines against a 1,500 budget (one store's `WriteTo` / `ReadFrom` per file is the
  bulk). Slice 2 is smaller (the AI's state is a few classes); keep the budget.
- `World.ForLoad(config, map)` + the private `World(config, map, placeResources: false)` is the load path; `Simulation(World)` the
  private loaded-sim constructor: slice 2 adds the AI construction from the stored pick there, **without** `PickOpening` running.
- Flow-field costs are rebuilt from direction bytes at load (mode 0) with a writer-side check and a raw fallback (mode 1): the
  round-trip test compares every cost. Don't "simplify" that away; it is why the menu save is 3.9 MB, not 12.
- Three test hosts ran at once this session: non-Perf rows held (0 failures on every branch's own QA run); Perf rows flake under load.

---

## View track

### Current session plan (2026-10-18-1236): M6-V14 — the Save / Load screen

Feature, QA standard, ≤ 1,200 lines. Files: `game/scripts/**` (`PauseMenu`, `LoadGameScreen`, a new `SaveStore` beside `ReplayStore`,
`Match` / `SimRunner` / `Main` / `LaunchOptions` resume path), `game/data/common/ui.json` new `save.*` keys and `pause.save` / `load.load`,
`game/tests/**`, docs/02 "Game flow" 1 and 6, docs/03 "Implementation (M6-V14)". Not `sim/**` outside read-only `ViewApi/` accessors.
- **Save** from the pause menu to `user://saves/<yyyyMMdd-HHmmss>-seed<seed>.sav` through `Simulation.TrySave` (between ticks, on the
  main thread), the recording so far written beside it as `<same stem>.replay`; a toast / line with the result (`save.saved` with the
  file's clock, or the `save.<code>` text). **The Save button is never greyed from `HasAiSeat`:** it calls `TrySave` and shows the code's
  text on refusal, so it works the moment the sim's slice 2 merges (same session, merged before the view).
- **Load Game** lists saves beside replays (one list, newest first; a save row reads date, seed, players, tick → clock, a "Save" tag;
  a replay row "Replay"): Load (saves) / Watch (replays) / Delete / Back. A load resumes the match through `SimRunner` at the saved tick
  with the HUD, camera, fog view, selection and minimap fresh, the seats from the save's header (faction, controller, difficulty, colours
  from the slot order); **a loaded match is not recorded** (the recorder attaches at tick 0 only): say so in docs/02 6 and docs/03, and the
  pause menu shows `save.not_recorded` for it. `--load <path>` flag.
- Every `SaveError` has a `save.*` text: `bad_magic`, `version_mismatch`, `data_mismatch`, `invalid_header`, `ai_seats_unsupported`,
  `truncated`, `checksum_mismatch`, `malformed` (plain words; name the keys in the report). Delete and unreadable files as `LoadGameScreen`
  handles replays today (BUG-0921's rules).
- **Tests must not pin the AI refusal:** no scene or test asserts that a match with an AI seat is refused (it stops being true at the
  merge); the AI path is covered by the human twin + the error texts' format rows only.
Acceptance: (1) `SaveLoadTest` scene: a `--skirmish --players 2` human twin saves at tick 500, quits to menu, loads, ticks 1,000 more beside
an unsaved twin: hash equal every tick, HUD / camera / selection fresh, `.replay` written beside the `.sav`; (2) the list shows saves and
replays with the right columns and tags, Delete removes a save, a corrupted `.sav` is refused with the right text and never crowds out good
rows; (3) `--load <path>` boots into the match; (4) `ShellTest` key rows for every new `ui.json` key (the format checks); (5) the loop
green, smoke PASS, 0 warnings, `sim/` diff empty or `ViewApi/` read-only; (6) docs/02 1 + 6 and docs/03 updated.

### Next task candidates (in order)

1. **M6-V14 — the Save / Load screen** against slice 1's frozen API (feature, counter 2 → 3 / 4; QA standard; ≤ 1,200 lines; files
   `game/scripts/**` (pause menu, `LoadGameScreen`, a `SaveStore` beside `ReplayStore`, `Match` / `SimRunner` resume path),
   `game/data/common/ui.json` `save.*` keys (rule 32: name them in the brief), `game/tests/**`, docs/02 "Game flow" 6, docs/03
   "Implementation (M6-V14)"; **not** `sim/**` outside `ViewApi/` read-only accessors). Save from the pause menu to
   `user://saves/<yyyyMMdd-HHmmss>-seed<seed>.sav` with the recording so far written beside it (Requests 42: the sim's save holds no
   replay); Load Game lists saves beside replays (date, seed, players, tick → clock) with Load / Delete / Back; a load resumes the match
   through `SimRunner` at the saved tick with the HUD, camera, fog and selection fresh, seats from the save's header; **a loaded match is
   not recorded** (the recorder attaches only at tick 0: document it in docs/02 / docs/03 and show nothing misleading); every `SaveError`
   has a `save.*` text (`bad_magic`, `version_mismatch`, `data_mismatch`, `invalid_header`, `ai_seats_unsupported` ("Saving a match with
   an AI opponent arrives next session" — honest wording the data of slice 2 then retires), `truncated`, `checksum_mismatch`,
   `malformed`); the pause menu's Save is greyed with the reason while a seat is AI (slice 1) and works once slice 2 merges (same
   session: test the AI path only through the refusal). Scenes: `SaveLoadTest` (save at tick 500 in a `--skirmish --players 2` human
   twin, quit to menu, load, tick 1,000 more beside an unsaved twin: hash equal every tick; the list; delete; a corrupted file refused
   with the right text; `--load <path>` flag), `ShellTest` keys, the loop green, smoke PASS.
2. The owner's sound re-cuts when the listening note arrives; teams when 38 lands; `tools/export.ps1` when the owner's templates
   arrive; criterion 7's generator options in the setup; the hand-made maps when a map format exists; the art pass when the owner
   answers; criterion 6's music when the track arrives.

### Debt (next view hardening, counter 2 / 4 after this session)
BUG-0970 (S4: an upgraded alert's slot overwritten by a raise in the same `Update` when the ring is full; drop the `_upgraded` entry when
a raise takes its slot), BUG-0955 (S3: `smoke.ps1` adopts `scene-loop.ps1`'s `-cmatch` + frame skip), BUG-0954 item 4, BUG-0922 (S4),
the stale `QaM6V11Test` header comment (says Age II at tick 21,001; it is 17,001 since M5-H3), BUG-0550 item 2 (the art pass), BUG-0521
items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).

### Watch-outs (view)
- `scene-loop.ps1 -Filter` is a **file glob**, not a regex; `-TimeoutSec` default 600. `QaM6V11Test` ~66-93 s; `M3PlayableTest` ~2 min.
- A clone without `git lfs pull` fails `LookTest` / `SfxTest` clip rows and smoke (BUG-0955); the game boots on tones.
- `Sfx` pool: 8 world + 6 UI players; a new UI event must be `World = false` in `Sfx.Table`. Space is reserved (`KeyMap.Reserved`).
- `MatchStats.OpeningOf` computes a watched replay's pick with `AiOpeningLookup.Pick`; it trusts `PickOpening`'s code staying the same (a
  replay checks the data hash, not the sim version). If the pick rule ever changes, old replays' lines mislabel silently (QA note).
- `AiSeatsUnsupported` is the only refusal the view can provoke on purpose before slice 2 merges; a `--players 2` match with slot 2 set
  human (dev runs) is the save path to test.

---

## Data track

### Current session plan (2026-10-18-1236): D-H6 — early data hardening (BUG-0982, the pairs report's wait length, a third seed)

Hardening (counter 2 → 0 / 4, taken early: the inbox holds no D21 answer and no schema the track needs is on `main`), QA standard, ≤ 400
lines. Files: `game/data/factions/malazan/ai.json` (only if the measure says so), `docs/factions/malazan.md` (+ `whirlwind.md` if a cell
moves), `sim/Rts.Sim.Tests/Content/AiOpeningPairsReportTests.cs`, `Content/AiContentTests.cs` (the pin, if the number moves),
`sim/Rts.Sim.Tests/QA/Content/D21OpeningsQaTests.cs`, the golden header
in `QA/GatherWedgeQaTests.cs` once if `ai.json` moves (reason in the `SameGameDataHashes` comment and the commit body). Not `whirlwind/ai.json`,
not `common/**`, no other C#.
- **BUG-0982, decided by measurement (Producer default, owner may revisit):** run `cadre_rush` v `raider_rush` on seeds 7, 8 and a third
  with `attack.minArmyPop` 18 (today), 16 and 14 (`minArmyCost` = the floor of the Age I mix at that pop, D21's rule). Take the lowest
  threshold ≤ 16 at which seed 8 waves **and** Age II on the measured seeds comes no more than 60 s later than today; else keep 18 and the
  page says in plain words that this greedy opening can fall to a rush without attacking. If the number moves: the description's "18" moves
  with it, the page's `cadre_rush` section and spending cells re-print, the golden regenerates once. Make QA's
  `EveryPairing_SecondSeed_BothSidesWaveIn20Minutes` row strict (`first[p] >= 0`) if fixed; else keep it printing and say why in the row.
- **`AiOpeningPairsReportTests`:** a step "legitimately waiting" on gold or wood prints how long it has been due (ticks → m:ss) and the
  banks at the end; a wait over 5 minutes is flagged in the output as BUG-0981's pattern (not a failure: the fix is the sim's M5-4g).
- **A third seed per pairing** in `D21OpeningsQaTests` (same allowances as the second); print every row's opening, first wave, Age II.
Acceptance: (1) the measurement table (threshold × seed: waved at, Age II at, defeated at) in the report and on the malazan page; (2)
`Content` + `QA/Content` + `DataValidationTests` + `ReplayGoldenTests` green on the branch; (3) no sim-owned test outside the files named
changes; if `ai.json` moves, `AiContentTests` pins the new number both ways and the golden's two lines only; (4) rule 31: the spending cells
re-printed if any moved; (5) a review table for the owner (opening / field / old → new / why, quoted description).

### Next task candidates (in order)

1. **Inbox first:** the owner's answers on the D21 review table (names, descriptions, the Malazan `standard` wave 20 → 16 which also
   lowers Easy Malazan's first wave to ~9.6 pop, `holy_war` as the name, the kept 12th house) → a D21 tweak task (QA light; golden regen
   once if a number or text moves).
2. **Else D-H6, an early data hardening** (counter 2 → 0 / 4 is allowed one session early, as D-H2 was): **BUG-0982** (S3: `cadre_rush`
   falls to `raider_rush` on seed 8 without a wave; decide with the pages: wave lower under pressure (e.g. 14 pop) or keep the greedy
   design and let the page say "this opening can die without attacking"; make QA's `EveryPairing_SecondSeed…` row strict when fixed),
   the `AiOpeningPairsReportTests` "legitimately waiting" wording (BUG-0981's pattern hidden), the stale `AiOpeningPickTests` doc
   comment, a third seed per pairing in the report; rule-31 re-print if the sim's M5-4g lands first.
3. **Else STOP cheaply:** no schema the data track needs is on `main` (D14 c and M7's rosters wait on sim systems).

### Debt (data)
BUG-0982 (S3), the parked D15 C finding is **closed by D21's measurement** (the 12th house stays: without it the cap reaches 100 later and
the house rule places it anyway).

### Watch-outs (data)
- The golden moved: old owner recordings refuse to play again (said in the review entry).
- A Normal / Hard AI picks by seed: every report names the opening it measured (`Simulation.AiOpening`); the pages name the seed.
- `AiScenes.EarlyWaves` now edits every opening; `AiOpeningPickTests` / `AiOpeningsQaTests` / `DataValidationTests` fixtures cut the shipped
  list to opening 0 before appending (rule 37): a fourth opening must not break them.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/02, docs/03: each track appends its own rows; the conductor unions them.
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's
  first hardening item.
- The next session's sim (slice 2) and view (the screen) tasks are built in parallel against slice 1: the view tests AI-seat saves only
  through the refusal; the conductor merges sim first so the merged tree has both.
