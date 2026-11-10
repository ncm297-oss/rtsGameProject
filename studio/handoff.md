# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-09-2226** (session 3 / 8 of 2026-11-09; the next session is 1 / 8 of
2026-11-10). Base: `main` after the sim + data merges of 2226 (M7-1 at 8d0f0ee, D-R35 at 4905cfb). Last session: sim **M7-1**
and data **D-R35** both ACCEPTED (0 fix rounds each), view STOP. **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 0 / 4 (bullets
1-2 half done).** Counters: **sim 1 / 4, view 1 / 4, data 0 / 4**. Bug ids next session: **sim 1167+, view 1177+, data 1184+**.
Open S1 / S2: none. The PLAN step re-checks the inbox first (New was empty at 2226).

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
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests.SameGameDataHashes` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.

## Watch-outs (all tracks)
- **Three test hosts at once next session (sim + view + data)**: wall-clock Perf and scene rows flake under load; rule 42 before
  filing. The default (non-Perf, non-Stress, non-Soak) loop is **~20-28 min** (8,416 rows now); the full non-Perf run ~1 h 20 m.
- QA runs the full non-Perf **once** per session (at the re-check, on the fixed commit, **with its own files in**).
- Merge order sim → view → data; `studio/qa/coverage.md` and `studio/bugs/README.md` are the usual conflicts, both sides kept.
  **No `game/data` file may be touched by two tracks:** D27 edits `game/data/factions/malazan/ai.json` and
  `.../whirlwind/ai.json` (Hard only); M7-2 edits no shipped faction file (schema code + `statuses.json` at most, and only if a new
  status is needed); M7-V1 edits nothing under `game/data` (test copies only).
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Since M7-1: save format 10 for every save; the golden header's data hash is `64DF327980743173`** (`k` lines unchanged since
  M6-SH1). A data task that changes any `game/data` value moves it again (regenerate once, reason in the comment, BUG-0750). D27 will.
- **Pinned matches on `main` after M7-1** (unchanged by M7-1; D27 may move the **Hard-seat** rows only, rule 43 on its own branch):
  `MatchCriterionTests` (Vathar **23,322** seat 1 / Raraku **28,231** / Pale Hills **50,140**, bound 72,000 / generated seed 2
  **17,053**, bound 24,000 / FFA **56,896** / 2v2 **26,070**), `MapFileTests.NormalVersusNormal_OnANewMap_EndsWithinItsBound`,
  `MapSlice2StressTests` (`KnownStalls` Pale Hills 3 / 4 / 6 = **33,484**, generated 6 25,941, Raraku 5 **36,004**; Pale Hills 2 =
  49,565 and 5 = 55,485), `StandardPresetPinQaTests` (7 digests), QA's `NonHardMatchPinQaTests` (8 digests: **must not move under
  D27**), `HardKnobsPinTests` / `HardKnobsMatchTests` (Hard: D27 re-pins), the 8 `CounterCompositionTests.TheNonHardPins_*` rows, the
  Malazan / Whirlwind page tables, `D21OpeningsQaTests` / `DH6ThresholdQaTests` + `KnownBeatenAlive`, the golden's `k` lines,
  `HuntStressTests` FFA seed 2 **75,382** / seed 3 53,892 / 2v2 seed 1 64,226 (and its H v N rows). The Hard v Normal tally:
  **29 / 11 / 2** (Raraku 10 / 4, Vathar 7 / 7, Pale Hills 12 / 0 / 2).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), QA's `FarExpandProbeQaStressTests` Pale
  Hills FFA seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_*`
  (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows in `EndGameH6QaTests`, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, QA's
  `RegenQaTests` rows for BUG-1165 (2) and BUG-1166 (2).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- Perf margins: `NextSweep`'s worst-case re-target 35.2 µs (bound 50); `AiPerfTests` 8 seats avg 0.525 ms (p95 0.863); the 500-unit
  regen brawl ~1.2 ms / tick with combat (QA, no gate).

---

## Sim track

### Where we are
M7-1 on `main`: `UnitDef.Scale`, `FactionDef.RegenPerTick` / `RegenDelayTicks`, `RegenSystem` (phase 5), `StatusKind.Regenerating`,
`StatusDef.StateHashId`, save format 10. Counter 1 / 4. Debt: BUG-1165 / 1166 S4 (new), the three `SaveFormat` alias names.

### Current session plan: M7-2, feature, QA full (the Producer confirms at PLAN)

**TASK_ID:** M7-2 · **TASK_TITLE:** Build the war-dog pack's sim half: a multi-spawn queue item and the on-hit passive, schemas
first (no Teblor file)

**Goal.** M7's third bullet (docs/05): "War-dog packs (multi-spawn queue item, Hamstring), Blood-oil Frenzy with aftermath slow".
Two generic hooks the Teblor page needs (docs/factions/teblor.md: war-dogs trained as a pack of N from one queue item, pop per
dog; Hamstring: a war-dog's bite applies `slowed`): a `units.json` `spawnCount` (one queue item → N units, the item's pop / cost
as the page says) and an `onHit` passive trigger (`DataLimits.PassiveTriggerIds` gains `onHit`: apply a status with a magnitude
and duration to the victim of each landed hit). The data rule: schemas, validation and the minimum test entries only; no shipped
unit gains either; no new faction id in C#.

**Scope (to be confirmed at PLAN against the Teblor page).**
- `units.json` optional `spawnCount` (whole, 1-8, default 1; `DataLimits.MaxSpawnCount`), `UnitDef.SpawnCount`, in `ContentHash`.
  Production: one queue item with its own `cost` / `buildTime`; at completion N units spawn at the rally / door in slot order; the
  item's half-pop is `pop` × N counted when the item starts (the cap check uses the whole pack); a cancel refunds the item; a pack
  that cannot fully spawn (no room) follows today's single-unit rule per dog. The AI's `pop` trigger and `minArmyPop` count the pack.
- `passives` trigger `onHit` `{trigger: onHit, status, magnitude, duration}` (as `whenBelowHp`'s shape plus `duration`): applied
  to the victim in `CombatSystem.HitUnit` (melee and projectile landings; not splash neighbours, not DoT pulses: say so in docs/03),
  through `StatusSystem.Apply` (stack rules as today's). `UnitPassive` gains the fields; `PassiveSystem` unchanged (the trigger is
  combat's).
- Blood-oil Frenzy's "aftermath slow" (a status that applies another when it ends): **design in the brief or defer** to M7-3; if
  deferred, say so in docs/05's bullet note.
- Docs: docs/03 "Data schema" rows (`spawnCount`, `onHit`), "Implementation (M7-2)"; docs/02 "Status effects" table's Slowed source
  "war-dog bite" already lists it. No save-format change expected (a queue item's count is derived from the type); if one is needed,
  format 11 under M7-1's pattern (every save, no old reader).
- **OUT:** any Teblor data file; any `ai.json` / `rules.json` value (D27 runs beside this); the view's pack rendering; models.

**Acceptance criteria (draft).** 1. `spawnCount` loads / validates / hashes (absent → 1; 0 / 9 / 1.5 errors). 2. On a test copy
(a Malazan unit at `spawnCount` 3, pop 1): one Train → one queue item → 3 units at completion, `HalfPop` +6 when started, refund on
cancel, refused when the cap lacks room for the whole pack; the AI's `pop` / `minArmyPop` count the pack (a `PlayerView` test).
3. `onHit` applies the status to the victim on melee and projectile hits, not splash / DoT; a `slowed` victim's speed follows;
validation rows (unknown status, duration 0, a second passive of the same trigger). 4. Determinism: two runs of a 3,000-tick
scripted match with both hooks hash-equal; a mid-match save with a pack item in a queue round-trips. 5. Every pin unchanged, the
golden's `k` lines identical, the header's data hash unchanged (no shipped `game/data` value moves; if `statuses.json` gains an
entry the hash moves once: reason in the comment). 6. No per-tick allocation; no Godot; text only in data. 7. Docs; build 0 / 0;
the loop green; smoke PASS.

**Riders (a few lines only):** BUG-1166's first half (refuse an off-step `regen.hpPerSecond` / status magnitude at load, as
`pop` does) if the loader is open anyway.

### Next task candidates (after M7-2)
1. M7-3: Blood-oil Frenzy's aftermath (a "when it ends apply" hook) if deferred; the Teblor `ai.json` needs nothing new.
2. The sim's hardening at 4 / 4 (or at M7's end): BUG-1165, BUG-1166, the `SaveFormat` alias names, BUG-1161, BUG-1010's lever.

---

## View track

### Where we are
STOP last session. Counter 1 / 4. M6 5 / 10 (the rest owner-blocked). **Unblocked now: M7-V1** (`UnitDef.Scale` on `main`).

### Current session plan: M7-V1, feature, QA standard

**TASK_ID:** M7-V1 · **TASK_TITLE:** Draw each unit at its data `scale`

**Goal.** M7's first bullet, the view half: "1.5× visuals". `UnitViews` builds each type's mesh at `UnitDef.Scale` × today's capsule
(docs/02 "Combat / Stats": the visual scale may be larger than the collision radius; the Teblor page: units 1.5×, war-dogs normal),
read from the sim's data as `Radius` is read today (`UnitViews` line ~136); no ViewApi change, no sim change.

**Scope.** The capsule mesh / its height and radius at `scale`; the selection ring, the target ring, the health bar's height and
width follow the scaled size; corpse discs / death fade follow it; the minimap dot does not (stays one size). A `--screenshot` look
at a 1.5× unit beside a 1.0× one on a test copy (`TestDataDir.CopyOfShipped()` with one Malazan unit at `scale` 1.5; the test scene
under `game/tests/`). **OUT:** any `game/data` edit; a "Regenerating" HUD cue (not until the owner asks); real models (the art
call); the Teblor buildings' 1.25× (a models-task matter).

**Acceptance criteria.** 1. A unit at `scale` 1.5 is drawn 1.5× the capsule in every dimension; one at 0.5 is half; 1.0 unchanged
(screenshot compared with today's). 2. Rings and health bar scale with it; the bar stays readable at 0.5 and 3.0. 3. Shipped data
(every unit 1.0) renders pixel-for-pixel as before (a scene test on the shipped data + the smoke). 4. No sim read beyond
`UnitDef.Scale`; the per-frame path allocates nothing new. 5. `ShellTest` / `UnitViewsTest` rows; smoke PASS; docs/03
"Implementation (M7-V1)" + the docs/02 line.

### Next task candidates
- An owner item from the inbox (the art call, ".NET 10 installed", templates, sound notes): a feature session, QA standard.
- The view's hardening at 4 / 4: BUG-1126, BUG-1147 item 1, BUG-1115 item 4, BUG-0860 / 0830 / 0890 / 0861.
- Otherwise STOP (cheap) once M7-V1 is on `main` and no other M7 view work exists until the Teblor models.

---

## Data track

### Where we are
D-R35 on `main` (the hardening; counter 0 / 4). BUG-1181 fixed; BUG-1182 S3 / BUG-1183 S4 open (docs / comments). M7-1's `scale` /
`regen` schemas are on `main`; `spawnCount` / `onHit` come with M7-2.

### Current session plan: D27, feature, QA standard

**TASK_ID:** D27 · **TASK_TITLE:** Tune Hard so it beats Normal on Vathar Crossing too (BUG-1163), Hard values only

**Goal.** BUG-1163: the 42-match Hard v Normal tally is 29 / 11 / 2 with Vathar Crossing level at 7 / 7 (Raraku 10 / 4, Pale
Hills 12 / 0 / 2). Move only Hard's `ai.json` values (`difficulties.hard`: `gatherBonus`, `counterStrength`, `focusFire`,
`pullBackWounded`, the Hard opening picks / weights if the schema allows per-difficulty openings; docs/02 "AI opponent" Hard row)
so Hard is ahead on every map with no-results fewer than 5, without touching any Normal / Easy value.

**Files (the only ones):** `game/data/factions/malazan/ai.json`, `game/data/factions/whirlwind/ai.json` (Hard only),
`docs/factions/*.md` (the Hard lines and any Hard-seat cell), `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`.
Not `rules.json`, not `units.json`, not docs/02 / 03 beyond the Hard numbers' lines.

**Scope.** Measure first (the tally on the base, `HardVersusNormalTallyQaStressTests`; Vathar's 7 matches in detail: who wins,
when, why: BUG-1163's file has the per-seed table); pick the smallest Hard change that flips Vathar; re-run the tally; re-pin the
Hard-seat rows (`HardKnobsPinTests` / `HardKnobsMatchTests`, `HuntStressTests` H v N rows, any page Hard cell) with the old values in
comments; the golden header regenerated once (the data hash moves; reason in the `GatherWedgeQaTests` comment). **The 8 non-Hard
digests, every Normal v Normal pin and the Malazan / Whirlwind tables' Normal cells must not move.** Review table for the owner
(old → new per Hard value, one-line why; the Hard tooltip text if `ai.json` carries it).

**Acceptance criteria.** 1. The tally re-run once on the branch: Hard ahead on every map, no-results fewer than 5, the total not
worse than 29 / 11 / 2 by more than the Vathar swing. 2. No Normal / Easy value changed (`git diff` on the two files shows Hard keys
only); the 8 `NonHardMatchPinQaTests` digests and every Normal pin green unchanged. 3. Hard-seat pins re-measured in the same commit,
old values in comments; the golden header regenerated once. 4. `Content` + `QA.Content` + `DataValidation` + `DataContentHash`
green; the pages' Hard lines pinned; the review table. 5. Build 0 / 0; the default loop green.

**Design references.** docs/02 "AI opponent" (the difficulty table), docs/03 "Implementation (M5-4c slice 2)" (the knobs) and the
M5-5 table; BUG-1163's file; docs/factions pages' difficulty lines.

**Constraints.** Rule 43 (Hard rows only; a Normal row that moves is a finding, stop and report), rule 41 (M7-2 changes no AI
value, so the two may run in one session), rule 48. QA tier standard (scenario numbers). The owner's `counterStrength` answer, if
it lands in the inbox, is applied first and the tally measured with it.

### Next task candidates (after D27)
1. **The Teblor roster, part 1** (`game/data/factions/teblor/` `faction.json` with `regen` 1 HP/s / 6 s, `units.json` with `scale`
   1.5 and the page's 2-3 pop costs, `buildings.json`, `techs.json`, `ai.json` openings; war-dogs only once M7-2's `spawnCount` /
   `onHit` are on `main`; QA standard; docs/factions/teblor.md's tables as the source; the review table with every name and
   description quoted).
2. The data's next hardening at 4 / 4: BUG-1182 (a dated re-word of the untabled figures), BUG-1183.
3. The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780;
   `counterStrength`).
