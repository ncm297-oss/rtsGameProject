# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-11-0739** (session 1 / 8 of 2026-11-11; a resume of the dead
2026-11-10-2116). Sim **M7-3b**, view **M7-VH1**, data **D-R37** all ACCEPTED (0 fix rounds each). **M5 8 / 9 (criterion 6
owner-only), M6 5 / 10, M7 0 / 4 (bullets 1-3 built as rules; tech and ability ids under the faction order; nothing shipped).**
Counters: **sim 4 / 4 (hardening next), view 0 / 4, data 0 / 4**. Bug ids next session: **sim 1220+, view 1230+, data 1240+**.
Open S1 / S2: none. Inbox New: empty at ACCEPT.

**Next session in one line:** sim **M7-H1** (hardening, QA full) · view **STOP** (nothing unblocked until D28) · data **D28**
(the Teblor roster, QA standard). The owner's "Teblor names" answer, if it is in the inbox, is applied inside D28.

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
- **A dead session loses the developers' reports and their bug-file patches** (they live in the conversation / scratchpad): 2116
  died after its builds and this session rebuilt the proofs from QA's re-measurements. When a session is resumed, QA writes the
  proof lines for the closed bugs and the Producer applies them at ACCEPT (done at 0739 for BUG-1179 / 1126 / 1147).
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rule 42 / 53). The default loop is ~25-30 min alone, ~45 min
  beside another track; the full non-Perf with Stress ~2 h 25 m. **The PC's speed varies with no load to blame (BUG-1170):** Perf
  alone was 203 / 0 / 3 at ~00:xx on 2026-11-11 and 188 / 15 / 3, 190 / 13 / 3 (branch), 174 / 29 / 3 (de591f0) at ~09:00 the same
  morning, idle. A Perf run is evidence only when a first run shows the PC at its recorded speed (`ASweepRetarget` near 16 us, not 80+).
- Merge order sim → view → data; `studio/bugs/README.md` and `studio/qa/coverage.md` are the usual conflicts, both sides kept;
  docs/03 conflicts when two tracks append an "Implementation" section at the same anchor (keep both, the sim's first).
- **Files no two tracks may share next session:** data **D28** is the only task that adds `game/data/factions/teblor/**`, appends
  `"teblor"` to `game/data/common/factions.json`, regenerates `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay`'s header and adds
  its reason to `sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs` (rule 51: those two sim files are the data task's next session, and
  only for the header line, the checksum and the comment). Sim **M7-H1 edits neither of those two files, no `game/data` byte, and
  nothing under `Content/**` or `QA/Content/**`**; BUG-1190 item 1's assertion goes in a new test file, not in `GatherWedgeQaTests`.
- A developer must never kill another track's Godot or `testhost` process; a developer ends its own (rule 52).
- **The developer cannot write `studio/**`** (a hook): bug-file updates come as a patch in the scratchpad; the Producer applies it.
- **Since M7-3b: the golden header's data hash is `3A04D4E370C643DA`** (`k` lines unchanged since M6-SH1). **Every save and
  recording made before M7-3b refuses `DataMismatch`** (`Save/PreM73bRefusalTests` pins it with de591f0's own fixtures in
  `Replays/pre_m7_3b/`). **D28 moves the header once more** (new keys in `ContentHash`) and so refuses every save again; no `k`
  line and no Malazan v Whirlwind digest may move (M7-3 + M7-3b's guarantee: a moved one is a finding, not a re-pin).
- **Tech and ability ids since M7-3b:** `age_ii` 0, `armor_1` 1, `armor_2` 2, `melee_weapons_1` 3, `melee_weapons_2` 4,
  `ranged_weapons_1` 5, `ranged_weapons_2` 6, `moranth_supply` 7, `dryjhnas_prophecy` 8; `cusser` 0, `telas_fire` 1, `sandstorm` 2.
  D28's `teblor_blood_oil_anointing` becomes tech 9 and `blood_oil_frenzy` ability 3 (appended).
- **Pinned matches on `main` after M7-3b (outcomes unchanged, digests re-measured):** the Hard v Normal tally **32 / 8 / 2** (Raraku
  10 / 4 / 0, Pale Hills 11 / 1 / 2, Vathar 11 / 3 / 0); `StandardPresetPinQaTests` (8 save scenes + 7 AI rows), `NonHardMatchPinQaTests`
  8, the 8 `CounterCompositionTests.TheNonHardPins_*`, `ReserveTwinTests` (2 rich rows), `SharedVisionFfaPinTests` (2): all re-pinned
  with the old value in a comment; **QA's `OutcomePinM73bQaTests` (Stress, 20 id-free fingerprints equal to de591f0's)** is the
  outcome pin that must hold through any later renumbering; `HardKnobsPinTests` / `HardKnobsMatchTests`, `ScoutManagerTests` (311),
  `GatherBonusTests` (169 / 191), the premap AI replay (81 commands; its Research ids moved 4 → 3 and 7 → 5 by key);
  `MatchCriterionTests` (Vathar 23,322 / Raraku 28,231 / Pale Hills 50,140 / generated 17,053 / FFA 56,896 / 2v2 26,070);
  `MapSlice2StressTests` (49,565 / 55,485); `D21OpeningsQaTests` / `DH6ThresholdQaTests` (outcomes only); `HuntStressTests`
  (75,382 / 53,892 / 64,226); the Teblor-copy FFA hash 6C448DFB90134A32 (`FactionOrderQaTests`), the four `FactionOrderTests` copy
  pins (Stress).
- **Known stalls / skips with ids on `main`:** Pale Hills FFA seed 1 (BUG-1068 / 1116), `FarExpandProbeQaStressTests` Pale Hills FFA
  seed 2 (BUG-1151), `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*` (BUG-1156), BUG-1161's
  three rows, `HuntStressTests.FourAiSeats_OnPaleHills_Seed1_KnownStall`, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's
  `QaM7V1Test` zoom-40 1.5 head row; `DebugOverlayTest`'s 1 ms frame row flakes under three-track load (passes on a rerun).
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.
- **The third faction's landing order:** sim M7-3 (done) → sim M7-3b (done) → data **D28** (next session) → sim M7-4 (the Frenzy
  aftermath) → data / sim balance → view (the setup's third row, the 1.25× building placeholder).

---

## Sim track

### Where we are
M7-3b on `main`: tech and ability ids under the faction order, `DataLoader.KeyIndex`, the golden header 3A04D4E370C643DA, every
outcome identical to de591f0. Counter **4 / 4: the hardening is due.** Debt: BUG-1190 S4 (new), BUG-1173 S4, BUG-1170 S3 (machine
state), BUG-1165 / 1166 S4, the three `SaveFormat` alias names, BUG-1161 S4, BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010.

### Proposed next task: M7-H1, hardening, QA full (to be confirmed at PLAN)

**TASK_ID:** M7-H1 · **TASK_TITLE:** The sim's hardening: the forged `REGN` parts (BUG-1165 / 1166), BUG-1190, BUG-1173, the
`SaveFormat` alias names, BUG-1161, and one Perf measurement for BUG-1170

**Scope (most valuable first; batch what fits ~800 lines).**
1. **BUG-1165 / 1166:** a forged `REGN` part is refused at load (`LastDamagedTick <= tick` for live slots; the `RegeneratingUnits`
   total recomputed or checked against the slots); the four skipped `RegenQaTests` rows un-skipped. No save-format number change.
2. **BUG-1190:** (1) a row asserting `seed21_playable.replay`'s Research at tick 5556 is refused, **in a new test file** (not
   `GatherWedgeQaTests.cs`: D28 edits that file's comment the same session); (2) `TechDataQaTests`' hash loop runs to `d.Techs.Length`.
3. **BUG-1173:** the zero-worth pack documented (docs/03), the Windows folder-case message, the ten per-faction ternaries in sim
   test files (not under `Content/**`).
4. The three `SaveFormat` alias names; BUG-1161; BUG-1010's lever if it fits.
5. **BUG-1170, a measurement only:** run Perf alone once at the start (rule 42). All green: close it as machine state with 0739's
   two runs as the record. Red at base and head alike: add the numbers to the file and move on. No re-bound, no profiling.
- **OUT:** any `game/data` byte; `cross_map_seed1.replay`; `GatherWedgeQaTests.cs`; `Content/**`, `QA/Content/**`; any digest
  re-pin (a pin that moves is a finding); any save or replay format number; M7-4.

**Acceptance criteria.** 1. A forged `REGN` part of each of BUG-1165 / 1166's shapes is refused with a named `SaveError`, the four
rows un-skipped and green, 50 honest mid-match saves still load and re-save byte-identical. 2. BUG-1190's two rows. 3. BUG-1173's
three items (each with its test or doc line). 4. The alias names gone with no caller left; BUG-1161's rows as its file says.
5. The Perf run reported (numbers, the PC's load, the verdict rule above); BUG-1170's file updated through a patch. 6. No digest
pin, golden line or outcome pin edited (`git diff --stat` shows none of the pin classes); determinism (3 processes); 0 B per tick;
build 0 / 0; smoke PASS; the default loop green.

**Design references.** docs/03 "Save format (M6-S2)" and "Implementation (M7-1)" (the `REGN` part), "Implementation (M7-3)" (the
pack's worth); each bug's file.

**QA focus (full).** Forged saves: every `REGN` field at its edges (a `LastDamagedTick` equal to, one past and far past the tick; a
total one off; a dead slot carrying values), fuzz 10,000 mutated `REGN` parts with no crash and no load of an inconsistent part;
the honest round trip; that no pinned digest moved (the Stress pin classes run).

### Next task candidates (after M7-H1)
1. **M7-4:** Blood-oil Frenzy's aftermath (Producer default: `applyStatus.onEnd {status, magnitude, duration}` carried in the status
   entry as its source's effect id, so only entries applied by an effect with `onEnd` slow on expiry; the Zealot's passive never).
2. A per-faction `counters` override in `ai.json` (Request 46: "War-dogs answer Ranged").
3. The Teblor balance pass's sim side (AI-v-AI rows with a Teblor seat) once D28 and M7-4 are on `main`.

---

## View track

### Where we are
M7-VH1 on `main`: `ViewApi/ShotEnds` (read-only), `ProjectileViews` without the inference, the wrap ruling, the tips' `{1}`.
Scene loop 93 / 93. Counter **0 / 4**. Debt: BUG-1200 S4 (new), BUG-1177 item 2 (the owner's taste), BUG-1115 item 4 (no shell theme).

### Proposed next session: STOP (cheap) unless an owner item is in the inbox
No unblocked view feature exists: M6's rest is the owner's (art call, music, fonts, templates, the .NET SDK), and the Teblor work
needs D28's files on `main` first. **The session after D28 lands:** M7-V3 (feature, QA standard): the Teblor row in the skirmish
setup (it reads `Factions` already: verify, screenshot, a scene row with three factions), the 1.25× building placeholder for
Teblor buildings, the "Regenerating" HUD cue, BUG-1200 folded in (the same file). An owner item (the art call, ".NET 10 installed",
templates, sound notes) is a feature session, QA standard, whenever it lands.

---

## Data track

### Where we are
D-R37 on `main`: every content pin class reads `ContentFactions.Keys` (four report classes two-faction by ruling), teblor.md has
its Defense and High-start bullets, the Warrior ×1.5, the corrected "War band" sentence. Counter **0 / 4**. **D28 is unblocked**
(M7-3b on `main`). Open debt: BUG-1210 S4 (folded into D28).

### Proposed next task: D28, feature, QA standard (to be confirmed at PLAN)

**TASK_ID:** D28 · **TASK_TITLE:** The Teblor roster: the faction's data files written from `docs/factions/teblor.md`, pinned both
ways, with BUG-1210

**First:** if the inbox holds the owner's answer on the Teblor names / the Warrior's bonus / the counters, apply it to the page
before writing the files.

**Files (the only ones):** `game/data/common/factions.json` (append `"teblor"`), `game/data/factions/teblor/**` (new: `faction.json`
with `regen` 1 / 6 and colours, `units.json` with `scale` 1.5, `giant`, 2-3 pop and the war-dog pack row `spawnCount` 3 + `onHit`,
`buildings.json`, `techs.json`, `abilities.json` with `blood_oil_frenzy` (no aftermath until M7-4), `ai.json` with the `standard`
"War band" opening), `docs/factions/teblor.md`, `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, and under
rule 51 only: `sim/Rts.Sim.Tests/Replays/cross_map_seed1.replay` (header data-hash + checksum) and the reason comment in
`sim/Rts.Sim.Tests/QA/GatherWedgeQaTests.cs`. If a field the page needs is missing from a schema: a Request for the sim track and
the page says "not in the data", never a C# change.

**Acceptance criteria.** 1. The data loads with three factions (`DataValidationTests` green) and every cell of the page's Units /
Buildings / Techs / Abilities / openings tables is pinned to the files both ways (the converted pin classes now run 3 / 3; "proposed"
removed from the page where the files decide). 2. BUG-1210's three items (rows derived from `ContentFactions.Keys`, the lookup
through `Typed`). 3. One golden header regen with its reason in `GatherWedgeQaTests`; **no `k` line and no Malazan v Whirlwind
digest moves** (rule 51: the sim pin classes and the Stress pin classes are run and listed; none is edited). 4. Every
`displayName` / `description` equals the page's review table (or the owner's corrected words). 5. The four filters green, build
0 / 0, smoke PASS, the default loop green.

**Design references.** `docs/factions/teblor.md` (every table), docs/02 "Factions" (the template slots, Line ×1.5 vs Mounted) and
"Faction template", docs/03 "Data schema" and "Implementation (M7-1)" / "(M7-2)" / "(M7-3)", docs/01 2026-11-10 (d).

**QA focus (standard).** A Teblor AI seat plays 12,000 ticks on each map without a refusal streak; Teblor v Malazan and v Whirlwind
twins hash-equal; a war-dog order spawns three and Hamstring slows on a bite; regeneration starts after 6 s; a page cell mutated
reads red and a file value mutated reads red; the setup lists three factions (smoke `--skirmish` with a Teblor seat boots clean).

### Next task candidates (after D28)
1. The owner's answers if any land (names, the Warrior's bonus, the counters, `goldShare`, BUG-0780, `counterStrength`).
2. The Teblor balance pass after M7-4 and the first AI-v-AI matches with a Teblor seat (QA standard, scenario numbers).
