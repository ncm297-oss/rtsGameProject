# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-10-13-0146** (first full session of 2026-10-13, 1 / 8; all three tracks
ACCEPT: sim M5-4a, view M6-V3, data D-H4). Counters after it: **sim 4 / 4, view 4 / 4, data 0 / 4**: the next session is the
**sim's and the view's hardening** (M5-H1, M6-VH1) and the data track's **D16** (a feature). Bug ids next session: **sim from
BUG-0650, view from BUG-0660, data from BUG-0670.** Inbox: empty at this ACCEPT (no owner answers on D13 / D14 / D11 / D12 / D15 yet).

## Where we are

- `main` after this integration (sim 5b366d2 → view 4f9a2fe → data ac58524; the conductor union-merges `studio/bugs/README.md` and
  `studio/qa/coverage.md`, no code conflict): the Producer's scratch merge of all three built with 0 warnings and ran the AI /
  content / ViewApi / map / replay slices, smoke and the two new scenes (the session log's "Merged tree"). Golden
  `cross_map_seed1.replay` is **format 6** (five `map.start-*` header lines), `data-hash` **8409DB7517579FEF** unchanged, every `k`
  line unchanged (seed 1's first layout fits the start pair and the start search draws no RNG).
- **The game from the menu now:** Skirmish → Start opens with a Town Hall and 5 workers a side, each base on a generated start
  location with 2 gold mines (2,500) within 18 m and a forest within 24 m, player 1's base the point mirror of player 0's; the
  AI gathers from the first minute and scouts at 15 s; each slot has a colour (8 in `ui.json`); `--skirmish` = the menu's Start
  (slot 2 AI Normal, no army). Still missing for a real game: the sim's defeat rule (Requests 37, M5-4b's first item), the AI's
  defense / expansion / abilities / rebuilding, victory / defeat on screen (M6-V4).
- **M5 1 / 6, criteria 2 and 3 advanced; M6 criterion 1 advanced (colour done; 2-4 slots and team left) and criterion 7's generator
  half advanced.** No open S1 / S2. Open S3: sim BUG-0598, 0620, 0621, 0640 (filed by the data QA against the AI), 0511, 0512,
  0461's movement half; view BUG-0603, 0632, 0604 (closes with the probe flip below); others S4.

## Standing rules (1-16)

1-15 as in the 1930 / 0146 PLAN handoffs (full suite incl. Perf alone for sim rule changes; a new `Command` kind into
`CommandDoorFuzzStressTests`; per-slot memory itemised; agents' scratch folders `<track>-<session>-<purpose>` with absolute paths; a
data brief names every sim-track test that pins a value it changes; bug statuses: dev proposes, QA sets what it verifies, the
Producer the rest; content-independent sim tests; `git checkout HEAD -- path`; the budget is a line; **one system per brief**, the
dev's count excludes QA's files and is reported).
16. **Agents never kill processes by image name** (`taskkill /IM`, `Stop-Process -Name`, `pkill`): only a PID the agent itself
    started, and only its own Godot / dotnet children. The data QA ran `taskkill /F /IM python.exe` this session and killed two
    python.exe processes machine-wide (recorded in the session log and For your review). A stuck tool is reported, not swept.

---

## Sim track

### Current session plan: M5-H1, the sim's hardening (counter 4 / 4) · hardening · QA full · dev ≤ 1,200 lines (QA outside)

**Goal.** Work the sim's debt before M5-4b: the AI's two stalls (a frozen wave, BUG-0598; house sites never finished, BUG-0640),
the two start-location gaps QA found in M5-4a (BUG-0620, BUG-0621), then the older movement S3s. In priority order; stop at the
budget and report what's left.

**Scope (in this order).**
1. **BUG-0598 (S3):** a wave whose members all stand by a target none can strike ends: when no member has been ordered or made
   progress for `ai.waveCooldown` ticks the wave goes home (cooldown), or the "crowded" branch stays only while some member is
   `Attacking` the target. Un-skip `QA/AiMilitaryQaTests.AWaveWhoseMembersAllStandByATargetNoneCanStrike_DoesNotStayForever`.
2. **BUG-0640 (S3, from the data's D-H4 report):** on M5-3's AI two Malazan Billet sites are placed and never finished (cap 82 at
   30 min, was 98). Root-cause first (QA's suspicion: the scout / rebalance retasks the builder); then a resume path: an own
   `UnderConstruction` site with no builder for N thinks gets an idle worker's `Repair` (join), and `EconomyManager`'s house rule
   is not switched off by a site that nobody builds. Prove it on `AiHouseRuleReportTests`' Malazan 36,000-tick row (11 placed →
   11 finished; the data track's file: read it, don't edit it; the report's numbers go in your report).
3. **BUG-0620 (S3):** `MapGenParams.Validate` requires `max(lo, StartSiteRadius + 2) <= min(Width / 2 - 1 - StartSiteRadius,
   Height - 3 - StartSiteRadius)` (refused by name), so every validated set generates and a replay header that validates always
   plays or is refused; un-skip QA's `EveryValidatedStartParamSet_Generates` and
   `Bug0620_ReplayHeaderThatValidatesButCannotFit_IsRefusedNotThrown`, drop the two `_Pinned` rows; docs/03's "Known gap" line goes.
4. **BUG-0621 (S3, Producer ruling in the bug file):** `FindStarts` requires every position's `Level` equal and the centres at
   least half the smaller map side apart; a layout with no such centre is redrawn like one without room. Re-run QA's survey
   (`Survey_StartLevelsAndSeparation_Seeds1To100`) and report the new flat-fallback rate per size (don't tune the generator to
   it). **The golden may move** (seed 1's player 0 is on level 1 today): regenerate once, say so in the commit; `data-hash` must not move.
5. **BUG-0511 (S3):** `OwnFighterAhead` takes only a blocker inside the caster's swept corridor (lateral offset ≤ the two radii +
   slack) on its level or a legal step away; un-skip QA's `AnOwnFighterBesideTheCaster_NotInItsWay_DoesNotHoldTheStuckCount`.
6. **BUG-0512 (S3):** the ordered-Attack give-up tests the goal field's cost at the unit's cell whatever its state; un-skip QA's
   `FiveHundredUnits_OrderedOntoAReachableBuildingThroughARampDetour_NeverGiveUp_ZeroBytes`.
7. If under budget, S4s most valuable first: BUG-0594 items 1-2, BUG-0622 items 1 / 2 / 4, BUG-0541 item 2, BUG-0573 item 1,
   BUG-0542's gated row, BUG-0513.
8. docs/03 lines for each fix (the M5-4a "Known gap" and BUG-0621 sentences replaced by the rule); docs/01 row only for item 4.

**Out:** Requests 37 (`IsDefeated` / `MatchOver`: M5-4b's first item, a feature), defense / expansion / abilities / difficulty
knobs (M5-4b), any `game/data/factions/**` edit, any view file (`StartBase.cs` included: the view's hardening doesn't touch it either).

**Acceptance criteria.** 1. Each fixed bug's QA row un-skipped and green, with a regression test that failed first. 2. BUG-0640:
the Malazan 36,000-tick report row finishes every placed house (numbers in the report). 3. BUG-0621: the survey shows 0 / 100
different-level pairs on every size, the fallback rates reported. 4. Golden: `k` lines moved at most once (item 4), `data-hash`
unchanged, reproduced twice. 5. Full non-Perf green, Perf alone green, smoke PASS; 0 warnings. 6. Statuses: dev proposes, QA sets
what it verifies. 7. The dev's count ≤ 1,200 reported.

**Design references.** docs/02 "AI opponent" (behaviours: rebuild losses), "Map and terrain" (symmetric layouts); docs/03
"Implementation (M5-3)" (`MaxTries`, the wave), "Implementation (M5-4a)" (the start rule), "Map generation"; each bug file's
Producer triage.

**QA focus (full).** Waves against fenced targets (trees two deep, a 2-tower variant); the AI's house sites over 30 minutes on both
factions with a killed builder; `Validate` × `Generate` sweep green across 444+ sets; the start survey seeds 1-100 × 3 sizes;
the golden twice from a clean clone; 500-unit ordered attacks through a ramp detour at 0 bytes.

### Watch-outs (sim)
- Item 4 changes which centre seed 1 picks: `CommandCardHashTwinTests` (seed 2 since M5-4a) and `PropsHashTwinTests` may need
  re-checking; the production / research Perf scenes stay pinned to `StartLocations` 0.
- The data track's D16 pins `ai.json` difficulty numbers the same session: don't move any `difficulties` value.

---

## View track

### Current session plan: M6-VH1, the view's hardening (counter 4 / 4) · hardening · QA standard · dev ≤ 1,000 lines (QA outside)

**Goal.** Close what M6-V3 left open now that the sim's start locations are on `main`, fix the colour table's legibility gap,
and the sandbox flake. In priority order; stop at the budget.

**Scope (in this order).**
1. **The start-location probes → Checks (BUG-0604 closes):** `AiSeatTest.CheckAiActed(gathering: true)` becomes
   `Check(working > 0, ...)` (on the merged tree the Producer saw the menu match's AI gathering by tick 400: numbers in the session
   log); `QaM6V3Test.MenuOpenings`' mine-in-sight print becomes a Check that every hall sees a mine; BUG-0604 → fixed.
2. **BUG-0632 (S3), Producer decision: data first, then a guard.** Retune `ui.json` `setup.colour_3` Crimson brighter and move
   Iron or Jade so every table colour's corpse fill has luminance ≥ 0.2 and every pair is ≥ 0.25 apart (BUG-0160's rules), names
   unchanged; then a `ShellTest` row that applies the corpse rules to all 8 colours and 28 pairs so a future recolour fails by
   name; flip `QaM6V3Test.CorpseRulesOverTable`'s probes to Checks. Quote the new RGB values in the report (owner review).
3. **BUG-0631 item 3, Producer decision:** a slot can't pick a colour the other slot holds: the setup greys the taken colour in
   the other list (and moves the pick to the first free colour when the lists collide); `--colour` with both slots on one colour
   warns and keeps slot 2's default. docs/02 "Game flow" 2: "colours are unique per match"; docs/01 row.
4. **BUG-0603 (S3):** `M4PlayableTest` seed 6 step 7's flake: pull the army back once the enemy army is gone, or check sight from
   a unit kept at the camp; replay `m4playable-seed6-fail-tick4094.replay`; 10 seed-runs under load.
5. If under budget: BUG-0550 item 1 (`GroundDiscPool.Take` reclaims stale nodes), BUG-0521 items 2-3, BUG-0581 item 3 (process:
   just close it), BUG-0631 item 1's `#` nit.
6. docs/03 "Implementation (M6-V3)" lines for each change (the probes, the table rule, unique colours); `ShellTest` rows.

**Out:** the match-over banner / stats screen (M6-V4: waits on the sim's Requests 37), 2-4 slots and teams (M6-V5), any sim file
(`ViewApi/` included), the look test, export.

**Acceptance criteria.** 1. `AiSeatTest` and `QaM6V3Test` pass with the probes as Checks (AI gathering by tick 400 from the menu;
36 / 36 halls see a mine). 2. The 8 colours and 28 pairs meet the corpse rules in a `ShellTest` row; `CombatViewTest` green with
slot 1 drawn in each table colour (QA sweeps). 3. Duplicate picks refused in the setup and `--colour`; `ShellTest` rows. 4.
BUG-0603's scene 10 / 10 seed-runs under another suite's load. 5. Scene loop green; smoke PASS both passes; 0 warnings; docs; the
dev's count ≤ 1,000 reported.

**Design references.** docs/02 "Game flow" 2; docs/03 "Implementation (M6-V3)", BUG-0160 item 3's corpse rules (docs/03 "Combat
views"); BUG-0604 / 0632 / 0631 / 0603 files.

**QA focus (standard).** Menu openings on 6 seeds × 3 sizes with the AI gathering; `CombatViewTest` with every table colour on
slot 1 and the 28 pairs; junk `--colour` with duplicates; 10 `M4PlayableTest` seed-runs beside the sim suite.

---

## Data track

### Current session plan: D16, the difficulty blocks pinned to docs/02 and the pages · feature · QA light · ~350 lines incl. QA

**Goal.** M5 criterion 4 (Easy / Normal / Hard per docs/02 "AI opponent") needs the three difficulty blocks in both `ai.json`
files documented and pinned: today nothing checks them against docs/02 (only the house report reads `ThinkTicks`). Inbox answers
first if any arrived.

**Scope.**
1. **Inbox answers first** (D13 / D14 / D11 / D12 / D15), each as a data edit + page pin + one golden header regen (`data-hash`
   only; every `k` line equal, asserted).
2. **A "Difficulty" table on both faction pages** (`docs/factions/malazan.md`, `whirlwind.md`, after "AI build order (Normal)"):
   rows Easy / Normal / Hard; columns thinks every (s), build-order slack (s), wave size ×, gather bonus, focus fire, pulls back
   wounded, uses abilities, expands after (s), filled from the shipped `ai.json` (no number change).
3. **Pins** (`Content/AiContentTests`, new rows J-K): (a) each page cell equals its `ai.json` field both ways (a one-line page
   edit fails by faction / difficulty / column); (b) docs/02's stated numbers hold for both factions: Easy thinks every 2 s, Hard
   every 0.5 s, Hard `gatherBonus` 0.25, Hard `focusFire` and `pullBackWounded` true and Easy / Normal false, Easy `useAbilities`
   false and Normal / Hard true; a mutation of any fails naming the field.
4. **docs/02 "AI opponent" Easy wording, Producer default (BUG-0541 item 3):** "rarely uses abilities" → "doesn't use abilities"
   (the schema's `useAbilities` is a bool; a rate would be a new field nobody asked for). One docs/02 line + one docs/01 change-log
   row ("Producer decision, owner may revisit"). BUG-0541 item 3 → fixed in your report (the Producer sets the status).
5. **BUG-0641 (S4):** the report's comment names every internal it reaches (`Simulation.AiOf`, `AiPlayer.Board`, `PendingOrder.*`,
   `PendingKind`); `thinkTicks` / `house` read from the AI (same reach as `Board`) or asserted equal once; the "workers" column
   wording.

**Out:** any `ai.json` or `units.json` number change without an inbox answer; `rules.json` (sim-owned); M7-M9 data.

**Acceptance criteria.** 1. Both pages carry the table; rows J-K green and each failing by name on a one-line edit (report the
message). 2. docs/02's Easy line reads "doesn't use abilities"; docs/01 row. 3. No `sim/Rts.Sim/**` or `game/data/**` change
(unless an inbox answer); no golden change; build 0 warnings; `Content` + `QA/Content` green and the full non-Perf suite green
with the sim's and the view's branches merged first. 4. The review table for the owner: the two pages' new tables quoted.

**Design references.** docs/02 "AI opponent"; docs/03 "For the data track (M5-1)" (the eight difficulty fields and their units:
`thinkInterval` / `buildOrderSlack` / `expandAfter` in seconds, converted to ticks at load); `studio/bugs/BUG-0541` item 3, `BUG-0641`.

**Tests required.** `Content/AiContentTests` J-K; the report rows still green.

**Constraints.** `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, `docs/factions/**`, plus one docs/02 line and one docs/01 row;
no sim-track test pins a difficulty value today (the sim's hardening won't move them: told in its brief).

**QA focus (light).** Each pin fails by name on a one-line edit of either page or either `ai.json` difficulty block.

---

## Watch-outs (all tracks)

- Three suites at once: wall-clock Perf rows fail under load; rerun alone before filing.
- `studio/bugs/README.md` and `studio/qa/coverage.md`: each track appends only its own rows (unions at the merge).
- **Merge order sim → view → data.** The sim's BUG-0621 fix may regenerate the golden (`k` lines, header unchanged); nobody else
  touches `Replays/`. The view edits `ui.json` (colours) and `game/tests/**`; the data track edits docs/02 one line and docs/01 one row.
- Rule 16 above: no process killing by name.
