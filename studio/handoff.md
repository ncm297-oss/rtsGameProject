# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-13-1616** (fourth full session of 2026-10-13, 4 / 8). All three tracks
**ACCEPT**: sim **M5-4c slice 1** (the ability manager; the knobs deferred under the 700-line hard stop), view **BUG-0674 / 0680 / 0681 +
M6-V4** (the Victory / Defeat / Draw banner and the stats screen), data **D17** (docs/02's Normal line, the pages' Defense lines, the
AI spending report). Counters after this session: **sim 2 / 4, view 2 / 4, data 2 / 4** (the next hardening after two more feature
tasks per track). Bug ids next: **sim from BUG-0703, view from BUG-0711, data from BUG-0721.** Inbox: empty at this ACCEPT.

## Where we are

- `main` after the conductor's merge (sim 419a9e8 → view 54b7ef2 → data 6645354; the Producer's scratch merge of the three built
  with 0 warnings, 1,186 targeted rows green, smoke PASS, `AiSeatTest` / `MatchOverViewTest` / `QaM6V4Test` PASS). Golden `data-hash`
  **C6FC120A55AE4A0B** (`rules.json` `ai.castMinTargets`, moved once; every `k` line unmoved). **No open S1 / S2.** **The scene loop is
  green on `main` again** (BUG-0674 / 0680 fixed) for the first time since 0916.
- The game from the menu: Skirmish → Start: a hall and 5 workers a side; the AI gathers, scouts, builds, waves, retreats, defends and
  **now casts** (Normal / Hard: Telas Fire on your clumps, the Cusser at your buildings, Sandstorm on your army); when your last
  building falls a **Defeat** banner pauses the match (Victory when the AI's does; Stats / Continue watching / Main menu); the pause
  menu offers Stats after the banner.
- Sim surface new this session: `PlayerView.TryGetAbilityReady(slot, abilityIndex, out readyTick)` / `TryGetCast(slot, out abilityIndex)`
  (own units only). Nothing new for the view; **Requests 39** (stats counters) and **38** (teams) are the view's open asks.

## Standing rules (1-21)

1-20 as in the 1616 PLAN handoff (full suite incl. Perf alone for sim rule changes; a new `Command` kind into `CommandDoorFuzzStressTests`;
per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a data brief names every sim-track
test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the Producer the rest; content-independent
sim tests; `git checkout HEAD -- path`; the budget is a line; one system per brief, the dev's count excludes QA's files and is
reported; no process killing by image name; a dev never loosens a QA row; the Producer's merged-tree check runs the whole scene loop
when the sim touches the generator / `ResourcePlacer` / `ViewApi/StartBase` / `World.Buildings` liveness / the command door; a QA scene
row for a bug left open prints `KNOWN BUG-nnnn`; a brief's slicing rule is a hard stop).
21. **A Perf gate measures the shipped data.** A perf row that edits data to hide a cost (as `AiPerfTests` turns Sandstorm's
    `blocksVision` off, BUG-0701) must say so in the row and file the cost it hides; the next sim hardening gives it a shipped-data row.

---

## Sim track

### Next task candidates (the PLAN writes the brief)

1. **M5-4c slice 2 · feature · QA full: the difficulty knobs** (criterion 4): `gatherBonus` (Hard 0.25, integer millionths with a
   per-player per-kind carry at the drop-off, the carries hashed and named; a human seat 0), `focusFire` (a wave member's `Attack` on
   the lowest-hp enemy within `attack.range + 2 m`, kept `FocusTarget`, cleared at the wave's end; off = today's bytes),
   `pullBackWounded` (a member under `retreat.hpFraction` gets one `Move` home and leaves the wave alive, once per wave). The 1616
   brief's System 2 text and criterion 5 carry over word for word. Golden `k` lines may not move (no AI seat); the header may not
   either (no data change). Fold **BUG-0702**'s first nit only if a few lines (seed the group from the enemy nearest the caster
   instead of the centroid of everything in reach), else leave it S4.
2. **Then Requests 39 (+ 38 if small) · feature · QA standard:** per-player counters for the stats screen: gold / wood **gathered**,
   units trained, kills / losses split units vs buildings, buildings built / lost, the Age II tick, an army-value sample every N s
   (`rules.json`), read-only spans like `Kills`; hashed or not, decided and documented. Teams (38): a `Team` per seat, `MatchOver` and
   the defeat rule per team. The Producer put slice 2 first because criterion 4 is the smaller, older debt and the view has unblocked
   work (keybinds, slots, BUG-0710); 39 is the sim's item right after it.
3. After: M5-4d (expansion, rebuilding, periodic re-scouting, high ground before attacking up; criterion 3), M5-5 (the 20-minute
   winner test; criterion 5). Sim hardening (counter 4 / 4) carries BUG-0701 (the fog cursor's per-zone cost; a shipped-storm Perf
   row), BUG-0702's rest, BUG-0675 c, BUG-0651, BUG-0650.

### Watch-outs (sim)
- **Check first that the sim branch merged.** Its ACCEPT was conditional on a one-line pre-merge fix round (`[Collection(SerialCollection.Name)]`
  on QA's `Stress/AbilityManagerFuzzStressTests`; the guard `SerialCollectionTests.EveryPerfTest_IsInTheSerialCollection` was red on
  419a9e8). If `main` lacks `Ai/AbilityManager.cs`, the first sim item is that line + re-landing slice 1 (QA re-checks), then slice 2;
  if `main` has it but the guard is red, the line is the first item of whatever runs next. A Perf-tagged row anywhere goes in a class in
  the serial collection (the guard says so by name).
- `AiPerfTests` now loads data with Sandstorm's `blocksVision` false (BUG-0701): don't copy that pattern into a gate without a row that
  measures the shipped data too.
- The casting determinism rows (`AiDeterminismTests.TwoAiSeats_Casting_*`) and `AiReplayTests.TwoCastingAiSeats_*` are the regression
  net for anything touching the think order or the `Blackboard`'s cast arrays.
- D17's spending report (flat scene, Normal, 20 min): wood is the limit, neither AI reaches Age II; the flat scene's 12 trees a base run
  dry, so re-measure on a generated map (the data track's D18) before any sim lever (a reserve for the due build-order step, or
  `ai.goldShare`).

---

## View track

### Next task candidates

1. **BUG-0710 first** (a few lines: the HUD's K / L synced when the banner opens; delete `sim/Rts.Sim/ViewApi/MatchOver.cs` with
   `ViewApi/MatchOverTests` and QA's `QA/ViewApi/MatchOverQaTests`, QA agreed; the view owns `ViewApi/`), then **M6 criterion 4's rest:
   keybinds** (a `keybinds` section in `ui.json` for the names, the bindings in `user://settings.cfg`, a Keybinds tab on the settings
   screen, every HUD hotkey read through one map; the command card's grid hotkeys stay data) · feature · QA standard. Or **M6-V5's slot
   part** (2-4 slots in the setup; check first that `MapGenParams.StartLocations` 3 / 4 gives four fair start locations and that the
   seats' colours / factions scale; teams wait on Requests 38).
2. After: save / load + replay playback (criterion 3), the look test (the owner's packs are in `asset-sources/`), `tools/export.ps1`
   with Requests 33. M5's view part: criterion 6's playable run against the AI once M5-4c slice 2 and M5-4d land.

### Watch-outs (view)
- The banner's dev screenshots were fog-black (`--no-bases` test matches); QA's two on a normal map are the ones to look at. Take the
  next screenshots on a based map.
- `Main.BeforeStart` is a public test seam (3-seat runs); keep it test-only.
- Editor runs may pass `--position`, which now keeps the engine's window at boot (BUG-0681); unverified, harmless.

---

## Data track

### Next task candidates

1. **Inbox answers first** (D13 / D14 / D11-D12 / D15 / colours / D17's proposal), else **D18 · feature · QA light:** BUG-0720 item 2
   (both pages' "more than 16 m from it" → "from the threat"; item 1 in docs/03 was fixed by the Producer at this ACCEPT) and **the
   spending report on a generated map** (seed 1 / 7, both factions, Normal, 20 min; the same table; the flat-scene run kept as a
   second row), so the owner's choice (keep `ai.json`, or a sim-side reserve) rests on real trees and mines. No `ai.json` value
   change unless the inbox asks.
2. STOP when nothing above applies and no schema it needs is on `main` (the Teblor's `ai.json` stays M7).

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing (QA did so this session: no regression).
- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01: each track appends its own rows; the conductor unions them (three
  same-spot conflicts this session, all unions).
- Merge order sim → view → data. The golden's `k` lines may not move next session; the header moves only with a data change.
- Shared files: `ui.json` is the view's unless a sim brief names a key; docs/02 "AI opponent" is the data track's; docs/03 gets one
  new section per track.
