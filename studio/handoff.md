# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-18-1814** (the third full session of 2026-10-18; cap 8, so **3 / 8** with this
one). Base commit **728dee4** (= `origin/main`, clean in all three worktrees). **GO on sim and view; the data track STOPs** (no inbox
answer, no schema it needs on `main`; the rule-31 re-print is dispatched at the integration, see "Notes for the conductor").
Counters before: sim 3 / 4, view 3 / 4, data 0 / 4 → **after this session sim 4 / 4 and view 4 / 4: both run their hardening next.**
Bug ids: **sim from BUG-1020, view from BUG-1030, data from BUG-1040**. Open S1 / S2: none.

## Checks at this PLAN

- Inbox **New**: empty. Nothing to process (the D21 review table, the art call, the sounds, the Shock pass, `goldShare`, BUG-0780,
  "densest cluster" still wait on the owner; none blocks).
- Repo: `git status` clean at 728dee4 in all three worktrees; `dotnet build RtsGame.sln` **0 warnings, 0 errors (19 s)**; the non-Perf
  suite on `main` **7,328 passed / 0 failed / 14 skipped of 7,342 in 38 m 23 s** (one host; the 14 skips carry bug ids); smoke gate in the
  view worktree **PASS** (menu + `--skirmish`, `Rts.Sim 0.0.1`, no ERROR).
- Trust-but-verify (all hold): `SaveFormat.CurrentVersion == 2` (`Save/SaveFormat.cs:12`); `HasAiSeat` exists only on `SimConfig`, no
  branch in `Simulation.TrySave`; `game/data/common/ui.json` holds exactly the 12 `save.*` keys the 1236 log lists.
- Docs drift found: docs/02 "Game flow" 2 lists **starting resources (standard / high) and game speed** as skirmish-setup fields, but the
  setup (M6-V1 / V5) has seed, map size and the slots only (`--speed` is a launch flag; no per-match starting resources exists in the
  sim at all: `rules.json` `startingGold` only). M6-V15 builds the speed list and makes the line say what is built; starting resources
  becomes **Requests for the sim track 43** (a `SimConfig` starting-resources choice) at ACCEPT. BUG-1001's repro step 2 ("Skirmish
  setup, game speed Fast") is therefore not reproducible by hand; the bug stands on the `--speed` path.
- Open S1 / S2: none. Debt: S3 20, S4 22.
- Sessions today: 2 done (0516, 1236) → this is 3 / 8.

## Standing rules (1-37)

Unchanged. 31 = a sim change that moves the faction pages' measured cells gets the data track's re-print at the integration. 35 = the sim
may rewrite data-track **fixtures** mechanically when its schema change breaks them (say so). 36 = the save API freeze (`TrySave` /
`TryLoad` signatures and the 8 `SaveError` codes; the layout may move with a version bump). 37 = the cross-track fixture fix.

---

## Sim track

### Current session plan

```
TASK_ID: M5-4g
TASK_TITLE: Keep a reserve for the build order's due step, gather toward what it lacks; then defend-then-counter if the budget allows
SESSION_TYPE: feature (counter 3 → 4 / 4)
QA_TIER: full
```

**Goal.** Four of the six AI openings never reach Age II against an active opponent (BUG-0981): the production manager spends every think's
purse first, so a due `research` / `build` step never gets paid, and the gather split stays at `goldShare` whatever the step lacks (banks of
1,500 gold / 25 wood). Slice 1 gives the build order the priority docs/02 "AI opponent" already states ("follow the build order → keep
producing workers and army"). Slice 2 (only if slice 1 is green and the budget allows) gives the AI a counter-attack after it beats off a
raid, the lever for BUG-1010 (two Malazan openings fall to the raider rush without ever attacking).

**Scope.**
- **Slice 1, the reserve (BUG-0981).** Each think, after the census, the executor names the **reserved step**: the first step in file order
  that is due (trigger reached, slack passed), not done, with no pending order, of action `build` / `research` / `expand`, whose cost the
  purse can't pay now. Its gold and wood are the think's **reserve** (a house build step counts like any step; with no such step the reserve
  is 0 / 0).
  - **Who spends only above the reserve:** the production manager, and the executor's `train` steps that come **after** the reserved step
    in file order. **Unaffected:** steps before it in file order (file order is the data track's priority), the economy manager's own house
    rule (a pop block costs more than a late step), the defense, scout and ability managers (they spend nothing anyway).
  - **No-node guard:** a kind the player knows no node of (the gatherers' `NearestNode` finds none) is dropped from the reserve that think
    (it would wait forever, starving the other kind's spending).
  - **Gather toward what it lacks:** while a reserved step exists, the wanted gold share in `Gatherers` and `Rebalance` is `goldShare +
    reserveShift` when the step is short of gold only, `goldShare − reserveShift` when short of wood only, `goldShare` when short of both
    or neither; clamped to 0..1. New `rules.json` `ai.reserveShift` **0.25** (schema in `AiRulesDef` / `DataJson` / `DataLoader`, validated
    0..1, a `DataValidationTests` row; the minimum entry in `game/data/common/rules.json`; it enters `ContentHash` like the other `ai`
    fields → the golden's `data-hash` header regenerates **once**, say why in the commit body and the `GatherWedgeQaTests.SameGameDataHashes`
    comment; the seed-21 recording is human-only, so **no `k` line moves**).
  - The reserve is think scratch (`AiPlayer` fields rebuilt each think; no `Blackboard` field, nothing to save).
- **Slice 2, defend-then-counter (BUG-1010), only if slice 1 is complete and green at ≤ ~900 lines.** When a defense ends (`DefenseManager`:
  `Holds` false after `Defending` was true) with the player's Town Hall standing, `Blackboard.DefenseEndedTick = Now`. In
  `MilitaryManager.TryStart`, within `ai.counterWindow` seconds (new, **30**) of that tick the wave cooldown is ignored and the start
  thresholds are `ai.counterFraction` (new, **0.5**) × the opening's `attack.minArmyPop` / `minArmyCost` (the difficulty's
  `waveSizeMultiplier` applies as usual); the wave then runs the normal rules (retreat, re-target, the defense may pull from it). Both knobs
  in `rules.json` `ai`, validated (`counterWindow` ≥ 0 s, `counterFraction` 0..1; 0 = never). A new `Blackboard` field moves the `AIST`
  layout: `Blackboard.Save.cs` + `SaveFormatTests` move with it and **`SaveFormat.CurrentVersion` 2 → 3, no reader for 2** (the owner may
  hold version-2 saves since the 1236 session: they refuse with "another version"; say so in the report so For your review can).
- **Out of scope:** `ai.json` (the data track's file; no opening changes, no per-opening knob), M5-4c slice 2's knobs, expansion /
  rebuilding / hunting (M5-4d), D14 c, the data-owned pinned rows (below), any view file, any `Tick()` / `Run` change outside `Ai/`.

**Acceptance criteria.**
1. **Reserve unit tests** (`Ai/ReserveTests`, new): the reserved step is the first due unaffordable `build` / `research` / `expand` in file
   order; production and a later `train` step can't spend into the reserve; an earlier `train` step, the house rule and an unaffordable
   step's own retry are unaffected; the reserve is 0 / 0 with no such step; the no-node guard drops a kind with no known node; a staged
   scene where the production manager starves a 400 / 200 research step today (BUG-0981's `age_ii` case) researches it after.
2. **Gather shift tests:** short of gold only / wood only / both / neither → the four wanted shares, clamped at 0 and 1; the
   `Rebalance` path moves one gatherer a think toward the lacking kind; `reserveShift` 0 reproduces today's split exactly (a twin run
   hash-equal to the pre-change behaviour on the AI scenes in `AiSaveScenes`, which proves the schema default is inert).
3. **The pairs reports:** on `Content/AiOpeningPairsReportTests` (9 pairings, smallest seeds) and `QA/Content/D21OpeningsQaTests`'
   second and third seeds (run them; do not edit them): **every side alive at 20:00 has reached Age II**; the "BUG-0981 pattern" flag
   count goes from 11 to 0 on sides alive at 20:00 (a side defeated before its Age II step is the only allowance; name each in the report);
   **both sides still wave within 20 minutes on every pairing** (D21 criterion 2, the existing named allowances allowed). Quote the "Age
   II at" and first-wave lines per pairing and seed in the report (the data track re-prints the pages from them).
4. **Slice 2 (if built):** on `cadre_rush` v `raider_rush` seeds 8 / 14 / 18 / 23 and `crossbow_push` v `raider_rush` seeds 19 / 25
   (Normal v Normal, the menu's map, 20 minutes) the Malazan side sends at least one wave **or** stands at 20:00 on at least 4 of the 6
   (print all 6 rows); no pairing side of criterion 3 loses its wave; a counter wave starts only inside the window, only with the hall
   standing, never while `Defending`, and respects the proportional defense's recall (`Ai/CounterWaveTests`, new, through the command log).
5. **Save:** with no `Blackboard` change, `CurrentVersion` stays 2 and `SaveRoundTripTests` / `AiSaveScenes` are untouched; with slice 2,
   `CurrentVersion` 3, `SaveFormatTests`' `AIST` layout updated, every AI save row still hash-equal to T + 2,000 with every `Blackboard`
   field equal, a version-2 file refused `FormatVersionMismatch`, and the `AiSaveScenes` probes still reached within 12,000 ticks (adjust a
   row's tick if a probe moves, never the probe).
6. **Determinism and cost:** the AI determinism twins (`AiDeterminismTests`, `ProportionalDefenseFuzzStressTests`) green; the think Perf rows
   (the 1 ms gate, `AiPlayerPerfTests` / the storms fuzz row) green **alone**; no allocation in a think after construction (the existing
   0 B rows); `ReplayGoldenTests` green with the header regen only (every `k` line byte-equal).
7. **Data-owned pinned rows:** `Content/AiSpendingReportTests`, `QA/Content/D20PinQaTests`, `Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable`,
   `CounterTriangleMarginsTests` (if a brawl changes) and `D21OpeningsQaTests.KnownBeatenAlive` **may go red: list each red row with its new
   output in the report, don't edit them** (rule 31: the data re-print at the integration). Everything else in the suite green.
8. **Docs:** docs/03 a new "Implementation (M5-4g)" (the reserve rule, who spends above it, the guard, the shift, the counter rule, the
   format bump if any, the tests); docs/02 "AI opponent" one bullet each for the reserve and (if built) the counter; the `rules.json` field
   table wherever `ai.defendRatio` is documented gains the new fields; docs/01 change-log rows are the Producer's at ACCEPT.

**Design references.** docs/02 "AI opponent" (priority order; the defense sizing bullet for the recall interplay); docs/03 "Implementation
(M5-2)" (a think's single purse: "the gold and wood the view showed, less each command's cost as it is sent"; the executor semantics,
which must not change), "Implementation (M5-4f)" (the defense's end and `Defending`), "Implementation (M5-8a)" (`ai.Opening.*`), "Save
format (M6-S2)" (the `AIST` layout rule); BUG-0981 (the measured banks; `age_ii` 400 gold / 200 wood); BUG-1010 (the six seeds).

**Tests required.** `Ai/ReserveTests` (criteria 1-2), `Ai/CounterWaveTests` (criterion 4, if built), the `DataValidationTests` rows for the
new fields (range, missing = error like the other `ai` fields), `SaveFormatTests` (criterion 5), the golden header regen commit, and the
existing AI suites green. QA adds its own.

**Constraints.** No per-tick allocation (the reserve is computed from the census arrays already there); no `Dictionary` iteration; the
AI reads only `PlayerView`; one command per unit per think; `Tick()` untouched; no Godot; the new numbers live in `rules.json`, never in
C#; docs in the same commit. Files: `sim/Rts.Sim/Ai/**`, `sim/Rts.Sim/Data/{AiRulesDef,DataJson,DataLoader*}.cs`, `Save/SaveFormat.cs`
(slice 2 only), `game/data/common/rules.json`, `sim/Rts.Sim.Tests/**` **except** `Content/` and `QA/Content/` (data-owned), docs/02,
docs/03. Budget ≤ 1,500 lines; slice 1 first and whole.

### QA focus (sim)

Full checklist. Attack hardest: (a) **starvation the other way**: a reserved step whose kind has no known node, or whose building is never
buildable (requires unmet, no site for 20 minutes): does production freeze? the guard must release it; (b) **the shift's oscillation**: a
step short of gold then wood then gold: do the gatherers ping-pong (one move a think is the bound: count moves per minute); (c) the
reserve against the **house rule and the defense** (a defended base with a due Age II step must still place houses and recall);
(d) **determinism**: twin runs on the 9 pairings × 3 seeds, hash every 50 ticks; (e) **save / load mid-reserve and mid-counter-window**
(slice 2): the loaded AI's command stream equal to the unsaved twin's at those moments (`AiSaveQaTests`' method); (f) a forged
`DefenseEndedTick` (negative, far future) loads without a throw; (g) **cost**: a think with every step due and unaffordable on a 4-seat
256 map, 0 B, under the 1 ms gate alone; (h) **outcomes**: replay the six BUG-1010 seeds and the 9 pairings' three seeds yourself,
independently of the dev's rows, and the Easy / Hard difficulties on three of them (the counter must scale with `waveSizeMultiplier`).
Clone into a track-named scratch folder (`simqa1814-*`). Data-owned pinned rows going red are expected (rule 31): list them, don't fix them.

### Next task candidates (after this one)

1. **The sim's hardening (counter 4 / 4):** BUG-0990 (the length bound's missing terms), BUG-0960 items 2-3, BUG-0912, BUG-0911, BUG-0889,
   BUG-0144, BUG-0941, BUG-0940, BUG-0914 + BUG-0915 (S4 flakes), BUG-0913 (7 nits), BUG-0113 item 2, BUG-0704 item 3, plus whatever M5-4g
   leaves at S3 / S4.
2. Requests 38 (teams), **43 (starting resources, standard / high, from this PLAN's drift finding)**; M5-4c slice 2 (`gatherBonus` /
   `focusFire` / `pullBackWounded`); **M5-4d** (expansion, rebuilding, high-ground scouting, hunting the last units: BUG-0953's sim half,
   seed 11); D14 c (counter-composition); M5-5 (the 20-minute winner test); M5-6.

### Watch-outs (sim)

- A `Blackboard` field added or reordered changes the `AIST` layout: `Blackboard.Save.cs` and `SaveFormatTests` move with it and
  `CurrentVersion` bumps; the owner may have version-2 saves now (M6-V14 shipped the button): say so.
- The AI's state is not hashed: a manager change moves no `k` line of the golden but moves the pairing reports and the spending cells
  (rule 31). The `data-hash` header moves once for the schema field.
- `AiSaveScenes` probes the Blackboard for its moments (rule 37); a manager change that makes a probe unreachable within 12,000 ticks
  fails `AiMatch_SavedAtFixedTicksAndProbedMoments_*`: adjust the row's tick, not the probe.
- Three test hosts share the machine when all tracks run; this session only two: wall-clock Perf rows still rerun alone before counting.

---

## View track

### Current session plan

```
TASK_ID: M6-V15
TASK_TITLE: AI-seat save test row, BUG-1000, the game-speed list and a load's speed (BUG-1001), the generator's forests / mines in the skirmish setup
SESSION_TYPE: feature (counter 3 → 4 / 4)
QA_TIER: standard
```

**Goal.** Finish the Save / Load screen's two S3 rough edges, prove the screen on a match against the AI (the sim's slice 2 landed after the
screen's test was written), and give the skirmish setup the two settings docs/02 already promises and the generator already supports
(game speed; forests and gold mines, M6 criterion 7's "generator in skirmish setup").

**Scope.**
- **The AI row:** `SaveLoadTest` gains the default `--skirmish` match (slot 2 a Normal AI): save at ~tick 600 (the pause menu's Save says
  "Saved at …", no refusal), quit, load, 1,000 ticks beside an unsaved twin: the state hash equal every tick **and** `Simulation.AiOpening(1)`
  equal; the loaded pause menu's note; the stats screen's "opened with" line after a load (`MatchStats.OpeningOf` reads the live sim).
- **BUG-1000:** `Main.ShowLoadNote` remembers the refused path and after the refresh selects `RowOf(path)` (row 0 when gone); the same for
  a refused Watch. Regression rows: refuse a damaged save that is not row 0 (and a damaged replay), assert `RowPath(SelectedIndex)` is it.
- **BUG-1001 (Producer's call):** the skirmish setup gains a **Game speed** list, Slow 0.75× / Normal 1× / Fast 1.5× (docs/02 "Game flow"
  3), default Normal, passed as `--speed`; **a loaded match resumes at Normal (1×)**: the save holds no speed and the setup's choice belongs
  to the match it started. `Save loaded: … speed 1x` already prints it. docs/02 "Game flow" 2 (the list) and 6 (the load's speed) and
  docs/03 "Implementation (M6-V14)" say so. `ui.json` `setup.speed`, `setup.speed_slow` "Slow", `setup.speed_normal` "Normal",
  `setup.speed_fast` "Fast".
- **Criterion 7's generator options:** the setup exposes **Forests** and **Gold mines** (spin boxes or lists; the range the generator accepts,
  `MapGenParams.MaxResourceGroups` 64, with a sane lower bound: find the smallest count at which every start location still gets its two
  mines and a forest, from `MapGenParams.Validate` / the start-location rule, and clamp the UI to it; defaults 12 / 8 =
  `LaunchOptions.DefaultForests` / `DefaultMines`), passed as `--forests` / `--mines` (already parsed). Start-location count follows the
  slots (already). A match started with non-default counts saves, loads and replays with the same map (the header carries every
  `MapGenParams` field): a `SaveLoadTest` row at 6 forests / 4 mines, hash-equal twin. `ui.json` `setup.forests` "Forests",
  `setup.mines` "Gold mines". docs/02 "Game flow" 2 lists what the setup now has and says starting resources are **not yet** (Requests 43).
- **Out of scope:** the three hand-made maps and mirrored terrain (no map format yet), starting resources (needs the sim), the speed in the
  save (frozen format), team slots (Requests 38), the art pass, BUG-1002's nits (hardening next session), any `sim/**` change beyond a
  read-only `ViewApi/` accessor if one is truly needed (say which).

**Acceptance criteria.**
1. `SaveLoadTest` PASS headless through `scene-loop.ps1` with the AI row (hash-equal 1,000 ticks, `AiOpening(1)` equal, "Saved at" shown
   for the AI match).
2. BUG-1000 rows PASS: after a refused Load (a damaged save not at row 0) and a refused Watch (a damaged replay not at row 0), the selected
   row is the refused file; Delete then removes that file and no other; with the refused file gone, row 0.
3. `ShellTest`: the speed list's three items map to `--speed 0.75` / `1` / `1.5` (`SimRunner.GameSpeed` after Start); the forests / mines
   controls map to `--forests` / `--mines` and clamp to the accepted range; the five new `setup.*` keys resolve; every new text comes from
   `ui.json`.
4. A loaded match's `SimRunner.GameSpeed` is 1 whatever `--speed` the saved match ran at (a `SaveLoadTest` row: start at `--speed 1.5`,
   save, load, assert 1x, and the log line); docs/02 6 and docs/03 say so.
5. The generator-options row: 6 forests / 4 mines → the match's `World` has those group counts (count the resource nodes' groups or read
   the `MapGenParams` back from the save header through `ViewApi/SaveSummary` if it exposes them, else the replay header); save → load
   hash-equal twin; Watch of its replay plays.
6. Loop green (every scene; 71 + the new rows), smoke PASS, build 0 warnings, `git diff 728dee4...HEAD -- sim/` empty or read-only
   `ViewApi/` + its tests only; `ArchitectureTests` green.
7. docs/02 "Game flow" 2 + 6, docs/03 "Implementation (M6-V15)" accurate to the code; BUG-1000 / BUG-1001 files set `fixed` with the
   test names (the Producer re-checks).

**Design references.** docs/02 "Game flow" 2 (the setup's fields), 3 (the three speeds), 6 (save / load); docs/03 "Implementation
(M6-V14)" (`Main.LoadSavedGame` / `ShowLoadNote` / `SaveStore`), "Implementation (M6-V9)" (the replay header's `MapGenParams`), "Implementation
(M6-V5)" (the slots and `--players`); `LaunchOptions` (`--speed`, `--forests`, `--mines`, `DefaultForests` 12 / `DefaultMines` 8);
`MapGenParams` (`MaxResourceGroups` 64, `StartLocations`, the per-base mine / forest rule from M5-4a).

**Tests required.** `SaveLoadTest` rows (criteria 1, 2, 4, 5), `ShellTest` rows (criterion 3), QA's own scene. Headless first, then one
windowed 1280 × 720 run of the setup with the new controls, screenshot looked at (definition of done 3).

**Constraints.** Views hold no gameplay state; nothing writes the sim (`TrySave` / `TryLoad` are the doors; the setup only composes launch
flags); no Godot in `ViewApi/`; player-facing text in `ui.json`; the wall clock names files only; `Main.SaveDir` null under a test host
(each scene sets its own folder, BUG-0770). Budget ≤ 1,200 lines. Files: `game/scripts/**`, `game/tests/**`, `game/data/common/ui.json`
(`setup.*` keys only), docs/02, docs/03, the two bug files.

### QA focus (view)

Standard. Attack: the AI twin saved at several ticks (1, the first think, mid-wave if reachable through `--skirmish`'s seat) and loaded twice
in a row; the refused-row selection with 1 / 2 / 200 rows, the refused file at the bottom, two damaged files, a refused Watch then a refused
Load; the speed list's value after Back → Skirmish again (does it keep?), a load from `--load` with `--speed 1.5` (the flag counts for
`--load` per docs/03: assert which wins and that the docs say so); the generator options at both clamps and one past each (the UI must not
let an invalid count through; the generator must not throw), on the 64 / 96 / 128 map sizes with 2 / 3 / 4 slots (every base still gets
its two mines and a forest: count them); the setup's layout at 1280 × 720 with four slots and the two new rows (nothing overlaps: screenshot).
Clone into a track-named scratch folder (`viewqa1814-*`).

### Next task candidates (after this one)

1. **The view's hardening (counter 4 / 4):** BUG-1002 (3 nits), BUG-0970 (S4), BUG-0955 (S3: `smoke.ps1` adopts `-cmatch` + frame skip),
   BUG-0954 item 4, BUG-0922 (S4), the stale `QaM6V11Test` header comment, BUG-0550 item 2 (the art pass, owner-gated), BUG-0521 items 2-3
   (unreachable), BUG-0743 item 3 (wontfix candidate), plus what M6-V15 leaves.
2. The owner's sound re-cuts when the listening note arrives; teams when 38 lands; starting resources when 43 lands; `tools/export.ps1` when
   the owner's templates arrive; the hand-made maps when a map format exists; the art pass when the owner answers; criterion 6's music when
   the track arrives.

### Watch-outs (view)

- `ViewApi/SaveSummary` hard-codes the header's shape (26 `MapGenParams` fields, 6 capacities, the fixed tail before the tick): a sim change
  to `SaveHeader.Write` breaks the list's tick column silently; `QA/SaveSummaryQaTests` catches it against real saves. **This session the
  sim may bump `CurrentVersion` to 3 (slice 2):** `SaveSummary.FormatVersion` is read as "any", so the list still shows the tick; the
  `SaveLoadTest` rows are built on the branch's own sim and merge after the sim's, so they run against format 3 at the integration: the
  Producer re-runs `SaveLoadTest` + `QaM6V14Test` on the merged tree before the push.
- `Main.SaveDir` is null under a test host (no Save button, nothing listed): every scene sets its own folder (BUG-0770's rule).
- `scene-loop.ps1 -Filter` is a **file glob**, not a regex; `-TimeoutSec` default 600. `SaveLoadTest` ~7 s, `QaM6V14Test` ~8 s headless.
- A clone without `git lfs pull` fails `LookTest` / `SfxTest` clip rows and smoke (BUG-0955); the game boots on tones.

---

## Data track

### Current session plan

```
DECISION: STOP (cheap; the counter stays 0 / 4)
REASON: inbox New is empty (no D21 answers), no schema the data track needs is on main, and the rule-31 re-print needs M5-4g merged first
```

**At the integration (conductor):** if the sim's M5-4g merges and any data-owned pinned row is red on the merged tree (`AiSpendingReportTests`,
`D20PinQaTests`, `Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable`, `CounterTriangleMarginsTests`, `D21OpeningsQaTests.KnownBeatenAlive`
printing "take it out"), dispatch the **D-R31-style re-print** (as at 2026-10-17-1624): re-print the pages' cells and the pairs tables from the
rows' output, re-read the keep-18 rule (the table may now pick another number: apply the rule, don't keep 18 by habit; a changed number is a
data change → golden regen and a For your review table), remove `KnownBeatenAlive` if its case passes, and **fold BUG-1011's five wording nits
and BUG-1010's page line** (quote the 5-seed counts, name seed 25) since the pages are open anyway. No C# outside `Content/` and
`QA/Content/`; QA light / none per rule 23.

### Next task candidates

1. **Inbox first:** the owner's D21 answers (names, descriptions, the Malazan `standard` wave 20 → 16) → a D21 tweak task (QA light).
2. The re-print above if the integration didn't do it.
3. D19's table review, the Shock pass, `goldShare` 0.6 → 0.5, BUG-0780: each waits on the owner's yes / no.
4. Else STOP until D14 c / M7's schemas land.

### Watch-outs (data)

- `Bug0982_CadreRushVersusRaiderRush_WaveThresholdTable` asserts the shipped number is the rule's pick **and** pins the page table cell for
  cell: M5-4g moves the outcome → re-print from the row's output and re-read the rule in the same task.
- A Normal / Hard AI picks by seed: every report names the opening it measured; the pages name the seed.

---

## Watch-outs (all tracks)

- `studio/bugs/README.md`, `studio/qa/coverage.md`, docs/02, docs/03: each track appends its own rows; the conductor unions them. This session
  docs/02 is touched by both sim ("AI opponent") and view ("Game flow"): different sections, should merge clean.
- QA scratchpads: clone into a track-named folder (`<track>qa1814-*`).
- Builds share the machine: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only when it
  fails again alone.
- BUG-0914 / BUG-0915: if the H3 0-byte probe or the `buildingSight` loader row fails in another full run, it becomes S3 and the sim's first
  hardening item.
