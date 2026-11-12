# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-11-1317** (session 2 / 8 of 2026-11-11). Sim **M7-H1** ACCEPT, view
**M7-V3a** ACCEPT, data **D28 ACCEPT and held on its branch** (0 fix rounds each). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10,
M7 0 / 4 (every rule built; the roster written, not on `main`).** Counters: **sim 0 / 4, view 1 / 4, data 1 / 4**. Bug ids next
session: **sim 1250+, view 1260+, data 1270+**. Open S1 / S2: none. Inbox New: empty at ACCEPT.

**Next session in one line (proposal):** sim **M7-5** (BUG-1222: what the real roster still breaks; feature, QA standard) ·
data **D28-L** (re-land the held roster; light) · view **M7-V3b** (BUG-1231: the three scenes the real roster breaks; feature, QA standard). **D28 lands at that ACCEPT if the three-way tree is green**.

## Where things stand

- **`main` after this session = sim de0a2a6 + view ceda412** (the data branch is not merged). The Producer's scratch clone of
  that tree: build 0 / 0; the default loop **8,707 / 0 / 21 of 8,728 in 24 m 21 s**; smoke PASS; the scene loop 96 / 96.
- **The held branch: `studio/2026-11-11-1317-data` (7093dec implement, 5aa5e1c QA).** It carries `common/factions.json`
  (`teblor` appended), `factions/teblor/**`, `docs/factions/teblor.md`, one cell of docs/02, the content tests, the golden
  header **67269D955D18D885** (+ checksum, + the reason in `GatherWedgeQaTests`), BUG-1241's file and QA's report
  `studio/qa/2026-11-11-1317-D28.md`. BUG-1210 is fixed there.
- **The three-way tree (sim + view + data, the Producer's scratch clone):** the default loop **8,875 / 28 / 21 of 8,924 in 24 m 36 s (the 28 are BUG-1222's rows exactly; 0 in the data folders)**; the scene loop
  **93 / 96 (smoke PASS; `QaM7V3aTest`, `ThreeFactionTest`, `ReplayPlaybackTest` red: BUG-1231, new)**. So the landing needs two fixes, in parallel next session: the sim's 28 rows (BUG-1222) and the view's 3 scenes (BUG-1231).
- **Hold 1 of 2.** Rule 46: if D28 is held again, its third brief carries a terminal acceptance rule.

## Standing rules (1-55)


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
and runs the default loop and the scene loop at PLAN; "unblocked" is said only after that. **55 (new):** that rehearsal uses a
copy that exercises every hook the new content will use (a passive, a scale, a pack, an aura, a new radius), or the content
branch itself when it exists; a Malazan copy alone missed 28 rows this session.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests.SameGameDataHashes` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.

## Watch-outs (all tracks)
- **Landing D28 (the conductor and the Producer at the next ACCEPT):** merge order sim, view, then the data track's D28-L
  branch. Before that merge the Producer builds the three-way tree on a scratch clone: the default loop, the Stress pin
  classes, the four data filters, the scene loop, smoke. Green: it lands. Red outside the data folders again: hold 2, and
  the third brief gets a terminal rule (rule 46).
- **BUG-1241's file is on `main` and on the data branch** (copied word for word at this ACCEPT, so the file merges clean); the
  index row will show twice after the merge: keep one. BUG-1210's file differs on the two sides: keep `main`'s status line
  until the landing, then set it `fixed`.
- **When the roster lands the golden header becomes 67269D955D18D885** and every earlier save and recording refuses once
  more (`DataMismatch`); no `k` line moves. Until then `main`'s header is 3A04D4E370C643DA.
- **Ids when it lands:** `teblor_blood_oil_anointing` tech 9, `blood_oil_frenzy` ability 3, units 14-20, buildings 20-29.
- **A giant's drawn size is unchecked:** the view draws `radius x scale` = 0.75 x 1.5 = 1.125 m against a soldier's 0.4. Nobody
  has looked at a Teblor on screen yet. M7-V3b takes the screenshot; if it looks wrong the choice is the owner's (radius in
  `units.json`, or the view drawing width from something other than the collision radius).
- **Radius 0.75 is a fourth radius class** (shipped: 0.4, 0.7, 0.9): `World.RadiusClassCount` sizes `PlugAnswers`, `FlankBlock`,
  `FlankFirst`, `FlankNext`; +131,072 bytes on a 1024 map. A 0.75 m crowd gives up more often in the movement sweeps (QA D28:
  the 6 give-up rows pass at 0.4); those rows are green on the merged tree only because M7-H1 keeps scenario spawns to the
  first two factions (`i % ScenarioUnitTypes`). A Teblor crowd's movement has no sweep of its own yet: M7-5 adds one row
  that measures and reports (rule 48), no bound.
- **A dead session loses the developers' reports and bug patches** (they live in the conversation / scratchpad). When a session
  is resumed, QA writes the proof lines and the Producer applies them at ACCEPT.
- **Three test hosts at once** flake the wall-clock Perf and scene rows (rules 42 / 53). The default loop is ~25-30 min alone,
  ~45-90 min beside other suites. **BUG-1170 is closed as machine state:** a Perf run is evidence only when `ASweepRetarget`
  reads near 16 us (85 us = the PC's slow state; do not chase it).
- Scene rows that flake under load and pass alone: `DebugOverlayTest` and `QaM25Test` (1 ms rows), `QaH1Test` (600 s timeout).
- Merge conflicts to expect: `studio/bugs/README.md`, `studio/qa/coverage.md` (both sides kept), docs/03 when two tracks
  append an "Implementation" section (keep both, the sim's first).
- A developer must never kill another track's Godot or `testhost`; a developer ends its own (rule 52). **The developer cannot
  write `studio/**`:** bug-file updates come as a patch in the scratchpad; the Producer applies it.
- `tools/qa/third-faction-copy.ps1 -Repo <scratch clone> [-Id <id>]` makes a Malazan copy; a Whirlwind-shaped copy is made
  by hand (`Stress/M7H1KillCounterQaStressTests.AddWhirlwindCopy` is the recipe).

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
- **The third faction's landing order now:** sim M7-5 + data D28-L (next session) → view M7-V3b → sim M7-4 (the Frenzy
  aftermath and +20% damage, Requests 47 / 49 / 50) → the balance pass (BUG-1241, Requests 46 / 48 / 51) → models.

---

## Sim track

### Where we are
M7-H1 on `main`. Counter **0 / 4**. Debt: BUG-1220 / 1221 S4 (new), the three `SaveFormat` alias names, BUG-1161 S4,
BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010. **BUG-1222 (S3) is the next feature task, not debt.**

### Next task candidates
1. **M7-5: what the real roster still breaks (BUG-1222).** Feature, QA standard. Work on a scratch clone with
   `studio/2026-11-11-1317-data` merged (never merge it in the worktree; the branch itself stays two-faction and green).
   - The 28 rows (`studio/bugs/BUG-1222-*.md` has the list and the means): `OnHitPassiveTests` x9, `PackMatchTests` x3,
     `QA.PackQaTests` x11 (fixtures that add an `onHit` passive to every unit: build them on
     `TestDataDir.CopyOfShippedTwoFactions()` or skip units that already have one), `PassiveLoaderTests.Shipped_Zealot_*`,
     `DataConversionTests.Scale_AbsentIsOne_*` / `SpawnCount_AbsentIsOne_*`, `SelfAuraTests.TheTestAbilities_*`
     (`TestSim.TwoFactionData`), the memory bound (the ruling is in the bug file), and the two rows a Whirlwind-shaped copy
     breaks (`AttackTargetsLoaderTests.ShippedData_TheRamAttacksBuildingsOnly_*`, `PassiveLoaderTests`).
   - No pinned literal changes value, as in M7-H1. Out of scope: any `game/data` byte, `Content/**`, `QA/Content/**`, the
     golden, `GatherWedgeQaTests`.
   - docs/03's two sentences that go stale at the landing ("No abilities.json entry uses selfAura yet", "no shipped faction
     file sets `scale` or `regen`"): reword so they are true on both trees.
   - One new row that measures a 0.75 m crowd (a Teblor-radius group through the crowd sweep's maps) and reports give-ups
     and arrival; no bound (rule 48).
   - Acceptance: on the scratch clone with the data branch merged, the default loop and the Stress pin classes have 0 red
     outside the data folders; the same with a Whirlwind-shaped copy on the branch alone (apart from the two golden-header
     rows); the branch itself green; no pinned value changed.
   - The view fixes its three red scenes (BUG-1231) in parallel; the sim touches no `game/` byte.
2. **M7-4:** Blood-oil Frenzy's aftermath (Producer default: `applyStatus.onEnd {status, magnitude, duration}`) and its +20%
   damage (Request 50), a tech effect on a status's duration (Request 49), a building `scale` (Request 47). QA full.
3. Requests 46, 48, 51 with the balance pass; BUG-1220 / 1221 at the next hardening.

---

## View track

### Where we are
M7-V3a on `main`. Counter **1 / 4**. Debt: BUG-1230 S3 (new), BUG-1177 item 2 (the owner's taste), BUG-1115 item 4.

### Next task candidates
1. **M7-V3b: the view-owned scenes the real roster breaks (BUG-1231).** Feature, QA standard. Work on a scratch clone with
   `studio/2026-11-11-1317-data` merged (never in the worktree; LFS assets copied in, BUG-0860).
   - `QaM7V3aTest`: "9 factions is above the maximum 8" (it forges six on top of what ships): count from what ships.
   - `ThreeFactionTest` / `ThirdFactionForge`: "forged factions: malazan, whirlwind, teblor, third"; "the added third slot:
     count 3, faction 2 (want 3)": the forge cuts its copy to the first two factions of the order, or the scene's
     expectations are computed from the shipped count.
   - `ReplayPlaybackTest.OpeningRows`: "the fixture holds not three openings per faction" (the Teblor ship one): the fixture
     reads the factions it was written for.
   - Also grep `game/tests` for other "two factions" assumptions that pass by luck (`Factions.Length == 2`, `[1 - f]`).
   - On that clone: `--screenshot` of the setup with the real Teblor row, of a Teblor base, of a Warrior beside a Malazan
     soldier; look at them and describe them (a giant's capsule is drawn at radius x scale = 1.125 m; say how it looks).
     Report only: nothing from this part lands.
   - Acceptance: the scene loop green on the branch and on the scratch clone with the data branch merged; no scene weakened
     (each still asserts what it asserted on two shipped factions); smoke PASS on both.
   - Files: `game/tests/**` only (and docs/03 if a sentence changes). Not `game/data/**`.
2. **M7-V3c** once D28 is on `main`: the Teblor row verified on `main`, the giants' drawn width (the owner's taste), the
   building placeholder at the data's scale after Request 47.
3. BUG-1230 (long faction names) at the next view hardening; an owner item (the art call, ".NET 10 installed", templates,
   the sound notes) whenever one lands.

---

## Data track

### Where we are
D-R37 on `main`; **D28 accepted, held on `studio/2026-11-11-1317-data`**. Counter **1 / 4**. BUG-1210 fixed on the branch;
BUG-1241 S3 (the balance pass).

### Next task candidates
1. **D28-L: re-land the held roster.** Light. The session's data branch starts from the new `main` and merges
   `studio/2026-11-11-1317-data` (conflicts: the two studio index files, BUG-1210's file; docs/03 is not touched by D28).
   Re-run the four data filters, `Content/**` / `QA/Content/**` in the default loop and the pin classes of D28's criterion 7.
   Apply the owner's Teblor answers if any are in the inbox (the page and the files together, pins both ways). No other
   change: the red rows outside the data folders are the sim's (M7-5, in parallel). It lands when the three-way tree is green.
2. After the landing: the buildings' `scale` 1.25 once Request 47 is on `main`; the Teblor balance pass after M7-4
   (BUG-1241: several seeds, both seat orders, the D21-style pairing rows).
3. The owner's other answers if any land (the counters, `goldShare`, BUG-0780, `counterStrength`).
