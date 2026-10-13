# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-10-13-1616** (fourth full session of 2026-10-13, 4 / 8). Base `adf7c6b` =
`origin/main` (sim M5-4b + view M6-V6; D16 already in). All three tracks **GO**: sim **M5-4c slice 1** (the ability manager, then the
difficulty knobs under a hard stop), view **BUG-0674 / 0680 (+ 0681) then M6-V4** (victory / defeat banner + the post-match stats),
data **D17** (docs/02's Normal line, the pages' "Defense" lines, an AI spending report). Counters going in: sim 1 / 4, view 1 / 4,
data 1 / 4 (each → 2 / 4 at ACCEPT). Bug ids: **sim from BUG-0700, view from BUG-0710, data from BUG-0720.** Inbox: empty.

## Where we are

- `main` = `adf7c6b`: build 0 warnings; the Producer's non-Perf run at the PLAN is in the session log. Golden `data-hash`
  **72EF431E70C49C39**, `k` lines unmoved since M5-3. **No open S1 / S2.** Two view scenes red on `main`'s loop (BUG-0674, BUG-0680; both
  test / reader side, the view's first item).
- The game from the menu: Skirmish → Start: a hall and 5 workers a side; the AI gathers, scouts, builds, waves, retreats, defends; a
  player with no building left is defeated (`Simulation.MatchOver`); the view prints only the "Match over:" dev line. Settings from
  the menu and the pause menu.
- Sim surface for the view (docs/03 "Implementation (M5-4b)" → "For the view (M6-V4)"): `World.DefeatEvents` (one-tick span of
  `DefeatEvent { Player, Tick }`), `Simulation.MatchOver` / `World.MatchOver` (latched), `World.IsDefeated(p)` / `EverBuilt(p)`,
  `World.Kills` / `Losses` (per-player spans), `World.Gold` / `Wood`, `TickNumber`.

## Standing rules (1-20)

1-16 as in the 1930 / 0146 / 0527 PLAN handoffs (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; **one system per brief**, the
dev's count excludes QA's files and is reported; **no process killing by image name**).
17. **A dev never loosens a QA row.** A failing QA row is a real finding (fix the code) or a wrong test (say so in the report; QA changes it).
18. **The Producer's merged-tree check runs the whole scene loop** whenever the sim branch touches the map generator, `ResourcePlacer`,
    `ViewApi/StartBase`, `World.Buildings` liveness or the command door.
19. **A QA scene row for a bug left open at ACCEPT prints `KNOWN BUG-nnnn ...`, not FAIL** (the sim's equivalent is a `Skip` naming the
    bug); the fix turns the row into a Check.
20. **A brief's slicing rule is a hard stop:** "if system 1 plus its tests passes N lines, ship it alone" means stop there and report.

---

## Sim track

### Current session plan: M5-4c slice 1 · feature · QA full

**Goal.** The last missing manager: the AI casts its faction's abilities through the same door as the player (`Command.UseAbility`),
when its difficulty's `useAbilities` is on; and, under a hard stop, the three difficulty knobs the sim still ignores (`gatherBonus`,
`focusFire`, `pullBackWounded`). Advances M5 criteria 2 ("ability manager") and 4 (Easy / Normal / Hard per docs/02).

**Scope.**
- **System 1, the `AbilityManager`** (`sim/Rts.Sim/Ai/AbilityManager.cs`), run in the think **after `MilitaryManager`** (docs/03 "AI
  architecture" order). Skipped entirely when the seat's `AiDifficultyDef.UseAbilities` is false (Easy casts never; a test proves 0
  `UseAbility` commands in 6,000 ticks of Easy beside a casting Normal twin).
  - **Casters** = own units whose `UnitDef.Abilities` is non-empty, not `Casting`, no AI command pending, not a wave member owed its
    `Move` home, and (new) whose ability is **ready**: add to `PlayerView` a read-only own-unit accessor for the cooldown, e.g.
    `bool TryGetAbilityReady(int slot, int abilityIndex, out int readyTick)` (own units only; an enemy slot answers false / 0; values
    only; the architecture tests' source scan and the oracle fuzz get a row). Name any other accessor you need the same way.
  - **Target rule per shipped ability shape** (every shipped ability is `targetGround`, `affects` enemy units; the Cusser also
    `HitsBuildings`): the candidate point is the **centroid of the enemy units seen within `Range + Radius` of the caster** that
    are themselves within `Radius` of that centroid; cast when at least **`castMinTargets`** enemies lie within `Radius` of the point
    (a new `rules.json` `ai` knob, default **2**, 1-50, `DataValidationTests` row, in `ContentHash`: the golden header may move once,
    `k` lines may not); the point clamped to `Range` from the caster (the walk-in does the rest). For an ability with
    `HitsBuildings`, when no unit point qualifies, an **enemy building seen within `Range + Radius`** (nearest first; a ghost does not
    count) is a target on its own. `selfAura` / `summon` kinds: cast at the caster when the same enemy count holds within `Radius` of
    it (no shipped ability has these kinds; the rule still exists, tested with test-only data). Never target own units
    (`friendlyFire` abilities: the point must be farther than `Radius` from every own unit seen, else skip this think).
  - **One cast per caster per think**, judged next think like the executor's steps (a cast that was dropped — `AbilityEvent.Dropped`
    reaches nobody in `PlayerView`; so judge by the cooldown moving: `readyTick` advanced = it resolved); a caster whose cast didn't
    land after `MaxTries` (3, like the military's) on the same version of the point is left alone until the point moves.
  - Respects `Board.Defending` (casts are still allowed while defending: a storm on the raiders is the point of it), `IsFleeing`
    (a fled worker never casts; no worker has abilities anyway).
  - Allocation-free per think; per-caster state in the `Blackboard` sized at construction, itemised in docs/03.
- **System 2, the knobs, only if System 1 + its tests + docs lands under 700 dev lines (rule 20: a hard stop; otherwise report and the
  knobs are M5-4c slice 2 next session):**
  - `gatherBonus` (Hard 0.25): at the drop-off (`EconomySystem.cs:131`, the one `AddToTotal` for cargo) a seat's cargo is scaled by
    `1 + gatherBonus` **in integer millionths with a per-player, per-kind remainder carried** (two hashed ints per player in the
    ledger: the long-run rate is exactly 1.25, never a floor loss); a human seat has bonus 0; `SimConfig.SeatOf(p)` → the faction's
    `AiDef.Difficulty(d).GatherBonus`. Say in docs/03 where it is hashed (the totals already are; the carries are new state → new hash
    bits, named). The golden has no AI seat: its `k` lines may not move.
  - `focusFire` (Hard): in `MilitaryManager.Orders`, a **wave member** picks among enemy units seen within its `attack.range + 2 m`
    the **lowest-hp** (ties: lowest slot) and gets one `Command.Attack` on it when the pick differs from the member's kept
    `FocusTarget` (Blackboard, per unit slot; cleared at the wave's end). Off: unchanged behaviour (today's bytes).
  - `pullBackWounded` (Hard): a wave member whose `Hp < retreat.hpFraction × its max hp` (add `PlayerView.MaxHp(slot)` for own units
    if `World.MaxHpOf` isn't reachable) gets one `Move` to the rally point and **leaves the wave alive** (BUG-0596's rule: not a loss),
    once per unit per wave; back home it is the rally rule's again.
- OUT: expansion, rebuilding, periodic re-scouting, high ground before attacking up (M5-4d); the 20-minute winner test (M5-5); any
  `ai.json` schema change (the data track is in `docs/factions/` and `Content/` this session; the `ai.json` files are frozen for you);
  `ui.json` (view-named this session); `targetUnit` / `autocast` ability kinds (deferred, docs/01); teams (Requests 38).

**Acceptance criteria.**
1. `Ai/AbilityManagerTests` (content-independent: the scene builds its own caster types / abilities, or uses the shipped Cadre Mage,
   Sapper and Priest by id with the numbers read from `GameData`, never literals): (a) a Normal seat's Cadre Mage casts Telas Fire at a
   group of 3 enemies in range within 2 thinks of seeing them, through `UseAbility` (seen in the command log / `AbilityEvents`), and not
   at a lone enemy; (b) the Sapper casts the Cusser at an enemy building when no unit group qualifies; (c) the Priest's Sandstorm
   lands on the centroid, not on a point farther than `Range`; (d) `castMinTargets` edge (1 → a lone enemy is cast on; 50 → never);
   (e) a caster on cooldown is not re-ordered; a cast judged resolved by the cooldown move; `MaxTries` on a point that never lands
   (an unreachable island) stops the re-sends; (f) an Easy seat sends 0 `UseAbility` in 6,000 ticks while its Normal twin casts;
   (g) a `friendlyFire` ability (test-only data or the Cusser's own value) is never cast within `Radius` of an own unit.
2. `PlayerViewArchitectureTests` green with the new accessor(s); `PlayerViewOracleFuzzTests` covers it (an enemy slot leaks nothing).
3. `AiDeterminismTests`: a casting match (both factions, Normal, seeds 1 / 7 / 12, 4,000 ticks): hash twin equal; a recorded AI match
   with casts plays back (`AiReplayTests` row).
4. `AiPerfTests`: 8 Hard seats with casters in range of enemies, under 1 ms a think, `AllocationProbe` 0 B; the per-caster memory
   itemised in docs/03 "Implementation (M5-4c)".
5. If System 2 shipped: `EconomySystem` test: a Hard seat's drop-offs over 100 trips credit exactly `floor(1.25 × total)` ± 0 (the
   carry), a Normal seat exactly the cargo; `StateHashTests` covers the carries; `MilitaryManagerTests` rows for focus fire (the
   lowest-hp enemy gets the `Attack`; off → no `Attack` commands beyond today's) and pull-back (a member at 29 % hp gets the `Move`
   home, counts as no loss, is not re-sent); `AiContentTests` untouched (the data track's file). Golden `k` lines unmoved.
6. Build 0 warnings; the whole sim suite green, **Perf alone** (the machine was slow yesterday afternoon: run Perf alone first thing
   on the untouched base and record the count, then on your HEAD; file only what fails on HEAD and passes on the base); docs/03
   "Implementation (M5-4c)" (the manager's rule, the knob, the accessors, memory, cost, "Not yet"), docs/02 "AI opponent" untouched
   (the data track owns its wording this session), docs/01 one row; the dev line count stated (budget 1,500; the System 1 hard stop at
   700).

**Design references.** docs/02 "AI opponent" (Easy doesn't use abilities; Normal uses abilities; Hard: focus fire, pulls back wounded,
good ability use, 25 % gather bonus); docs/03 "AI architecture" (manager order; `AbilityManager` "when and where to cast (simple
heuristics per ability kind)"), "Implementation (M5-2)" (the executor's judge-next-think pattern, `ICommandSink`), "Implementation
(M5-3)" (`MaxTries`, the rally, BUG-0596's leaving-alive rule), "Implementation (M5-4b)" (think order, `Board.Defending`, `IsFleeing`,
the knob pattern in `rules.json` `ai` + `ContentHash`); "Abilities, statuses, zones" (`UseAbility` validation, the walk-in, cooldown
at the resolve, `AbilityEvent.Dropped`).

**Tests required.** Above; plus a `CommandDoorFuzzStressTests` row only if a `Command` kind is added (none planned).

**Constraints.** No Godot; no wall clock / `System.Random`; no per-think allocation; no AI file but `PlayerView.cs` names `World` or
a store (the architecture scan); the knob in data, structural rules in code; no `ui.json` / `ai.json` / `docs/factions/` /
`Content/` / `QA/Content/` edits (data track); the golden's `k` lines may not move (no AI seat in it), its header once at most
(`castMinTargets`), with the reason in the commit; one system first, rule 20.

### QA focus (sim)
Attack the manager's targeting: a caster with enemies exactly at `Range + Radius`, enemies straddling the radius, a centroid that
falls on a cliff / unreachable cell (the walk-in gives up: no endless re-sends), 3-4 seats all casting at each other, a storm
already on the point (double-casting the same ground: wasteful but legal; is there a re-cast loop?), own units inside the Cusser's
radius (criterion 1g), a defeated seat (silent), the hash twin with casts under `--no-combat`-like scenes, `castMinTargets` 1 / 50,
Perf alone before filing. If System 2 shipped: gather totals at the 25 % rate over long runs (the carry never drifts), focus fire vs a
unit that dies the same tick (the kept target stale: is a dead handle re-attacked?), pull-back of a unit already home.

---

## View track

### Current session plan: BUG-0674 + BUG-0680 (+ BUG-0681) first, then M6-V4 · feature · QA standard

**Goal.** Make `main`'s scene loop green again (two small test / reader fixes), then put the match's end on screen: a Victory /
Defeat / Draw banner from the sim's defeat rule and a post-match stats screen from what the sim counts today. Advances M6 criterion 2.

**Scope.**
- **BUG-0674** (`game/tests/AiSeatTest.cs`, `MatchOverLine`): the row rewritten for the real rule: round 1 cancels player 0's only site
  → one `DefeatEvent` for player 0 (the dev line once); round 2's `Build` is **dropped** (0 buildings, no second line) — or keep a
  second building standing; whichever, the row asserts the sim's rule as docs/03 "Implementation (M5-4b)" states it.
- **BUG-0680** (`GameSettings.LineShape`): cap the integer digit run (e.g. `-?\d{1,18}`; a longer run only with a fraction or exponent),
  so the 20-digit file takes the default with one warning and **no engine ERROR**; QA's two `QaM6V6Test` rows turn from FAIL to Check.
- **BUG-0681** if a few lines: `SettingsApplier.Apply` skips the window part (size / mode / position) when an engine window flag was
  given (`--resolution`, `--fullscreen`, `-f`, `--maximized`, `--position`, `-w` in `OS.GetCmdlineArgs()`); docs/03 "Implementation
  (M6-V6)" beside the boot order. Skip it (say so) if it's more.
- **M6-V4, the banner.** `Match` watches `World.DefeatEvents` / `World.IsDefeated(local)` / `Simulation.MatchOver` each tick (between
  ticks, read-only). The local player is slot 0. **Defeat** the moment the local player is defeated (even with others still standing);
  **Victory** when `MatchOver` and the local player is not defeated; **Draw** when `MatchOver` and nobody stands. One banner per match
  (latched). It opens **through the pause path** (the tree paused exactly as the pause menu does: BUG-0684 item 3 watch-out), on its
  own layer above the HUD, with three buttons: **Stats**, **Main menu**, **Continue watching** (unpauses and hides the banner for the
  rest of the match; Esc then is the pause menu as before; no second banner). Titles and buttons from `ui.json` (new `match_over`
  section; e.g. `victory`, `defeat`, `draw`, `stats`, `menu`, `continue`, and the stats labels). The "Match over:" dev line is now
  driven by `DefeatEvents` (one line per event, same text) — and `ViewApi/MatchOver` + `ViewApi/MatchOverTests` are **deleted** (the
  sim's rule replaces the stand-in) unless keeping them is cheaper; say which.
- **M6-V4, the stats screen** (from the banner's Stats; Back returns to the banner; also reachable from the pause menu once the banner
  has shown): one row per seat: colour swatch + faction `DisplayName` + "You" / the difficulty name (`ui.json` `setup.difficulty_*`),
  then **the columns the sim has today**: Kills, Losses (`World.Kills` / `Losses`), gold and wood **in hand** (`World.Gold` / `Wood`,
  labelled as such, not "gathered"), and the match length (`TickNumber` × 50 ms as m:ss) in the header; a "Defeated at m:ss" cell from
  the `DefeatEvents` the view saw. Every label from `ui.json`. **Requests 39 for the sim track** (write the exact list in your report;
  the Producer records it): per-player **gathered** gold / wood totals, units trained, units lost / killed split from buildings, buildings
  built / lost, time to Age II, and an army-value sample every N s — docs/02 "Game flow" 5. Lay the table out so those columns drop in
  as rows of `ui.json` labels (no code per column beyond a getter).
- **Tests.** `MatchOverViewTest.tscn` (headless, in the loop): a scripted 2-seat match where the test seam razes the enemy's buildings
  → the Victory banner (tree paused, title from `ui.json`, Stats shows 2 rows with the right Kills / Losses, Continue unpauses and no
  second banner, Main menu returns to the menu); the local hall razed → Defeat; a 3-seat run where the local player falls first →
  Defeat before `MatchOver`; `ShellTest` rows for the new `ui.json` keys (each required by name). `AiSeatTest`'s rewritten row. Windowed
  screenshots of the banner and the stats screen, looked at (acceptance 3 of the DoD).
- OUT: 2-4 slots / teams (M6-V5), keybinds, save / load, the army-value graph (after Requests 39), any `sim/` edit outside `ViewApi/`
  (read-only only), `game/data/factions/**`, `docs/factions/**`.

**Acceptance criteria.**
1. `tools/qa/scene-loop.ps1 -SkipPlayable` is **green on your branch**: `AiSeatTest` PASS, `QaM6V6Test` PASS (the two BUG-0680 rows are
   Checks now, not FAIL / KNOWN), every other scene as before.
2. `MatchOverViewTest`: the three outcomes (Victory / Defeat / Draw or the 3-seat early Defeat) each shown once, the tree paused while
   the banner is up, Continue resumes (`TickNumber` advances) with no second banner, Stats shows one row per seat with Kills / Losses
   equal to `World.Kills` / `Losses` and the length / defeat times right to the second; Main menu reaches the menu with the match
   freed (no leaked nodes: the existing exit checks).
3. Every player-facing string in `ui.json` (`match_over.*`); `ShellTest` requires each key by name; no C# literal (QA's no-literal row).
4. The "Match over:" dev line count equals the number of `DefeatEvents` seen (AiSeatTest's row), `ViewApi/MatchOver` deleted or kept
   with the reason.
5. BUG-0680 verified by QA's rows; BUG-0674 by the loop; BUG-0681 fixed or deferred with the reason.
6. Build 0 warnings, smoke PASS (both passes), sim non-Perf green (no `sim/` change outside `ViewApi/` / its tests), docs/03
   "Implementation (M6-V4)" (the banner's rule: local = slot 0, Defeat-first, pause path, latch; the stats columns and their source;
   Requests 39's list), docs/02 "Game flow" 4-5 unchanged (or an amendment stating what is shown today), docs/01 one row; two
   screenshots in the report; the dev `game/` line count stated (budget 1,200; a bigger test file is fine, say so).

**Design references.** docs/02 "Game flow" 4 (a player is defeated with no buildings left; the last standing wins) and 5 (the
post-match stats list); docs/03 "Implementation (M5-4b)" → "For the view (M6-V4)" and "Behaviour to know"; "Implementation (M6-V1)"
(the shell, `PauseMenu`'s paused path), "Implementation (M6-V6)" (layer 21, `CommitSettings`, the `user://` per-process test folder
rule from BUG-0684 item 1); CLAUDE.md rules 7 and 8.

**Constraints.** Views hold no gameplay state (the banner latch and the seen defeat times are presentation state, fine); nothing in
`sim/` but read-only `ViewApi/` code; text only from `ui.json`; the banner never issues a sim command; tests use a per-process
`user://` folder; a QA row for a bug left open prints `KNOWN BUG-nnnn` (rule 19).

### QA focus (view)
Attack the banner's timing: a defeat on the very tick the player clicks (orders dropped, no exception), two players falling on one
tick (Draw, one banner), the local player falling first in a 3-seat match (Defeat, the match goes on: Continue works, the HUD's clicks
do nothing and the ghost says "Defeated"), the banner while the pause menu is open (and the reverse), Settings opened from the pause
menu after a banner, Main menu from the banner then a second skirmish (no stale banner / stats), the stats numbers against the sim's
spans at the moment of the banner and again after Continue (do they keep moving? they should, the match runs), the dev line count vs
`DefeatEvents` under `--no-bases`. BUG-0680: 19 / 20 / 64-digit runs, a fraction after 20 digits, an exponent; BUG-0681: the bench's
viewport with and without the flag.

---

## Data track

### Current session plan: D17 · feature · QA light

**Goal.** Make the design wording say what the AI does: docs/02's Normal line states outright that Normal doesn't focus fire or pull
back wounded (today it is only implied by "fair play at a relaxed pace", which the K pin leans on), the faction pages' "Defense" lines
match M5-4b's shipped rule, and an **AI spending report** measures the unspent gold the D15 note flagged, as a proposal for the owner.
No number changes this session.

**Scope.**
- **docs/02 "AI opponent"**, the Normal bullet: add "doesn't focus fire or pull back wounded" (a Producer default, docs/01 row:
  "Producer decision, owner may revisit"); `Content/AiContentTests.DesignStates` pins Normal's two flags on **that** phrase instead of
  "fair play at a relaxed pace" (K row; the K theory's existing Normal mutations stay green). Keep Easy's "doesn't micro".
- **`docs/factions/malazan.md` "Defense"** (line ~166) and **`whirlwind.md` "Defense"** (line ~169), reworded to M5-4b's rule (docs/03
  "Implementation (M5-4b)" → Defense): the army comes home when a building near the hall is hit or an enemy soldier is seen within the
  defend radius; workers near the threat flee to the **nearest finished drop-off away from the threat** (the Keep, or a Storehouse);
  the Whirlwind's "counter-attack the enemy base with Horse Raiders when its army is away" is **not built**: keep it as a one-line
  "later idea (not in M5)" or drop it, and say which in the report. No test pins these prose lines; none needed.
- **The spending report** (`Content/AiSpendingReportTests`, report rows, non-gating like `AiHouseRuleReportTests`): both factions,
  Normal, the house report's flat scene, 20 minutes (24,000 ticks): gold and wood in hand at 5 / 10 / 15 / 20 min, the share of
  ticks each hall / production building had an empty queue, army half-pop at those marks, waves started. Print as a table in the test
  output and copy it into the report; **a proposal** (which `ai.json` step or composition change would spend it, or "keep as is")
  under For your review for the owner; **no `ai.json` change this session** (the sim's M5-4c changes the AI beside you; re-measure
  after it lands).
- OUT: any `ai.json` / `units.json` / `buildings.json` value; `docs/02` beyond the Normal bullet; the Teblor's `ai.json` (M7); C#
  outside `sim/Rts.Sim.Tests/Content/` and `QA/Content/`.

**Acceptance criteria.**
1. docs/02's Normal bullet carries the new phrase; `AiContentTests.K_Docs02_StatesWhatThePinsHold` and
   `K_BothFactions_HoldDocs02sNumbers` green with `DesignStates` Normal rows on the new phrase; the K mutation theory's three Normal rows
   still fail naming the field (run them).
2. Both pages' "Defense" lines describe M5-4b's rule (the Producer reads them against docs/03); the Whirlwind counter-raid marked as
   not built or removed; the report says which.
3. `AiSpendingReportTests` prints the table for both factions (20 min, Normal), runs under 2 minutes total, asserts only preconditions
   (the seat is an AI of the faction, the match ran 24,000 ticks); the numbers and the proposal are in the report, dated.
4. `Content` + `QA/Content` slices green; no data / golden change (`git diff --stat -- game/data` empty; `data-hash` 72EF431E70C49C39);
   docs/01 one row for the Normal wording; the line count stated (budget 400).

**Design references.** docs/02 "AI opponent" (Easy / Normal / Hard bullets); docs/03 "Implementation (M5-4b)" → Defense and
"Behaviour to know"; docs/03 "Implementation (M5-3)" (waves, the rally); `AiHouseRuleReportTests` (the report pattern, BUG-0641's
reads through `AiPlayer`'s public surface).

**Tests required.** The K rows as above; `AiSpendingReportTests`. **Sim-track tests that pin a value this task changes: none** (no
value changes). The sim's M5-4c writes its tests content-independent and does not touch `Content/` or `docs/02` (its brief says so);
you don't touch `rules.json` or `ai.json`.

**Constraints.** Files: `docs/02-game-design.md` (the Normal bullet only), `docs/factions/malazan.md` + `whirlwind.md`,
`sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, docs/01 (one appended row), `studio/bugs/README.md` (append your rows only). The
spending report reads the AI only through its public surface (BUG-0641's rule).

### QA focus (data)
The K pin after the rewording (a docs/02 edit that drops the new phrase fails by name; the old phrase gone from `DesignStates`), the
pages' Defense lines against docs/03 word for word (nearest drop-off *away from the threat*, not "the Keep" alone), the report's
preconditions (an AI seat of the right faction; the match really ran 24,000 ticks; no exception), its runtime beside the other suites.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing. The PC ran at its base clock yesterday
  afternoon; the sim dev measures Perf alone on the base first.
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge). docs/01: one row
  per track, appended.
- Merge order sim → view → data. The golden's `k` lines may not move this session; its header may move once (the sim's
  `castMinTargets`), with the reason in the commit. The data track changes no data file; the view none.
- Shared files this session: `ui.json` is the **view's** (the sim adds no text); `docs/02` "AI opponent" is the **data track's** (the
  sim doesn't edit docs/02); `docs/03` gets one new section per track (sim "Implementation (M5-4c)", view "Implementation (M6-V4)").
- Rules 17-20 above.
