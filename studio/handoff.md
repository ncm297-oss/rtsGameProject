# Handoff: brief for the next session

Written by the Producer at the PLAN step of session **2026-11-09-2226** (session 3 / 8 of 2026-11-09, a one-off run the owner
dispatched; inbox New empty). Base `6763141`. Last session (2026-11-09-1336): sim **M5-H6** and data **D26a** both ACCEPTED, view
STOP. **M5 8 / 9 (criterion 6 owner-only; the sim's hardening done), M6 5 / 10.** Counters before this session: **sim 0 / 4,
view 1 / 4, data 3 / 4** (so the data track's session is its hardening one). Bug ids this session: **sim 1165+, data 1182+**
(view STOPs). Open S1 / S2: none.

## Standing rules (1-48)

Unchanged 1-48 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle; 43: every sim task that
can change Normal AI behaviour names which M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table
in the same commit, and a row that stops ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on
a scratch clone and integrated first, a new task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says
what to do when a pinned criterion row stops ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so
no branch lives a fourth session; 47: QA's re-check runs the default loop on the commit that includes QA's own suites, and a QA file
that measures allocation declares `[Collection(SerialCollection.Name)]`; 48: at PLAN the Producer checks every acceptance criterion
against the brief's own out-of-scope list; a figure the scope cannot move is "measure and report", never pass / fail).

## Watch-outs (all tracks)
- Two test hosts at once this session (sim + data): wall-clock Perf and scene rows flake under load; rule 42 before filing. The
  default (non-Perf, non-Stress, non-Soak) loop is **~20-28 min** (the AI match rows are in it); the full non-Perf run ~1 h 15 m.
- QA runs the full non-Perf **once** per session (at the re-check, on the fixed commit, **with its own files in**).
- Merge order sim → data (view STOPs); `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
  **No `game/data` file is touched by both tracks:** the sim edits `game/data/common/statuses.json` (one new entry) and the
  `units.json` / `faction.json` *schemas* only (no shipped faction file gains a value); the data task edits no `game/data` file.
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Pinned matches on `main` after M5-H6** (none may move this session; the sim task changes no shipped AI value):
  `MatchCriterionTests` (Vathar **23,322** seat 1 / Raraku **28,231** / Pale Hills **50,140**, bound 72,000 / generated seed 2
  **17,053**, bound 24,000 / FFA **56,896** / 2v2 **26,070**), `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithinItsBound`,
  `MapSlice2StressTests` (`KnownStalls` Pale Hills 3 / 4 / 6 = **33,484**, generated 6 25,941, Raraku 5 **36,004**; Pale Hills 2 =
  49,565 and 5 = 55,485), `StandardPresetPinQaTests` (7 digests), QA's `NonHardMatchPinQaTests` (8 digests), `HardKnobsPinTests`,
  the 8 `CounterCompositionTests.TheNonHardPins_*` rows, the Malazan / Whirlwind page tables, `D21OpeningsQaTests` /
  `DH6ThresholdQaTests` + `KnownBeatenAlive`, the golden's `k` lines, `HuntStressTests` FFA seed 2 **75,382** / seed 3 53,892 /
  2v2 seed 1 64,226. The Hard v Normal tally: **29 / 11 / 2**.
- **The golden header's data hash moves this session** (new `UnitDef` / `FactionDef` fields and a new status entry are in
  `GameData.ContentHash`): regenerate the header once, `k` lines identical, the reason in the commit body and the
  `GatherWedgeQaTests.SameGameDataHashes` comment (BUG-0750's rule). Every recording and save made before this session will refuse
  to play / load; the ACCEPT's For your review entry must say so.
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), QA's `FarExpandProbeQaStressTests` Pale
  Hills FFA seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_*`
  (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows in `EndGameH6QaTests`, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- Perf margins: `NextSweep`'s worst-case re-target 35.2 µs (bound 50); `AiPerfTests` 8 seats avg 0.525 ms (p95 0.863).

---

## Sim track

### Current session plan (2026-11-09-2226): M7-1, feature, QA full

**TASK_ID:** M7-1 · **TASK_TITLE:** Add the per-unit visual `scale` field, prove half-step population end to end, and build
faction regeneration (schemas first, no shipped faction gains a value)

**Goal.** M7 (Teblor) opens with two generic systems the design docs already settle: a per-unit visual scale the view reads from
data (docs/02 "Combat / Stats": "Visual scale can be larger than the collision radius"; the Teblor page: units 1.5×, war-dogs
normal) and regeneration (docs/02 "Status effects": Regenerating "+HP per second", "Andii and Teblor passives"; docs/03 "Data
schema": "dynamic bonuses (regen, ...) are faction modifiers in `faction.json`"; the Teblor page: 1 HP/s after 6 s without damage;
the Andii page: 1.5 HP/s after 5 s). The half-step population system exists since M3 (`pop` 1.5 → 3 half-pop,
`Pop_OneAndAHalf_LoadsAsThreeHalfPop`; the Catapult is 3 pop) but is proven only at the loader; this task proves it through
production, the cap and the AI's `pop` triggers. The data rule: the sim ships the schemas, their validation and the minimum entries
its own tests need (test copies of the data via `TestDataDir.CopyOfShipped()`); no shipped faction gains a `scale` or `regen`
value and no new faction id appears in C#.

**Scope.**
- `units.json` optional `scale` (decimal, default 1.0; `DataLimits.MinUnitScale` 0.5 / `MaxUnitScale` 3.0, an error outside, an
  error on a non-number as every field), `UnitDef.Scale`, added to `GameData.ContentHash` (and the `DataContentHashTests` row that
  flips every field). No ViewApi accessor is needed: the view reads `UnitDef.Scale` as it reads `UnitDef.Radius` today
  (`UnitViews` line 136); the rendering is the view's next task.
- `faction.json` optional `regen: { hpPerSecond, delay }` (`hpPerSecond` a decimal above 0 and at most 100; `delay` seconds, 0 or
  more, at most 3600, to ticks; both required when the object is present; absent = no regeneration), `FactionDef.RegenPerTick`
  (fixed-point, see below) + `RegenDelayTicks`, both in `ContentHash`.
- A new status kind `regenerating` (`StatusKind.Regenerating`, `DataLimits.StatusKindIds`): its magnitude is HP per second added
  while it lasts, above 0 (the `applyStatus` / `createZone` validation, as `slow`'s rule); one shipped entry in
  `game/data/common/statuses.json` (sim-owned): id `regenerating`, `displayName` "Regenerating", a one-line `description` (the
  data track may re-word it later). Nothing applies it yet in shipped data; it is the hook the M9 Darkness zone and future
  abilities use.
- `RegenSystem.Run(World)` in tick phase 5 after `StatusSystem.Run` and before `ZoneSystem.Run` (docs/03 "Tick model" already
  names "regen" in phase 5): every live unit, slot order, whose rate (its faction's + the sum of its regenerating statuses'
  magnitudes) is above 0, whose hp is below its max (`World.MaxHp`), and whose last damage is at least `delay` ticks old (a
  regenerating status heals through the delay: its magnitude applies whenever the unit is below max; the faction rate waits), gains
  hp by an **integer fixed-point accumulator** (as the repair accumulators, `EconomyConstants.RepairFixedOne` precedent: e.g.
  1 HP = 400 units, 1.5 HP/s = 30 units a tick), never above max; at max the accumulator is cleared. Skipped in O(1) when no
  faction in the match has a rate and no unit holds a regenerating status (a count on `UnitStore`, kept as `CasterCount` is), so
  today's matches pay nothing.
- `UnitStore`: two per-unit int arrays, `LastDamagedTick` (set in `CombatSystem.HitUnit`, the one path every hit, landing, splash,
  friendly-fire hit and DoT pulse goes through; -1 / never at spawn) and the regen accumulator; both **hashed** (they drive future
  hp) and **saved**: `SaveFormat.CurrentVersion` 9 → 10 for every save (these arrays are in every unit store), formats 9 and
  older refused `FormatVersionMismatch` through the existing path; `SaveFormatTests` fixtures updated the way M5-9a's were.
  Replay format stays 9 (no replay byte changes beyond the header's data hash).
- A `giant`-class damage row: one `DamageCalc` test that a unit of armor class `giant` takes ×1.25 pierce and ×1.25 magic and
  ×0.75 siege from the shipped `damage_table.json` (the class has shipped since M4; the test is the proof M7's second item
  needs).
- Docs: docs/03 "Data schema" rows for `units.json` (`scale`), `faction.json` (`regen`), `statuses.json` (`regenerating`); a new
  "### Implementation (M7-1)" with the rule, the phase, the fixed-point unit, the hashed / saved fields and the format bump; the
  "Save format" section (version 10); docs/02 "Status effects" row for Regenerating gets the delay rule in one sentence; docs/05
  M7's first bullet is ticked only by the Producer at ACCEPT (the view half is still open: say so in the retro line).
- **OUT of scope:** the view's rendering of `scale` (the view's next task, once this is on `main`); a `regen` **tech stat** (the
  Andii Night Forge "+1 HP/s", M9); the Darkness zone's double regen (M9: a `createZone` with a `regenerating` status covers it
  then); a building `scale` (the Teblor buildings' 1.25× is the models task); a per-unit regen override; the war-dog multi-spawn
  queue item (M7's third bullet); any Teblor data file; any `ai.json` / `rules.json` value; any AI rule.

**Acceptance criteria.**
1. `units.json` `scale`: absent → `UnitDef.Scale` 1.0; `1.5` loads as 1.5f; `0.4` and `3.5` are one error each naming the field
   and the 0.5-3.0 range; a shipped unit with `scale` set on a test copy changes `ContentHash` (`DataContentHashTests`).
2. Half-step population end to end, on a test copy where one Malazan unit is `pop` 1.5 and another 3: a producer refuses to start
   the item when `HalfPopCap - HalfPop` is 2 (one half short) and starts it at 3; the cap bar (`PopText`) prints `x.5`; the AI's
   `pop` step trigger and `attack.minArmyPop` count the half (an `AiPlayer` / `BuildOrderExecutor` test through a `PlayerView`);
   spawn, death and cancel keep `HalfPop` exact (an invariant over a 2,000-tick scripted match: `HalfPop` equals the sum of live
   units' `HalfPop` plus started queue items', every tick).
3. `faction.json` `regen`: absent → no regeneration (rate 0); `{ "hpPerSecond": 1, "delay": 6 }` → 20 units a tick (at 400 per
   HP) and 120 delay ticks; `hpPerSecond` 0 or missing, `delay` missing or negative, an unknown key → one error each.
4. The rule, on a test copy with the Malazan faction at 1 HP/s after 6 s: a Heavy Infantry hit to 100 of 130 hp at tick t has
   100 hp through tick t + 119 and 101 hp at t + 139 (one HP every 20 ticks from t + 120), reaches 130 exactly and stops; a
   second hit at t + 100 restarts the delay; a unit at max never changes; 1.5 HP/s heals 3 HP every 2 s with no drift over a
   minute (the accumulator carries the half); a Hp tech researched mid-regen raises the ceiling and the unit keeps healing to it.
5. A `regenerating` status (applied in a test through `StatusSystem.Apply`, magnitude 2, 5 s) heals 2 HP/s through the faction
   delay, stacks with the faction rate while it lasts, stops when it expires; its validation: magnitude 0 or negative is an error,
   `damageType` on it is an error (the kind rule).
6. Determinism and persistence: two runs of a scripted 3,000-tick match with the regen test copy give the same per-tick hashes; a
   save at a mid-regen tick (a unit inside its delay, another with a half-HP accumulator) loads and continues to the same hashes
   as the unsaved run; a format-9 save is refused `FormatVersionMismatch`; `SaveFormat.CurrentVersion` is 10.
7. **Nothing on `main` moves but the data hash:** every pinned match tick and digest in the Watch-outs list is unchanged, the golden
   replay's `k` lines are identical and only its header's data hash is regenerated (once, reason in the commit body and the
   `GatherWedgeQaTests.SameGameDataHashes` comment); `AiPerfTests` and `CombatPerfTests` rows unchanged alone (rule 42).
8. Per-tick code allocates nothing (`RegenSystem` under the existing allocation gate pattern); no Godot, no `Dictionary` /
   `HashSet` iteration, no `System.Random`; the status's display text only in `statuses.json`.
9. The `giant` damage row test (×1.25 pierce / magic, ×0.75 siege, ×1.0 melee) passes against the shipped table.
10. Docs as in Scope; `dotnet build` 0 / 0; the default loop green; smoke PASS (the view builds; `statuses.json` gained a row).

**Design references.** docs/02 "Combat / Stats" (lines 56-58: visual scale vs collision radius), "Economy" table (Pop costs:
integers or half-steps, half-pop internally), "Status effects" (Regenerating); docs/03 "Tick model" phase 5, "Data schema"
(`faction.json` modifiers note, `units.json` / `statuses.json` rows, `DataLimits`), "Save format (M6-S2)" and the M5-9a version
bump pattern, "Implementation (M3-4)" (population), "Implementation (M4-4a)" (statuses), "Economy implementation" (the repair
fixed-point accumulators); docs/factions/teblor.md "Faction bonus" (1.5× scale, 2-3 pop, 1 HP/s after 6 s, Giant class);
docs/factions/andii.md (1.5 HP/s after 5 s, 1.5 pop); docs/05 M7 bullets 1-2.

**Tests required.** `DataValidationTests` / `DataConversionTests` rows for `scale`, `regen`, `regenerating`;
`DataContentHashTests` rows for the new fields; a new `RegenTests` (criteria 4-5, the delay, the ceiling, the tech ceiling, the
stacking, max-hp no-op); `PopulationTests` (criterion 2, incl. the invariant run); `SaveFormatTests` (version 10, the refusal,
the mid-regen round trip); `AiDeterminismTests`-style two-run hash equality with the regen copy; a `DamageCalcTests` giant row;
the golden header regenerated; the allocation gate row for `RegenSystem`.

**Constraints.** CLAUDE.md 4 (no wall clock, seeded RNG only, no dictionary order), 5 (no per-tick allocation; the O(1) skip),
6 (no stat in C#: the limits in `DataLimits`, the fixed-point unit a named constant), 8 (the status name in data). Rules 38, 42,
43 / 45 (no AI value changes: prove it by the pins), 48 (every criterion above is inside Scope: checked). Keep off `ai.json` /
`rules.json` (rule 41). Save-format bump: follow the M5-9a pattern exactly (one constant, the refusal path, fixtures). If the
fixed-point unit or the delay semantics turn out ambiguous, pick the reading above and write it in docs/03; do not stop.

### Next task candidates (after M7-1)
1. M7's third bullet, the war-dog pack: a multi-spawn queue item (`spawnCount` on a unit, one queue item → N units, pop per unit)
   and the Hamstring on-hit passive (a `onHit` passive trigger applying `slowed`); Blood-oil Frenzy needs only data once the
   `frenzied` status's aftermath slow exists (a `createZone`-less "when it ends apply" hook: design first).
2. The Teblor `ai.json` schema needs nothing new; the Teblor roster is the data track's once M7-1 is on `main`.
3. BUG-1010's lever if small; BUG-1161 riders if the task touches that code.

---

## View track

### Current session plan (2026-11-09-2226): STOP (cheap)
Inbox New empty; nothing changed for the view. Every remaining M6 criterion needs the owner (the art call, music / fonts / export
templates, ".NET 10 installed", a friend's playtest); the M7 view half (the 1.5× visual scale) needs `UnitDef.Scale`, which lands
on the sim's branch this session and is not on `main` yet; the view's debt (BUG-1126 / 1147 item 1 / 1115 item 4 / 0860 / 0830 /
0890 / 0861) is not a session's worth and the counter is 1 / 4.

### Where we are
STOP (cheap). Counter 1 / 4. M6 5 / 10.

### Next task candidates
- **When M7-1 is on `main`: M7-V1, the visual scale from data** (`UnitViews` builds each type's mesh at `def.Scale` × the
  capsule, selection rings / target rings / health bars follow the scaled height; a `--screenshot` look at a 1.5× unit beside a
  1.0× one on a test copy; QA standard). Also a "Regenerating" HUD cue is **not** wanted until the owner asks.
- An owner item from the inbox (the art call, the SDK, templates, sound notes): a feature session, QA standard.
- Otherwise STOP (cheap).

---

## Data track

### Current session plan (2026-11-09-2226): D-R35, hardening (counter 3 / 4 → this is the fourth), QA light, docs only

**TASK_ID:** D-R35 · **TASK_TITLE:** Fix the pages' far-expansion sentence (BUG-1181), re-word the Malazan prose to the cells
M5-H6 re-printed, and close D26a's nits

**Goal.** The two faction pages must say what the sim plays and what their own tables show. BUG-1181 (S3): both "Far expansion"
bullets read as if a mine at a free start location qualifies at any distance; the sim applies the 384 m reach to every mine
(docs/02 line 451 now says: "once it has seen a free mine within `rules.json` `ai.farExpandReach` (384 m) of its Town Hall with
no enemy drop-off by it"). And M5-H6 re-printed ten table cells under rule 40 while the Malazan prose above them still quotes
the old times.

**Files (the only ones):** `docs/factions/malazan.md`, `docs/factions/whirlwind.md`, `sim/Rts.Sim.Tests/Content/**`,
`sim/Rts.Sim.Tests/QA/Content/**`. **Not** `game/data/**`, not docs/02 / docs/03 (the Producer fixed docs/02 at the last ACCEPT),
no C# outside the two test folders. The data hash must not move.

**Scope.**
1. BUG-1181's S3: malazan.md lines 192-195 and whirlwind.md lines 190-193: the sentence says the depot goes only by a free mine
   within `ai.farExpandReach` 384 m of its hall, wherever that mine was seen (the walk goes to the free start locations whatever
   their distance; a mine there farther than 384 m is never taken). Keep `AiAdaptsContentTests`' regex `ai.farExpandReach 384 m`
   matching on both pages.
2. The Malazan prose to the re-printed cells, no cell edit: line 410 ("seed 8 at 13:50, 14 at 15:03") and lines 563-564 ("seed 8
   now falls at 13:50 ... seed 14 at 15:03") → 13:47 and 14:16, worded as D-R34 did ("re-printed 2026-11-09 for the sim's M5-H6
   ..."), with the old figures kept in the "was" form; check every other prose figure against the tables it describes ("Age II
   against the Whirlwind" line 602 on: seed 45 "9:28 (defeated 19:02)", seed 8 "8:41 (defeated 13:47)", seed 14 "never (defeated
   14:16)"; the Whirlwind twin table line 492: seed 19 10:34) and re-word any that disagrees; list each in the review table.
3. BUG-1181's nits that live in the repo: (1) "Three rules sit on top of every opening above" introduces five bullets on both
   pages: make the count true (e.g. "Three rules ... and two notes" or split the bullets); (4) `AiAdaptsContentTests`' failure
   message prints `page ''` for a "none" row: print `'none'`; (2) optional, only if cheap: the number guard's equal-value gap (a
   number that happens to equal a pinned value passes); (5) if the (B) reason wording is on a page, say "the only Malazan unit that
   answers Shock is Heavy Infantry (`ai.counters` `shock: ["line"]`)"; if it is only in a report, skip it.
4. A review table for the owner (no unit / field changed; every re-worded sentence quoted, old → new).
- **OUT of scope:** any `game/data` edit (D27, Hard's tuning, is the next data task and not in this session: rule 41, the sim's
  M7-1 is on its own branch with a moving data hash); any new measurement or match run; docs/02 / docs/03; the Malazan / Whirlwind
  table cells (rule 40 is the sim's).

**Acceptance criteria.**
1. Both "Far expansion" bullets state the 384 m rule for every mine, a start location's included, and
   `Content/AiAdaptsContentTests` + QA's `D26aAdaptsPinQaTests` are green both ways (the Producer mutates one number on a page and
   one in `rules.json` on a scratch copy: both read red).
2. No Malazan or Whirlwind prose figure disagrees with the table it describes (lines 410, 563-564 at least; the review table lists
   every change); no table cell edited (`git diff` shows no `|`-row change).
3. "Three rules" (both pages) counts what follows; the "none" failure message prints `'none'` (a test that triggers it).
4. `Content` + `QA.Content` + `DataValidation` + `DataContentHash` filters green; the data hash unchanged (no `game/data` in the
   diff); `dotnet build` 0 / 0.
5. BUG-1181 patch for the Producer (status fixed, the proof), the review table in the report.

**Design references.** docs/02 "AI opponent" (line 451, the far-expansion sentence as re-written 2026-11-09); docs/03
"Implementation (M5-9b)" (`FarExpansion.Candidate`'s reach on every mine) and "(M5-H6)" (the re-printed cells); BUG-1181's file;
the pages' own tables (lines 551-558, 602-624 malazan.md; 492-495 whirlwind.md).

**Tests required.** `AiAdaptsContentTests` both ways still green (the regex kept); a row for the `'none'` message; the prose /
cell checks that exist (`D21OpeningsQaTests` / `DH6ThresholdQaTests` / `MapTextContentTests`) green.

**Constraints.** Docs-only; no match tick or digest quoted in "How the AI adapts"; keep page lines under 120 characters as the
pages do; the pin must stay green both ways after every edit.

### Next task candidates (after D-R35)
1. **D27, Hard's tuning (BUG-1163; feature, QA standard):** `game/data/factions/*/ai.json` Hard knobs / openings so Hard is ahead
   of Normal on Vathar Crossing too, measured by the 42-match tally (`HardVersusNormalTallyQaStressTests`) and a larger sample if
   cheap; rule 43 / 41: the Hard-seat pins re-measured on its own branch, no Normal / Easy value touched (the 8 non-Hard digests
   must not move); **not in the same session as a sim task that changes AI play** (M7-1 does not).
2. **The Teblor roster once M7-1 is on `main`** (`game/data/factions/teblor/` `faction.json` with `regen`, `units.json` with
   `scale` 1.5 and 2-3 pop, `buildings.json`, `techs.json`, `ai.json`; QA standard; the Teblor page's tables as the source).
3. The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780;
   `counterStrength` if the owner wants stronger).
