# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-11-2256** (session 3 / 8 of 2026-11-11). Sim **M7-5** ACCEPT, view
**M7-V3b** ACCEPT, data **D28-L** ACCEPT (0 fix rounds each, all three QA PASS). **The Teblor roster is on `main`.**
**M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 1 / 4 (criterion 2 ticked).** Counters: **sim 1 / 4, view 2 / 4,
data 2 / 4**. Bug ids next session: **sim 1280+, view 1290+, data 1300+**. Open S1 / S2: none. Inbox New: empty at ACCEPT.
The next session starts on 2026-11-12 (1 / 8 of that day).

**Next session in one line (proposal):** sim **M7-4** (Blood-oil Frenzy's aftermath and +20% damage, a tech effect on a
status's duration, a building `scale`; feature, QA full) · view **M7-V3c** (BUG-1260: giants drawn no wider than the room
they take, a scene with the real Teblor; feature, QA standard) · data **D29** (BUG-1241's AI half inside `ai.json`, only
if the PLAN's measurement shows it is fixable without M7-4's rules; else STOP until M7-4 is on `main`).

## Where things stand

- **`main` after this session = sim 1262b0e + view 4ff96b0 + data 60619c8.** The Producer's scratch clone of that tree:
  build 0 / 0; the default loop **8,906 / 0 / 21 of 8,927 in 20 m 33 s**; smoke **PASS**; the scene loop **97 / 97**; the Stress pin
  classes, the memory rows and the wide-crowd rows **103 / 0 / 1 of 104**; the data filters **2,143 / 2,143**.
- **Three factions ship:** `common/factions.json` order `malazan, whirlwind, teblor`. Ids: `teblor_blood_oil_anointing`
  tech 9, `blood_oil_frenzy` ability 3, units 14-20, buildings 20-29. The golden header is **67269D955D18D885**; every
  earlier save and recording refuses (`DataMismatch`); no `k` line moved.
- **The held branch `studio/2026-11-11-1317-data` was never merged** (D28-L brought its files over). It can be deleted.
- **Not in the Teblor data yet** (the page says so in each place): Frenzy's +20% damage and aftermath slow, the
  Anointing's +4 s (M7-4), the buildings' 1.25× (Request 47), the Gatherer's 15 a trip (Request 48), the AI's later-wave
  rules (Requests 46 / 51).
- **On screen the giants are wrong (BUG-1260, S3):** drawn 1.125 m wide on 0.75 m of room, so they stand inside each
  other. Pictures: `studio/review/2026-11-11-2256/`. M7 criterion 1 (scale) is not ticked for that reason.

## Standing rules (1-56)


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
**54:** before a task adds a faction, a data type or a default that every test reads, the Producer applies it on a scratch clone
and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that. **55:** that rehearsal uses a
copy that exercises every hook the new content will use (a passive, a scale, a pack, an aura, a new radius), or the content
branch itself when it exists; a Malazan copy alone missed 28 rows this session.
**56 (new):** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)

## Watch-outs (all tracks)
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **`QaM7V3aTest` fails on purpose at six shipped factions** ("no room for three clones under 8"). `ThreeFactionTest` and
  `RegenCueTest` never show the real Teblor (the forge cuts them away): M7-V3c adds a scene that does.
- **`tools/qa/third-faction-copy.ps1`'s display name "<id> (Malazan copy)" trips BUG-1230** at 1152 x 648 with five
  factions (5 setup scenes red from name width): use short ids or expect them.
- **A QA scratch scene left a data copy** at `%APPDATA%\Godot\app_userdata\RtsGame\qa-teblor-shot\` (could not be
  deleted: permissions). Harmless. Scratch scenes should write under the scratchpad, not `user://`.
- **A dead session loses the developers' reports and bug patches** (they live in the conversation / scratchpad). When a session
  is resumed, QA writes the proof lines and the Producer applies them at ACCEPT.
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rules 42 / 53). The default loop is ~25-30 min alone,
  ~45-90 min beside other suites. **BUG-1170 is closed as machine state:** a Perf run is evidence only when `ASweepRetarget`
  reads near 16 us (85 us = the PC's slow state; do not chase it).
- Scene rows that flake under load and pass alone: `DebugOverlayTest` and `QaM25Test` (1 ms rows), `MinimapTest` (0.5 ms),
  `BenchTest` (7 s), `QaH1Test` (600 s timeout).
- Merge conflicts to expect: `studio/bugs/README.md`, `studio/qa/coverage.md` (both sides kept), docs/03 when two tracks
  append an "Implementation" section (keep both, the sim's first).
- A developer must never kill another track's Godot or `testhost`; a developer ends its own (rule 52). **The developer cannot
  write `studio/**`:** bug-file updates come as a patch in the scratchpad; the Producer applies it.
- `tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` makes a Malazan copy; a Whirlwind-shaped copy is made
  by hand (`Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy` is the recipe); a Teblor-shaped one is the `teblor`
  folder under another id (QA's `barghast`).

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
- **The third faction's order of work now:** sim M7-4 (the Frenzy aftermath and +20% damage, Requests 47 / 49 / 50) and
  view M7-V3c (BUG-1260) next session → the data's building `scale` and the balance pass (BUG-1241, Requests 46 / 48 / 51)
  → models (the owner's art call first).

---

## Sim track

### Where we are
M7-5 on `main` (BUG-1222 fixed). Counter **1 / 4**. Debt: BUG-1220 / 1221 S4, the three `SaveFormat` alias names
(BUG-1161 S4), BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010. No "Current session plan": the next PLAN writes it.

### Next task candidates
1. **M7-4: Blood-oil Frenzy, whole** (feature, QA full; around 800 lines: a new hook on the status store). Request 50:
   Frenzy's +20% damage beside its +40% attack speed, and the aftermath slow when it ends (Producer default:
   `applyStatus.onEnd {status, magnitude, duration}`; read docs/03 M4-4b-3a "Known limits" (1) on the one-entry-per-id
   merge first and say in the brief what a re-cast during the aftermath does). Request 49: a tech effect on a status's
   duration (the Anointing's +4 s). Request 47: an optional `scale` on a building row (visual only, in the data hash, the
   sim never reads it; like the unit's).
   - Schema + validation + the minimum test entries only (the data rule): the Teblor rows are the data track's, after.
     If a Teblor file must change for a test, use a copy.
   - It changes a save format if the aftermath needs stored state: rule 50 (name the view scenes that pin those bytes).
   - Rule 54 / 55 rehearsal at PLAN is not needed (no new faction); rule 43 only if a Normal AI casts Frenzy in a
     pinned match (the Teblor are in no pinned match: say so in the brief after checking).
   - If the size is over budget, split: M7-4a (damage share + `onEnd`), M7-4b (the tech effect + building `scale`).
2. Requests 46, 48, 51 with the balance pass (measure which the pass needs before building any).
3. Hardening at 4 / 4: BUG-1220 / 1221, BUG-1161's names, BUG-1149 / 1151 / 1152.

---

## View track

### Where we are
M7-V3b on `main` (BUG-1231 fixed). Counter **2 / 4**. 97 scenes. Debt: **BUG-1260 S3 (new: the next task, not debt)**,
BUG-1230 S3, BUG-1177 item 2 (the owner's taste), BUG-1115 item 4.

### Next task candidates
1. **M7-V3c: the real Teblor on screen (BUG-1260).** Feature, QA standard.
   - Producer default (record it in docs/01's change log and docs/03 at the PLAN, "owner may revisit"): the body's
     width, the selection and target rings and the corpse disc follow the collision radius alone; `scale` sets the
     height, the bar's length and height and the shot heights as today. One place: `UnitViews.DrawnRadius`. Shipped
     Malazan and Whirlwind units (scale 1) must render pixel-identical.
   - A scene on the shipped data (no forge) with the real Teblor: the setup's third row, a Teblor base at tick ~30 (five
     Gatherers not overlapping: assert drawn centres at least two drawn radii apart, or the sim's spacing), a Warrior
     beside a Heavy Infantry (width ratio 0.75 / 0.4, height ratio 1.5), "Regenerating" on a real hurt Warrior, the
     War-dog's "×3" square at the Stables & Kennels.
   - Screenshots of the same four views as `studio/review/2026-11-11-2256/` after the change, looked at and described
     (rule 56); the Producer copies them for the owner.
   - BUG-1260 item 2 (a white first frame): reproduce in a normal match or close it as the scratch scene's.
   - Out of scope: models, the building placeholder at 1.25× (after Request 47), BUG-1230.
   - M7 criterion 1 is ticked at that ACCEPT if the pictures hold.
2. BUG-1230 (long faction names) at the view hardening (counter 4 / 4 in two sessions).
3. An owner item (the art call, ".NET 10 installed", templates, the sound notes) whenever one lands.

---

## Data track

### Where we are
**D28 is on `main`** (D28-L). Counter **2 / 4**. BUG-1210 fixed. Debt: BUG-1241 S3 (the Normal Teblor AI is
gold-starved: the balance pass).

### Next task candidates
1. **D29: the Teblor AI reaches Age II (BUG-1241's opening half).** Standard tier (it needs scenario numbers). Only
   `factions/teblor/ai.json` and the page's opening table: the bug file's first candidate (the Age II research before
   the eight extra Gatherers in time; the Armor upgrade after it), no unit cost, no stat. Measure on QA's eight pairings
   (`TeblorAiMatchQaStressTests`) on three seeds, both seat orders: Age II by 10:00 and a wave sent in at least 6 of 8,
   or report why the order alone cannot do it (then the fix is Request 51's gold reserve, the sim's, and the task ends
   as a measurement).
   - `ai.json` is in the content hash: the golden header moves again (rule 51: the data brief lists the sim pin classes
     and runs their Stress rows; the reason goes in `GatherWedgeQaTests`' comment). The Teblor are in no pinned match:
     check `OutcomePinM73bQaTests` stays equal.
   - At PLAN: confirm on a scratch clone that the reorder moves the result at all (one pairing, one seed) before saying GO.
     If it does not, STOP the data track until M7-4 and Request 51.
2. After M7-4 is on `main`: the Teblor rows for Frenzy's damage / aftermath / the Anointing's +4 s and the buildings'
   `scale` 1.25, the page and the pins both ways.
3. The full balance pass (several seeds, both seat orders, the D21-style pairing rows; the 0.75 m crowd figures as an input).
4. The owner's answers if any land (the Teblor table, the counters, `goldShare`, BUG-0780, `counterStrength`).
