# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-10-0146** (session 1 / 8 of 2026-11-10; the next session is 2 / 8).
Base: `main` after the sim + view + data merges of 0146 (M7-2 at 3add793, M7-V1 at c015bcb, D27 at d950ffa, plus the Producer's
BUG-1169 patch at the merge). Last session: sim **M7-2**, view **M7-V1**, data **D27** all ACCEPTED (0 / 0 / 1 fix rounds). **M5 8 / 9
(criterion 6 owner-only), M6 5 / 10, M7 0 / 4 (bullets 1-3 built as rules, nothing shipped).** Counters: **sim 2 / 4, view 2 / 4,
data 1 / 4**. Bug ids next session: **sim 1171+, view 1179+, data 1186+**. Open S1 / S2: none. The PLAN step re-checks the inbox
first (New was empty at 0146).

**PLAN 2026-11-10-1236 (session 2 / 8, base 0416dcc = `origin/main`): all three drafts below confirmed as the current session plan**
(sim **M7-3** feature / QA full; view **M7-V2** feature / QA standard; data **D-R36** hardening / QA light). Inbox New empty. Premises
re-checked on the base: `DataLoader.cs:57` sorts the faction folders ordinally (the renumbering is real); `PackQaTests` lines 377 / 730
carry the BUG-1167 / 1168 skips; `game/data/common/` has no `factions.json`; `ProjectileViews.LaunchHeight` is a 1.2 constant;
`QaM6V18Test.cs:103` pins `HMAP` right after the preset; docs/03:2978 holds the correction paragraph M7-3 replaces; `teblor.md` has
building ids and no unit / tech ids. **Refinements:** (sim) the literal pair `"malazan", "whirlwind"` is in **42 test files, about 28
sim-owned** (also `Stress/`, `ViewApi/`, `QA/DefenseQaTests` / `EndGameH6QaTests` / `FarExpandQaTests` / `ReserveQaTests` /
`SurroundQaTests` / `TeamAiQaTests` / `M6V13QaTests` / `ReplayQaTests`), not the ten the draft lists: criterion 6 applies to every
sim-owned file; a file whose scenes are about those two factions keeps the pair with one line of reason in the report; the sweep is
mechanical and inside the budget. (sim ↔ data) M7-3 may add `TestSim.ShippedFactions`; D-R36 must **not** edit `TestSim.cs` /
`TestDataDir.cs` (sim-owned) and reads `TestSim.Data.Factions` (on `main` already), so the two merge without a conflict. (view) nothing
new. Merge order sim → view → data; docs/03 will conflict at the "Implementation" anchor (keep both, the sim's M7-3 first).

## Standing rules (1-51)

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
**New: 49** (BUG-1169): no test pins the golden replay's header `data-hash` (or the shipped `ContentHash()`) as a literal; a test that
ties the two compares them (`GatherWedgeQaTests.SameGameDataHashes` is the model); a session-time "the hash did not move" claim
lives in QA's report, not in a permanent row. **50** (BUG-1178): a sim task that changes a save or replay format names the view
scenes that pin those bytes (`QaM6V18Test`, `ShellTest`'s save rows, `QaVh*` save rows) and the view track re-checks them the same
session or the next. **51** (BUG-1184 item 4): a data brief that re-pins Hard-seat (or any sim-owned) digests lists the sim test
folders its re-pins may touch (`Ai/`, `Economy/`, `Data/`, `Vision/`, `Save/`, `QA/`, `Replays/`), the pin classes by name, and
says the `Stress` pin classes must be run (the default loop skips them).
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests.SameGameDataHashes` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.

## Watch-outs (all tracks)
- **Three test hosts at once (sim + view + data)**: wall-clock Perf and scene rows flake under load; rule 42 before filing. The
  default loop is **~25-30 min** now (8,490 rows); the full non-Perf run ~1 h 50 m. **BUG-1170:** four Perf rows failed *alone* at
  1eb231d on this PC (the machine was slow after hours of three-track load); the sim's first step next session measures them cold.
- QA runs the full non-Perf **once** per session (at the re-check, on the fixed commit, **with its own files in**).
- Merge order sim → view → data; `studio/bugs/README.md` and `studio/qa/coverage.md` are the usual conflicts, both sides kept;
  **this session docs/03 also conflicted** (sim and view both appended an "Implementation (M7-x)" section at the same anchor;
  keep both, the sim's first). Next session the sim's M7-3 and the view's M7-V2 both add one: expect it again.
- **No `game/data` file may be touched by two tracks:** M7-3 adds `game/data/common/factions.json` (sim-owned, schema + the
  shipped two-entry list) and touches no faction file; M7-V2 edits nothing under `game/data`; D-R36 edits nothing under `game/data`.
- A developer must never kill another track's Godot or `testhost` process.
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Since D27: the golden header's data hash is `3E33BCE5D418EC9B`** (`k` lines unchanged since M6-SH1). M7-3 must not move it
  (the order file is not hashed: the ids it fixes already are). **Every recording made before 0146 refuses to play.**
- **Pinned matches on `main` after D27** (Normal / Easy pins unchanged since M5-H6; Hard-seat pins re-measured by D27): the Hard v
  Normal tally **32 / 8 / 2** (Raraku 10 / 4 / 0, Pale Hills 11 / 1 / 2, Vathar **11 / 3 / 0**); `StandardPresetPinQaTests` 7 digests
  (five Hard-seat rows at D27's values); `HardKnobsPinTests` / `HardKnobsMatchTests`, `ReserveTwinTests`, `SharedVisionFfaPinTests`,
  `ScoutManagerTests` (the scout leaves at tick **311**), `GatherBonusTests` (169 / 191 ticks), the premap AI replay (81 commands);
  unchanged: `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA 56,896 / 2v2 26,070),
  `MapSlice2StressTests` (Pale Hills 2 / 5 = 49,565 / 55,485), QA's `NonHardMatchPinQaTests` (8), the 8
  `CounterCompositionTests.TheNonHardPins_*`, the page tables' Normal cells, `D21OpeningsQaTests` / `DH6ThresholdQaTests`,
  `HuntStressTests` (FFA seed 2 75,382 / seed 3 53,892 / 2v2 seed 1 64,226), the golden's `k` lines. QA's M7-2 pack digest
  658D1EF784AA8197 (3 processes), the M7-1 regen digest 96013817C26CCFA1.
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), QA's `FarExpandProbeQaStressTests` Pale
  Hills FFA seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), QA's `M5H5HuntAfterDefeatQaTests.A2v2_*`
  (BUG-1149), `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows in `EndGameH6QaTests`, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, QA's
  `RegenQaTests` rows for BUG-1165 (2) and BUG-1166 (2), QA's `PackQaTests` rows for BUG-1167 (1) and BUG-1168 (1); the view's
  `QaM7V1Test` zoom-40 1.5 head row (inside the 12 px floor); **`QaM6V18Test` red on `main` (BUG-1178, M7-V2 fixes it)**.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's landing order:** sim M7-3 (append-only ids) → data D28 (the Teblor roster) → data / sim balance. Today a
  `teblor` folder sorts before `whirlwind` in `DataLoader` (folders and unit / building keys are ordinal-sorted) and would renumber
  every Whirlwind type id, i.e. every digest, every page cell's hash and the golden's `k` lines. Nobody adds the folder before M7-3.

---

## Sim track

### Where we are
M7-2 on `main`: `UnitDef.SpawnCount` / `ItemHalfPop` / `HitPassive`, `PassiveTrigger.OnHit`, the all-or-nothing pack spawn, the
direct-hit `onHit` rule; no format change; QA's `PackQaTests` / `PackQaStressTests`. Counter 2 / 4. Debt: BUG-1167 / 1168 S3
(M7-3's riders), BUG-1170 S3 (measure cold), BUG-1165 / 1166 S4, the three `SaveFormat` alias names.

### Current session plan: M7-3, feature, QA full (draft; the PLAN step confirms)

**TASK_ID:** M7-3 · **TASK_TITLE:** Make room for a third faction: an explicit, append-only faction / type id order, the pack's
per-unit worth (BUG-1168) and the hard-cap load check (BUG-1167)

**Goal.** The Teblor roster (data, D28) must land as data only without moving any Malazan v Whirlwind digest. Today
`DataLoader.LoadAll` sorts the faction folders (`Array.Sort(folders, Ordinal)`, "faction id = index in this order") and sorts every
per-faction key table globally (`unitKeys`, the buildings' `keys`, the techs' / abilities' `sorted`), so a `teblor` folder and
`teblor_*` keys land between `malazan` and `whirlwind` and renumber every Whirlwind type: every state hash, digest and golden `k`
line would move, for M8 (Shadow) and M9 (Andii) again. The fix is an explicit order the data owns and new factions append to.
Beside it, the two pack bugs the Teblor war-dog row and AI need before they ship, and BUG-1170's cold measurement first.

**Scope.**
- **Step 0 (report, no code):** the four BUG-1170 Perf rows alone after a cold idle (`AiPerfTests.EightHardSeats_MidWave_*`,
  `VisionGatePerfTests.Brawl250v250_*`, `PassiveScaleQaTests.EmptyTick_2500OneOwner_*(zealot, True)`,
  `EndGameH6Round2QaTests.ASweepRetarget_*`): green → the bug closes as a machine state with the numbers; red → profile the sweep
  re-target first (67 us against 50, measured 16.4 us at M5-H6); a re-bound only with the regression explained (CLAUDE.md "Testing
  rules"), in the report and the bug file patch.
- **`game/data/common/factions.json`** (Producer default; the alternative, an `order` integer in each `faction.json`, spreads one
  decision over N files): `{ "order": ["malazan", "whirlwind"] }`. Rules: every folder under `factions/` must be listed and every
  entry must have a folder (one error each, naming the id); a repeat is an error; the list's order is `GameData.Factions`' index
  order **and** the order of every per-faction id table: unit types, buildings, faction techs, faction abilities (common techs /
  abilities / statuses keep today's rule), each faction's keys ordinal-sorted *within* the faction. For the shipped two this is
  bit-identical to today's ids (prove it: criterion 2). **Not hashed** (`GameData.ContentHash` already hashes the keys in id
  order, so the order is in the hash through them): the golden header `3E33BCE5D418EC9B` does not move; no golden regen.
- **BUG-1168:** a per-unit worth, `UnitDef.UnitCostGold` / `UnitCostWood` = the item's cost / `SpawnCount` (integer division;
  the remainder is dropped and documented: a 120 / 0 pack of 3 is 40 / 0 a dog), read by every per-unit worth sum (`AiPlayer.ArmySize`,
  `MilitaryManager`'s `minArmyCost` sum, `DefenseManager`'s worths, `AiPlayer.IntruderWorth`, `MatchStats.Sample`'s army value);
  `AiPlayer`'s queue census `Queued[type] += SpawnCount`; a build-order `train` step's `count` counts **items** (document in docs/03
  "For the data track (M7-2)": "train 1 war-dog pack = 1"); correct docs/03's M7-2 "The AI" bullet to the fixed statement (the
  Producer's correction paragraph goes). Shipped data has no pack, so no shipped number or pin moves (prove with the pin sweep).
- **BUG-1167:** the loader refuses `HalfPop * SpawnCount > rules.HalfPopCap` (one error at `units[i].spawnCount`, or `.pop` when
  `spawnCount` is absent, naming both numbers), in the unit pass once `rules.json` is read.
- **Tests' two-faction literals in sim-owned folders** (`Ai/AbilityManagerTests`, `Ai/AiScenes`, `Ai/CounterCompositionTests`,
  `Ai/FarExpandTests`, `Ai/ReserveTwinTests`, `Combat/OnHitPassiveTests`, `DataConversionTests`, `DataValidationTests` (its
  `["malazan","whirlwind"]` key assertion becomes the order file's), `QA/AbilityManagerQaTests`, `QA/AiOpeningsQaTests`): a shared
  `TestSim.ShippedFactions` (the data's keys in order) or the explicit pair where the test means those two factions (the
  developer's call per file, said in the report). The `Content/` and `QA/Content/` folders are the data track's (D-R36 does them).
- Docs: docs/03 "Data schema" row for `common/factions.json` + "Implementation (M7-3)" (the id rule, what is and isn't hashed,
  the worth rule, the hard-cap check), docs/02 "Factions" one sentence ("a new faction is a folder and a line in the order file").
- **OUT:** any faction file; any Teblor data; the Frenzy aftermath hook (M7-4); the view's setup list (it reads `Factions` in
  index order already: a third faction appears by itself); the `SaveFormat` alias clean-up; BUG-1165 / 1166 (the hardening).

**Acceptance criteria.**
1. `common/factions.json` loads: the shipped list; a missing entry (a folder not listed), a listed id without a folder, a repeat
   and a non-string entry are one error each naming `factions.order[i]` / the folder; `DataLimits.MaxFactions` (8) bounds it.
2. Shipped ids bit-identical: a test pins today's id table (every unit, building, faction tech and ability key → index, generated
   once from 1eb231d and written as a literal table or a digest) and it passes on the branch; `ContentHash()` equals the golden's
   header (dynamic compare, rule 49); the pin sweep unchanged; the golden untouched.
3. The proof on a copy: `TestDataDir.CopyOfShipped()` plus a minimal `teblor` folder (a `faction.json`, a `units.json` with one worker,
   a `buildings.json` with the hall and a house, no `ai.json`) appended to the order: a 3,000-tick Malazan v Whirlwind AI match hashes
   tick for tick as on the shipped data, and two `StandardPresetPinQaTests` rows (one Hard-seat, one Normal) and two
   `NonHardMatchPinQaTests` digests are identical on the copy; the copy's `ContentHash()` differs (new keys). The same copy with
   `teblor` **first** in the order renumbers everything (the digests move): the order is the rule, not the folder name.
4. BUG-1168: QA's `PackQaTests.APacksArmyValue_IsTheItemsCost_NotSpawnCountTimesIt` un-skipped and green (74 for one 74-cost pack
   of three); an AI row: a Normal seat on the pack copy launches its wave at `minArmyCost` counted per dog at item / 3 (a scene where
   the x3 count launched early and the fixed count does not); `Queued` counts a queued pack as 3; the build-order `count` row.
5. BUG-1167: `APackAboveTheHardCap_IsALoadError` un-skipped and green; a single unit at pop 101 is the same error at `.pop`.
6. The literals: no sim-owned test file (outside `Content/`, `QA/Content/`) lists `"malazan", "whirlwind"` as a literal pair unless
   the test is about those two factions (the report lists the kept ones with a reason).
7. BUG-1170 measured cold and reported (step 0) before any other work; the Perf suite's verdict under rule 42.
8. Determinism unchanged (the pin sweep, the golden), no per-tick allocation (the worth helpers are reads; `Queued` is per think), no
   Godot / `Dictionary` / `HashSet` / `Random` / `DateTime` / LINQ in sim code; player-facing text only in data; docs as in Scope;
   build 0 / 0; smoke PASS; the default loop green.

**Design references.** CLAUDE.md rule 6 ("a new faction is data + models, not new systems") and rule 4; docs/03 "Data schema",
"Implementation (M7-2)" (the pack's cost / pop rule), "Implementation (M5-1)" (the AI's census), "Testing strategy" (the golden's
header rule, M6-SH1); docs/02 "Factions"; BUG-1167 / 1168 / 1170's files.

**Tests required.** `Data/FactionOrderTests` (criteria 1-3), `DataValidationTests` rows, the id-table pin (criterion 2), QA's two
un-skipped rows + the AI wave row + the census row (criteria 4-5), the pin sweep and golden green unchanged.

**Constraints.** CLAUDE.md rules 2, 4, 5, 6, 8; rule 41 (no AI value; the order file is a common schema, sim-owned; no faction file);
rule 43 does not apply (no Normal behaviour change on shipped data: no pack ships); rule 49; the developer cannot write `studio/**`
(bug patches in the scratchpad, BUG-1167 / 1168 / 1170 set fixed or closed with the proof). Bug ids from **BUG-1171**. Size: the
design is clear (~700-1,000 lines incl. the literal sweep); if the per-faction id tables for techs / abilities turn out to be shared
across factions in a way the order can't split cleanly, ship units + buildings under the order, say so, and leave the rest with a
bug id.

**QA focus (full).** The id rule under every order (shipped, teblor appended, teblor first, a two-faction list with the folders
swapped), the error rows; the copy proof with a *full* minimal Teblor (every file incl. `ai.json`) and an AI Teblor seat playing
(nothing crashes; the Malazan v Whirlwind digests still hold); the worth rule on `spawnCount` 8 at 7 / 0 (integer division: 0 / 0 a
dog, document), on the defense and reserve sums, on the stats graph's sample; the hard-cap check at exactly the cap (200 half: loads)
and one above; the pin sweep; the golden `k` lines and header unchanged; a save made on the shipped data loads on the branch (no
format change); determinism across 3 processes; 0 B per think with packs queued; the four Perf rows alone (rule 42).

### Next task candidates (after M7-3)
1. M7-4: Blood-oil Frenzy's aftermath (a "when it ends, apply" hook; the decision on the store's one-entry-per-id merge: a passive
   Frenzied (the Zealot) must not slow on expiry, the Shaman's cast one must; Producer default: the aftermath is a field of the
   *applying effect* (`applyStatus.onEnd {status, magnitude, duration}`), carried in the status entry as its source's effect id, so
   only entries applied by an effect with `onEnd` slow when they expire or are replaced).
2. The sim's hardening at 4 / 4: BUG-1165 / 1166, the `SaveFormat` alias names, BUG-1161, BUG-1010's lever, the README / STATE
   open-count reconciliation.

---

## View track

### Where we are
M7-V1 on `main`: every unit type drawn at `radius × scale`; rings, bar, corpse and cues follow; the hit test keeps the sim radius.
Counter 2 / 4. Debt: BUG-1177 S4 (item 1 M7-V2's), BUG-1178 S3 (M7-V2's), BUG-1126, BUG-1147 item 1, BUG-1115 item 4, BUG-0860 /
0830 / 0890 / 0861 (the hardening).

### Current session plan: M7-V2, feature, QA standard (draft; the PLAN step confirms)

**TASK_ID:** M7-V2 · **TASK_TITLE:** Show a pack's count in the production queue strip, fix the red M6-V18 scene, and let shots
follow the drawn height

**Goal.** M7 bullet 3's view nicety now that `UnitDef.SpawnCount` is on `main`: a queue square for a pack item reads its initial plus
"×N", so the player sees one order is three dogs. Two riders in the same files' neighbourhood: BUG-1178 (`QaM6V18Test` red on
`main` since format 10; a test-only fix) and BUG-1177 item 1 (`ProjectileViews.LaunchHeight` fixed at 1.2 m whatever the scale).

**Scope.**
- `ProductionQueueStrip`: a unit item whose type's `SpawnCount` is above 1 shows `<initial>×N` (a number format composed in C#,
  like `PopText`'s "9.5": not player-facing prose, no `ui.json` change; say so in docs/03); written only when the item changes
  (today's rule: a steady frame allocates nothing); the head's bar unchanged. A test copy with a Malazan unit at `spawnCount` 3
  (the `QaM5V2Test` `Forge` pattern) in `game/tests/` (`QueuePackTest.tscn`): the square's text, the cancel click still refunds the
  pack, the strip's 0-B frame.
- **BUG-1178:** `game/tests/QaM6V18Test.cs` expects format 10's empty map-id string field between the preset and `HMAP`; its other
  rows that name formats 4-9 re-read against docs/03 "Save format" (every save is format 10); the full scene loop green (87 / 87).
- **BUG-1177 item 1:** `ProjectileViews.LaunchHeight` becomes `1.2 m × the type's scale` for the shooter's launch point and the
  target's hit flash (Producer default: shipped visuals pixel-identical since every shipped scale is 1; the alternative, a share of
  `DrawnHeight`, would move the catapult's launch point today). Docs/03 "Implementation (M7-V1)" gains the line.
- **OUT:** any `game/data` file (test copies only); BUG-1177 item 2 (the click rule: the owner's taste); the "Regenerating" HUD cue;
  the Teblor models; any ViewApi change (`SpawnCount` is a `UnitDef` field, read as `Scale` is).

**Acceptance criteria.** 1. On the forged copy a queued pack shows "H×3" (or the type's initial) and a single unit shows the initial
alone; the text is written once per change (a 60-frame steady strip allocates 0 B). 2. A click on the pack square cancels it with the
full refund and releases the reservation (the sim's `CancelTrain`; read the ledger). 3. `QaM6V18Test` green at HEAD and the scene loop
87 / 87 (`scene-loop.ps1 -SkipPlayable`). 4. A shot from a 0.5 unit leaves at 0.6 m, from a 3.0 unit at 3.6 m, the hit flash at the
target's `1.2 × scale`; shipped data's `--screenshot` of a shooting scene pixel-identical to `main`'s (as QA did for M7-V1). 5. No
per-frame allocation (the strip, projectile views) with 300 mixed-scale units and 20 pack items queued; smoke PASS; docs/03
"Implementation (M7-V2)" (+ the M7-V1 line); `UnitViewsTest` / `ShellTest` green.

**Design references.** docs/03 "Implementation (M3-V3)" (the queue strip), "Implementation (M7-2)" (one item = N units, the cancel
refund), "Implementation (M7-V1)", "Save format" (10); docs/02 "Population" (the pack's pop); BUG-1177 / 1178's files.

**Tests required.** `game/tests/QueuePackTest` (criteria 1-2, 5's strip row); `QaM6V18Test` fixed; a `ProjectileViews` row in
`CombatViewTest` or `ScaleViewTest` (criterion 4); the scene loop; smoke.

**Constraints.** CLAUDE.md rules 1, 7, 8 (the "×N" is a number format: say so); no `game/data` edit; no ViewApi write; the
developer must not kill the sim's or data's `testhost` / Godot. Bug ids from **BUG-1179**.

**QA focus (standard).** The strip with a pack at every queue position (head / middle / last of `MaxItems`), a pack cancelled from
the middle (the squares shift, the texts re-written once), a pack item under a tech item; the 0-B frame; `QaM6V18Test` on a clean
clone of `main` + the fix; the full scene loop twice; the shot heights at 0.5 / 1.5 / 3.0 incl. a lob (the catapult: unchanged);
the shipped-data screenshot against `main`.

### Next task candidates
- An owner item from the inbox (the art call, ".NET 10 installed", templates, sound notes): a feature session, QA standard.
- The view's hardening at 4 / 4: BUG-1126, BUG-1147 item 1, BUG-1115 item 4, BUG-0860 / 0830 / 0890 / 0861, BUG-1177 item 2 if the
  owner asks.
- Otherwise STOP (cheap) until the Teblor roster is on `main` (then a "Teblor in the setup" look + the 1.25× buildings' placeholder).

---

## Data track

### Where we are
D27 on `main`: Hard `gatherBonus` 0.4, the tally 32 / 8 / 2, BUG-1163 fixed; BUG-1184 / 1185 fixed in the round. Counter 1 / 4.
Open debt: BUG-1182 S3, BUG-1183 S4 (docs / comments). **The Teblor roster (D28) is blocked until the sim's M7-3 is on `main`** (a
`teblor` folder today renumbers every Whirlwind type id: see Watch-outs).

### Current session plan: D-R36, hardening (early, the only unblocked data work), QA light (draft; the PLAN step confirms)

**TASK_ID:** D-R36 · **TASK_TITLE:** Make the Teblor page pin-ready for the roster, make the content pins faction-agnostic, and
clear BUG-1182 / 1183

**Goal.** When M7-3 lands, D28 should be one session: the files written from a page that already has every table the content pins
read (`Content/UnitContentTests`, `BuildingContentTests`, `TechContentTests`, `AbilityContentTests`, `PassiveContentTests`,
`AiContentTests` J / K, `AiAdaptsContentTests`), in the Malazan / Whirlwind pages' shape. Today `docs/factions/teblor.md` lacks unit /
tech ids, the `Targets` column, a Techs table, the Difficulty table, the `ai.json`-shaped build order (openings with `at` triggers,
`train` counts, compositions, `attack` thresholds) and the status / passive rows. Plus the data's open debt.

**Files (the only ones):** `docs/factions/teblor.md`, `docs/factions/malazan.md` (BUG-1182's four figures only), `sim/Rts.Sim.Tests/
Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. **No `game/data` byte** (the hash stays 3E33BCE5D418EC9B). Not docs/02 / 03.

**Scope.**
- `teblor.md`: the Units table with `Id` and `Targets` columns (ids `teblor_gatherer`, `teblor_warrior`, `teblor_javelin_thrower`,
  `teblor_destrier_rider`, `teblor_shaman`, `teblor_breaker`, `teblor_war_dog`; the pack row: `spawnCount` 3, `pop` 1 each, 120 / 0
  per item, train 24; `scale` 1.5 on every unit but the war-dog), the `giant` class on the combat units, `regen` 1 HP/s / 6 s in the
  faction bonus, a **Techs** table in the Malazan shape (the common techs' availability at the Bloodwood Carver, Blood-oil
  Anointing: Age II, 200 / 150, 45 s, +2 armor on Warrior / Destrier Rider, Frenzy duration +4 s, id `teblor_blood_oil_anointing`),
  the Abilities table with the `applyStatus` fields the schema has today (Frenzied +40 % attack speed / +20 % damage, 8 m, 0.5 s
  cast, 40 s cooldown, 10 s; the aftermath slow marked "M7-4, not in the data until then"), the Hamstring passive row (`onHit`,
  `slowed` 0.2, 2 s), the Buildings table (ids exist; the template's footprints / HP / costs stated as "template"), the Difficulty
  table (the same rows as the other pages, Hard 40 %), and **"AI build order (Normal)"** in the `ai.json` shape with one opening
  `standard` (the sketch's steps as `at: {pop | time}` + `do`, a composition by pop 45 / 15 / 15 / 10 / 10 / 5, the attack
  thresholds 8 then 24 army pop, retreat 0.4 HP) and the "How the AI adapts" sub-section's counter answers (`ai.counters` per enemy
  kind: Line → Shaman / Breaker; Ranged → Destrier Rider / War-dogs; Shock → Warrior; Caster → Destrier Rider / Javelin Thrower;
  Siege → Warrior / Destrier Rider: the data track's proposal, marked so).
- **Every player-facing `displayName` / `description` the roster will carry, quoted in the page** (units, buildings, the tech, the
  ability, the opening) so the owner reviews the words now and D28 copies them.
- `Content/**` and `QA/Content/**`: the faction list read from `TestSim.Data.Factions` (keys in order) instead of the literal pair,
  so a third faction's content is pinned the session it lands; a page without a section a pin reads is a clear failure message
  naming the page and the section (so D28's first run tells the developer what is missing, not a null).
- BUG-1182 (the dated re-word of the four untabled Malazan figures: seed 18 10:50, seed 23 three waves from 12:29 / falls 18:49,
  seed 25 12:22, seed 19 502 gold, "measured 2026-11-07 / re-measured 2026-11-09"), BUG-1183 (the 145-character line; the
  seed-19 comment's "(today 10:34 / 502)").
- **OUT:** any `game/data` file; any Malazan / Whirlwind number; docs/02 / 03; the Teblor roster itself (D28).

**Acceptance criteria.** 1. `teblor.md` has every section and column the seven content pin classes read on `malazan.md` (a
checklist in the report, section by section). 2. The content tests iterate the data's factions and still pass 2 / 2 on the shipped
data; a mutation (a `malazan.md` cell) reads red both ways as today. 3. The review table for the owner: every quoted name and
description, and the proposed counters table. 4. BUG-1182's four figures re-worded with their dates; BUG-1183's two items. 5.
`Content` + `QA.Content` + `DataValidation` + `DataContentHash` green; no `game/data` byte in the diff; build 0 / 0.

**Design references.** docs/factions/teblor.md (the source of every number), docs/factions/malazan.md (the shape), docs/02 "AI
opponent" (the difficulty table), docs/03 "For the data track (M5-1)" (the `ai.json` shape), "For the data track (M7-2)" (the
pack row and the `onHit` shape), "Implementation (M7-1)" (`scale`, `regen`), BUG-1182 / 1183's files.

**Tests required.** The content classes faction-agnostic; a `Content/PagePinReadinessTests` row that lists the sections a page must
have for every faction in the data (passing for the two shipped ones); the four filters green.

**Constraints.** The data track's files only (above); no `game/data` byte; QA light; the developer cannot write `studio/**`. Bug ids
from **BUG-1186**.

**QA focus (light).** The checklist against the seven pin classes' actual regexes (not the Malazan page's prose); the mutation both
ways; the quoted names against the codename table at the page's end (internal codenames stay; no release name leaks into a
`displayName`); the counters proposal's consistency with the units' bonuses (Warrior ×1.25 vs Mounted answers Shock; Destrier
×1.5 vs Light answers Ranged / Caster).

### Next task candidates (after D-R36)
1. **D28, the Teblor roster** once M7-3 is on `main`: `game/data/common/factions.json` gains `"teblor"` (appended), `game/data/
   factions/teblor/` `faction.json` (`regen` 1 / 6, colours), `units.json` (`scale` 1.5, `giant`, 2-3 pop, the war-dog pack row with
   `spawnCount` 3 + `onHit`), `buildings.json`, `techs.json`, `ai.json` (the `standard` opening); the page's tables pinned both ways;
   the golden header regenerated once (new keys: the hash moves; `k` lines must hold, that is M7-3's guarantee); QA standard: a
   Teblor AI seat plays 12,000 ticks on each map without a refusal streak, a Teblor v Malazan and v Whirlwind twin hash-equal, the
   setup lists three factions; the review table with every name and description. Rule 51 applies (no re-pin expected: say so).
2. The owner's answers if any land (the D21 / D22 / D23 / D24 / D-H7 tables; the Shock pass; `goldShare`; BUG-0780; `counterStrength`).
3. The data's hardening at 4 / 4.
