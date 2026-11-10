# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-09-2226** (session 3 / 8 of 2026-11-09; the next session is 1 / 8 of
2026-11-10). Base: `main` after the sim + data merges of 2226 (M7-1 at 8d0f0ee, D-R35 at 4905cfb). Last session: sim **M7-1**
and data **D-R35** both ACCEPTED (0 fix rounds each), view STOP. **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 0 / 4 (bullets
1-2 half done).** Counters: **sim 1 / 4, view 1 / 4, data 0 / 4**. Bug ids next session: **sim 1167+, view 1177+, data 1184+**.
Open S1 / S2: none. The PLAN step re-checks the inbox first (New was empty at 2226).

**Confirmed at the PLAN of 2026-11-10-0146** (session 1 / 8 of 2026-11-10, base edaeeac = `origin/main`): inbox New empty; build 0 / 0
on a scratch archive (rule 38); M7-1's claims verified (`UnitDef.Scale`, `SaveFormat.CurrentVersion` 10, `Combat/RegenSystem.cs`,
`Economy/PopulationTests`, `DamageCalcTests.TheGiantRow_*`, docs/03 "Implementation (M7-1)"); open S3 26 / S4 20; counters sim 1 / 4,
view 1 / 4, data 0 / 4 (all three feature sessions). All three tracks GO: **M7-2** (sim), **M7-V1** (view), **D27** (data). The three
briefs below are the confirmed ones (the drafts of 2226 adjusted where marked **PLAN**). Bug ids: sim 1167+, view 1177+, data 1184+.

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

### Current session plan: M7-2, feature, QA full (confirmed at the 2026-11-10-0146 PLAN)

**TASK_ID:** M7-2 · **TASK_TITLE:** Build the war-dog pack's sim half: a multi-spawn queue item and the on-hit passive, schemas
first (no Teblor file)

**Goal.** M7's third bullet (docs/05): "War-dog packs (multi-spawn queue item, Hamstring), Blood-oil Frenzy with aftermath slow".
Two generic hooks the Teblor page needs (docs/factions/teblor.md "Units" row Unique + "Unit notes": "one queue item spawns 3
normal-sized dogs (1 pop each)", 120 / 0 per pack, train 24 s; Hamstring: "each bite Slows the target 20% for 2 s"): a `units.json`
`spawnCount` (one queue item → N units; the item's cost and build time are the pack's, pop is per dog) and an `onHit` passive
trigger (`DataLimits.PassiveTriggerIds` gains `onHit`: apply a status with a magnitude and duration to the victim of each landed
hit). The data rule: schemas, validation and the minimum test entries only; no shipped unit gains either; no new faction id in C#.
**PLAN: Blood-oil Frenzy's aftermath slow is deferred to M7-3** (a "when it ends, apply" hook needs a decision about the status
store's one-entry-per-status-id merge rule (docs/03 M4-4b-3a "Known limits" (1)): a Zealot's passive Frenzied must not slow when
it ends, only the Shaman's; that is M7-3's design, not a rider). Say so in docs/05's bullet note.

**Scope.**
- `units.json` optional `spawnCount` (whole, 1-8, default 1; `DataLimits.MaxSpawnCount` 8), `UnitDef.SpawnCount`. **PLAN, the
  hash rule:** `GameData.ContentHash` adds `SpawnCount` **only when above 1** (a default appends nothing), so the golden header's data
  hash **64DF327980743173 does not move** this session from the sim (D27 regenerates the golden once for Hard's values; two tracks
  regenerating the same file would conflict at the merge). Passives are hashed per entry already, so an `onHit` entry costs nothing
  on shipped data.
- Production (`Economy/ProductionSystem`, `BuildingStore`): one queue item with the type's `cost` / `buildTime`; the item's
  reservation is `HalfPop` × N at start (`ProductionSystem` line ~226's cap check and `Ledger.AddHalfPop`, `BuildingStore`'s reserved
  head pop at line ~194 both × N); a cancel refunds the item's cost and releases the whole reservation; at completion N units spawn in
  slot order at the rally / door. **Spawn rule (Producer default): all-or-nothing per tick**: find N free cells (the push-out cache
  as today), spawn all N or none and try again next tick (today's single-unit rule for a full plateau), so no new per-slot state and
  **no save-format change** (a queue item's count is derived from its type). If all-or-nothing cannot be done without allocation or
  a cache change, the fallback is a per-slot "head units already spawned" count saved under **format 11** (M7-1's pattern: every
  save, older refused, say why in docs/03 "Save format"); say which in the report. The AI's `pop` trigger and `minArmyPop` already
  read `HalfPop` (live + started), so they count the pack once started: prove it, don't re-implement.
- `passives` trigger `onHit` `{trigger: onHit, status, magnitude, duration}` (a `whenBelowHp` shape plus `duration` seconds, at
  least one tick; `StatusEntry`'s existing validation; `radius` / `units` / `hpFraction` are "no such field" errors as today);
  `UnitPassive` reuses `Status` / `Magnitude` / `DurationTicks`; `UnitDef.HitPassive` index beside `BelowHpPassive` / `DeathPassive`;
  one per trigger (the existing rule, now three triggers). Applied in `CombatSystem.HitUnit` to a surviving victim (a killing blow
  applies nothing) for melee and projectile landings; **not** splash neighbours, **not** DoT pulses, **not** a tower's hit (a
  building has no passives); through the status store's apply path with today's stack rule (a stronger slow replaces, an equal one
  refreshes the duration: whatever `StatusSystem` does for `applyStatus` today, cite it). The victim's speed follows (the derived
  speed is `StatusSystem`'s already). `Abilities/PassiveSystem` is unchanged (the trigger is combat's; one field read per hit on a
  type without it: `HitPassive < 0`).
- Docs: docs/03 "Data schema" rows (`spawnCount`, `onHit`), "Implementation (M7-2)" (the spawn rule, the hit rule's exclusions, the
  hash rule), docs/02 "Combat" one line on packs if the pop sentence needs it (Slowed's "war-dog bite" source is already in the
  "Status effects" table); docs/05 bullet 3's note (what landed, the aftermath deferred to M7-3).
- **OUT:** any Teblor data file; any shipped `game/data` byte (no `statuses.json` change is needed: `slowed` exists); any `ai.json`
  / `rules.json` value (D27 runs beside this, rule 41: M7-2 changes no AI behaviour on shipped data); the aftermath slow; the view's
  pack rendering (one view per unit works as is); the `QueueStrip` showing "×3" (a later view nicety); models.

**Acceptance criteria.**
1. `spawnCount` loads / validates: absent → 1; 3 → 3; 0, 9 and 1.5 one error each naming `units[i].spawnCount` and the range 1-8.
   `DataContentHashTests`: 3 flips the hash, an explicit 1 does not (equals absent).
2. On a test copy (a Malazan Age I unit at `spawnCount` 3, pop 1, `TestDataDir.CopyOfShipped()`): one Train command → one queue item
   (`QueueCount` 1) → exactly 3 units alive at completion, all the owner's, all of that type; `HalfPop` +6 the tick the item starts and
   unchanged by the spawn; a cancel refunds the full cost and releases the 6; Train refused (today's refusal) when the cap lacks room for
   the whole pack but would fit one dog; the `PlayerView` pop the AI reads counts the pack once started (a `pop` trigger fires on it).
3. The spawn rule: with room for 2 dogs only, 0 spawn and the item stays at the head; with room for 3 next tick, 3 spawn (or, under the
   fallback, the count round-trips a save: say which rule shipped). Rally order: the three dogs sit on the three cells nearest the rally
   in the same order every run.
4. `onHit`: on a test copy, a melee hit and a projectile landing each apply `slowed` 0.2 for 2 s (40 ticks) to the victim; a splash
   neighbour, a DoT pulse and a tower's hit do not; a killing blow applies nothing; the victim's speed is ×0.8 while it lasts and back
   after; a second hit inside the 2 s refreshes to 40 ticks left. Validation rows: unknown status, duration 0, a second `onHit` passive
   on one unit, `radius` on an `onHit` passive (each one error).
5. Determinism: two runs of a 3,000-tick scripted match with both hooks (packs training on both sides, dogs biting) hash-equal every
   tick; a save mid-match with a pack item started and a dog's slow active loads and continues to the same hashes.
6. Every pin unchanged (the pin sweep), the golden's `k` lines identical and **the header's data hash unchanged** (no golden regen from
   the sim this session); the default loop green.
7. No per-tick allocation (an allocation row for a 200-tick match with 20 packs completing and 100 bites); no Godot; no `Dictionary` /
   `HashSet` / `Random` / `DateTime` / LINQ in the new code; no player-facing text in C#.
8. Docs as in Scope; build 0 / 0; smoke PASS.

**Design references.** docs/factions/teblor.md "Units" (the Unique row), "Unit notes" (War-dog pack), "Abilities" (Frenzy: deferred);
docs/02 "Status effects" (Slowed: speed × (1 − magnitude)), "Population" (half steps); docs/03 "Implementation (M4-4b-3a)" (passives,
the one-per-trigger rule, Known limits (1)), "Implementation (M3-4)" (production, the reservation), "Implementation (M7-1)" (the
hash and save-format patterns), "Save format".

**Tests required.** `DataValidationTests` / `DataConversionTests` rows for `spawnCount` and `onHit`; `DataContentHashTests`;
`Economy/PackProductionTests` (or in `ProductionTests`): criteria 2-3; `Combat/OnHitPassiveTests`: criterion 4; a determinism and a
save round-trip row (criterion 5); an allocation row (criterion 7); the pin sweep and the golden test green unchanged.

**Constraints.** CLAUDE.md rules 2, 4, 5, 6, 8; rule 41 (no AI value, no shipped data byte); rule 43 does not apply (no Normal behaviour
changes); the hash rule above; the developer cannot write `studio/**` (bug patches in the scratchpad). Bug ids from **BUG-1167**.

**Riders (a few lines only):** BUG-1166's first half (refuse an off-step `regen.hpPerSecond` at load, as `pop` is refused) if the
loader's regen rows are open anyway; otherwise leave it for the hardening.

**QA focus (full).** The pack's pop accounting under every exit (cancel at 0 % / 99 %, the building destroyed mid-item, the owner
defeated, a cap raised by a house mid-item): `HalfPop` must equal live units + started reservations every tick (M7-1's invariant
extended to packs); a pack at a door with 0 / 1 / 2 / 3 free cells across consecutive ticks; `spawnCount` 8 at pop 3 (24 half-steps…
12 pop) against a 10-pop cap; the AI training packs on a test copy for 10,000 ticks (its `pop` triggers must not stall); `onHit` on
a unit that is also the victim of its own target's `onHit` (mutual slows), under Frenzied, under Blinded, at the hit that kills,
on a stealthed attacker (the reveal still fires), with the status store's entry limit full; determinism across 3 processes; save
fuzz at every tick of a 400-tick pack match; allocation under 50 packs; the pin sweep; the golden hash **must not move**.

### Next task candidates (after M7-2)
1. M7-3: Blood-oil Frenzy's aftermath (a "when it ends apply" hook) if deferred; the Teblor `ai.json` needs nothing new.
2. The sim's hardening at 4 / 4 (or at M7's end): BUG-1165, BUG-1166, the `SaveFormat` alias names, BUG-1161, BUG-1010's lever.

---

## View track

### Where we are
STOP last session. Counter 1 / 4. M6 5 / 10 (the rest owner-blocked). **Unblocked now: M7-V1** (`UnitDef.Scale` on `main`).

### Current session plan: M7-V1, feature, QA standard (confirmed at the 2026-11-10-0146 PLAN)

**TASK_ID:** M7-V1 · **TASK_TITLE:** Draw each unit at its data `scale`

**Goal.** M7's first bullet, the view half: "1.5× visuals". `UnitViews` builds each type's mesh at `UnitDef.Scale` × today's capsule
(docs/02 "Combat / Stats": the visual scale may be larger than the collision radius; the Teblor page: units 1.5×, war-dogs normal),
read from the sim's data as `Radius` is read today (`game/scripts/UnitViews.cs` lines 131-138: `BodyHeight(def.Radius)`, the
`CapsuleMesh` per type); no ViewApi change, no sim change.

**Scope.** The capsule mesh's radius and height × `def.Scale`; the selection ring (`SelectionRings.cs`), the target ring
(`TargetRing.cs`), the health bar's height and width follow the scaled size; corpse discs / death fade follow it; the minimap dot does
not (stays one size); the click / drag-select hit test keeps the sim's `Radius` (a bigger body must not become easier to click:
Producer default, say so in docs/03). A `--screenshot` look at a 1.5× unit beside a 1.0× one on a **forged data copy**, the pattern
`game/tests/QaM5V2Test.cs` `Forge` uses (copy the shipped `game/data` to a temp dir, set one Malazan unit's `scale`, load from it);
the test scene under `game/tests/` (`ScaleViewTest.tscn`). **OUT:** any `game/data` edit (test copies only); a "Regenerating" HUD
cue (not until the owner asks); real models (the art call); the Teblor buildings' 1.25× (a models-task matter); the pack's "×3"
in the queue strip (after M7-2).

**Acceptance criteria.** 1. A unit at `scale` 1.5 is drawn 1.5× the capsule in every dimension; one at 0.5 is half; 1.0 unchanged
(screenshots: 1.5 beside 1.0, and 0.5 beside 1.0, looked at). 2. Rings and health bar scale with it; the bar stays readable at 0.5
and 3.0 (screenshot at 3.0). 3. Shipped data (every unit 1.0) renders as before: a scene test asserting every type's mesh radius /
height equals today's formula, and the smoke. 4. Clicking / box-selecting uses the sim's `Radius`, not the drawn size (a test row on
the forged copy: a click just outside the 1.0 radius of a 1.5× unit selects nothing). 5. No sim read beyond `UnitDef.Scale`; the
per-frame path allocates nothing new (the scale is applied at mesh build, not per frame). 6. `ShellTest` / `UnitViewsTest` rows; smoke
PASS; docs/03 "Implementation (M7-V1)" (the formula, the hit-test rule) + one docs/02 line under "Combat / Stats".

**Design references.** docs/02 "Combat / Stats" (visual scale vs collision radius); docs/factions/teblor.md "Art notes" (units 1.5×,
war-dogs normal-scale); docs/03 "Implementation (M7-1)" (`UnitDef.Scale` 0.5-3.0, never read by the sim); docs/03 "Dumb views".

**Tests required.** `game/tests/ScaleViewTest` (criteria 1-4 on the forged copy; the shipped-data formula row); the existing
`UnitViewsTest` / `ShellTest` green; smoke PASS.

**Constraints.** CLAUDE.md rules 1, 7 (views hold no gameplay state; the scale is read once from `GameData`); no `game/data` file
under the view's diff; no ViewApi write; the developer must not kill the sim's or data's `testhost` / Godot. Bug ids from **BUG-1177**.

**QA focus (standard).** The hit test at 1.5× and 3.0× (a drawn body outside the sim radius must not select, drag-select must still
use the sim radius); the health bar and rings at 0.5 (readable, not overlapping the neighbour's); a 3.0× unit beside a building (the
capsule may overlap the building's mesh: say so, no fix); corpse / death fade at 1.5×; the minimap unchanged; the per-frame allocation
row with 300 units of mixed scale; the shipped data's screenshot against the previous session's (pixel-identical for the unit meshes).

### Next task candidates
- An owner item from the inbox (the art call, ".NET 10 installed", templates, sound notes): a feature session, QA standard.
- The view's hardening at 4 / 4: BUG-1126, BUG-1147 item 1, BUG-1115 item 4, BUG-0860 / 0830 / 0890 / 0861.
- Otherwise STOP (cheap) once M7-V1 is on `main` and no other M7 view work exists until the Teblor models.

---

## Data track

### Where we are
D-R35 on `main` (the hardening; counter 0 / 4). BUG-1181 fixed; BUG-1182 S3 / BUG-1183 S4 open (docs / comments). M7-1's `scale` /
`regen` schemas are on `main`; `spawnCount` / `onHit` come with M7-2.

### Current session plan: D27, feature, QA standard (confirmed at the 2026-11-10-0146 PLAN)

**TASK_ID:** D27 · **TASK_TITLE:** Tune Hard so it beats Normal on Vathar Crossing too (BUG-1163), Hard values only

**Goal.** BUG-1163: the 42-match Hard v Normal tally is 29 / 11 / 2 with Vathar Crossing level at 7 / 7 (Raraku 10 / 4, Pale
Hills 12 / 0 / 2). Move only Hard's `ai.json` values so Hard is ahead on every map with no-results fewer than 5, without touching
any Normal / Easy value. **PLAN facts:** the openings schema has **no per-difficulty keys** (`id`, `displayName`, `description`,
`buildOrder`, `compositions`, `attack` only), so the levers are `difficulties.hard`'s nine keys: `waveSizeMultiplier` 1.25,
`gatherBonus` 0.25, `expandAfter` 300, `counterStrength` 1, `buildOrderSlack` 0, `focusFire` / `pullBackWounded` / `useAbilities`
true, `thinkInterval` 0.5. **Do not lower `thinkInterval`** (it is the AI's CPU cost; `AiPerfTests`' 8-seat bound) and do not turn
a boolean off (the Hard tooltip in `game/data/common/ui.json` `difficulty_hard_tip`, a sim-track file, promises them). The owner's
`counterStrength` answer has not landed: measure with today's 1.0. If the best lever needs a value the tooltip's text doesn't
cover (it prints `gatherBonus` as `{0}%`, so that one is free), write a Request for the sim track rather than editing `ui.json`.
**Measurement budget:** the base tally is in BUG-1163's file (don't re-run it); on the branch run the **14 Vathar rows first** per
candidate (at most three candidates), then the full 42 **once** on the chosen one. A candidate that flips Vathar but costs Raraku or
Pale Hills a win is reported, not shipped, unless the total still beats 29 / 11 / 2.

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
value and does not move the data hash, so the two may run in one session; D27 is the only golden regen this session), rule 48. QA
tier standard (scenario numbers). The owner's `counterStrength` answer, if it lands in the inbox, is applied first and the tally
measured with it. Files: only the two `ai.json` (Hard keys), `docs/factions/*.md` Hard lines / Hard-seat cells, the golden header,
`sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. Bug ids from **BUG-1184**.

**QA focus (standard).** `git diff` of both `ai.json` files shows Hard keys only; the 8 `NonHardMatchPinQaTests` digests and every
Normal v Normal / Easy pin byte-identical; the tally's per-seed Vathar table (both Hard seats) against BUG-1163's, and that no map
got worse by more than the Vathar swing; a Hard v Hard mirror on Vathar still ends (no new stall); `AiPerfTests` 8 seats alone
(rule 42) if `thinkInterval` or `expandAfter` moved; the golden header regenerated exactly once with the reason in
`GatherWedgeQaTests.SameGameDataHashes`; the page's Hard cells match the data both ways (the `Content` pins).

### Next task candidates (after D27)
1. **The Teblor roster, part 1** (`game/data/factions/teblor/` `faction.json` with `regen` 1 HP/s / 6 s, `units.json` with `scale`
   1.5 and the page's 2-3 pop costs, `buildings.json`, `techs.json`, `ai.json` openings; war-dogs only once M7-2's `spawnCount` /
   `onHit` are on `main`; QA standard; docs/factions/teblor.md's tables as the source; the review table with every name and
   description quoted).
2. The data's next hardening at 4 / 4: BUG-1182 (a dated re-word of the untabled figures), BUG-1183.
3. The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780;
   `counterStrength`).
