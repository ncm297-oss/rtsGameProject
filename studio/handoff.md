# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-18-1236** (the second full session of 2026-10-18; cap 8, so **2 / 8 done**).
**All three tracks ACCEPT**, merge order sim → view → data. **Merge conflicts to expect (the conductor unions them):** `studio/bugs/README.md`
(the data branch's two rows against the sim's edits to the BUG-0960 / BUG-0982 rows + its BUG-0990 row) and `studio/qa/coverage.md` (the sim
edited the Save row; the view and data appended rows): a scratch three-way merge with "theirs" on both files built with 0 warnings, smoke PASS,
`SaveLoadTest` + `QaM6V14Test` PASS. docs/02 (view only), docs/03 (sim and view in different sections), docs/01 + docs/05 (Producer, sim worktree)
merge clean. **Golden unchanged** (554A3832241A0896; no `k` line moved; `ai.json` untouched). Counters after: **sim 3 / 4, view 3 / 4, data 0 / 4**
→ sim and view run one more **feature** session, then their hardening; data ran its hardening early. Bug ids: highest used **1011**; next **sim from
BUG-1020, view from BUG-1030, data from BUG-1040**. Open S1 / S2: **none**. New open: BUG-0990 (S4 sim), BUG-1000 / BUG-1001 (S3 view), BUG-1002
(S4 view), BUG-1010 (S3, sim-side AI, found by data), BUG-1011 (S4 data). Closed: BUG-0960 item 1 (sim), BUG-0982 (by decision: keep 18).

## Where we are

- **M6 3 / 10** (criteria 2, **3**, 4 ticked; 1, 5, 6, 7 advanced). **Criterion 3 ticked this session:** M6-S3 (the AI seats in the save,
  format version 2) + M6-V14 (the Save / Load screen) on top of M6-V9 (replay playback) and M6-S2 (the format). **The owner can now save a
  skirmish against the AI and load it** (Esc → Save; Main menu → Load Game → Load). Polish open: BUG-1000 / BUG-1001 (S3, view), BUG-1002 /
  BUG-0990 (S4).
- **M5 4 / 9** unchanged (criteria 1, 2, 7, 8). Left: M5-4g (BUG-0981's reserve; BUG-1010's lever), M5-4d, M5-4c slice 2, M5-5, criterion 6
  (the owner's play), D14 c.
- Still wanting the owner (not blocking): the art call, listening to the SFX, the D21 review table, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster".

## Standing rules (1-37)

Unchanged. 36 = the save API freeze (`TrySave` / `TryLoad` signatures and the 8 `SaveError` codes; `CurrentVersion` is 2 since M6-S3, no reader
for 1). 37 = the cross-track fixture fix. Long forms in the 2344 / 0516 handoffs in git history. **Accepted this session, not a rule:** a dev-only
launch flag the brief didn't ask for (`--ai <slot> human`) when it is needed by the task's own tests, documented, with no player-facing text.

## Checks to run at the next PLAN

- Inbox **New**; `git status` / `git log`; build; the non-Perf suite on the merged head (expect ~7,400 tests; `AiSaveQaTests` ~3.5 min,
  `DH6ThresholdQaTests` + the D21 third-seed rows several minutes); smoke in the view worktree; the loop rows `SaveLoadTest` / `QaM6V14Test`.
- Trust-but-verify candidates: `SaveFormat.CurrentVersion == 2` and `Simulation.TrySave` has no `HasAiSeat` branch; `game/data/common/ui.json`
  holds the 12 `save.*` keys; the malazan page's "Fast Age II against the raider rush" table matches
  `AiOpeningPairsReportTests.Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable`'s output (the row pins it); a windowed run: skirmish vs a
  Normal AI, Esc → Save → "Saved at m:ss", Load Game lists it with the Save tag, Load resumes it ("Save loaded: …" in the log).
- Sessions today: count `studio/sessions/2026-10-18-*.md` (2 after this one; cap 8).
- **Rule 31 at the next sim merge:** M5-4g changes the AI's spending, so the faction pages' spending cells (`AiSpendingReportTests`,
  `D20PinQaTests`, `CounterTriangleMarginsTests` if a brawl changes) will move: plan the data track's re-print at the integration (as D-R31), or
  give the data track that task outright.

---

## Sim track

### Next task candidates (in order)

1. **M5-4g — a reserve for the due step (BUG-0981) + the wave-under-pressure lever (BUG-1010) if it fits** (feature, counter 3 → 4 / 4; QA
   full; ≤ 1,500 lines; files `sim/Rts.Sim/Ai/**` (`EconomyManager`, `ProductionManager`, `BuildOrderExecutor`, `MilitaryManager`,
   `Blackboard` if a field is needed: then `Blackboard.Save.cs` and `SaveFormatTests`' AIST layout move with it, **`CurrentVersion` 2 → 3**, no
   reader for 2 unless the owner has saves: say so in the brief), `sim/Rts.Sim.Tests/Ai/**`, docs/03 "Implementation (M5-2)" / a new "(M5-4g)",
   docs/02 "AI opponent" if a rule is player-visible; **not** `game/data/**` beyond `rules.json` schema fields with minimum entries (the data
   track fills them), not the data track's test folders).
   - **Slice 1 (BUG-0981):** the economy / production managers keep the earliest due `build` / `research` step's cost in reserve (gold and wood
     separately) and weight the gather split toward what it lacks; a step due more than N thinks ago may also pause production of the mix
     until it is paid. Acceptance: Age II within ~2 minutes of its step on every opening against an active opponent on the 9 pairings' three
     seeds (`AiOpeningPairsReportTests`' "Age II at" lines and the new wait flags: 11 sides carry it today → 0 or a named allowance);
     `AiSpendingReportTests` re-printed by the data track (rule 31); no per-tick allocation; the golden unmoved unless the AI's commands change
     the seed-21 recording (it is a human-only recording: it must not move).
   - **Slice 2 (BUG-1010, only if the budget allows):** "send what you have when the base is being lost": a wave with the whole army when
     the hall's kept hp falls under a data threshold (`ai.json` `attack.desperationHp`, schema + the minimum entry; the data track sets it),
     or a defend-then-counter rule (after a defense ends with the enemy's wave beaten, wave at the current army if ≥ half the threshold).
     Acceptance: `cadre_rush` v `raider_rush` seeds 8 / 14 / 18 / 23 and `crossbow_push` v `raider_rush` 19 / 25 send a wave (QA's
     `DH6ThresholdQaTests` and `D21OpeningsQaTests.KnownBeatenAlive` turn strict), the other pairings' outcomes unchanged in kind.
2. **The sim's hardening (counter 4 / 4 after M5-4g):** BUG-0990 (the length bound's three missing terms; bound or document the command
   capacity), BUG-0960 items 2-3, BUG-0912, BUG-0911, BUG-0889, BUG-0144, BUG-0941, BUG-0940, BUG-0914 + BUG-0915 (S4 flakes), BUG-0913 (7
   nits), BUG-0113 item 2, BUG-0704 item 3.
3. Requests 38 (teams); M5-4c slice 2 (`gatherBonus` / `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground
   scouting, hunting the last units: BUG-0953's sim half, seed 11); D14 c (counter-composition); M5-5 (the 20-minute winner test); M5-6.

### Watch-outs (sim)
- A `Blackboard` field added or reordered changes the `AIST` layout: `Blackboard.Save.cs` and `SaveFormatTests` move with it and
  `CurrentVersion` bumps (rule 36 keeps the signatures and codes, not the layout). The owner may have saves by then: a version bump refuses them
  with "another version" (say so in For your review).
- The AI's state is not hashed: a change to the managers moves no `k` line of the golden (a human-only recording) but moves the pairing reports
  and the spending cells (rule 31).
- `AiSaveScenes` probes the Blackboard for its moments (rule 37); a manager change that makes a probe unreachable within 12,000 ticks fails
  `AiMatch_SavedAtFixedTicksAndProbedMoments_*` ("never mid-wave …"): adjust the row, not the probe.
- Three test hosts ran at once again this session: non-Perf rows held on every branch; wall-clock Perf rows flake under load (QA re-measured
  the save rows alone: 27-33 / 55-70 ms against 100 ms gates).

---

## View track

### Next task candidates (in order)

1. **M6-V15 — an AI-seat save row, BUG-1000, BUG-1001, and criterion 7's generator options in the skirmish setup** (feature, counter 3 → 4 /
   4; QA standard; ≤ 1,200 lines; files `game/scripts/**` (`LoadGameScreen`, `Main`, `Match`, `SkirmishSetup` / `LaunchOptions`), `game/data/common/ui.json`
   new `setup.*` keys (name them), `game/tests/**`, docs/02 "Game flow" 2 + 6, docs/03 "Implementation (M6-V15)"; not `sim/**` outside
   read-only `ViewApi/`).
   - **The AI row:** `SaveLoadTest` gains the default `--skirmish` match (slot 2 a Normal AI) saved at ~tick 600, quit, loaded, 1,000 ticks
     beside an unsaved twin: hash equal every tick **and** `Simulation.AiOpening(1)` equal; the pause menu's Save shows "Saved at …" for an AI
     match (no refusal). Slice 2 is on `main`: the refusal is gone.
   - **BUG-1000:** `Main.ShowLoadNote` remembers the refused path and re-selects it after the refresh (`RowOf`; row 0 when gone); a regression
     row refuses a damaged save that is not row 0 and asserts `RowPath(SelectedIndex)` is it.
   - **BUG-1001 (Producer's call):** a load resumes at the game speed the skirmish setup currently shows (a view setting, never in the save);
     `Save loaded: … speed Nx` prints it; docs/02 6 and docs/03 say so. If the setup's speed isn't reachable from `Main.LoadSavedGame`
     cheaply, the doc line alone closes the bug (say which).
   - **Criterion 7's generator options:** the skirmish setup exposes the generator's forests / mines (and start-location count follows the
     slots) as `MapGenParams` fields through `LaunchOptions` / `SimRunner`; recorded in the replay header already (every `MapGenParams` field
     is in the save header too, so a loaded match keeps them). Keep the three hand-made maps out (no map format yet).
2. **The view's hardening (counter 4 / 4 after M6-V15):** BUG-1002 (3 nits), BUG-0970 (S4), BUG-0955 (S3: `smoke.ps1` adopts `-cmatch` +
   frame skip), BUG-0954 item 4, BUG-0922 (S4), the stale `QaM6V11Test` header comment, BUG-0550 item 2 (the art pass, owner-gated), BUG-0521
   items 2-3 (unreachable), BUG-0743 item 3 (wontfix candidate).
3. The owner's sound re-cuts when the listening note arrives; teams when 38 lands; `tools/export.ps1` when the owner's templates arrive;
   the hand-made maps when a map format exists; the art pass when the owner answers; criterion 6's music when the track arrives.

### Watch-outs (view)
- `ViewApi/SaveSummary` hard-codes the header's shape (26 `MapGenParams` fields, 6 capacities, the fixed tail before the tick): a sim change to
  `SaveHeader.Write` breaks the list's tick column silently in the game; `QA/SaveSummaryQaTests` catches it against real saves (keep that row).
- `Main.SaveDir` is null under a test host (no Save button, nothing listed): every scene sets its own folder (BUG-0770's rule).
- `scene-loop.ps1 -Filter` is a **file glob**, not a regex; `-TimeoutSec` default 600. `SaveLoadTest` ~7 s, `QaM6V14Test` ~8 s headless.
- A clone without `git lfs pull` fails `LookTest` / `SfxTest` clip rows and smoke (BUG-0955); the game boots on tones.
- `MatchStats.OpeningOf` computes a watched replay's pick with `AiOpeningLookup.Pick`; a loaded match reads `Simulation.AiOpening` live.

---

## Data track

### Next task candidates (in order)

1. **Inbox first:** the owner's answers on the D21 review table (names, descriptions, the Malazan `standard` wave 20 → 16) → a D21 tweak task
   (QA light; golden regen once if a number or text moves), **with BUG-1011's five wording nits and BUG-1010's page line folded** (the pages
   are touched anyway).
2. **Else the rule-31 re-print if the sim's M5-4g merges this session** (dispatched at the integration as D-R31 was, or planned outright when
   the PLAN knows M5-4g is the sim task: `AiSpendingReportTests` / `D20PinQaTests` / the pairing reports' "Age II at" cells on both pages;
   no data change; QA light / none per rule 23).
3. **Else STOP cheaply:** no schema the data track needs is on `main` (D14 c and M7's rosters wait on sim systems); the counter is 0 / 4.

### Debt (data)
BUG-1011 (S4, text only), BUG-1010's page line (quote the 5-seed counts, name seed 25). BUG-0982 closed by decision.

### Watch-outs (data)
- `Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable` asserts the shipped number is the rule's pick **and** pins the page table cell for cell:
  a sim change that moves the outcome (M5-4g will) fails it → re-print the table from the row's output and re-read the rule in the same task.
- `KnownBeatenAlive` prints "take it out" when its case passes: do so in the task that makes it pass.
- A Normal / Hard AI picks by seed: every report names the opening it measured; the pages name the seed.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/01, docs/02, docs/03: each track appends its own rows; the conductor unions them
  (this session: README and coverage conflict, see the header).
- **QA scratchpads:** two tracks' QA inspectors cloned into the same `scratchpad/clone` folder this session and clobbered each other once (the
  sim's QA report notes it; no result was lost). Every QA brief should say: clone into a track-named folder (`<track>qa<session>-*`).
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's first
  hardening item.
