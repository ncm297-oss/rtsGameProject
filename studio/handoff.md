# Handoff: brief for the current session

Written by the Producer at the PLAN of session **2026-11-07-0926** (session 2 / 8 on 2026-11-07; base `e1750ce` = `origin/main`,
clean). Inbox **New: empty**. Build on `main` 0 warnings / 0 errors; the AI / Content / Data / Replay / Save subsets **2,072 / 0 / 0** in 16 m 26 s on a
scratch clone (rule 38). **Open S1 / S2: none** (S3 20, S4 17). Counters before: **sim 3 / 4 (feature), view 0 / 4, data 1 / 4.** Bug ids:
**sim from 1120, view from 1127, data from 1137.** Save formats **6 / 7**, replay format unchanged, golden **30C51BB619A7E650**.

## Standing rules (1-41)

Unchanged (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content and
QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit `docs/factions/**`
for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief names the allowance and
the data task that touches it re-measures on the sim's branch, or the two are not planned in the same session). **This session:** the
sim's Hard knobs must leave every Normal / Easy / human measurement bit-identical (criterion 6 below), the data's D-R34 touches prose
only, and **the sim does not edit `docs/factions/**` this session** (D-R34 owns both pages): no rule-41 overlap.

## Watch-outs (all tracks)
- Three test hosts at once: wall-clock Perf and scene rows flake under load; rerun alone before filing. A Perf failure counts only
  when it fails alone.
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
  `game/data/common/ui.json` is the **view's** this session (docs/03: view-only text); the sim does not touch it.
- A developer must never kill another track's Godot or `testhost` process. The scene loop's own timeout handles a hung scene.
- Sessions longer than 3 h: the conductor refreshes `studio/.session.lock` at each phase. A sim fix round costs ~45 minutes of suite.
- **Pinned matches (Normal seats; must NOT move this session):** `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithin36000Ticks`
  (Pale Hills seed 1 at 34,219, Vathar at 22,794), `MapSlice2StressTests.KnownStalls` (Pale Hills 3 / 4 / 6 at 29,562, 5 at 55,631,
  generated 6 at 26,034), `StandardPresetPinQaTests` (7 digests), the Malazan / Whirlwind page tables, `D21OpeningsQaTests` /
  `DH6ThresholdQaTests` and their `KnownBeatenAlive` lists, the golden header. A Hard-seat row that moves is listed with why.
- **Known stalls on Pale Hills seed 2** (both skipped with ids): `MapSlice2StressTests.NormalVersusNormal_PaleHillsSeed2_Stalemate_...`
  (BUG-1068) and `HuntStressTests.FourAiSeats_OnPaleHills_Seed2_KnownStall` (BUG-1116 / 1068 / 1119). The sim's hardening (next
  session) re-runs both after BUG-1119 and the lone-idle-worker hunt gap. **BUG-1119 is not a rider this session** (its fix moves
  Normal 4-seat pins and would blur criterion 6).

---

## Sim track

### Current session plan: M5-4c slice 2, the Hard knobs · feature (counter 3 / 4 → 4 / 4) · QA full

**Goal.** M5 criterion 4 (Easy / Normal / Hard per docs/02 "AI opponent"): make the three Hard knobs the data already carries do
something: `gatherBonus` (Hard gathers 25% faster), `focusFire` (Hard concentrates its fire), `pullBackWounded` (Hard pulls wounded
units out of the fight). After this slice the only thing left on criterion 4 is the view's "shown in the skirmish setup" (the view
track builds it this same session) and the ACCEPT tick. Design: docs/02 lines 411-417; the knobs' schema in docs/03 "Implementation
(M5-2)" (`ai.json` `difficulties.*`, `DataLoader.Ai.cs`). ~800-1,000 hand-written lines (tests included), one slice.

**Scope (read docs/02 "AI opponent", docs/03 "Implementation (M5-2)" / "(M5-3)" / "(M5-4f)" / "(M5-4d slice 1 and 2)" first).**
1. **`gatherBonus`.** Gathering accrues in `EconomySystem` as `GatherProgress += rules.GoldPerTick | WoodPerTick` per tick. Give the
   world a per-player gather rate pair derived at construction and at load from the seat (`PlayerSeat.Difficulty` + the faction's
   `AiDef.Difficulties[d].GatherBonus`): `rate × (1 + bonus)` for an AI seat, `rate × 1` otherwise (bit-identical to today for a
   human or a 0 bonus: `x * 1f == x`). **Derived, not saved, not hashed**: no save-format bump, no new `AIST` field. Workers of a
   Hard seat fill their carry 25% sooner; nothing else (carry size, trip, drop-off) changes.
2. **`focusFire`.** When a Hard seat's wave (or hunt, or defense answer: wherever members fight enemy *units*) has enemy units in
   sight within a reach knob (`rules.json` `ai.focusReach`, m, hashed, validated, `DataValidationTests` rows), the wave picks **one**
   focus target each think: the seen enemy unit with the lowest `Hp` in reach, ties by handle index; members not already ordered
   on it get `Attack(target)`; when it dies or leaves sight the next is picked; with none in reach the existing rules apply
   unchanged (`AttackMove` to the target building). Respect the order-churn guard (BUG-0595 / 0596: a member's orders per target
   are bounded) so a Hard wave does not spam `Attack`. Normal / Easy: not one command different.
3. **`pullBackWounded`.** A Hard seat's wave member below `rules.json` `ai.pullBackHpFraction` (a fraction, hashed, validated, rows;
   default 0.3 unless the dev measures a better value and says why) is ordered `Move` to the rally point and leaves the wave (as
   M5-3's "a member that leaves alive is not a loss"); the wave's start threshold is re-checked as today. Nothing heals in the game
   yet, so there is no return timer: a unit below the fraction is **not sent on a new wave** (it stays home and still answers the
   defense), the wave-start count and cost exclude it, and a wave of only wounded units never starts. The dev may choose the
   simpler "never a member while below the fraction" form if it yields the same observable behaviour; say which in docs/03.
4. **Docs:** docs/02's difficulty bullets stay true (no text change needed unless a number moves); docs/03 "Implementation
   (M5-4c slice 2)": the three rules, the knobs, the measurements (think cost, the Hard v Normal result on Pale Hills seed 1 and
   Raraku seed 1), what moved; the "Not yet" lines in "(M5-4b)" / "(M5-4c)" that say the knobs are unread → re-stated as done.
- **OUT:** BUG-1119 (the sim's hardening next session), the house rule (BUG-1116), expansion rebuild, any view change,
  `game/data/factions/**`, `game/data/common/ui.json`, `docs/factions/**` (D-R34 owns both pages this session; if a Hard-seat
  cell on a page is now wrong, list it in the report for the data track), the data's test folders.

**Acceptance criteria.**
1. Build 0 warnings; non-Perf green (the two known-stall rows stay skipped); Perf alone green; smoke PASS.
2. `gatherBonus`: a unit test shows a Hard seat's worker fills its carry in 1 / 1.25 of a Normal worker's ticks (the exact tick
   counts asserted); a human and a Normal seat's worker tick-for-tick identical to today's values.
3. `focusFire`: built-scene tests by the command log: a Hard wave meeting three enemy units with different hp sends `Attack` on
   the lowest-hp one from every member in reach and re-targets when it dies; the same scene with Normal sends no `Attack` on a
   unit the old rules would not have (command log identical to the pre-slice code on that scene).
4. `pullBackWounded`: built-scene tests: a Hard member dropping below the fraction gets `Move` to the rally and is out of the wave;
   it is not a member of the next wave while still below; a Normal member at the same hp is not pulled back; a wave whose only
   candidates are wounded does not start (and the AI does not stall: production goes on).
5. Determinism: two processes on a Hard v Hard match (Raraku seed 1 and Pale Hills seed 1, 20,000+ ticks) hash-equal every tick;
   a save mid-wave with a focus target and a pulled-back member reloads hash-equal 1,000 ticks later; formats 6 / 7 asserted
   unchanged; replay format unchanged.
6. **Nothing but Hard moved:** the golden header unchanged; every Normal / Easy pinned match and digest listed in the watch-out
   above unchanged (the dev runs `MapFileTests`, `MapSlice2StressTests.KnownStalls`, `StandardPresetPinQaTests`,
   `D21OpeningsQaTests`, `DH6ThresholdQaTests` and quotes the numbers); any Hard-seat row that moves (e.g. a Hard-seat fixture's
   post-load hash, a Hard pairing's end tick) regenerated in the same commit and listed with why.
7. Fog: the oracle (`PlayerViewOracleFuzzTests`' form) on a Hard seat's every think over a real match: the focus target is always a
   unit the seat sees at that think; `PlayerViewArchitectureTests` holds.
8. `AiPerfTests`: 8 Hard seats mid-wave with focus targets avg < 1 ms / think, 0 B allocated per think (no LINQ, no `new`, no
   closure in `MilitaryManager` / `EconomySystem` paths).
9. docs/02 true; docs/03 "Implementation (M5-4c slice 2)" written; `DataValidationTests` rows for the two new `rules.json` knobs
   (missing, negative, > 1 / NaN refused with the field path). **M5 criterion 4 ticks at the ACCEPT** (with the view's M5-V2).

### Then (sim)
- **The sim's hardening at 4 / 4** (the next session): BUG-1119 first, the lone-idle-worker hunt gap (`KnownStalls` seed 5's worker
  3 m past a sweep point's sight), BUG-1116's house rule re-measured with both in, BUG-1118 item 1, BUG-1058, BUG-1059, BUG-1044's
  rest, BUG-1067, BUG-1068's gold, the movement S3s BUG-0911 / 0889 / 0144, the `HuntRing` load range, `HasMapId`'s special case; then
  both Pale Hills seed-2 known stalls re-run. Then BUG-1010's lever, D14 c's schema (M5 criterion 9; the data track waits on it),
  M5-5's 20-minute test.

---

## View track

### Current session plan: M5-V2, show the Hard AI's gather bonus in the skirmish setup · feature (counter 0 / 4 → 1 / 4) · QA light

**Goal.** docs/02 "AI opponent": Hard has "a **25% gather bonus**, shown in the skirmish setup"; docs/03 "Implementation (M5-2)"
repeats it ("shown in the skirmish setup"). Today the setup's Difficulty list shows only the three names. Give each difficulty a
tooltip, text from data, with the bonus percent read from the loaded `ai.json` (never typed into C#), so M5 criterion 4 can tick
with the sim's slice. ~150-250 lines.

**Scope.**
- `game/data/common/ui.json` (view-owned text, docs/03 "ui.json"): `setup.difficulty_easy_tip` / `_normal_tip` / `_hard_tip`, one
  sentence each from docs/02 lines 412-417 in plain words (e.g. Easy "Thinks slowly, follows its build order late, attacks with
  small waves, expands late."; Normal "Fair play at a relaxed pace: follows its build order on time, uses abilities, no focus fire,
  no pulling back wounded units."; Hard "Fair play with tighter decisions: thinks every half second, focuses fire, pulls back
  wounded units, and gathers {0}% faster."). `{0}` is filled from `data.Ai[faction].Difficulties[hard].GatherBonus × 100` for the
  slot's faction (the two factions carry the same number today; if they ever differ the tooltip follows the slot's faction), formatted
  with `CultureInfo.InvariantCulture` and `0.##`; a 0 bonus shows the text without the clause only if the dev adds a second key
  (`difficulty_hard_tip_no_bonus`), else the "{0}%" with 0 is acceptable and said.
- `SkirmishSetup`: `SetItemTooltip` on each Difficulty `OptionButton` item (`TooltipWrap.Wrap`), and the control's own
  `TooltipText` following the selection as `ShowPresetTooltip` does for presets; re-applied when the slot's faction changes.
- A `game/tests` scene (`QaM5V2Test` or `DifficultyTipTest`) asserting the three tooltips equal the data text with the number from
  the loaded data, after a faction change too, and that the hard tip contains "25" on today's data; `UiText` missing-key path logs a
  `GD.PushError` as for every other key.
- **Optional rider, only if the fix is a few lines in `TooltipWrap.Wrap`:** BUG-1126 (a blank middle line when a space sits at a
  break). Otherwise leave it and say so.
- docs/03 "Implementation (M5-V2)" (short) and the docs/02 sentence stays as is (it is now true).
- **OUT:** Load Game / stats screens (they name the difficulty only), anything in `sim/**` (the view reads `GameData` it already
  loads; no ViewApi change expected), `game/data/factions/**`, any art or theme work (BUG-1115 item 4 stays with the art pass).

**Acceptance criteria.**
1. Build 0 warnings; smoke PASS; the new scene PASS; the view's loop unchanged otherwise (`SkirmishSetup`'s existing scenes PASS).
2. Hover each Difficulty item in the setup (and the control itself): the three tooltips show; the Hard one says the bonus from
   the data (25 today); change `gatherBonus` to 0.4 in a scratch copy of `ai.json` and the tooltip reads 40 (the scene proves it
   with a forged `GameData`, or the dev shows a screenshot of each).
3. No player-facing literal in C#: the only new strings in `game/scripts` are `ui.json` keys and format calls.
4. `git diff e1750ce -- sim game/data/factions` empty; the data hash unmoved (`ui.json` is outside it; the dev confirms with
   `DataContentHashTests`).

### Then (view)
- STOP (cheap) unless an owner item lands (the art call, ".NET 10 installed", templates dropped, sound notes). Debt: BUG-1126 if not
  the rider, BUG-1115 item 4 (the art pass).

---

## Data track

### Current session plan: D-R34, the prose round the re-printed cells + the seed-19 paragraph + BUG-1136 · feature (counter 1 / 4 → 2 / 4) · QA light

**Goal.** The sim's M5-4d slice 2 re-printed 12 cells of `docs/factions/malazan.md` and one of `whirlwind.md` (rule 40) and the prose
round them is stale (BUG-1118 item 3); the seed-19 paragraph got one Producer sentence at the merge and wants a proper re-write; D-R33's
nits (BUG-1136). ~100 lines, docs and the data's test folders only.
1. `docs/factions/malazan.md`: the prose near the "Age II against the Whirlwind" table and BUG-0982's table quotes 13:25 / 12:33 /
   "defeated 10:24" (line ~380: "seed 8 at 13:25, 14 at 12:33, 23 at 15:36"; lines ~550 and ~569-570) where the cells now read 13:50 /
   15:03 / "never"; re-word to the cells (the cells themselves are the sim's, keep them). The seed 19 / 25 paragraph (lines ~544-556):
   say plainly that since M5-4d slice 2 seed 19 stands at 20:00 again (its last worker rebuilds; 1 pop, no building, 522 gold;
   BUG-1116 / BUG-1119) and is a named allowance again, seed 25 defeated at 12:34.
2. `docs/factions/whirlwind.md`: the prose round the `raider_rush` v `crossbow_push` seed-19 cell (Age II 10:21) if it quotes 10:34.
3. **BUG-1136:** `MapTextContentTests.ThePin_NamesEveryShippedMap_RarakuFirst` renamed to what it does (e.g.
   `ThePin_HasRarakuFirst_AndPrintsUnpinnedMaps`; update every reference, incl. QA's `DH7MapTextQaTests` and `DR33QaTests` comments
   and the bug files BUG-1104 / BUG-1136 "Fixed by" lines); the `_out` field and constructor moved to the class's top; the re-wrapped
   short line on the Malazan page.
- **Files:** the two faction pages, `sim/Rts.Sim.Tests/Content/MapTextContentTests.cs`, `QA/Content/DH7MapTextQaTests.cs` and
  `QA/Content/DR33QaTests.cs` (comments). **OUT:** `game/data/**`, the page tables' cells, `KnownBeatenAlive`'s entries (rule 41: the
  Producer's merge-time listing stands until the sim's hardening re-measures), any sim code, any Hard-seat number (the sim's slice may
  move those; it lists them for the next data task).

**Acceptance criteria.** 1. Build 0 warnings; the data filter (`Content|DataValidation|DataContentHash|MapFile`) green; smoke PASS.
2. No stale time in either page's prose (the dev greps the old numbers: 13:25, 12:33, 10:24, 10:34, 17:18, 15:39 and quotes the result).
3. BUG-1136 and BUG-1118 item 3 `fixed` in the report with the proof. 4. `git diff e1750ce -- game/data` empty; the data hash unmoved.

### Then (data)
- The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780); the Hard-seat
  page cells the sim's slice lists (if any); D14 c's content when the sim ships its schema; M7's rosters when M7's schemas exist. With
  none of those, the data track STOPs (cheaply).
