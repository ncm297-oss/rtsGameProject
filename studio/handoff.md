# Handoff: brief for the next session

Confirmed by the Producer at the PLAN of session **2026-11-10-2116** (session 3 / 8 of 2026-11-10; base `fae4074` = `origin/main`).
Last session (1236): sim **M7-3**, view **M7-V2**, data **D-R36** all ACCEPTED (0 fix rounds each). **M5 8 / 9 (criterion 6 owner-only),
M6 5 / 10, M7 0 / 4 (bullets 1-3 built as rules, the sim has room for a third faction; nothing shipped).** Counters: **sim 3 / 4,
view 3 / 4, data 0 / 4**. Bug ids this session: **sim 1190+, view 1200+, data 1210+**. Open S1 / S2: none. Inbox New: empty at 2116.

## Standing rules (1-53)

Unchanged 1-51 (38: the Producer runs every suite on a scratch `git archive` / clone, never in a worktree; 39: generated map content
and QA's suites don't count toward a size budget; 40: a sim task that re-prints a pinned cell on a faction page may edit
`docs/factions/**` for those cells only; 41: when a sim task can move a measurement a data test allowance rests on, the sim brief
names the allowance and the data task that touches it re-measures on the sim's branch, or the two are not planned in the same
session; 42: a Perf failure counts only when the row fails alone in its own process after at least 30 s idle **and with no
non-studio load on the machine** (QA says what else was running); 43: every sim task that can change Normal AI behaviour names which
M5-5 rows it expects to move and re-measures the bound comments and docs/03's M5-5 table in the same commit, and a row that stops
ending is a finding, not a re-bound; 44: a session that dies after ACCEPT is re-verified on a scratch clone and integrated first, a new
task starts on a fresh branch; 45: a brief whose task changes Normal AI behaviour says what to do when a pinned criterion row stops
ending; 46: a branch held twice gets a terminal acceptance rule in its third brief, so no branch lives a fourth session; 47: QA's
re-check runs the default loop on the commit that includes QA's own suites, and a QA file that measures allocation declares
`[Collection(SerialCollection.Name)]`; 48: at PLAN the Producer checks every acceptance criterion against the brief's own out-of-scope
list; a figure the scope cannot move is "measure and report", never pass / fail; 49: no test pins the golden replay's header
`data-hash` as a literal (`GatherWedgeQaTests.SameGameDataHashes` is the model); 50: a sim task that changes a save or replay format
names the view scenes that pin those bytes and the view track re-checks them; 51: a data brief that re-pins sim-owned digests lists
the sim test folders and pin classes and says the `Stress` pin classes must be run; 52: a developer's report ends every test process
the developer started; a `testhost` or Godot left running in a worktree after the report is a finding; 53: the QA tier bounds the
suites QA runs: **light** = the task's filters + the default loop (`Category!=Perf&Category!=Stress&Category!=Soak`), never the full
suite; **standard** = the default loop + the Stress rows the task touches + the scene loop (view); **full** = the default loop with
Stress, then Perf alone under rule 42).
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests.SameGameDataHashes` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.

## Watch-outs (all tracks)
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rule 42 / 53). The default loop is ~25-30 min alone, ~45 min
  beside another track; the full non-Perf with Stress ~2 h 40 m. **BUG-1170:** the four Perf rows are still red at head and base, but
  QA's rerun had 31-52 % CPU from the owner's apps: no verdict; the sim's hardening (at 4 / 4, next session) measures once more.
- Merge order sim → view → data; `studio/bugs/README.md` and `studio/qa/coverage.md` are the usual conflicts, both sides kept;
  docs/03 conflicts when two tracks append an "Implementation" section at the same anchor (keep both, the sim's first).
- **Files no two tracks may share this session:** sim M7-3b touches **no `game/data` byte** and **nothing under
  `sim/Rts.Sim.Tests/Content/**` or `sim/Rts.Sim.Tests/QA/Content/**`** (those hold no digest literal: a row there that goes red
  on the branch is a finding to report, not a file to edit); view M7-VH1's only `game/data` file is **`game/data/common/ui.json`**
  (the three `setup.difficulty_*_tip` values); data D-R37 edits **only `docs/factions/teblor.md`, `docs/factions/malazan.md` (line
  417), `Content/**`, `QA/Content/**`** (not `TestSim.cs` / `TestDataDir.cs`, no `game/data` byte). **D28 (later) is the only task
  that adds `game/data/factions/teblor/**` and the order's third line.**
- A developer must never kill another track's Godot or `testhost` process; a developer ends its own (rule 52).
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Since D27: the golden header's data hash is `3E33BCE5D418EC9B`** (`k` lines unchanged since M6-SH1). **M7-3b moves both on purpose**
  (header and `k` lines: tech and ability ids renumber): one regen with the reason in `GatherWedgeQaTests`, and every digest pin
  re-measured (the list below). **Every recording made before M7-3b refuses to play once it lands, and so does every save** (the
  save header carries the data hash: `SaveError.DataMismatch`; a pre-M7-3b save holds tech / ability ids as indexes and could not
  load correctly anyway). No save-format or replay-format number changes.
- **Pinned matches on `main` after M7-3 (unchanged by it):** the Hard v Normal tally **32 / 8 / 2** (Raraku 10 / 4 / 0, Pale Hills
  11 / 1 / 2, Vathar 11 / 3 / 0); `StandardPresetPinQaTests` 7 digests; `NonHardMatchPinQaTests` 8; `HardKnobsPinTests` /
  `HardKnobsMatchTests`, `ReserveTwinTests`, `SharedVisionFfaPinTests`, `ScoutManagerTests` (311), `GatherBonusTests` (169 / 191), the
  premap AI replay (81 commands); `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA
  56,896 / 2v2 26,070); `MapSlice2StressTests` (49,565 / 55,485); the 8 `CounterCompositionTests.TheNonHardPins_*`; the page tables'
  cells; `D21OpeningsQaTests` / `DH6ThresholdQaTests` (outcomes only, in `QA/Content`: the data track's files); `HuntStressTests`
  (75,382 / 53,892 / 64,226); the golden's `k` lines; QA's pack digest 658D1EF784AA8197, the regen digest 96013817C26CCFA1, the Teblor
  FFA hash 6C448DFB90134A32 (`FactionOrderQaTests`), the four `FactionOrderTests` copy pins (Stress). **M7-3b re-measures every digest
  in this list that hashes a tech or ability id** (all of them, in practice: `age_ii` is in every match) and must show the *outcomes*
  (end ticks, winners, page cells) unchanged.
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); QA's
  `FactionOrderQaTests.ATeblorAbility_NamedNaturally_RenumbersTelasFire_Bug1171Symptom` (flip its first assertion with M7-3b); the view's
  `QaM7V1Test` zoom-40 1.5 head row. `QaM6V18Test` is green (BUG-1178 fixed).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's landing order:** sim M7-3 (done) → sim **M7-3b** (this session) → data **D28** (the whole roster incl.
  `abilities.json`; next session if M7-3b is on `main`) → sim M7-4 (the Frenzy aftermath) → data / sim balance → view (the setup's third
  row, the 1.25× building placeholder). Nobody adds the `teblor` folder before M7-3b is on `main`.

---

## Sim track

### Where we are
M7-3 on `main`: `common/factions.json` (append-only), unit / building ids by faction, `UnitDef.UnitWorth`, the hard-cap load error, the
literal sweep. Counter 3 / 4 (this session is the 4th feature session: the hardening comes next). Debt: BUG-1171 S3 (this task),
BUG-1172 S3 (its rider), BUG-1173 S4, BUG-1170 S3 (machine state), BUG-1165 / 1166 S4, the three `SaveFormat` alias names, BUG-1161 S4,
BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010, the README / STATE open-count reconciliation.

### Current session plan: M7-3b, feature, QA full (confirmed at the 2116 PLAN)

**TASK_ID:** M7-3b · **TASK_TITLE:** Put tech and ability ids under the faction order (BUG-1171) with one golden regen, and a sorted
lookup index for the loader (BUG-1172)

**Goal.** M7-3 fixed the faction, unit and building ids; techs and abilities are still one global ordinal table each
(`DataLoader.Techs._keys` sorted globally; `DataLoader.Abilities` `AcceptIds` → `sorted`), so the Teblor's `blood_oil_frenzy` would
renumber `cusser` / `sandstorm` / `telas_fire` and every digest with them, and M9's `andii_*` techs would do the same to the shipped
techs. Finish the rule so D28 can land the whole roster next session: ids faction by faction, with one deliberate regeneration of the
golden and every digest pin, and prove no *outcome* moved.

**Scope.**
- **Techs:** `GameData.Techs` = `common/techs.json`'s techs first (ordinal within), then each faction's `techs.json` in
  `factions.json` order (ordinal within). **Abilities:** `GameData.Abilities` faction by faction in faction order (ordinal within).
  Statuses, resources, projectiles, damage classes stay global (common files only). Every `Array.BinarySearch` on `_keys` /
  `ids.TechKeys` / the abilities' `keys` / `sorted` → a lookup through a **load-time sorted index** (keys sorted ordinally + their ids,
  binary search into it; built once per table at load; never iterated for anything that affects ids or hashes). The same index
  replaces `KeyId`'s `Array.IndexOf` for units and buildings (BUG-1172).
- **The regen:** `ContentHash` moves (the keys' order changes) and so does every state hash that holds a tech or ability id (research
  flags, cooldowns, status sources): one golden header + `k` lines regen with the reason in `GatherWedgeQaTests.SameGameDataHashes`
  ("M7-3b: tech / ability ids by faction order; outcomes unchanged"), and **every digest pin in the Watch-outs list re-measured in the
  same commit** with the old digest kept in a comment. The outcomes (end ticks, winners, refusals, the 42-match tally 32 / 8 / 2, every
  page cell, `MatchCriterionTests`' ticks) must be identical: a moved *outcome* means the id order leaked into a decision (iteration by
  tech / ability id somewhere, e.g. the AI's research or cast order) and is a finding to fix, not a re-pin (rule 43).
- **Saves and recordings:** no format number changes. Pin that a save made on `main` (fae4074) with a researched tech is refused
  `SaveError.DataMismatch` on the branch, and that a replay recorded on `main` refuses with the data-hash reason; a save made on the
  branch loads and re-saves byte-identical. (If the save header's data hash turns out not to be checked on load, the task must make a
  pre-M7-3b save refuse: ids inside it are indexes into the renumbered tables.)
- `FactionOrderTests`' id pin gains the techs' and abilities' new tables; QA's BUG-1171 symptom row flips; a full Teblor copy with
  `blood_oil_frenzy` and `teblor_blood_oil_anointing` appended moves no Malazan v Whirlwind digest (the copy pins); `teblor` first moves
  all.
- Docs: docs/03 "Implementation (M7-3)" (the BUG-1171 paragraph becomes the rule, with the index), "Data schema" `techs.json` /
  `abilities.json` rows, "Testing strategy" (the regen), "Save format" one line (a data-hash move refuses saves); docs/05 M7 bullet 4's
  note. BUG-1171 / 1172 fixed via patches in the scratchpad.
- **OUT:** any faction file or `game/data` byte; any file under `sim/Rts.Sim.Tests/Content/**` or `QA/Content/**` (the data track's,
  edited this session; a red row there is a finding to report); the Frenzy aftermath (M7-4); BUG-1173; BUG-1170 (the hardening
  re-measures it); any save or replay format number.

**Acceptance criteria.**
1. The id rule for techs and abilities under six orders (QA's `TheIdRule_HoldsUnderEveryOrder` extended) and the shipped tech /
   ability tables pinned as literals (`FactionOrderTests`).
2. The copy proof with a full Teblor incl. a natural ability name (`blood_oil_frenzy`): every Malazan v Whirlwind digest identical to
   the shipped data's; `teblor` first renumbers (both pinned).
3. One golden regen with the reason in `GatherWedgeQaTests.SameGameDataHashes`; `ContentHash()` equals the new header (rule 49); every
   digest pin in the Watch-outs list re-measured with the old value in a comment; no second regen.
4. **Every outcome unchanged**: the six `MatchCriterionTests` ticks, the tally 32 / 8 / 2 per map and seed, every page cell,
   `HuntStressTests` / `MapSlice2StressTests` end ticks, `ScoutManagerTests` 311, `GatherBonusTests` 169 / 191, the premap replay's 81
   commands, `D21OpeningsQaTests` / `DH6ThresholdQaTests` green untouched.
5. BUG-1172: `DataLoaderQaTests.TenThousandUnitFile_LoadsUnderTwoSeconds` back near the base's ~560 ms alone (report the number; no
   bound change), and the same index serves techs / abilities.
6. Saves / recordings as in Scope (the `DataMismatch` row and the branch's own save round trip pinned).
7. Determinism (3 processes, a Teblor-copy FFA and a shipped 1v1), 0 B per tick unchanged, no forbidden API, docs as in Scope, build
   0 / 0, smoke PASS, the default loop green.

**Design references.** docs/03 "Implementation (M7-3)" (BUG-1171's paragraph), "Testing strategy" (the golden rule), "Data schema",
"Save format (M6-S2)" (the header's data hash); CLAUDE.md rules 4 and 6; BUG-1171 / 1172's files (the decision note in BUG-1171,
the fix sketch in BUG-1172).

**Tests required.** `FactionOrderTests` extended (techs / abilities pins, the copy with a natural ability), `DataLoaderQaTests`' 10k
row re-measured, the pin sweep, the golden, `GatherWedgeQaTests` comment, the save / replay refusal rows.

**Constraints.** Rules 43 / 49 / 53; the pin sweep is the task's bulk (expect ~40 pin edits); size ~450 code lines + the re-pins; the
developer cannot write `studio/**`; `TestSim.cs` is the sim's to edit, `Content/**` is not. Bug ids from **BUG-1190**.

**QA focus (full).** The outcome invariance (criterion 4) above all: re-run every pinned match and compare the *outcome* to the base,
not just the new digest; a Teblor copy with natural names as an AI seat; the id rule under every order incl. a faction with no
abilities file and a faction whose tech ids sort before `age_ii`; 3-process determinism; the 10k-unit load; the `DataMismatch`
refusal of a `main` save with a researched tech and a cooldown running; a recording made on `main` refusing with the data-hash
reason, not a crash.

### Next task candidates (after M7-3b)
1. **The sim's hardening at 4 / 4 (next session):** BUG-1170 measured with the PC idle (profile `ASweepRetarget` if still red),
   BUG-1165 / 1166, the `SaveFormat` alias names, BUG-1173, BUG-1161, BUG-1010's lever, the README / STATE open-count reconciliation.
2. M7-4: Blood-oil Frenzy's aftermath (Producer default: `applyStatus.onEnd {status, magnitude, duration}` carried in the status entry
   as its source's effect id, so only entries applied by an effect with `onEnd` slow on expiry; the Zealot's passive Frenzied never).
3. A per-faction `counters` override in `ai.json` (the data's request: "War-dogs answer Ranged").

---

## View track

### Where we are
M7-V2 on `main`: the queue strip's "H×3", shots at `1.2 m × scale` by shooter inference, `QaM6V18Test` green. Counter 3 / 4. No
unblocked view feature exists (M6's rest is the owner's; the Teblor setup row waits on D28), so the hardening is pulled forward.

### Current session plan: M7-VH1, hardening (pulled forward), QA standard (confirmed at the 2116 PLAN)

**TASK_ID:** M7-VH1 · **TASK_TITLE:** The view's hardening: a read-only projectile accessor replaces the shooter inference (BUG-1179),
plus BUG-1126, BUG-1147 item 1 and the old S4s

**Scope (most valuable first, batch what fits ~800 lines).**
1. **BUG-1179 + the inference:** add a read-only accessor in `sim/Rts.Sim/ViewApi/` (allowed: reads only, never the tick or the hash)
   exposing a live projectile slot's attacker unit type id and victim handle (`ProjectileStore.AttackerType` / `Attacker` / `Victim`
   are `internal` arrays today; QA read them by reflection); `ProjectileViews` reads the ends from it and drops `Nearest` / the spatial
   query (the sim's `SpatialHash._scratch` is no longer written by the view); `QaM7V2Test`'s inference row becomes an exactness row
   (0 wrong of 1,411); docs/03 "Implementation (M7-V2)"'s "How the view knows the ends" replaced.
2. **BUG-1126:** `TooltipWrap.Wrap`'s blank middle line (3,282 of 20,000 fuzzed strings; the Producer's ruling in the file: at a break
   the wrap consumes every adjacent space; a line exceeds the width only as a single word; `QaVh5Test`'s fuzz amended to that rule).
3. **BUG-1147 item 1:** the difficulty tips' "every 2 seconds" / "every half second" read from `thinkInterval`: a `{1}` placeholder
   (seconds, `0.##`) in `game/data/common/ui.json` `setup.difficulty_easy_tip` / `difficulty_hard_tip` (and `_normal_tip` if the
   Normal interval is worth saying) with a load rule like `{0}`'s; the only `game/data` edit, those keys only.
4. BUG-0860 / 0830 / 0890 / 0861 as far as they fit; BUG-1115 item 4 only if a shell theme exists (it doesn't: skip).
- **OUT:** BUG-1177 item 2 (the owner's taste); any `game/data` edit except item 3's keys; any `sim/` file outside
  `sim/Rts.Sim/ViewApi/`; the Teblor setup row; the art pass.

**Acceptance criteria.** 1. The accessor is read-only (no setter, no tick call, no write to any store) and `StateHash` before / after a
frame of views is equal (QA's twin replay row kept); every aimed shot's drawn launch and hit height equals `1.2 × scale` of the real
shooter / victim, incl. a shooter killed on its firing tick (0 wrong of the brawl's shots). 2. BUG-1126's fuzz 0 / 20,000 blank middle
lines under the ruling; `QaVh5Test` and `QaVh6Test` both green. 3. The tips print the data's `thinkInterval`; a changed value changes
the text (a test copy of `ai.json`); the shipped text reads as before ("2" and "0.5" seconds, or the wording the developer chooses
from the data). 4. The scene loop green (89 / 89 + the new rows), smoke PASS, 0 B steady frames. 5. Each closed bug's file patched
with the proof (a patch in the scratchpad).

**Design references.** CLAUDE.md rules 1 / 7 / 8 and the ViewApi rule (STATE); docs/03 "Implementation (M7-V2)", "Implementation
(M5-V2)" (the tips), BUG-1179 / 1126 / 1147's files.

**Constraints.** `sim/Rts.Sim/ViewApi/**` reads only, new files preferred; no other `sim/` file; `game/data/common/ui.json` the
three tip keys only; rule 52 (end every test process). Bug ids from **BUG-1200**.

**QA focus (standard).** The accessor under every projectile kind (aimed, lob, tower, a re-led shot, a shot whose shooter dies the same
tick, a shot whose victim dies mid-flight, a slot reused in the same tick); the hash twin; the wrap fuzz under the ruling; the tips
with `thinkInterval` 0.333 / 2 / 7; a `git diff --stat` showing no `sim/` change outside `ViewApi/`.

### Next task candidates
- An owner item from the inbox (the art call, ".NET 10 installed", templates, sound notes): a feature session, QA standard.
- After D28: the Teblor row in the setup (reads `Factions` already: verify and screenshot), the 1.25× building placeholder, the
  "Regenerating" HUD cue. Otherwise STOP (cheap).

---

## Data track

### Where we are
D-R36 on `main`: teblor.md pin-ready, the seven pin classes faction-agnostic, BUG-1182 / 1183 fixed. Counter 0 / 4. **D28 (the roster)
waits for the sim's M7-3b** (BUG-1171: a Teblor ability would renumber the shipped ones). Open debt: BUG-1186 S3, BUG-1187 S4.

### Current session plan: D-R37, hardening (the only unblocked data work), QA light (confirmed at the 2116 PLAN)

**TASK_ID:** D-R37 · **TASK_TITLE:** Make the remaining content pins faction-agnostic and give the Teblor page its Defense and
High-start bullets (BUG-1186), and clear BUG-1187

**Files (the only ones):** `docs/factions/teblor.md`, `docs/factions/malazan.md` (BUG-1187's line 417 only), `sim/Rts.Sim.Tests/
Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`. **No `game/data` byte**; not docs/02 / 03; not `TestSim.cs` / `TestDataDir.cs`.
The sim's M7-3b edits no file in these folders this session (its pin sweep stops at their door), so no merge conflict is expected;
the sim's branch moves the data hash and every digest, and the Content folders hold no digest literal, so nothing here re-pins.

**Scope.**
- BUG-1186: the 19 files' `{ "malazan", "whirlwind" }` / `InlineData("malazan")` → `ContentFactions.Keys` where the test is a pin
  (`BuildingRosterQaTests`, `UnitRosterQaTests`, `ZealotPassivePinQaTests`, `D19QaTests`, `D20PinQaTests`, `D20DefenseWordingQaTests`,
  `D21OpeningsQaTests`, `DR35ProsePinQaTests`, `D18ReportPreconditionQaTests`, `AiHouseRuleReportTests`); the *report* classes
  (`AiOpeningPairsReportTests`, `AiSpendingReportTests`, `BuildingKillTimeReportTests`, `CounterTriangleMarginsTests`) may stay typed to
  the measured factions **if** each says so in one comment line (the Producer's ruling: a report measures what exists; a pin must cover
  what lands). teblor.md gains the `- **Defense**` and "High starting resources" bullets in the shape the prose pins read (the numbers
  are `rules.json`'s and the page's own; say "proposed" where D28 decides).
- BUG-1187: the comment's indentation, the 170-character line, the War band description ("War-dogs" as the `displayName`, the Javelin
  Thrower named or "the rest of the clan").
- **Also:** the Warrior row's bonus vs Mounted **×1.5** (docs/02's Line rule; the Producer's default, docs/01 2026-11-10 (d)) with the
  ×1.25 kept dated in the unit notes.
- **OUT:** any `game/data` file; any Malazan / Whirlwind number; the roster (D28).

**Acceptance criteria.** 1. `git grep` for the literal pair in `Content/` and `QA/Content/` returns only the four report classes, each
with its comment line. 2. The converted pins pass 2 / 2 on the shipped data and a `malazan.md` mutation reads red in a converted class
(name it). 3. teblor.md passes `PagePinReadinessTests` + the prose pins' regexes on a Malazan-substituted parse (QA's
`TeblorPageParseQaTests` pattern) for the two new bullets. 4. BUG-1187's three items; the Warrior ×1.5. 5. The four filters green; no
`game/data` byte; build 0 / 0.

**Design references.** BUG-1186 / 1187's files; docs/factions/malazan.md's Defense and High bullets (the shape); docs/02 "Factions"
(the Line slot's ×1.5); docs/01 2026-11-10 (d) (the Warrior decision).

**Tests required.** The converted classes; `PagePinReadinessTests` extended to the two bullets; the four filters.

**Constraints.** QA light = the four filters + the default loop (rule 53), nothing more; the developer cannot write `studio/**`. Bug ids
from **BUG-1210**.

**QA focus (light).** The mutation both ways in a converted pin; the two new bullets against the prose pins' actual regexes; the
report classes' comment lines; `git diff --stat` showing only the four allowed paths.

### Next task candidates (after D-R37)
1. **D28, the Teblor roster** once M7-3b is on `main`: `common/factions.json` gains `"teblor"` (appended), `game/data/factions/teblor/`
   `faction.json` (`regen` 1 / 6, colours), `units.json` (`scale` 1.5, `giant`, 2-3 pop, the war-dog pack row with `spawnCount` 3 +
   `onHit`), `buildings.json`, `techs.json`, `abilities.json` (`blood_oil_frenzy`, the aftermath absent until M7-4), `ai.json` (the
   `standard` opening, the BUG-1187 description); the page's tables pinned both ways; the golden header regenerated once (new keys;
   `k` lines must hold: M7-3 + M7-3b's guarantee); QA standard: a Teblor AI seat plays 12,000 ticks on each map without a refusal
   streak, Teblor v Malazan and v Whirlwind twins hash-equal, the setup lists three factions; the review table. Rule 51 (no re-pin
   expected: say so).
2. The owner's answers if any land (names, the Warrior's bonus, the counters, `goldShare`, BUG-0780, `counterStrength`).
3. The Teblor balance pass after M7-4 and the first AI-v-AI matches with a Teblor seat.
