# Handoff: brief for the next session

Written by the Producer at the ACCEPT of session **2026-11-13-1118** (session 1 / 8 of 2026-11-13; it ran about 9.5 hours).
Sim **M7-6** ACCEPT (1 fix round, QA full FAIL → PASS), view **M6-V20** ACCEPT (0, QA standard PASS_WITH_ISSUES), data
**D30** ACCEPT (0, QA standard PASS_WITH_ISSUES). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 2 / 4.**
Counters: **sim 3 / 4, view 4 / 4 (hardening next), data 4 / 4 (hardening next)**. Bug ids next session:
**sim 1340+, view 1350+, data 1360+** (the Producer took BUG-1323 at this ACCEPT). Open S1 / S2: none. The next session is 2 / 8 of 2026-11-13, or 1 / 8 of
2026-11-14 if it starts after midnight.

**Next session in one line (proposal):** sim **M7-7** (Request 53: why the Normal AI almost never casts Blood-oil
Frenzy, and a cast rule for a buff; Request 48's carry schema if it fits; feature, QA full) · view **M6-VH7, the view's
clean-up** (BUG-1323 first, a red test row; then BUG-1321, the cost of animated figures; BUG-1320, BUG-1322, BUG-1230;
hardening, QA standard) · data
**D-H8, the data's clean-up** (BUG-1330, BUG-1331 items 2-3, V24 re-measured with the share on the Teblor's own opening;
**no shipped file**; hardening, QA standard). **D31, the one landing, is the session after.**

## Where things stand

- **`main` after this session = sim 3f67a6f + view 19cdacf + data 302c225.** The Producer's scratch clone of that tree
  (`accept3` in the session scratchpad): build 0 / 0; the default loop **9,080 / 0 failed / 22 skipped of 9,102 in 41 m 7 s (beside the scene loop)**; smoke **PASS**; the scene loop
  **100 of 101 (red: `QaM6V20Test` alone, BUG-1323, a test defect, by ruling)**.
- **No shipped data moved this session.** The golden header is still **67269D955D18D885**; save format 10; every pin as
  it was. The owner's saves from after 2026-11-11-2256 still load.
- **The Teblor landing is unblocked on the sim side.** BUG-1280 is fixed (the shared test copy tolerates shipped rows) and
  an opening may carry its own `attack.reserveArmyShare` (Request 52; absent = `rules.json`'s 1; `null` is an error).
  docs/03 "Implementation (M7-6)" ends with the JSON for the data track.
- **The data's pins are ready for the landing.** `Content/TeblorLanding` holds the exact D31 copy (four rows, both
  directions, idempotent; `D30_WRITE_LANDING=1` writes it into a scratch clone); the page says "the rule exists (M7-4);
  the row lands with D31" and `Content/RuleRowClaimTests` holds each such sentence to the data both ways.
- **The first two real figures are in the game:** the Malazan Laborer and Crossbowman are animated, team-coloured models
  (2,597 / 2,727 triangles, 4.5 MB in LFS); every other unit is still a capsule. Pictures:
  `studio/review/2026-11-13-1118/`. **400 of them cost 14.5-21 ms a frame against 2 ms for capsules (BUG-1321):** no
  further model type until that is fixed (rule 60).
- **The opening, measured again with the Frenzy rows (seeds 1-8, 64 matches each):** the rows change nothing before 15:00
  (the Normal AI casts Frenzy 0.02-0.16 times a match). With the share at 0.5, V24 meets "Age II and a wave by 10:00" on
  6 of 8 seeds (3 of 8 at share 1), Age II by 10:00 in 52 of 64, the Anointing in 23, defeated by 15:00 in 15.
  **Provisional pick for D31 (Producer): V24 with `reserveArmyShare` 0.5 on its own opening, its Stables time kept**; the
  0.5 was measured through the global field, so D-H8 re-measures it per opening before D31 lands it.
- **Not in the Teblor data yet:** Frenzy's +20% damage, the aftermath slow and the Anointing's +4 s, the buildings'
  1.25× (all at D31), the Gatherer's 15 a trip (Request 48), the AI's later-wave rules (Requests 46 / 51), an AI that
  uses Frenzy (Request 53).
- **A stray empty file sits in the owner's main checkout:** `C:\Dev\rtsGameProject\ModelShots.cs` (0 bytes, untracked,
  made 12:07 by the view developer's run; BUG-1322 item 3). Nobody in the studio may touch the main checkout; it is on
  the owner's list in STATE.

## Standing rules (1-60)


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
**57 (new):** a QA regression scene or test that is red by an open S3 reaches `main` only by an explicit ruling at ACCEPT
(named in the session log and STATE's Tests row), with the fix as the first item of that track's next brief; until then
every loop's expected result names that one row. Two sessions red = the fix becomes the whole task.
**58 (new):** a test helper that forges a data row must tolerate the shipped data already carrying it (look the id up,
then add or edit), and when docs/03 tells the data track which rows to write, the sim's QA applies them on a scratch
clone and runs the task's classes (BUG-1280).
**59 (new):** a developer who writes a fix for "the data track's rows break our tests" runs its own new tests on a copy
carrying those rows before reporting (M7-6's fix broke on exactly the rows it was written for: BUG-1310 / 1311, one fix
round). A sim test about "every shipped row" reads the count from the shipped file, never a literal.
**60 (new):** no further unit model type is imported while BUG-1321 is open: the 400-figure frame time is measured alone
(rule 42) and must be under 16.7 ms on average before the art pass widens.
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
- **`QaM6V20Test` is red on `main` on purpose (BUG-1323, rule 57): the scene loop reads 100 of 101** (99 +
  `ModelViewTest` + QA's `QaM6V20Test`; BUG-1290's `QaM7V3cTest` is green). Its restart row fails when the object count
  rises three restarts running, which is garbage-collection timing (5 of 6 runs on the merged tree; 16 restarts on a
  scratch copy are flat at 3,623-3,708; nodes, resources and orphans exactly flat). The fix is the first item of
  M6-VH7. Rows that flake under load are listed below; any other red row is a finding.
- **Unit models are on by default** (`UnitViews.UseModels`; `--placeholder-art` forces capsules). Five scenes pin capsules
  and set it false: `EconomyViewTest`, `QaM6V3Test`, `QaM7V3cTest`, `ScaleViewTest`, `ShellTest`. A new scene that reads a
  slot node's own mesh or material must do the same or read `ModelMeshOf`. The model files are Git LFS: a clone needs
  `git lfs pull` then an import (a pointer file gives the capsule and one warning, never an `ERROR`).
- **`tools/import-assets.ps1` needs Blender 5.2 and `asset-sources/` (the main checkout's)**; its outputs are committed and
  a re-run is byte-identical (QA, fresh clone, 180 s). `tools/asset-manifest.json`, `tools/import-assets.ps1` and
  `tools/art/**` are the view track's from now on (the rest of `tools/**` stays the sim's): say so in each brief.
- **BUG-1280 is fixed: the Teblor rows may land.** With docs/03's M7-4 + M7-6 rows applied to this session's merged tree,
  exactly 24 default-loop rows go red and all are expected at the landing: the data's own `Content/**` / `QA/Content/**`
  pins (the landing's page and docs/02 edits turn them green; 7 are "rows not landed yet" checks that D31 flips) and the
  two golden data-hash pins `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders` and
  `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders` (sim-owned: the D31 brief hands them to the data track, rule 51).
  Any other red row at the landing is a finding. A Frenzy status id must sort after `stealthed` (docs/03 "For the data
  track (M7-4)"; QA's `D30LandingQaTests.ShippedData_TheM4StatusesKeepTheirStateHashNumbers` now names a wrong id).
  `StatusLoaderTests.Shipped_Regenerating_*` assumes the same of any later status: a brief that adds one says so.
- **M7-4's fields are in the data hash only when used:** the first shipped `damageBonus` / `onEnd` / `statusDuration` /
  building `scale` moves the golden header (rule 51 applies to that data brief).
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
- **The third faction's order of work now:** the data's clean-up first (counter 4 / 4: BUG-1330, BUG-1331, and V24
  re-measured with the share on the Teblor's own opening only) → data **D31, the one landing** (the Frenzy row, the
  ability's effect, the Anointing's +4 s, the buildings' `scale` 1.25, the opening with `reserveArmyShare`; one golden
  header move; M7 criterion 3 ticked there) → the view draws the buildings' `scale` (rule 56 on the shipped Teblor) → the
  balance pass (Requests 46 / 48 / 51 / 53 as measured) → models once BUG-1321 is closed (rule 60).

- **Session length:** 2026-11-13-1118 ran 9.5 hours (the sim's QA alone 4.8 h under three tracks' load). Two tracks on
  light or standard QA beside one on full is the shape that fits a day; avoid Soak or all-category runs in a QA pass
  (rule 53) and say so in the sim brief.

---

## Sim track

### Current session plan (2026-11-13-2306, session 2 / 8 of 2026-11-13): M7-7, feature, QA full

**Goal.** Request 53: the Normal Teblor AI casts Blood-oil Frenzy 0.02-0.16 times a match before 15:00. Measure why,
then (only if the cast rule is the cause) give the AI a cast rule for a buff aura on its own fighters, behind a data
knob that no shipped file carries yet, so nothing shipped moves until D31 lands it with the one golden header move.

**Producer correction to the last hand-over (rule 48):** it asked for a knob "required, hashed" in `rules.json` and
also put `game/data/**` and the golden out of scope; both cannot hold. Ruling: the knob is **optional** (absent = today's
rule), hashed only when present, exactly like M7-4's fields and Request 52. No `game/data/**` byte this session.

**Scope.**
- Part A (always): a Stress-category report (no bound) on `Abilities/BloodOilScenes`' copy: Normal Teblor AI against
  Normal Malazan and Normal Whirlwind, both seat orders, seeds 1-8, to 15:00 (32 matches). Per match and summed: the
  tick of the first Shaman, Shamans alive at 5 / 10 / 15 min, the thinks at which a free Shaman had Frenzy ready, and of
  those: with 2+ seen enemies within 8 m (today's condition), with N own army units within 8 m, with N own army units
  within 8 m that are attacking, the Shaman's distance to the army's centroid; casts sent and landed. Also once with the
  data track's landing opening if it can be read without editing `Content/**` (read-only for the sim).
- Part B (only if A points at the cast rule): in `AbilityManager.Target`, a self-aura that affects own units, applies a
  status and deals no damage is cast when at least `ai.castMinOwnFighting` own army units (workers and the caster not
  counted) within its radius are attacking, instead of the enemy count. `rules.json` `ai.castMinOwnFighting`: optional
  integer 1-64 (limits in `DataLimits`), absent = the enemy-count rule as today; in the content hash only when present.
  Generic by kind / affects / effects; no ability, unit or faction id in C#. Own units only (the AI's own view; no fog
  question); no allocation in the think. Re-run Part A's report with the knob at 2, 3 and 4 and recommend one value.
- If A points elsewhere: Shaman not with the army = report it with the numbers and a proposed generic rule, build
  nothing; too few Shamans = a note for the data track (composition is `ai.json`'s). Then, and only then, Request 48's
  schema rides along: `units.json` optional `carry` (a worker's own carry amount; absent = `rules.json`'s; validated;
  hashed only when present; test data only).
- OUT of scope: any `game/data/**` byte, the golden, every pin's literal, save / replay formats, `Content/**` and
  `QA/Content/**` (the data track's), `sim/Rts.Sim/ViewApi/**`, Requests 46 / 51, Soak runs.

**Acceptance criteria.**
1. Part A's report exists as a Stress test, prints the table above, and the developer's report names the cause in one
   sentence with the three candidate causes' figures side by side. Measure and report; no pass / fail figure.
2. (B) `AiLoaderTests` / `DataValidationTests` rows: absent loads (rule off); 1, 2, 64 load; 0, 65, -1, 1.5, a string and
   `null` are each one error at `ai.castMinOwnFighting`.
3. (B) `AbilityManagerTests` scenes on the test copy: knob present, N own fighters attacking within the radius and no
   enemy within it → cast; N - 1 → no cast; N own units idle beside the caster → no cast; workers and the caster not
   counted; knob absent → today's rule exactly. A damage self-aura and a ground ability are untouched by the knob.
4. (B) The shipped content hash is unchanged (computed on base and head, no literal, rule 49); a `rules.json` carrying
   the knob hashes differently; `git diff 06277d6...HEAD -- game/` is empty.
5. (B) With the knob absent a full Teblor AI match on the test copy is hash-identical tick by tick to the base (one
   literal row, Teblor v Malazan); every existing pin class green with no literal edited (rules 43 / 45: no shipped
   match may move; if one does, stop and report, do not re-pin).
6. (B) Part A's report re-run at 2 / 3 / 4: casts a match before 15:00 and the outcome columns (defeated by 15:00,
   army pop at 15:00, wins) beside the knob-absent row. Measure and report; the value is picked at D31.
7. (B) `AllocationTests`' think rows green; a save taken mid-match with the knob present loads and runs 2,000 ticks
   hash-identical (no new saved state, or the brief is wrong: stop and report).
8. Build 0 / 0, the default loop green, docs/03 "Implementation (M7-7)" with a "For the data track (M7-7)" paragraph
   (the JSON for `rules.json`, the recommended value, "lands with D31") and docs/02's AI section if a rule was built.

**Design references.** docs/03 "Implementation (M5-4c)" (the cast rules), "Implementation (M7-4)", "Implementation
(M7-6)" (the optional-field pattern: `RawValue`, hash only when present); docs/02 AI section; `docs/factions/teblor.md`
"Blood-oil Frenzy"; STATE Requests 53 and 48.

**Constraints.** Rule 41: the data track re-measures V24 this session on `main` without this rule; your figures and
theirs are not comparable and both reports say so. Rule 58 / 59: a test that forges the knob or the Frenzy rows must
pass on a copy that already carries them (D31 will); run your new tests on such a copy before reporting. Rule 52: end
every test process you started. Fog-filtered view only; no Dictionary iteration; no allocation per think. Bug ids 1340+.
Size: about 800 lines of production + developer tests.

**QA focus (full, rule 53: default loop with Stress, Perf alone under rule 42, no Soak).** The knob's boundary (N / N - 1,
a fighter exactly on the radius, a fighter that dies the tick of the think, a Shaman that is a wave member owed its
Move home, two Shamans beside one fight: do both cast and waste one?); the cooldown / MaxTries judging with a cast that
has no point; Easy (which casts since D19) and Hard on the knob; a 2v2 where the fighters within the radius are an
ally's (must not count); determinism: same seed twice, save / load mid-Frenzy, cross-process digest; knob absent =
bit-identical on three shipped-pin matches; D31's rows applied on a scratch clone with the knob written into
`rules.json` (rule 58): which default-loop rows go red beyond the 24 already known.

### Where we are
M7-6 on `main`. Counter **3 / 4** (one more feature session, then the hardening). Fixed: BUG-1280, BUG-1281, and
BUG-1310 / 1311 / 1312 inside the session. Debt: BUG-1220 / 1221 S4, BUG-1161's three alias names S4, BUG-1149 / 1151 /
1152 S3, BUG-1150 S4, BUG-1010; QA's unfiled notes: `StatusLoaderTests` x4 find shipped rows by file position;
`BloodOilScenes.Edit` addresses the Anointing as `techs[0]` and the Frenzy as `abilities[0]`.

### Next task candidates
1. **M7-7: an AI that uses Blood-oil Frenzy (Request 53).** Feature, QA full (AI decisions).
   - **Measure first, in the brief's first criterion:** on the data's landing copy (`Content/TeblorLanding` is the data
     track's; the sim uses `Abilities/BloodOilScenes`), why the Normal Teblor AI casts 0.02-0.16 times a match before
     15:00: too few Shamans, the Shaman not with the army, or the cast rule (`ai.castMinTargets` 2 enemies within the
     aura's 8 m of the caster).
   - **Then the rule, only if the measurement points at it:** a self-aura that buffs own units is cast when enough of the
     caster's own fighters inside the radius are fighting (a data knob in `rules.json` `ai`, required, hashed), not when
     enemies stand beside the caster. Generic: by the ability's kind and effect, no ability id in C#.
   - **Rule 43 / 45:** only a faction with a self-aura buff can move: name the Teblor pins (the `FactionOrderTests` copy
     pins, the Teblor-copy FFA hash) and say what to do if one moves. Malazan / Whirlwind pins must be identical.
   - **Rule 41:** the data's D-H8 re-measures V24 the same session on a copy that carries the Frenzy rows, without this
     rule: its figures are for `main` before M7-7. Say so in both briefs; D31 re-runs the one chosen row on the tree it
     lands on (64 matches, about 7 minutes alone).
   - Request 48 (a per-unit or per-faction worker carry in the data, schema + validation + hash only when present) may
     ride along if the first part is small; otherwise it is the task after the hardening.
   - Out of scope: `game/data/**`, the golden, any format.
2. **The sim's hardening at 4 / 4** (the session after): BUG-1220 / 1221, BUG-1161's names, BUG-1149 / 1151 / 1152, the
   two unfiled notes above.
3. Requests 46 / 51's remaining parts, each only when the balance pass's numbers ask for it.

---

## View track

### Current session plan (2026-11-13-2306): M6-VH7, the view's hardening (counter 4 / 4), QA standard

**Goal.** Get the scene loop back to all green (BUG-1323), then make animated figures cheap enough that the art pass
can widen (BUG-1321, rule 60), and stop the worker's state colour hiding the figure (BUG-1320).

**Scope, in this order.**
1. **BUG-1323 (first, a few lines):** `QaM6V20Test`'s restart row forces a collection before each sample
   (`GC.Collect`, `GC.WaitForPendingFinalizers`, `GC.Collect`, two frames) or samples 12+ cycles and compares the halves'
   minimums. The exact checks on nodes, resources and orphans stay.
2. **BUG-1321:** take the baseline bench in your first minutes, before the other tracks' suites start
   (`ModelShots --bench 30 --vsync off`, models and `--placeholder-art`), and write beside every figure how many
   `testhost` / Godot processes were running. Then, in order, measuring after each: (a) drop the 40 finger bone tracks
   from the animation library at import (`tools/art/build_unit.py`); (b) crowd animation at a lower, staggered rate
   (players in manual mode, N slots advanced a frame, nearest the camera first; the rate and N are view constants or
   settings, not gameplay data); (c) no shadow casting for models past a zoom. Stop when the target is met. If (a) + (b)
   do not reach it, stop and report what a shared-skeleton or baked-animation approach would cost; do not start it.
3. **BUG-1320 (Producer default, owner may revisit):** on a model the gathering / returning / building tint is not
   drawn over the body; the hit flash stays; capsules keep their tints. A skirmish-start picture before and after.
4. **BUG-1322** items 1-2 (the old model child renamed or removed before it is freed; the team-less warning once per
   file per run). Item 3 is the owner's (never touch `C:\Dev\rtsGameProject` outside your worktree).
5. **BUG-1230** (long faction names in the setup lists) only if the above is done and the size allows.
- Files: `game/**` except `game/data/**`; `tools/asset-manifest.json`, `tools/import-assets.ps1`, `tools/art/**`;
  docs/03 / docs/04. OUT: `sim/**` (no ViewApi change is needed), `game/data/**`, any new unit model type (rule 60),
  the buildings' drawn `scale`, a shared-skeleton or baked-animation rewrite.

**Acceptance criteria.**
1. `QaM6V20Test` green ten runs of ten alone; the scene loop all green (101 of 101 plus any scene this task adds; no red row
   on purpose).
2. 400 models (`ModelShots --bench`) under 16.7 ms average with no other studio test process running, the worst 1%
   reported; 1,000 models and the capsule baseline in the same window as a report. If no quiet window exists, report
   the loaded figures with the capsule figure from the same window; the Producer measures alone at ACCEPT and that
   figure decides.
3. Animation still reads: pictures at 20 m and 40 m of walking, attacking and gathering figures after (b), looked at;
   the nearest figures to the camera animate every frame; no figure is frozen for longer than the stagger period
   (a scene row checks every visible player advanced within it).
4. Re-import: `tools/import-assets.ps1` twice gives byte-identical outputs; the committed `.glb` sizes before / after;
   total LFS under 12 MB; `git status` clean after a re-run.
5. BUG-1320: a gathering Laborer model's team surface shows the player colour (a scene row reads the material; the
   picture); `EconomyViewTest`'s capsule tints unchanged; the hit flash on a model still pinned.
6. BUG-1322 items 1-2 each with a scene row (the reused slot's child is named `Model`; one warning per file per run).
7. `--placeholder-art` pictures pixel-identical to the base; 0 bytes allocated over 120 syncs of 400 models still holds.
8. Build 0 / 0, smoke PASS, docs/03 "Implementation (M6-VH7)" and docs/04 (the finger tracks, the animation rate) in
   the commit.

**Design references.** docs/03 "Target hardware" and "Implementation (M6-V20)"; docs/04 "Style guide", "Team color",
"Animation setup"; BUG-1320 / 1321 / 1322 / 1323 / 1230.

**Constraints.** Dumb views (rule 7): the animation scheduler holds no gameplay state and reads only snapshots. Five
scenes pin capsules with `UseModels` false; a new scene that reads a slot's mesh does the same or reads `ModelMeshOf`.
Never kill another track's `testhost` or Godot; end your own (rule 52). Scratch scenes write under the scratchpad, not
`user://`. Bug ids 1350+.

**QA focus (standard: default loop, the scene loop, no Stress beyond what the task touches).** Re-measure the bench on a
fresh clone and say what else ran; the stagger under slot reuse, a restart, a replay seek, pause (does a paused match
freeze every figure on the same pose, and does un-pausing jump?), game speed 2x, zooming in on a far crowd (do figures
pop?), an attack clip shorter than the stagger period (is a blow ever skipped whole?); a death during a skipped frame;
the re-import on a fresh clone; `QaM6V20Test` twenty runs.

### Where we are
M6-V20 on `main`. Counter **4 / 4: the next session is the view's hardening.** 101 scenes, `QaM6V20Test` red on
purpose (BUG-1323). New debt: **BUG-1323 S3 (the Producer's), BUG-1321 S3 (ruled at ACCEPT, see the log), BUG-1320 S3,
BUG-1322 S4.** Older: BUG-1230 S3, BUG-1177 item 2 (the
owner's taste), BUG-1115 item 4.

### Next task candidates
1. **M6-VH7, the view's clean-up.** Hardening, QA standard.
   - **BUG-1323 first (a few lines, rule 57):** `QaM6V20Test`'s restart row forces a collection before each sample
     (`GC.Collect`, `WaitForPendingFinalizers`, `GC.Collect`, two frames) or samples 12+ cycles and compares halves; the
     exact checks on nodes, resources and orphans stay. Criterion: the scene green ten runs of ten alone, the loop 101 of
     101.
   - **Then BUG-1321 (the rest of the session if it needs it).** Measure alone (rule 42, no other track's suite running: the
     brief may ask the developer to take the bench first thing). Steps in the order the developer and QA proposed: drop
     the 40 finger bone tracks from the library at import (`tools/art/build_unit.py`); advance crowd animation at a lower,
     staggered rate (the players in manual mode, N slots a frame, nearest the camera first); shadows off for models past a
     zoom. **Criterion: 400 models (the `ModelShots --bench` scene) under 16.7 ms average and the worst 1% reported, alone;
     1,000 as a report.** If the first two steps do not reach it, stop and report what a shared-skeleton or
     baked-animation approach would cost; do not start it.
   - **BUG-1320 (Producer default, owner may revisit):** on a model the gathering / returning / building tint is not
     drawn over the body (the clip and the cargo marker already say what the worker does); the hit flash stays. A picture
     of a skirmish start before / after. Capsules keep their tints.
   - **BUG-1322** items 1-2 (rename before freeing; warn once per file per run). Item 3 is the owner's.
   - **BUG-1230** (long faction names in the setup lists) if the size allows.
   - Files: `game/**` except `game/data/**`, `tools/asset-manifest.json`, `tools/import-assets.ps1`, `tools/art/**`,
     docs/03 / docs/04. Re-importing changes the committed `.glb`: one commit, sizes reported, the tooling still
     byte-reproducible.
2. **The buildings' drawn `scale`** (the session after, a feature): the placeholder box at `BuildingDef.Scale` about the
   footprint's centre; footprint, outline, hit test and ghost unchanged; rule 56's screenshot on the shipped Teblor once
   D31 has landed. Closes BUG-1331 item 1.
3. **The art pass's next slice** only after BUG-1321 (rule 60): Heavy Infantry and the Whirlwind line from the two outfits
   recoloured, per the default in STATE's Waiting item; a per-weapon attack clip.
4. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...") whenever one lands.

---

## Data track

### Current session plan (2026-11-13-2306): D-H8, the data's hardening (counter 4 / 4), QA standard; no shipped file

**Goal.** Close the two gaps D30's QA found in the landing's checks and settle the one number D31 still rests on
(the Teblor opening's own reserve share), so D31 next session is a copy-and-verify landing.

**Scope.**
- **BUG-1330:** the description-number rule reads a status row's `damageBonus` and its `onEnd` magnitude as two claims,
  each tied to its own words ("20% more damage"; "Slowed 20%"), and a row number the description never states is a
  problem. Un-skip `D30LandingQaTests.Mutant_OneOfTheTwo20s_Moved_TheStatusTooltipIsNamed`.
- **BUG-1331 item 2:** in `Content/TeblorLanding`, the Frenzy ability's own description says what the status does (the
  40% faster attacks, the 20% more damage, Slowed 20% for 4 seconds afterwards), pinned to the numbers both ways; the
  Owner review table on the page gains a row for the new status's name and description. Quote the new text in your
  report (it is the owner's to approve at D31).
- **BUG-1331 item 3:** `TeblorLandingWriteTests` keeps each file's formatting and line endings: ten one-field edits
  show as ten changed lines, the docs keep LF.
- **V24 re-measured with the share on its own opening** (Request 52 is on `main`): `attack.reserveArmyShare` on the
  Teblor opening only, the opponent at `rules.json`'s 1; values 0.25, 0.5, 0.75; seeds 1-8, 64 matches a row, D30's
  columns, beside D30's global-share 0.5 row and the share-1 row. Stress category, a report with no bound.
- Files: `sim/Rts.Sim.Tests/Content/**`, `sim/Rts.Sim.Tests/QA/Content/**`, `docs/factions/teblor.md`.
  OUT: every `game/data/**` byte, `docs/02`, any other C#, the golden, the sim's pin classes. BUG-1331 item 1 is the
  view's (next session). BUG-1241 stays open until D31.

**Acceptance criteria.**
1. The un-skipped theory is green: moving `damageBonus` alone, `onEnd.magnitude` alone, or dropping `damageBonus` on
   the landing copy each names `teblor_frenzied description`; the unmoved copy has no problem; shipped data has none.
2. The rule change costs no existing row: Content + QA/Content all green on shipped data and on the landing copy
   (1,684 or more, the count reported).
3. The ability description on the landing copy states all three numbers and a mutant of each is named; the page's
   Owner review table carries the status row and `RuleRowClaimTests` / the page pins hold it both ways.
4. `D30_WRITE_LANDING=1` on a scratch clone (outside `.claude/worktrees`): `git diff --stat` shows only the lines the
   landing changes, no line-ending warning; applying it twice changes nothing more.
5. The share table (four rows + D30's two) is in the report with a one-line reading of the curve. Measure and report
   (rule 48); the Producer picks at D31.
6. `git diff 06277d6...HEAD --stat` holds only the three paths above (+ nothing under `studio/**`: bug updates come as
   a patch in the scratchpad).

**Design references.** `docs/factions/teblor.md` (Abilities, Status effects, AI, Owner review table); docs/02 "Status
effects"; docs/03 "For the data track (M7-4)" and "(M7-6)"; BUG-1330, BUG-1331; D30's tables in
`studio/sessions/2026-11-13-1118.md`.

**Constraints.** Rule 41: the sim builds an AI cast rule for Frenzy this session behind a knob no shipped file carries;
your matches run on `main`'s rules without it, and the report says so. D31 re-runs the one chosen row on the tree it
lands on. Player-facing text only in data (here: the landing copy and the page). Rule 52. Bug ids 1360+.

**QA focus (standard: the task's filters, the default loop, the Stress report rows this task adds; never the full
suite).** Mutants of the new rule beyond the three (a description that states the right number with the wrong words,
both numbers moved to the same new value, an `onEnd` status that is not a slow, a status with `damageBonus` and no
`onEnd`); does the stricter "every row number is stated" rule now flag any shipped Malazan / Whirlwind row (it must
not, or the finding is the rule's); the write helper on a file with CRLF and one with a trailing newline missing; the
0.5 row re-run by QA's own harness equal in every column.

### Where we are
D30 on `main` as tests and page prose; nothing shipped moved. Counter **4 / 4: the next session is the data's
hardening.** Debt: **BUG-1330 S3, BUG-1331 S4** (new), BUG-1241 S3 (open until the opening lands).

### Next task candidates
1. **D-H8, the data's clean-up before the landing; no shipped file.** Hardening, QA standard (scenario numbers).
   - **BUG-1330:** the description-number rule reads a status row's `damageBonus` and its `onEnd` magnitude as two claims,
     each tied to its own words ("20% more damage", "Slowed 20%"); QA's skipped theory
     `D30LandingQaTests.Mutant_OneOfTheTwo20s_Moved_TheStatusTooltipIsNamed` un-skipped and green.
   - **BUG-1331 item 2:** the Frenzy ability's own tooltip in `TeblorLanding` says what the status does (the +20% damage
     and the slow afterwards), pinned to the numbers; the Owner review table gains a row for the new status. **Item 3:**
     the write helper keeps each file's formatting and line endings (a ten-field edit shows ten changed lines).
     Item 1 is the view's (the buildings' drawn `scale`).
   - **Re-measure V24 with the share on its own opening** (`attack.reserveArmyShare` 0.5 on the Teblor opening only,
     the opponent at `rules.json`'s 1; Request 52 is on `main`): seeds 1-8, D30's columns, beside D30's global-share row.
     Also 0.75 and 0.25 for the shape of the curve. Measure and report (rule 48); the pick is the Producer's at D31.
   - Files: `sim/Rts.Sim.Tests/Content/**`, `QA/Content/**`, `docs/factions/teblor.md`. No `game/data/**`.
2. **D31: the one landing** (the session after). `common/statuses.json` (`teblor_frenzied`; under the sim's folder: the
   PLAN names the file in the data brief and keeps the sim off it), `factions/teblor/abilities.json`, `techs.json`,
   `buildings.json`, `ai.json` (the opening with its `reserveArmyShare`), docs/02 (a "Blood-oil Frenzy" Status effects
   row and the Frenzy Effect cell: name docs/02 in the brief, it is outside the data's usual files), the page and the pins
   both ways (the `Landing` effect moved into `Upgrade` in `TechContentTests`), the golden header once (rule 51: the two
   sim-owned hash pins named in the watch-outs, the Stress pin classes, the reason in `GatherWedgeQaTests`' comment). New
   player-facing text quoted for the owner. M7 criterion 3 is ticked at that ACCEPT.
3. The full balance pass (several seeds, both seat orders; inputs: the 0.75 m crowd figures, the aftermath-slow stacking
   note, Request 53's result).
4. The owner's answers if any land ("teblor ai: early raid", the Teblor table, the counters, `goldShare`, BUG-0780,
   `counterStrength`).
