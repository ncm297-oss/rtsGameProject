# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-14-2156** (session 3 / 8 of 2026-11-14, started 21:56; the
ACCEPT ran from 03:45 on the 15th, about 7 hours in all). Sim **M6-S9** ACCEPT (feature, 1 fix round, QA full FAIL on
two criteria the Producer corrected at the ACCEPT: rule 66), view **M7-V5** ACCEPT (0, QA standard PASS_WITH_ISSUES),
data **D-R38** ACCEPT (0, QA light PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8
0 / 3.** Counters: **sim 2 / 4, view 3 / 4, data 3 / 4**. Bug ids next session: **sim 1460+, view 1470+, data
1480+**. Open S1 / S2: none. The next session is **1 / 8 of 2026-11-15**.

**Next session in one line (proposal):** sim **M6-S10: BUG-1431 (the Keep-corner drop-off wedge), then the shipped
value `gatherOverlap` 0.5, one hash move** (feature, QA full; staged, nothing ships if the wedge is not cured) · view
**the owner's Shift-queue made visible** (feature, QA standard) · data **D-H9, the hardening pulled forward** (light,
no hash move: BUG-1450 first, the own-names pins, BUG-1451, Q1-Q11 onto the page, BUG-1421 items 3-4, BUG-1360).

**At the PLAN, first:** (1) the inbox; (2) set the README index rows of **BUG-1400** and **BUG-1391** to fixed and add
a row for **BUG-1440** (their files say so already; the rows sit beside the view's edits, so the Producer left them to
avoid a conflict); (3) **rule 54's rehearsal for the value:** on a scratch clone of `main` add `"gatherOverlap": 0.5`
to `game/data/common/rules.json`, run the default loop and the Stress pin classes, and list every row that moves (the
two golden data-hash pins and the golden will, by design; the M5-5 rows per docs/03 "Implementation (M6-S9)" table
(b): Vathar Crossing 23,322 p1 → 28,729 p0, past its 24,000 bound; Raraku 28,231 → 30,132; **Pale Hills seed 1
50,140 → does not end in 72,000**). The brief names those rows (rules 41 / 43 / 45).

## Where things stand

- **`main` after this session = sim 7c99fac + view 06a5af3 + data d15577c** (plus this ACCEPT's studio and docs
  files). The Producer's scratch clone of that tree (`acc2156` in the session scratchpad; conflicts only in
  `studio/bugs/README.md` and `studio/qa/coverage.md`, both sides kept; docs/03 merged by itself): build
  `--no-incremental` **0 errors / 0 warnings**; the default loop **9,515 / 0 failed / 21 skipped of 9,536 in 22 m 18 s (beside the scene loop)**; smoke **PASS**; the scene loop
  **107 of 107, first pass**.
- **The gather-overlap rule is on `main`, off.** `rules.json` `gatherOverlap` (absent = 0 = off; 0 to 0.75,
  `DataLimits.MaxGatherOverlap`; hashed only when non-zero; JSON `null` is an error, told from absent by `NumberJson`).
  A gather pair = two live units of one owner with `GatherNode` set. `MovementSystem.PairSum` is the pair's touching
  distance at every site (written out in Plan's neighbor loop: keep the two in step); `GatherFloor` /
  `ShovedPastGatherFloor` keep a gather pair at `ShoveSpacing × PairSum` or more (on for any value above 0: BUG-1433).
  `World.GatherOverlapOn` / `GatherPairScale` are derived from the data, never saved. The shipped hash
  **5353DF8A1473FC6D**, the golden, save format 10 and every pin are unchanged; no existing test file was edited.
  docs/03 "Implementation (M6-S9)" has the sites, the tables (a) income, (b) four AI matches at 0 / 0.5, (c) the fuzz.
- **What the tables say:** 24 Laborers on one mine bank 3,430 / 3,630 / 3,930 / 4,080 at 0 / 0.25 / 0.5 / 0.75; 24
  Gatherers 2,790 / 3,450 / 3,960 / 4,320; five workers: no change. At 0.75 workers stand 0.17-0.24 m apart (drawn as
  one). **0.5 stays the value to ship.** Matches at 0.5: the menu map ends 6,286 ticks sooner, Vathar's winner flips
  (28,729, past its bound), Raraku 1,901 later, Pale Hills seed 1 does not end (seat 1 is left with one worker from
  tick 66,000 that is never hunted: BUG-1434 / BUG-1116's shape, not an economy wedge).
- **The fuzz's 60 s trip window does not hold with the knob off** (BUG-1431: a returning worker 1.3-1.5 m out of
  reach at a Keep's corner behind mates whose goal cell is the Keep's stand cell; also an outbound worker held at its
  Keep, seed 27). Seeds past 60 s over seeds 2-56: off 6, 0.25 5, 0.5 2 (seed 23 knob-only: 77.7 s against 42.1 s;
  seed 27: 90.8 against 89.8), 0.75 1 (seed 7). Which (seed, value) breaches is chance: the knob switched on one
  tick late moves them.
- **Five Stress rows are red on `main` by ruling (rule 57; none in the default loop):**
  `GatherOverlapQaStressTests.EconomyFuzz_Seeds2To16_NoWedgeOnlyTheKnobForms` at 0.25 and 0.75, `Bug1430_*` x2,
  `GatherOverlapWedgeQaStressTests.WiderSeedSweep_17To56_BreachesByValue`. BUG-1400's row is green again. M6-S10 turns
  them green (BUG-1431 cured) or re-states them as "no more breaches than off, none away from a Keep corner". **Two
  sessions red = the fix becomes the whole task.**
- **Eleven unit types are figures (M7-V5):** the five Teblor humanoids joined (Gatherer, Warrior, Javelin Thrower,
  Shaman, Breaker), drawn at the data `scale` 1.5 (2.76-2.78 m; the file holds the 1.84 m base figure), rings on the
  0.75 m radius. New library clips `attack_throw` (UAL2 `OverhandThrow`) and `attack_cast` (UAL1
  `Spell_Simple_Shoot`). New `build_unit.py` hooks: `alignBone`, tint `brightness`, `propColourSpace: "srgb"`.
  **`tools/import-assets.ps1` now marks each rebuilt model's texture imports stale so Godot re-extracts them** (before,
  a changed tint never reached the game). The Destrier Rider and the War-dog are capsules. **Bench alone (Producer):
  400 figures 11.41 ms while 90 %+ alive (worst 1 % 15.67), 1,000 figures 29.20 ms** (QA, base
  against head: 10.20 → 11.14 and 27.32 → 29.32; draw calls 4,542 → 5,200 and 12,073 → 13,969).
- **The Shadow page is pin-ready (D-R38):** `docs/factions/shadow.md` in the Teblor / Malazan shape, `PagesAhead =
  { "shadow" }`, 0 readiness problems; "Schema gaps (D-R38)" G1-G14 (STATE Request 56); "Owner review (D-R38,
  proposed)" (24 names and descriptions, the longest 131 characters). The Producer's answers to the developer's
  Q1-Q11 are docs/01 2026-11-15 (a); D-H9 writes them onto the page.
- **The stray empty `C:\Dev\rtsGameProject\ModelShots.cs` is still in the owner's main checkout** (on the owner's list).

## Standing rules (1-67)


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
**56:** a "is drawn right" claim about new content is checked on a screenshot of that content in the shipped data,
not on a forged copy with other numbers (M7-V1's 1.5× looked right on a 0.4 m unit; the real giants are 0.75 m).
**57:** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58:** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59:** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (lifted 2026-11-13-2306):** BUG-1321 is closed for 400 figures (10.1 ms alone); model types may be imported again.
**61:** before more than ten unit types are models, a view task measures a 1,000-figure crowd of mixed types alone
(rule 42) and reports what 16.7 ms would take (merged surfaces, a shared skeleton or baked animation); every art brief
reports the 400- and 1,000-figure bench with the capsule figure from the same window and reads the "while 90%+ alive"
column. An LFS size criterion names "the art pass's binaries", never "total LFS".
**62:** a developer runs the full default loop after its last edit, or its report says in its first lines that
it did not (two sessions running a developer fixed one row and re-ran only the affected classes).
**63:** a scratch folder's name starts with its track and task (`m7v4-...`, `d31-...`), is created new, and no
agent writes into, cleans or resets a folder it did not create (2026-11-14-0616: the view developer reset another
agent's clone named `clone`; nothing committed was lost). A brief that asks for a scratch clone says so.
**64:** "build 0 / 0" in a report means `dotnet build RtsGame.sln --no-incremental` after the last edit (an
incremental build hid two warnings in a new test file: BUG-1380, one fix round).
**65 (new):** QA's re-check ends with `dotnet build RtsGame.sln --no-incremental` on the commit that holds QA's own
files, and its report says so (2026-11-14-1246: QA's new `QaM6V21Test` carried a CS8600 warning that the developer's
0 / 0 could not have seen: BUG-1412). A warning in a QA file with the developer's commit clean is S3, fixed first in
that track's next task; in the developer's own files it stays S2 (BUG-1380).
**61 (amended 2026-11-14-1246):** the measurement is done (M6-V21: 1,000 mixed figures 26.9 ms alone; merged surfaces
are worth about 4-5 ms, a shared skeleton or baked animation up to about 8 ms, both together about 14-15 ms). The owner
asked for the Teblor figures first (inbox 2026-11-14), so the checkpoint moves: **merged surfaces are the view task
right after the Teblor humanoids**, and every art brief still reports the 400- and 1,000-figure bench alone.
**Note (not a rule):** the conductor writes the implement commit's message, so a golden header regen's reason (BUG-0750) goes in
the `GatherWedgeQaTests` comment and in the ACCEPT's `COMMIT_SUMMARY`; the Producer checks the comment.
(`SameGameDataHashes` is the name of the rule in that class's comments, not a test method: do not write it as a
criterion's test name again.)
**66 (new, 2026-11-14-2156):** at PLAN every invariant criterion ("no worker ...", "every pair ...", "within N s") is
checked two ways before it is written as pass / fail: **does it hold today with the feature off** (if not, or if the
out-of-scope list forbids what would make it hold, it is written "measure and report, against off": rule 48), and
**can it hold together with every other criterion** (M6-S9: criterion 5's Stop clause against criterion 4, criterion
7's 60 s window against "off is identical"; both cost a fix round and an ACCEPT by ruling). A criterion QA shows to be
the brief's error is corrected in the session log by name, never passed silently.
**67 (new):** a developer's open questions for the Producer are written where QA can read them (a "Questions for the
Producer" section of the page or docs section the task owns, each with the recommendation); the Producer's answers
go to docs/01 and the next task writes them in (D-R38: Q1-Q11 lived only in the returned report).
**61 (amended again 2026-11-14-2156):** the order after the Teblor humanoids is the owner's Shift-queue request, the
view's hardening (4 / 4), then merged surfaces; 400 mixed figures of eleven types are at 11.1-11.4 ms alone (worst 1 % 15.4-15.7).

## Watch-outs (all tracks)
- **New this session:** five Stress rows are red on `main` by ruling (BUG-1430; listed under "Where things stand"):
  a "default loop with Stress" run expects exactly those five. `main` builds **0 errors / 0 warnings** again.
- **`gatherOverlap` is absent from the shipped `rules.json`.** A helper that forges it must tolerate shipped data that
  later carries it (rule 58; `GatherOverlapScenes` writes or removes the field itself). Any value above 0 turns the
  gather floor on; "off" is the field absent or 0, and only that is bit-identical to before.
- **Eleven unit types are models** (Laborer, Crossbowman, Heavy Infantry; Camp Follower, Raider, Desert Archer; the
  Teblor Gatherer, Warrior, Javelin Thrower, Shaman, Breaker); ten are capsules (`ModelViewTest` prints "11 models,
  10 capsules; teblor capsules: [teblor_destrier_rider teblor_war_dog]"). A count of model types is read from
  `tools/asset-manifest.json`, never a literal (`QaM6V21Test` row 1). A model file holds the 1.84 m base figure; the
  data `scale` is applied by the view. Bars and marks still use the capsule's height (BUG-1441).
- **Session length:** 2026-11-14-2156 ran about 7 hours (builders and QA about 6; one sim fix round; the lock was
  refreshed four times). QA full on the sim beside a standard and a light pass still gave default loops of 1 h 4 m
  to 1 h 15 m against 22 m alone. Next session the sim is on QA full again with pin re-measures: keep the data task
  light and tell the view's QA to run its bench last.
- **The third faction's order of work now:** M6-S10 (the wedge + the gather value; every opening measurement is
  redone after it) -> data **D33** (BUG-1420) as the start of the balance pass -> the sim reads the Frenzy report
  again (Request 53 part two). The Teblor humanoids are done; the Rider and the dog wait on the owner's packs. M7
  criterion 4 is ticked after the balance pass; the end-of-M7 hardening follows it.
- **Three factions ship now.** A fixture that means "the two original factions" uses `TestSim.TwoFactionData`,
  `TestDataDir.CopyOfShippedTwoFactions()` / `CutToFirstFactions(2)` (sim) or `ThirdFactionForge` (view, cut to two).
  A pin about "every shipped unit" must be true for the Teblor too (a bite, a pack, a scale, a self-cast aura, radius
  0.75, regeneration).
- **Radius 0.75 is a fourth radius class** (+131,072 bytes on a 1024 map; the shipped-data memory row allows it by
  class count). A single-type 0.75 m crowd arrives about 40% less often than a 0.4 m one on the crowd sweep's maps
  (`Stress/WideCrowdStressTests`, a report with no bound): 500 to 4 points 172.6 v 288.1 mean arrivals, 128 to 64 goals
  58.4 v 112.1. If a sim task changes movement, re-run it and print both columns.
- **The scene loop is all green: 107 scenes** (`TeblorModelTest` and QA's `QaM7V5Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
  samples after a forced collection (objects flat at 3,252); BUG-1350 is fixed by cause, so an ERROR line there is a
  finding. Rows that flake under load are listed below; any other red row is a finding.
- **Model animation is manual:** every model's `AnimationPlayer` is in manual mode and `UnitViews._Process` advances it
  through `CrowdAnimator` (48 nearest every frame, the rest 1 frame in 3). A scene that calls `Sync` directly and wants
  motion calls `AdvanceAnimations(delta, eye)` too. A model shows no worker tint (`ShownTint` still records the state).
  Since M6-V21 the figures follow the game speed (`UnitViews.AnimationRate`: the runner's speed, 0 while the tree is
  paused); a replay that has reached its end is not paused, so its figures keep moving in place (a small view item).
  Each model type has its own `AnimationLibrary` (state names -> the shared clips, from the clip map in the `.glb`'s
  glTF extras); a slot's type and model are set before the fog check.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **The Teblor rows are on `main`.** A Frenzy status id sorts after `stealthed`, and `teblor_frenzied` is the last row
  of `common/statuses.json`: a brief that adds a status says where it sorts (`StatusLoaderTests.Shipped_Regenerating_*`
  pins the sorted order on purpose). The two golden data-hash pins
  (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`, `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`)
  are sim-owned: a data brief that moves the hash is handed them by name (rule 51).
- **Two tracks must not both move the data hash in one session** (each regenerates the golden's header and checksum:
  a certain conflict). A sim task that ships a value in `game/data/common/**` and a data task that ships a faction
  file are planned in different sessions, or one of them lands test data only.
- **Every data change moves the golden header now** (the Teblor use `damageBonus`, `onEnd`, `statusDuration`, a
  building `scale`, `carry` and a per-opening share): batch data changes into one hash move a session, and say in the
  report that older saves and recordings stop loading.
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


---

## Sim track

### Where we are
M6-S9 on `main`: the gather-overlap rule, off. Counter **2 / 4**. New: BUG-1431 S3 (today's Keep-corner wedge),
BUG-1430 S3 (the fuzz window; gates the value), BUG-1434 S3 (Pale Hills seed 1), BUG-1433 S4; BUG-1432 wontfix
(Producer decision). Debt: BUG-1161's three alias names S4, BUG-1149 / 1151 / 1152 S3, BUG-1150 S4, BUG-1010; notes:
`[0]` lookups left in `FrenzyRulesLoaderTests`, `StatusDurationTechTests`, `DataContentHashTests`, QA's `M7H1QaTests`;
the JSON-`null` policy (`forestStealth` / `regen` read it as absent, `reserveArmyShare` and `gatherOverlap` refuse it:
`NumberJson` shows how to tell them apart).

### Next task candidates
1. **M6-S10: BUG-1431, then the shipped value, one hash move** (feature, QA **full**; uncertain: about 800 production
   lines; the pin re-measures don't count). Stages, in this order, each reported:
   - **(a) The wedge, knob off.** Cure the drop-off wedge at a Keep's corner (the returning worker that arrives out of
     reach behind mates whose goal cell is the Keep's stand cell and retries the same way; and the outbound worker held
     at its Keep, seed 27) by a generic rule, no new per-unit state if possible. Proof: `GatherOverlapFuzzTests` and
     QA's sweep with the knob **off** over seeds 2-56: no worker past 60 s while its node lives (today 6 seeds).
     `KnownWedges` empties. This moves knob-off movement: the golden's `k` lines and the AI pins move here.
   - **(b) The value.** `"gatherOverlap": 0.5` in `game/data/common/rules.json` (the data hash moves once; older saves
     and recordings refused; fixtures as D32 did). QA's five red rows green, or re-stated against off with the
     figures if a knob-only breach remains (then say which and why).
   - **(c) The pins, once, on the final tree** (rules 41 / 43 / 45): the golden regenerated with the reason in the
     `GatherWedgeQaTests` comment; the two data-hash pins; the M5-5 rows and docs/03's M5-5 table; the Hard v Normal
     tally; `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, `OutcomePinM73bQaTests` (outcomes must hold or the
     report says which changed and why), the openings' content report rows the data track owns are **named and
     handed over, not edited** (rule 51 in reverse: list them for D33).
   - **Pale Hills seed 1 (rule 45, decide in the brief):** if it does not end, that is BUG-1434 / BUG-1116 (the hunt
     never finds a lone worker), not the overlap. Preferred: fix the hunt for a lone loop-less worker in the same
     task if it is a few lines; else the row moves to the known-stalls list with its id and M5-5's criterion row is
     re-pointed to another seed that ends on all three maps (a finding in the report, not a silent re-bound).
   - **Terminal rule:** if (a) is not cured inside the slice, stop there: report, ship nothing, the knob stays off
     and no pin moves. The data track moves no hash that session.
   - `WideCrowdStressTests`: both columns re-printed (movement changed).
2. **M8-2: the Shadow Archer's stand-still stealth** (Request 56 G1; then G2 the first-shot bonus), schema first,
   test data only (QA full).
3. **Requests 54 / 55** (from D32). Each changes Normal AI behaviour (rules 43 / 45): plan with a data task that
   re-measures; best batched with a session that already moves the pins.
4. **Request 53, part two:** read the Frenzy report on `main`, re-print docs/03's table, explain "army pop 15:00
   (sum)" 0.
5. **Request 56 G3-G8** (Pack Hunter, `maxAlive`, teleport, the summoned slot, a tech that grants an ability, the
   AI's summon cast), one or two a session.
6. A hardening with no opening measurement beside it: BUG-1149 / 1151 / 1152, BUG-1161's names, BUG-1433, the
   JSON-`null` policy.

---

## View track

### Where we are
M7-V5 on `main`. Counter **3 / 4**. 107 scenes, all green alone. Fixed: BUG-1412, BUG-1410. New: BUG-1441 S4 (bars and
marks 1-1.4 m above a giant's head; a foot off a ramp; the javelin into the ground; Casting plays `idle`), BUG-1440's
addendum (a no-LFS clone: 261 `ERROR` lines, 71 `.import` files rewritten). Older: BUG-1411 S4, BUG-1381 S4, BUG-1177
item 2 (the owner's taste), BUG-1115 item 4. What reads badly on the pictures: the five Teblor wear the same two
outfits and differ by prop only; the Shaman's robe is a tunic in the player's colour, not bone white.

### Next task candidates
1. **The owner's request: the Shift-queue made visible** (inbox 2026-11-14; feature, QA standard). From the PLAN's
   audit: every unit order takes the queue flag in the sim (`Command.QueuedFlag`; 8 entries a unit) and the view
   passes Shift on all of them; the one gap is the first Shift-placed building (`CommandCard.GhostClick`: `queued:
   shift && _placed > 0`). Build: the selected units' queued orders drawn (a line from the unit through each queued
   point, a marker per order kind; hidden for enemies and under fog); the first Shift-placed building joins the
   queue; a scene row per order kind (Move, AttackMove, Attack, Gather, Build, Repair, UseAbility, Stop, Hold) that
   the order is queued, drawn, and starts when its turn comes. If the queue is not readable from the view, a
   read-only accessor in `sim/Rts.Sim/ViewApi/` (no tick or hash change); if an order kind does not start, that is a
   Request for the sim, not a fix here. Pictures on the shipped data. No allocation per frame for the lines.
2. **The view's hardening** (the counter is 4 / 4 after task 1): BUG-1441 item 1 first (bars, status and cast marks
   and the cargo marker follow the drawn figure's height, not the capsule's: every Teblor match shows it), a Casting
   clip (`attack_cast`) for a type that has one, BUG-1411, BUG-1440 (the import refuses pointer files with one clear
   line and touches no tracked file), BUG-1381, figures at a finished replay.
3. **Merged surfaces** (rule 61): one material and one atlas a model, every model re-generated; 400 figures are at
   11.1-11.4 ms of 16.7 with eleven types, worst 1 % 15.4-15.7.
4. The Destrier Rider and the War-dog once a horse / dog pack is on disk (owner); Teblor buildings.
5. The view's part of M8 criterion 1 once a roster ships the block.
6. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Where we are
D-R38 on `main`: the Shadow page pin-ready; no data byte moved. Counter **3 / 4**. Fixed: BUG-1400. New: **BUG-1450
S3** (two page cells the real pins read differently; two QA rows skipped on it), BUG-1451 S4. Open: BUG-1420 S3,
BUG-1421 S4, BUG-1360 S4.

### Next task candidates
1. **D-H9, the data's hardening pulled forward** (light; **no hash move and no opening measured: the sim ships the
   gather value that session**). In order: **BUG-1450** (capitalise "Passive" twice in shadow.md; `AbilityContentTests`
   accepts "—" for the Radius of a summon with spawn effects only, as it does for Range; `PagePinReadinessTests` flags
   a `passive **` bullet that `PagePassives` does not read; un-skip QA's two rows); **the own-names pins** (the
   developer's Q4: `UnitContentTests` E and `BuildingContentTests` F blank out the faction's own unit and building
   names before the "names only own-faction things" check, with a row showing "Edur Warrior" / "Warrior Lodge" pass
   and a real foreign name still fails); **BUG-1451** (G14's wording, the Edur Ram row's labels); **the Producer's
   answers to Q1-Q11 on the page** (docs/01 2026-11-15 (a), as a short "Decisions (D-R38)" section); BUG-1421 items
   3-4; BUG-1360. Only `docs/factions/**` and the two content test folders.
2. **D33: Breakers, Destrier Riders and War-dogs back in the Teblor army** (BUG-1420; BUG-1421 items 1, 2, 5), the
   start of the balance pass, **on the economy after M6-S10** (the session after it lands). Measure Easy, Normal and
   Hard and the mirror; count distinct matches. Keep the Shamans (27) and the second Longhouse (18). One hash move.
3. The owner's answers if any land ("Shadow names ok", "teblor ai: early raid", the Teblor table, "teblor buildings:
   1.5", "opening text: ...", "Frenzy text: ...", the counters, `goldShare`, BUG-0780, `counterStrength`).
4. The Shadow roster, file by file, as Request 56's schemas reach `main`.
