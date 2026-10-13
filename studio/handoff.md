# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-13-0916** (third full session of 2026-10-13, 3 / 8). Both active tracks
accepted: sim **M5-4b** (the defeat rule + the AI's defense; 1 fix round) and view **M6-V6** (BUG-0661 / 0662 + the settings screen;
0 fix rounds); the data track had no task (D16 was already on `main`). Counters after it: **sim 1 / 4, view 1 / 4, data 1 / 4**.
Bug ids next session: **sim from BUG-0700, view from BUG-0710, data from BUG-0720** (0690-0699 unused). Inbox: empty.

## Where we are

- `main` after the 0916 integration = sim M5-4b + view M6-V6 (merge order sim → view; conflicts docs/01 + `studio/qa/coverage.md`,
  unions as the session log says). Golden `data-hash` **72EF431E70C49C39** (the header moved once for three `rules.json` `ai` knobs;
  every `k` line equal). **No open S1 / S2.**
- **Two view scenes red on `main`'s loop, both small:** `AiSeatTest` (BUG-0674 S3: its match-over row cancels player 0's only
  building and rebuilds; under the defeat rule the cancel defeats and the rebuild is dropped; keep a second building standing or expect
  the drop) and `QaM6V6Test` (BUG-0680 S3: a 20-digit integer in `settings.cfg` makes Godot's parser log an engine ERROR; cap the digit
  run in `GameSettings.LineShape`; QA's two rows print FAIL until then). **The view's first item**, before M6-V4.
- **The game from the menu:** Skirmish → Start: a hall and 5 workers a side; the AI gathers, scouts, builds, waves, retreats, and now
  **defends** (its army comes home, its workers flee, no wave while the threat holds); a player with no building left is **defeated**
  (orders dropped, AI silent, `Simulation.MatchOver`); the view still prints only the "Match over:" dev line (the banner is M6-V4).
  **Settings** from the menu and the pause menu, saved in `user://settings.cfg`.
- Sim surface for the view (docs/03 "Implementation (M5-4b)" → "For the view (M6-V4)"): `World.DefeatEvents` (one-tick span of
  `DefeatEvent { Player, Tick }`), `Simulation.MatchOver` / `World.MatchOver` (latched), `World.IsDefeated(p)`, `World.Kills` /
  `Losses`, the ledger's totals; `PlacementError.Defeated` + `ui.json` `placement.defeated` for the ghost.

## Standing rules (1-19)

1-16 as in the 1930 / 0146 / 0527 PLAN handoffs (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; **one system per brief**, the
dev's count excludes QA's files and is reported; **no process killing by image name**).
17. **A dev never loosens a QA row.** A failing QA row is a real finding (fix the code) or a wrong test (say so in the report; QA changes it).
18. **The Producer's merged-tree check runs the whole scene loop** (`tools/qa/scene-loop.ps1 -SkipPlayable`) whenever the sim branch
    touches the map generator, `ResourcePlacer`, `ViewApi/StartBase`, `World.Buildings` liveness or the command door.
19. **A QA scene row for a bug left open at ACCEPT prints `KNOWN BUG-nnnn ...`, not FAIL** (the sim's equivalent is a `Skip` naming the
    bug), so `main`'s loop shows only regressions; the fix turns the row into a Check. (From 0916: `QaM6V6Test`'s two BUG-0680 rows.)
20. **A brief's slicing rule is a hard stop:** "if system 1 plus its tests passes N lines, ship it alone" means stop there and report;
    the 0916 sim dev shipped both systems inside the total budget and the fix round went to the second one.

---

## Sim track

### Next task candidates (the PLAN picks)

1. **M5-4c slice 1 · feature · QA full: the ability manager + the difficulty knobs** (criteria 2 and 4). The AI casts its faction's
   abilities through `Command.UseAbility` (the same door as the player) when `useAbilities` is on for its difficulty: a manager after
   the military in the think, reading only `PlayerView` (own casters' cooldowns / `Casting` state need read-only view accessors if
   missing: name them), targets from the enemy memory / seen units (Telas Fire and Sandstorm at an enemy group, the Cusser at an enemy
   building, Martyrdom / self-auras are passives), one cast per caster per think, judged next think like the executor's steps.
   `gatherBonus` (Hard 25 %: a per-player income multiplier at the drop-off, hashed where it changes totals: say where in docs/03),
   `focusFire` (wave members `Attack` the lowest-hp enemy in reach when on) and `pullBackWounded` (a member under `retreat`'s hp
   fraction gets a `Move` home) read and applied; D16's K row pins what docs/02 states. Content-independent tests (the scene builds its
   casters); `AiDeterminismTests` / `AiPerfTests` rows; the golden `k` lines may not move (no AI seat in the golden match); `data-hash`
   moves only if a `rules.json` knob is added (none planned: these are `ai.json` fields the data track already ships).
2. Then **M5-4d**: expansion (`expandHallReach`), rebuilding a lost hall / drop-off, periodic re-scouting, ramps and high ground
   before attacking up (criterion 3's rest). Then **M5-5** the AI-vs-AI 20-minute winner test (criterion 5): `AiVersusAiTests` on
   `AiScenes.Generated`, both factions, Hard v Hard, 24,000 ticks, `MatchOver` with exactly one undefeated seat, no error, hash twin.
3. Hardening (sim counter 1 / 4): after three more feature tasks; debt: BUG-0651 (S3), BUG-0650 / 0622 item 1 / 0675 c (S4).

Watch-outs (sim): the defense runs first in every think; a new manager must respect `Board.Defending` (no wave / rally while it holds)
and `IsFleeing` (don't order a fled worker). `MatchOver` is latched; `DefeatEvents` is a one-tick list. The door drops a defeated
player's commands at apply: an AI test must not expect a defeated seat's orders to land. Perf alone before filing (0916's QA machine
was slow: 33 / 173 timing rows on an untouched control).

## View track

### Next task candidates (the PLAN picks)

1. **BUG-0674 + BUG-0680 first** (a few lines each; the loop green on `main`), BUG-0681 if a few lines (skip `ApplyWindow`'s size when
   an engine window flag was given; document in docs/03 "Implementation (M6-V6)").
2. **M6-V4 · feature · QA standard: victory / defeat on screen + the post-match stats** (criterion 2). A banner from
   `World.DefeatEvents` / `Simulation.MatchOver` ("Victory" / "Defeat" / "Draw" from `ui.json`), the match keeps ticking under it
   (docs/02 "Game flow" 4: last standing wins; the view decides what the local player sees), a button to the stats screen and to the
   menu; the stats screen from the ledger (`Kills` / `Losses`, gold / wood gathered if the ledger has it, else "Requests for the sim
   track": per-player gathered totals, units trained, buildings built / lost, time to Age II, an army-value series: name exactly what
   the sim must add, the Producer plans it for the sim right after S1 / S2). The "Match over:" dev line retired or kept under a flag;
   `AiSeatTest`'s row rewritten for the banner (it is BUG-0674's row). Headless scene in the loop; windowed screenshots looked at.
3. Later: M6-V5's slot part (2-4 slots; teams need Requests 38, a sim team rule), keybinds (criterion 4's rest), save / load + replay
   playback, the look test (the owner's packs in `asset-sources/`), `tools/export.ps1` with Requests 33.

Watch-outs (view): the settings screen runs `Always` on layer 21 and `CommitSettings(stay: false)` with a live unpaused match shows the
menu (BUG-0684 item 3): a banner that opens Settings must go through the pause path. `user://` is one folder for every worktree
(BUG-0684 item 1): new tests use a per-process folder. `ui.json` is view-named this session unless the sim says otherwise.

## Data track

### Next task candidates (the PLAN picks)

1. **Inbox answers first** (D13's ×1.1 / ×1.5 / hp-pass choice, D14's build orders, the D11 / D12 text tables, D15's house-rule choice,
   the colour names) whenever present.
2. **D17 · feature · QA light:** docs/02's Normal line stated outright ("doesn't focus fire or pull back wounded", the D16 QA note) as
   a Producer default + a `Content/AiContentTests` K row; the faction pages' "Defense" lines checked against docs/03 "Implementation
   (M5-4b)" (workers flee to the nearest finished drop-off away from the threat; the army returns to the hall's radius) and reworded if
   they claim more; no number changes. No `ai.json` field came from M5-4b (its knobs are `rules.json`, sim-owned). If the sim's
   M5-4c reads `gatherBonus` / `focusFire` / `pullBackWounded` in the same session, D17 must not pin those values the same session
   (rule 16: name every sim test that pins a value, and the sim writes its tests content-independent).
3. STOP when nothing above applies (the Teblor's `ai.json` stays with M7: Producer decision, 0916 PLAN).

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing (0916: QA's control run failed 33 / 173 on an
  untouched commit).
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge). docs/01: one row
  per track, appended.
- Merge order sim → view → data. The golden's `k` lines may move only once per session and only on the sim branch, with the reason in
  the commit; `data-hash` moves with any `rules.json` / `ai.json` field (every one is in `ContentHash` since M5-3), so a brief says
  "header may move" when it adds one.
- Rules 17-20 above.
