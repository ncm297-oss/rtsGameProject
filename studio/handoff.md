# Handoff: brief for the next session

## Current session plan (2026-11-26-1847, session 3 / 8 of 2026-11-26; written at the PLAN)

Sim **M6-S14** GO (feature, QA full, the one pin move, the one data-hash move, one save-format bump in the AI's
parts only) · view **M6-VH10** GO (hardening, QA standard) · data **D39** GO (feature, QA light, no game data).
Bug ids: sim 1850-1869, view 1870-1879, data 1880-1889.

**Decisions at this PLAN (handoff "At the PLAN" items 5-6):**
- **The save-format riders leave the AI-play session.** BUG-1490 and Request 60 change no AI play and no pin, so
  they move to the sim's hardening (next session, with BUG-1730 / 1731: all four touch the draw part or the order
  queue), where the view re-checks its save scenes in the same session (rule 50). The re-scout count of BUG-1554 /
  1556 stays here: it is AI play, and it is saved in the AI's own parts.
- **"Press the attack while the draw clock runs" is not in this session** (new behaviour with no design; it goes
  to the sim's queue).
- **D39 is cut so no file is shared with the sim's hand-over (rule 68):** `QA/Content/D21OpeningsQaTests`,
  malazan.md's Age II paragraph and teblor.md move to the data task after this one (the re-measure).
- The producer's grep for rule 74 is in the scratchpad file named in the sim brief (a superset, 150 files).

### Track sim: M6-S14, cure the computer player's stalls (the AI-play session)

Goal: Every open defect that changes how a Normal computer player plays is cured in one session, so the pinned
test matches are re-measured once: a hunt that forgets its sweep (BUG-1661), a seat that never rebuilds its workers
(BUG-1660), a fog leak in the rally search (BUG-1555), the blind re-scout's two halves (BUG-1554 / 1556) and the
rally point's edge cases (BUG-1558). Read first: docs/03 "Implementation (M7-8)" (the Vathar trace and the cures),
"Implementation (M6-S12)" (the rally, rebalance and re-scout rules), "Implementation (M6-S13)" (how the last pin
move was reported), docs/02 "AI opponent", docs/01 rows 2026-11-17 (3)-(4) and 2026-11-24 (2), and the six bug
files in `studio/bugs/`.

Scope, in this commit order. Each lettered item is its own commit, and after A and after B you measure before
going on (rule 70): the Vathar Crossing four-seat seed 2 match (`Stress/VatharStallTraceTests`) and
`MatchCriterionTests`' rows, end tick before and after, written into docs/03.
- **A. BUG-1661, first and alone.** When a hunt turned into a wave on a building it found and that wave razed the
  building, the next hunt keeps the last hunt's visited marks and resumes nearest-first from where the building
  stood. The free starts come first only for a hunt with no such find within `ai.huntMemory`. `MilitaryManager`
  (`BeginHunt` / `NextSweep` / `NextHuntStart`). Derive the resume from saved state if you can; if it needs a new
  saved value it goes in the `HUNT` part (see "Save format" below).
- **B. BUG-1660.** The zero-worker retrain becomes a floor: new `game/data/common/rules.json` `ai.minWorkers`,
  shipped value **4** (Producer's value). A seat with a finished hall and fewer live + queued workers than the
  floor sends one worker Train a think when the bank and the population allow. Loader, validation (an integer
  from 1 to a `DataLimits` bound; 1 is today's behaviour and a test proves that on a copy), `DataValidationTests`,
  the content hash. **This is the session's one data-hash move:** regenerate the golden replay's header and
  checksum once, say why in the `GatherWedgeQaTests` comment, and the four golden-header rows must follow without
  an edit (`FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
  `QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`, `StillStealthLoaderTests.TheHash_*`,
  `Data/MaxAliveLoaderTests.TheHash_*`). No new test pins the hash or save bytes as a literal (rules 49 / 73).
- **C. BUG-1555.** `PlayerView.IsKnownClosed` answers from the seat's memory: a cell under a remembered ghost is
  closed until the seat sees the ground again, and a node reads through the per-player node memory, both as
  `CanPlace` does. `M6S12AiRulesQaTests.IsKnownClosed_FollowsTheFog_ButARazeOutOfSight_Leaks_Bug1555` flips to
  the cured answer and loses "Leaks" from its name. Correct docs/03 M6-S12 Q3 for the node case.
- **D. BUG-1554 / 1556** (the ruling in docs/01 2026-11-17 (3)). The seat counts the blind re-scouts it has lost.
  While that count is below `ai.scoutReplacements`, a blind seat with no idle worker may take a gatherer of the
  kind it knows (still one at a time, one per `ai.scoutAfter` window). At the count, blind re-scouting stops for
  good. The count is saved and hashed with the AI's state.
  `M6S12AiRulesQaTests.KnowsGold_NeverSawATree_ShortOfWood_NoIdleWorker_NeverRescouts_Bug1554` flips (it
  re-scouts), and `M6S12AiRulesQaStressTests.EveryScoutKilledAtOnce_TheBlindSeatsDrain_20Minutes_Report` gains
  asserts: blind re-scouts lost equals `ai.scoutReplacements`, and the seat still has workers and gold at 20
  minutes. `ReachRuleQaTests.EveryScoutKilledAtOnce_TheSeatDoesNotFeedItsWorkersToTheScoutRole` must stay green.
  If it cannot with its bound as written, do not change the rule: report the measured loss, and the bound may be
  raised by at most `ai.scoutReplacements` with a comment naming BUG-1556. Update docs/02's re-scout sentence.
- **E. BUG-1558, items 2 and 3 only.** The rally point is kept while it is still acceptable (no move because the
  nominal target flickered), and when no point is acceptable the last acceptable point is kept instead of the
  nominal one. Whatever must outlive a think is saved and hashed. Item 1 (6 m from the hall) is "measure and
  report" on `Stress/M6S12RallySurveyQaStressTests`' columns, before against after; no rule for it.
- **F. The pin move, once, after E.** Re-measure every pinned AI match, old value in a "was" comment, and docs/03's
  M5-5 table (rule 43). Un-skip `FarExpandProbeQaStressTests.VatharCrossingSeed2_FourSeats_FarFogOracleAndExtraOracles`
  if the match now ends and its oracles pass; if not, it stays skipped with its ids and the new trace. Correct
  `AiDeterminismTests`' stale comment (BUG-1700). Drop the `FourAiSeats_OnPaleHills_Seed2_KnownStall` twin of
  `HuntStressTests`' oracle row.
- **G. Docs.** docs/03 "Implementation (M6-S14)": each cure, the per-item measurements, the pin list (below), a
  "Questions for the Producer" part (rule 67). Write the Producer's answers to M8-4's Questions 1-4 (docs/01, the
  fourth 2026-11-26 row, (1): keep all four) into docs/03 "Implementation (M8-4)". docs/02 "AI opponent" for B, D
  and E. A bug patch for `studio/bugs/` in the scratchpad (you cannot write `studio/**`).

Save format: a new saved value goes at the end of an AI part (`AIST`, `HUNT`, `CTRM`), `SaveFormat.CurrentVersion`
goes up once for the whole task, and the header and every other part stay byte for byte (a differential row
against the base on test data proves it, `StillStealthSaveBytesTests` is the model). Keep the meaning of
`SaveFormat.HuntMaplessVersion` and `CurrentVersion` as the view's scenes read them. These view scenes must pass
on your branch un-edited (you may not edit `game/**`; run each alone): `SaveLoadTest`, `AiSeatTest`,
`TeamFilesTest`, `ReplayPlaybackTest`, `QaM6V14Test`, `QaM6V16Test`, `QaM6V17Test`, `QaM6V18Test`, `QaM6V18bTest`,
`QaM6V29Test`, `DrawViewTest`. A red one is reported by name in the first lines of your report.

Out of scope: BUG-1490, Request 60, BUG-1730, BUG-1731 (the sim's next session); "press the attack while the draw
clock runs"; Request 56 G5-G8; movement (BUG-1463); any byte under `game/data/factions/**` or `game/**` outside
`game/data/common/rules.json`; tuning a data value to make a row end; any other `ai` knob.

The pin list (rule 71; your report and docs/03 carry this list one class a line with "re-measured: yes / moved to
... / unchanged / cannot move: why"). Step zero: reconcile it with the Producer's grep in
`C:\Users\Nick\AppData\Local\Temp\claude\162c01fae57881b0\d373c799-79df-4407-8f0a-5b163940ef62\scratchpad\prod1847-ai-match-classes.txt`
(a superset of every test file that seats a computer player; a class that plays a whole match and is missing
below is added, rule 74).
- Default loop: `Ai/MatchCriterionTests` (and `HalfWay`)
- `StandardPresetPinQaTests`
- `QA/NonHardMatchPinQaTests`
- `CounterCompositionTests.TheNonHardPins_*` (eight)
- `ReserveTwinTests`
- `SharedVisionFfaPinTests`
- `OutcomePinM73bQaTests`
- `HardKnobsPinTests`
- `AiVersusAiTests`
- `Ai/OpeningReserveShareTests`
- `Draw/DrawRuleTests`, `Draw/DrawSaveReplayTests`, `Draw/DrawCostTests`, `QA/DrawRuleQaTests`
- `Cli/CliTests.Match_TheLiteralRow_*`
- the premap AI replay, `save_ai_seed2`, `seed21_playable.replay`, the golden (human seats: only its header moves)
- Stress: `AiDeterminismTests`
- Stress: `HuntStressTests`
- Stress: `M6Sh2DeterminismStressTests`
- Stress: `M6Sh2QaStressTests`
- Stress: `StarvedSaveTwoProcessQaStressTests`
- Stress: `ReservePairingsStressTests`
- Stress: `MatchRowsM6S10Report`
- Stress: `MatchCriterionSweepQaStressTests`
- Stress: `FarExpandProbeQaStressTests`
- Stress: `HardVersusNormalTallyQaStressTests`
- Stress: `TeamAiSoakStressTests`
- Stress: `DefeatAiStressTests`
- Stress: `MapSlice2StressTests`
- Stress: `DrawStalledRowsReportTests`
- Stress: `EndGameHardeningTests`
- Stress: `M78ReserveSwitchQaStressTests`, `M78ShippedDigestQaStressTests`, `ShippedDigestReportTests`,
  `ReserveSwitchReportTests`
- Handed over from the data track by name (rule 68; you may re-pin these classes and the page cells they read,
  rule 40, and nothing else in the data track's folders): `Content/AiOpeningPairsReportTests`,
  `Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
  `QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`,
  `QA/Content/AiHouseRuleReportTests`. Not re-pinned by you: `Content/D38ShippedOrdersReportTests` and
  `QA/Content/D38ShippedOrdersQaStressTests`: run them; if Teblor play moved, skip them with
  `Skip = "BUG-<id>: re-measured by the data track after M6-S14"` and put the bug in your patch. The data track's
  hour-long Teblor report classes are not run.

Acceptance criteria:
1. A: a scene test that fails on the base: a hunt finds a building, its wave razes it, a second enemy building
   stands within 60 m, and the next hunt's first sweep target is the unvisited point nearest the razed building,
   not a free start. A hunt with no find within `ai.huntMemory` still goes to the free starts first (a row that
   passes on the base and after).
2. A, measure and report: the Vathar four-seat seed 2 end tick before (88,998) and after A alone.
3. B: a seat with a finished hall, 1 worker and the gold trains up to `ai.minWorkers` live + queued, one Train a
   think (fails on the base). With `ai.minWorkers` 1 on a copy, a pinned two-seat match's state hash at its end
   tick equals the base's. A seat with no hall, no gold or no population room sends no Train and does not throw.
4. B: `rules.json` without `ai.minWorkers`, or with 0 or a value above the bound, fails to load with an error
   naming the field. The shipped value is 4.
5. C: the BUG-1555 row reads closed for a ghost razed out of sight and open again once the seat sees the ground;
   the same for a node felled out of sight.
6. D: the BUG-1554 row re-scouts; the 20-minute drain row loses exactly `ai.scoutReplacements` blind re-scouts and
   ends with workers alive and gold above 0. The count survives a save and load (a twin row: save mid-match, load,
   both runs' hashes equal to the end).
7. E: on the staged flicker (a target seen on one think and gone the next) the rally point does not change while
   it is acceptable; with none acceptable the point stays the last acceptable one. A save / load twin row.
8. Determinism: every new rule has a same-seed twin row and a save-mid-match twin row; `AiDeterminismTests`
   green; the golden replay's outcome and checkpoint hashes equal the base's (only the header's data hash moves).
9. No allocation in the AI's think for the new code (the existing allocation rows green; a new saved array is
   allocated at seat creation).
10. The pin list is in docs/03 "Implementation (M6-S14)" in rule 71's form, every class marked; every re-pinned
    value carries its old value.
11. Measure and report, not pass / fail (rule 48: this task may not tune data): the Hard v Normal tally per map
    (was 34 / 5 / 3), Normal v Normal seeds 1-8 on the three maps (all ended), the four-seat Pale Hills rows.
12. Rule 45: when a pinned criterion row stops ending, the row moves to the nearest seed that ends (say whether
    another row already plays that seed), a bug with the trace goes in the patch, and no bound is raised to make it
    pass. A `MatchCriterionTests` map with no ending seed in 1-8 is a finding in the first lines of the report.
13. The default loop (`Category!=Perf&Category!=Stress&Category!=Soak`) is green after the last edit, the handed
    classes included; the Stress category is green apart from rows skipped with a bug id; `dotnet build
    RtsGame.sln --no-incremental` 0 errors / 0 warnings; smoke PASS; the eleven view scenes above pass.
14. docs/02, docs/03 as in G. The report says in its first lines: saves and recordings from before stop loading
    (the data hash moved; the save format went up if it did).

Design references: docs/02 "AI opponent" (the hunt finishes the match; the AI keeps an economy; the re-scout
sentence); docs/03 "Implementation (M7-8)" (the cures and the trace), "(M6-S12)" items 1-6 and Q3-Q4, "(M5-5)"
(the table), "Save format"; `rules.json` `ai`: `huntMemory` 120, `scoutReplacements` 2, `scoutAfter` 15,
`rallyReach` 6, `rallyDistance` 12; new `minWorkers` 4.

Tests required: the scene rows of criteria 1, 3-7 (each failing first where it says so), the twin rows of 8, the
loader rows of 4, the differential save row; the re-measured pins.

Constraints: the AI reads the world only through its fog-filtered view and acts only through commands (rule 3);
no `Dictionary` / `HashSet` iteration that orders results, no LINQ or `new` in a think; no stat or threshold as a
C# literal (the floor is data); a test helper that forges an `ai` field tolerates the shipped data already
carrying it (rule 58); `AiScenes.SetBuildOrder` for any forged order; record the PID of every `dotnet test` /
Godot you start and end only those (rules 52 / 69); scratch folders start with `m6s14-` (rule 63); run the full
default loop after your last edit (rule 62). Size: the cures about 800 lines without re-pins and docs; if D or E
cannot be finished cleanly, land A-C and F whole and say so in the first lines, never a half-built rule.

QA focus (sim, full): (1) Determinism of each new rule: same-seed twins, save / load twins at an awkward tick
(mid-hunt between a razed find and the next hunt; the tick a floor Train is queued; the think after the last
allowed blind re-scout dies), two-process rows. (2) The floor: a seat at the population cap, a seat with a hall
under construction only, two halls, a worker killed every think for 20 minutes (does it drain the bank, and is
that bounded by anything but gold: report), Easy and Hard seats, a Teblor seat (pack and carry rules), a seat
whose draw clock runs (does a floor Train with gold left restart or block the draw: report against BUG-1731).
(3) The hunt resume: a razed find with nothing near it (does the hunt still cover the map), two finds in a row,
a find razed by an ally, four seats and 2v2; run the four-seat rows on all three maps seeds 1-8 and print which
end, against the base. (4) The fog: can the rally search or the re-scout learn anything the seat never saw
(BUG-1555's family: build, raze and fell out of sight in every order). (5) The re-scout cap against a camping
enemy for 20 minutes on each difficulty: workers lost, gold spent. (6) The pin list: check it against a grep of
your own for classes that play a whole AI match (rule 74) and run the whole Stress category on the branch;
then Perf alone (rule 42). (7) Rule 58: apply `ai.minWorkers` 1 and 8 on a scratch clone and run the default
loop's AI classes. Rehearse nothing the loader refuses (rule 80). After a tests-only fix round, no Stress
re-run (rule 72).

### Track view: M6-VH10, the view's hardening

Goal: The view track's fourth session since its last clean-up works its debt: a drawn two-sided match must read
Draw however it is reached, and five small defects and two docs items are closed. Read first: docs/03
"Implementation (M6-V29)" and "(M6-V30)", docs/01 the second and fourth 2026-11-26 rows, and the bug files
BUG-1790, 1792, 1710, 1831, 1830.

Scope:
- **1. BUG-1790, the two-sided half.** A drawn match with exactly two sides reads Draw for both sides whoever is
  out now: live, after loading a save taken after a later fall, and in playback (`Match.ResultFor`).
  `QaM6V29Test`'s `KNOWN BUG-1790` row becomes a passing row for two sides and stays a printed known row for
  three or more sides (Request 60, the sim's next session).
- **2.** Correct docs/03 "Implementation (M6-V29)" Question 1 ("a recording watched past it reads Defeat";
  measured: Draw).
- **3. BUG-1792.** The draw notice stays centred and on screen for a long text (grows both ways or wraps) at
  1152 x 648; correct the stale 640 px comment.
- **4. BUG-1710.** The lost-cast cue neither sounds nor shows in replay playback.
- **5. BUG-1831.** `tools/import-assets.ps1` fails (non-zero exit, no PASS line) when its Godot import exits
  non-zero or is killed; a test row proves it in seconds with a stand-in for the Godot exe, not a real hang.
- **6. BUG-1830.** The Stone Cairn's team colour reads for the Iron player: a larger team part or a wall tint,
  your choice, said in docs/03. Pictures of the Cairn in all eight colours go to a
  scratchpad folder `m6vh10-pictures` named in your report (you cannot write `studio/**`; the Producer copies
  them to `studio/review/2026-11-26-1847/`). Every other model file stays byte for byte.
- **7.** Write the Producer's answers to M6-V30's Questions 1-5 (docs/01, the fourth 2026-11-26 row, (2)) into
  docs/03 "Implementation (M6-V30)" (rule 67).

Yours: `game/**` except `game/data/**`, `tools/import-assets.ps1`, `tools/asset-manifest.json`, `tools/art/**`.
Out of scope: new models, the Whirlwind's tents, anything in `sim/**` (no ViewApi change is needed), `game/data/**`
(if item 3 or 4 needs a text, say so under Questions instead), Request 60's three-sided case, save or replay
formats. The sim track moves the data hash and the save format version this session: read
`SaveFormat.CurrentVersion`, never a literal, and add no stored save fixture.

Acceptance criteria:
1. A scene row: a two-sided match drawn, then one side's last building razed; `ResultFor` reads Draw for both
   sides live, after a save and load taken after the fall, and in playback of the recording. It fails on the base
   for the load case.
2. A three-sided drawn match still prints `KNOWN BUG-1790` and the scene stays green; `--strict` fails it.
3. The notice with a 120-character text is centred within 2 px and wholly inside 1152 x 648; the shipped text is
   drawn where it was (a pixel or position row).
4. In playback, a lost cast produces no cue sound and no cue visual; live it still does (both rows).
5. `tools/import-assets.ps1` with a stand-in that exits 1, and one that is killed, exits non-zero and prints no
   PASS line; with the real Godot it still passes and a re-run leaves `git status` clean.
6. The Cairn: on the picture, the Iron player's team part differs from the wall by at least 40 luminance levels
   (it is 113 against 135 today) or covers at least twice today's area; say which. All eight colours pictured.
   `git diff --stat` shows no other model binary changed.
7. Bench, only because item 6 changes a model (rules 61 / 77): 400 figures + 60 building models, 400 capsules and
   1,000 figures, run alone after the other tracks' suites have ended when you can; report against 6.43 / 0.82 /
   16.57 ms. Measure and report; over 16.7 ms at 400 is a finding.
8. The scene loop is green (130 scenes plus any you add; `BenchTest`, `QaM27Test`, `QaH1Test`, `QaV3Test` may be
   re-run alone, `QaV3Test` with `-TimeoutSec 1200`); the default loop
   (`Category!=Perf&Category!=Stress&Category!=Soak`) green; `dotnet build RtsGame.sln --no-incremental` 0 / 0;
   smoke PASS.
9. docs/03 items 2 and 7 written; BUG-1792's comment corrected; a bug patch in the scratchpad.

Design references: docs/02 "Game flow" (the result screen, Draw), docs/03 "Implementation (M6-V29)" (the notice,
`hud.draw_countdown`), "(M6-V30)" (team faces, `FitTo`), docs/04 (the import pipeline, team colour).
Tests required: scene rows for criteria 1-4, the script row for 5, the picture for 6.
Constraints: views hold no gameplay state (the result is read from the sim, not latched in a way a load loses);
player-facing text only from data; no GDScript; record the PID of every Godot / `dotnet test` you start and end
only those; scratch folders start with `m6vh10-`; after an import check `git status` and file hashes, not the
PASS line; the full default loop after your last edit.

QA focus (view, standard): the Draw result in every order of events (draw then fall, fall then draw is a
defeat and must stay one, save before and after each, playback, 2 sides and 3-4 sides, teams 2v2 counted as two
sides); the notice at 1152 x 648 and 1920 x 1080 with short, shipped and very long texts; the cue in playback at
speed x4 and after a seek to the end; the import script with a stand-in exe that exits 1, exits 0 with an ERROR
line, and is killed, then one real import with hashes before and after; the Cairn in eight colours at the
default zoom and zoomed out; the scene loop, the default loop, the bench last and alone.

### Track data: D39, the Shadow page after the unit limit, and BUG-1840's static checks

Goal: Keep the Shadow page ready for its roster: the Hounds' "at most 7" is now a rule the sim has (M8-4), so the
page lists it as built and a content check reads the number from the field. Close what is left of BUG-1840 in
one QA class. No game data changes. Read first: docs/03 "For the data track (M8-4)" and "Implementation (M8-4)",
docs/factions/shadow.md (Unit notes, Schema gaps G1-G8, the pin tables), D35's `Content/UnitHitBonusText` and its
tests (the model), BUG-1840.

Scope:
- **1. `docs/factions/shadow.md`:** G4 listed as built (`units.json` `maxAlive` 7 on the Hound; live plus queued
  count; training only, a summon is never refused; the computer player trains other types at the limit), every
  sentence that says "no field holds it yet" corrected, the Hound's row and its "at most 7" description sentence
  pin-ready. G5-G8 stay open.
- **2. A content check** in `sim/Rts.Sim.Tests/Content/` that reads the limit from the unit's `maxAlive` and
  fails when a description's "at most N" differs from it or the field is missing, on forged test data only (use
  `Economy/UnitLimitTests.s_capped` / `s_pack` or your own forge that strips a shipped `maxAlive` first, rule 58).
- **3. BUG-1840, in `QA/Content/AiContentQaTests` only:** the static checks (a-d, g) read every list a seat can
  play (`buildOrder` and each `buildOrders` entry).

Yours: `docs/factions/shadow.md`, `sim/Rts.Sim.Tests/Content/**` (new files), `sim/Rts.Sim.Tests/QA/Content/AiContentQaTests.cs`.
Out of scope (the sim track holds them this session, rule 68): `QA/Content/D21OpeningsQaTests`, `DR35ProsePinQaTests`,
`TeblorOpeningQaTests`, `D20PinQaTests`, `AiHouseRuleReportTests`, `Content/AiOpeningPairsReportTests`,
`HolyWarFirstWaveReportTests`, `AiSpendingReportTests`, the D38 classes, malazan.md, whirlwind.md, teblor.md; any
byte under `game/data/**`; any measurement of AI play (rule 41: the sim moves it this session); any C# outside
the two test folders.

Acceptance criteria:
1. shadow.md says G4 is built with the four facts above, and no sentence on the page says the cap has no field.
2. The content check passes on a forged Hound with `maxAlive` 7 and the page's sentence, and fails (shown by rows)
   with `maxAlive` 6, with no `maxAlive`, and with "at most 8" in the text.
3. A row proves the page's pinned Hound limit equals the forged field both ways (page to data, data to page), as
   D35's pins do.
4. `AiContentQaTests` (a-d, g) run over every list of every shipped opening; a forged opening whose `hard` entry
   breaks check (a) fails with the entry named. They pass on the shipped data.
5. `git diff --stat` against the base shows nothing under `game/data/**`, and only the files listed under Yours.
6. `dotnet test sim/Rts.Sim.Tests --filter "Category!=Perf&Category!=Stress&Category!=Soak"` green after your
   last edit; `dotnet build RtsGame.sln --no-incremental` 0 / 0. No test pins the data hash as a literal (the
   sim moves it this session).
7. A "Questions for the Producer (D39)" part on shadow.md if you have any (rule 67); a bug patch in the
   scratchpad. No owner review table is needed (no game data and no new player-facing text ships).

Design references: docs/factions/shadow.md (the Hound of Shadow, G4, the description sentences), docs/02
"Factions" (Shadow), docs/03 "For the data track (M8-4)".
Tests required: criteria 2-4's rows.
Constraints: no C# outside the content test folders; numbers on the page match the docs/03 section exactly;
scratch folders start with `d39-`; record and end only your own test processes.

QA focus (data, light; filter `Category!=Perf&Category!=Stress&Category!=Soak`, never the full suite): the
content check against a forged Hound with `spawnCount` above 1 and `maxAlive` 7 (the page's wording about packs);
the page against docs/03 "Implementation (M8-4)" fact by fact; `AiContentQaTests` on a forged opening with an
entry for one, two and (refused by the loader, so not rehearsed: rule 80) three difficulties; docs conformance;
the diff's file list.

---

Written by the Producer at the ACCEPT of session **2026-11-26-0717** (session 2 / 8 of 2026-11-26, started 07:17).
Sim **M8-4** ACCEPT (feature, 0 fix rounds, QA full PASS_WITH_ISSUES), view **M6-V30** ACCEPT (feature, 0, QA
standard PASS_WITH_ISSUES), data **D38** ACCEPT (feature, 0, QA standard PASS_WITH_ISSUES; **the data hash moved:
B7947DC602B1E11B**). **M5 8 / 9 (criterion 6 owner-only), M6 5 / 10, M7 3 / 4, M8 0 / 3.** Counters: **sim 3 / 4,
view 4 / 4 (hardening next), data 3 / 4**. Bug ids next session: **sim 1850-1869, view 1870-1879, data 1880-1889**
(1811-1829, 1832-1839, 1841-1849 unused). Open S1 / S2: none. The next session is **3 / 8 of 2026-11-26** (or
1 / 8 of the next day).

**Next session in one line (proposal):** sim **M6-S14, the AI-play session: BUG-1661 first and alone, then
BUG-1660, 1555, 1554 / 1556 with 1490 and Request 60, 1558** (feature, QA full; **the one pin move**; no data hash
move unless it adds an `ai` knob) · view **M6-VH10, the view's hardening: BUG-1790 (two sides), 1792, 1710, 1831,
1830** (hardening, QA standard) · data **D39, shadow.md after M8-4 and BUG-1840's leftovers** (feature, QA light;
no byte under `game/data/`, nothing measured that the sim's task moves).

**The default loop on `main` is green.** Expect **0 failed / 20 skipped** (10,809 passed / 0 failed / 20 skipped of 10,829 in 22 m 9 s on the Producer's scratch
clone `prod0717-accept` of sim 71f07d2 + view 8e80575 + data 33ee8ec). The skips are last session's 20. Scene loop
**130 of 130, first pass**. Stress ran as a category on the sim's branch (QA: default loop + Stress 11,616 / 0 / 29; Perf alone
215 / 0 / 3); QA's rehearsal loop on the sim head merged with D38 read 10,767 / 0 / 27. The data track's Stress
classes ran on its own branch (the rule 71 list is in the session log); the whole Stress
category has not run on the three-way tree (rule 74). Any red row is a finding.

**At the PLAN, first:** (1) the inbox (waiting, none blocking: D33's, D34's and D38's tables, "buildings ok",
"teblor buildings ok", "whirlwind colours ok", the draw notice, a tent pack; tweaks come before roadmap work);
(2) the default loop on `main`; (3) **the bug index:** one row each for BUG-1770, 1771, 1810, 1612, 1421 (all
"fixed") and BUG-1830, 1831, 1840 (open); **BUG-1840's file** (it came from the data branch): add "item 3, the
stale docs/03 sentence: fixed by the Producer at the 0717 ACCEPT" and leave it open for the static checks; (4)
`CLAUDE.md`'s "Current milestone" line is still stale (on "Waiting on you"); (5) **for the AI-play brief (rules 43 /
45 / 71 / 74):** the pin list one class a line, checked against a grep for every test class that plays a full AI
match, Stress classes included, plus the data track's rows handed by name (rule 68's seven content classes, and
now `Content/D38ShippedOrdersReportTests` and `QA/Content/D38ShippedOrdersQaStressTests`, which may be skipped
with a bug id when Teblor play moves: docs/01, the fourth 2026-11-26 row, (3)); (6) **rule 50, because Request 60
and BUG-1490 change the save format:** name the view scenes that pin save bytes, and decide whether the format
change stays in the AI-play session or moves to the sim's hardening (the session after), so that one session
carries one kind of move; the view's BUG-1790 fix this session is the two-sided half and needs nothing from the
sim; (7) the golden-header rows are now **four**: `FactionOrderTests.TheShippedContentHash_IsTheGoldenHeaders`,
`QA/PackQaTests.TheShippedDataHash_IsTheGoldenHeaders`, `StillStealthLoaderTests.TheHash_*`,
`Data/MaxAliveLoaderTests.TheHash_*` (all read the header, none holds a literal).

## Proposed briefs (for the next PLAN to complete)

### Sim: M6-S14, the AI-play session (feature, QA full, one pin move)
Every cure that changes how a Normal computer player plays, in one session, each measured apart (rule 70):
**BUG-1661 first and alone** (commit and measure before the next), then BUG-1660 (`ai.minWorkers` from 4), BUG-1555,
BUG-1554 / 1556 (with BUG-1490 and Request 60 if the PLAN keeps the save-format change here), BUG-1558; un-skip
the Vathar four-seat row (BUG-1621 / 1610) with the cure; BUG-1700's comment when `AiDeterminismTests` is
re-measured; candidate: a seat with an army presses its attack while the draw clock runs. Every pinned match is
re-measured once (the list under "Pinned matches" below, rule 71's form in the report); a row that stops ending
is a finding (rules 43 / 45). The data track ships no opening beside it; the content classes of rule 68 and the
two D38 Stress rows are handed by name. Write the Producer's answers to M8-4's Questions 1-4 (docs/01, the fourth
2026-11-26 row, (1): keep all four) into docs/03 "Implementation (M8-4)" (rule 67). Out: Request 56 G5-G8; any
byte under `game/data/factions/**`.

### View: M6-VH10, the view's hardening (hardening, QA standard)
(1) **BUG-1790, the two-sided half:** a drawn match with exactly two sides reads Draw for both, whoever is out
now, live, after a load and in playback (`Match.ResultFor`; `QaM6V29Test`'s `KNOWN BUG-1790` row turns into a
passing row for two sides and stays "known" for three or more: Request 60). (2) The wrong sentence in docs/03
"Implementation (M6-V29)" Question 1 ("a recording watched past it reads Defeat"; measured: Draw). (3) BUG-1792
(the notice grows both ways or wraps; the stale 640 px comment). (4) BUG-1710 (the lost-cast cue in playback).
(5) **BUG-1831:** `tools/import-assets.ps1` fails when its Godot import exits non-zero or is killed (a row that
proves it without a 50-minute hang). (6) **BUG-1830:** the Stone Cairn's Iron team colour reads (a wall tint or a
larger team part; the picture in all eight colours; every other model byte for byte). (7) The Producer's answers
to M6-V30's Questions 1-5 (docs/01, the fourth 2026-11-26 row, (2)) written into docs/03 "Implementation
(M6-V30)" (rule 67). Bench alone only if (6) changes a model. Out: new models, the Whirlwind's tents (the task
after), sim code.

### Data: D39, shadow.md after M8-4 and BUG-1840's leftovers (feature, QA light; no game data)
(1) `docs/factions/shadow.md`: G4 listed as built (docs/03 "For the data track (M8-4)": `maxAlive` 7 on the
Hound; alive plus queued; training only; the computer player trains other types at the limit), the Hound's row
and its "at most 7" description sentence pin-ready, a content check that reads the number from the field (as
D35's `Content/UnitHitBonusText` did for M8-3), on forged test data only (no Shadow file ships). (2) BUG-1840's
leftovers: QA's static checks in `QA/Content/AiContentQaTests` (a-d, g) and `QA/Content/D21OpeningsQaTests` read
every list a seat can play (`buildOrder` and each `buildOrders` entry). (3) malazan.md's "Age II against the
Whirlwind" paragraph. Nothing here measures AI play (the sim moves it this session: rule 41). Say the filter in
the brief: `Category!=Perf&Category!=Stress&Category!=Soak`. Out: `game/data/**`, the three Teblor orders (their
re-measure is the task after the sim's session).

## Where things stand

- **`main` after this session = sim 71f07d2 + view 8e80575 + data 33ee8ec** (plus this ACCEPT's studio and docs
  files). Scratch clone `prod0717-accept` (code merged clean; `studio/bugs/README.md` and `studio/qa/coverage.md`
  conflicted, both sides kept): build `--no-incremental` 0 / 0, default loop **10,809 passed / 0 failed / 20 skipped of 10,829 in 22 m 9 s**, smoke **PASS**,
  scene loop **130 of 130, first pass**.
- **The data hash moved once: 3D93D45345A3B4EA -> B7947DC602B1E11B (D38).** Saves and recordings from before are
  refused. `game/data/common/ui.json` gained `train.limit_reached` (view data, not in the hash).
- **The Teblor `standard` opening (D38):** `buildOrder` = M1RS (Normal's), `buildOrders` = { easy: K9KC10S, hard:
  QRD1 }, 29 steps each, step for step the three D37 fixtures (kept;
  `Content/D38ShippedOrdersTests.TheShippedOpening_IsTheThreeD37Fixtures_StepForStep`). `throughReserve` on
  Normal's steps 21 (Rider) and 27 (Shaman), Easy's 22-24 (Shaman, Rider, War-dog), Hard's 21-22 (Rider, War-dog).
  Measured on the shipped file, 64 seats a difficulty: Easy 1 / 0 / 38 / 28 / 34 / 42 / 22, Normal 9 / 23 / 23 /
  18 / 30 / 40 / 18, Hard 11 / 23 / 23 / 40 / 42 / 47 / 25 (defeated / 2nd Longhouse / Shaman / Breaker / Rider /
  War-dog / Anointing); every figure of the bar met. `Content/D38ShippedOrdersReportTests` and QA's
  `QA/Content/D38ShippedOrdersQaStressTests` (Stress) fail on any difference from those rows. teblor.md has a step
  table per difficulty, each pinned both ways (`AiContentTests`, QA's `D38QaTests`). "Houses at the cap" on M1RS:
  the cap at 1007 s, 10 Huts, steps 15 and 16 skipped.
- **A test never writes `["buildOrder"] =` in the sim's folders** (`Ai/BuildOrderHelperSourceTests`): it calls
  `AiScenes.SetBuildOrder(opening, steps)`, which removes `buildOrders`; a seat's list is `AiPlayer.Steps`.
  `Ai/TeblorSwitchOrders` and `Ai/PerDifficultyOrders` are built on `Content/Fixtures/teblor_standard_d34.json`,
  so their figures do not follow the shipped file.
- **The unit limit (M8-4, test data only):** `units.json` optional `maxAlive` (1 to `DataLimits.MaxUnitMaxAlive`
  1,000, not below `spawnCount`; hash marker 4 + value only when set); `TrainError.LimitReached` (appended last;
  checked after `LockedByRequirement`, before `QueueFull` / `CannotAfford`); the kept count in
  `Economy/PlayerLedger.Limits.cs` (live + queued x `spawnCount`; rebuilt by `RecountDraw`; never saved or
  hashed); `World.UnitLimit(player, type, out count, out limit)` and `PlayerView.UnitLimit`; the AI's
  `AiPlayer.AtUnitLimit` (production skips the type, a `train` step at the limit is done unsent). Test data:
  `Economy/UnitLimitTests.s_capped` / `s_pack`. A summon counts and is never refused. Known: the draw rule's "can
  still train" does not read the limit (with BUG-1731 at the sim's hardening).
- **Building models (M6-V30):** 14 of 30 types (six Malazan, eight Teblor: Longhouse, Hut, Woodcutters' Lodge, War
  Lodge, Javelin Ground, Stone Cairn, Bloodwood Carver, Shaman's Circle). Boxes: Stables & Kennels, Breaker's Hall,
  four Malazan, every Whirlwind building, every site. `BuildingModels.FitTo` multiplies the fitted height by the
  data `scale` (1.25 for the Teblor; scale 1 is bit-identical). Manifest key `teamFaces` moves a source's roof
  faces to the team tile. `ModelShots --bench-building-types <id,id>`. **130 scenes** (`TeblorBuildingsTest`, QA's
  `QaM6V30Test`). Bench (the Producer, merged tree): 400 figures + 60 building models 6.43 ms a frame, the same with boxes 6.40 (the eight Teblor types alone 6.41 against 6.41), 400 capsules 0.82, 1,000 figures 16.57, measured with no test run beside it.
- **Requests:** 55, 58, 59 done; **56: G1-G4 built, G5-G8 open**; 60 open (which seats stood at the draw).
- **Stalled rows, pinned matches:** unchanged from 2026-11-25-1437 (see the "Pinned matches" bullet under
  Watch-outs); no pin moved this session.
- **Queues after the proposals above.** Sim: its hardening at 4 / 4 (BUG-1730, BUG-1731's decision with the
  limit's "can still train"), then Request 56 G5-G8. View: the Whirlwind's buildings as kit-bashed tents (or the
  owner's pack), the Cairn as stacked rocks, the mounted units and machines once packs are on disk. Data: the
  three Teblor orders re-measured after the AI-play session (and the hour-long report classes D38 did not run:
  the order-search rows of `D34TeblorStepOrderReportTests`, `D36TeblorOrderSearchReportTests`,
  `D37TeblorNormalOrderReportTests`, `DH10TeblorSwitchReportTests`, `QA/D36TeblorOrderQaStressTests`, and
  `D33BalanceReportTests`, `TeblorLandingOpeningReportTests`, `TeblorOpeningHorizonReportTests`,
  `TeblorOpeningVariantsReportTests`, `TeblorReserveShareReportTests`, `QA/D34OrderQaStressTests`,
  `QA/D37TeblorNormalOrderQaStressTests`, `QA/DH10TeblorOrderQaStressTests`: each forges its own order, so D38
  could not move them), then the Shadow roster.
- **Session length:** about 10 hours (07:17 to about 17:30). The sim's QA full ran the default loop with Stress in
  5 h 2 m under up to five other test runs, and three D38 rehearsal loops of 3.8 to 4.1 h each; the view's and
  data's default loops took 2.5 to 3 h under that load (22 to 28 m alone). The conductor refreshed the session
  lock three times (rule 81).
- **Scratch folders left** (harmless; the owner's housekeeping list): in this session's scratchpad
  `prod0717-plan`, `prod0717-d38`, `prod0717-accept`, `m84-*`, `m6v30*`, `d38*`. The stray empty
  `C:\Dev\rtsGameProject\ModelShots.cs` and a file named with a yellow-circle emoji are still untracked in the
  owner's main checkout.
- **Usage at the ACCEPT:** weekly 30 % / 30 % (stop at 95 %); the 5-hour window was not reported.

## Standing rules (1-81)


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
**68 (new, 2026-11-15-0506):** a brief never writes "the default loop green" beside rows it forbids the developer to
fix. When a task re-prints cells that another track's prose pins read, the brief hands those pins and sentences over
by name, or writes the criterion as "red only in these named rows, ruled at ACCEPT". At PLAN the rehearsal's red list
is sorted into "re-pin", "re-stage" and "another track's" before the criteria are written (M6-S10: criterion 14
against 10 and 12; five default-loop rows reached `main` red by ruling). A fix round with nothing to act on but a
Producer ruling may be skipped by the conductor; the session log says so.
**69 (new, 2026-11-15-1536):** a developer or QA agent ends only the processes it started itself, by the process ids it
recorded when it started them; never by matching command lines or names (M6-S11's developer force-stopped 34 processes
whose command line matched "Total tests" / "Passed!|Failed!", some possibly another track's; nothing was lost, by luck).
A brief that runs long suites says "record the PID of every `dotnet test` / Godot you start".
**70 (new, 2026-11-15-2136):** when a QA FAIL leaves nothing a developer can act on without a Producer ruling, the
conductor calls the Producer, who answers with rulings and a fix brief instead of a verdict; the last fix round applies
them (tests and docs only when possible, so no Stress re-run is needed). `main` takes no red default-loop row by ruling:
a row the ruling cannot make green is skipped with its bug id in the repo's `Skip = "BUG-..."` form, its cure first in
the next brief. An owner bug that changes gather or AI behaviour moves every AI pin through divergence (BUG-1520 did,
alone): plan it only where a pin move is planned, and tell the developer to measure its effect apart from the rest.

**68 (amended 2026-11-16-2036, BUG-1552):** the content classes handed to a sim developer whose task moves Normal AI
play while the data track is stopped are seven: `Content/AiOpeningPairsReportTests`,
`Content/HolyWarFirstWaveReportTests`, `Content/AiSpendingReportTests`, `QA/Content/DR35ProsePinQaTests`,
`QA/Content/TeblorOpeningQaTests`, `QA/Content/D21OpeningsQaTests`, `QA/Content/D20PinQaTests`, with the pages' cells
they read (Age II tables, "Houses at the cap", "Spending on the menu's map", the holy war's first wave, the Teblor
opponent sentence).
**71 (new, 2026-11-16-2036):** a pin-move brief lists the pin classes one a line, and the developer's report carries
the same list with "re-measured: yes / moved to ... / unchanged" beside each, Stress pin classes included (M6-S12:
the four-seat FFA pin was on the list and never run: BUG-1559, S2, one fix round). Such a task gets 20 bug ids
(M6-S12 used all ten). When rule 45 moves a row onto a seed another row already plays, the report says so.
**72 (new, 2026-11-17-0816):** after a fix round that changed tests or docs only, QA's re-check runs the touched
classes, the default loop and the `--no-incremental` build; it does not re-run Stress or Perf (M8-2's re-check spent
4 h 46 m on a full loop for three test files). Rule 70's "no Stress re-run" holds for every tests-only round.
**73 (new, 2026-11-17-0816):** no new test pins the shipped data hash, or the bytes / digest of a save made on
shipped data, as a literal (rule 49 widened: BUG-1580, S2, one fix round). The hash is read from the golden header;
"byte for byte what `main` writes" is proven in-session against the base and kept as a differential row
(`StillStealthSaveBytesTests` is the model). A brief never states the shipped hash as a pass / fail literal in a
session where another track moves it.


**74 (new, 2026-11-18-0926):** "the Stress category has no expected red row" is said only after the Stress
category ran on the merged tree. `FarExpandProbeQaStressTests` was not on rule 71's list and went red with D33
unseen for a session (BUG-1621). Rule 71's list gains `Stress: FarExpandProbeQaStressTests`, and at PLAN the list
is checked against a grep for every test class that plays a full AI match.
**75 (new):** a bar in a data brief ("ship only if ...") names every figure the bug is about on every
difficulty it is about. D34's bar guarded Easy's upgrade and Normal's / Hard's unit kinds, so the order that met it
cost Easy its Riders (36 -> 4) with the bar green. And when a developer reports "none meets the bar", QA tries
an order of its own before agreeing (it found one: BUG-1641).
**76 (new):** a pin-move list is cut to what can move. A change to one faction's `ai.json` lists the classes that
seat that faction plus the hash pins and the golden's header; the other classes are named as "cannot move: no
seat of this faction" and are not run (D34 ran nineteen classes that seat only factions 0 and 1).


**77 (new, 2026-11-24-1431):** an art or view brief's frame-time criterion is judged on a quiet window. When the
developer's and QA's bench both ran beside another track's test process (M6-VH9: capsules x1.45-2.0 slower than
the last session's, so every figure row was "within the window's factor"), the Producer runs the pinned rows alone
on the merged tree at the ACCEPT and the log carries that figure; a brief tells the view's QA to run the bench
after the other tracks' suites have ended when it can. And a criterion of the form "only these files change" is
checked at PLAN against every other criterion that asks for a new mark in a file (M6-VH9's 11 against 6: rule 66).


**78 (new, 2026-11-25-0924):** a brief that asks for a search ("find the order that meets the bar") names the levers to vary
(which steps carry a switch, each step's count, time and position) and the developer's report says which levers were not
varied; "none meets the bar" is read as "none of these" until then (D-H10: all eleven candidates kept the Shaman step's
count of 2; QA's one-Shaman orders halved the shortfall: BUG-1721). And a ruling about how existing code behaves is
checked against that code at PLAN when the brief forbids changing it (M7-H3 item 10: the `null` switches).

**79 (new, 2026-11-26-0207):** a ruling of the form "the same answer live, after a load and in playback" is checked
at PLAN against what a save actually keeps (M6-V29: the view latches its result live, a save keeps no order of "draw"
and "defeat", and the brief forbade the sim change that would: BUG-1790; rule 66's family). And rule 58 widened: a
helper that *replaces* a data field also removes every field that can override it (`buildOrder` and `buildOrders`:
BUG-1771), and sim QA's rehearsal of "what the data track will ship" uses the data track's real fixture when one
exists, whole (D37's Normal fixture carries `throughReserve`; the M7-9 rehearsal kept the shipped `buildOrder`).

**80 (new, 2026-11-26-0717):** a brief's rehearsal or "second shape" is loaded on a scratch clone at PLAN before
it is written into the QA focus (the sim's QA focus asked for a Teblor `standard` with entries for all three
difficulties, which the loader's ruling (a) refuses; QA chose a shape that loads, `buildOrder` = Easy's with
`normal` and `hard` entries: rule 78's family). And whatever a criterion asks for "in the report" (a rule 71
class list, the owner's review table) is also written where QA can read it: the page or docs section the task
owns, or a scratchpad file the brief names (D38's criteria 7 and 9 were "partly verified" and "unverifiable" by
QA because they lived only in the developer's returned text: rule 67's family).
**81 (new, 2026-11-26-0717):** the conductor refreshes `studio/.session.lock` at every phase change and at least
every two hours while a suite runs, and says so in its notes to the Producer (this session ran about ten hours
against the lock's three-hour window; three refreshes kept the hourly fallback from starting a second session).
A session that could not refresh its lock is logged as an incident.


## Watch-outs (all tracks)
- **New this session (0717): the Teblor `standard` ships `buildOrders` and `throughReserve`.** Its `buildOrder` is
  the Normal seat's list only; Easy and Hard play their entries. A test or report that means "what a seat plays"
  reads `AiPlayer.Steps` or `AiOpening.BuildOrderFor(difficulty)`; one that replaces a list calls
  `AiScenes.SetBuildOrder` (sim folders: enforced by `Ai/BuildOrderHelperSourceTests`) or removes `buildOrders`
  itself (content folders: BUG-1840 for what is left). A shipped opening with an entry for all three difficulties
  does not load (ruling (a)). The golden-header rows are four (`MaxAliveLoaderTests.TheHash_*` is new): a data
  brief that moves the hash regenerates the header and all four follow without an edit.
- **New this session: a unit type may carry `maxAlive`.** No shipped unit does. A test that forges it uses
  `Economy/UnitLimitTests.s_capped` / `s_pack` (they strip any shipped `maxAlive` first: rule 58). A test that
  writes store arrays directly calls `World.RecountDraw()` (it rebuilds the limit counts too). `TrainError` has a
  new last value, and `ui.json` `train` must hold a text for every value or the game does not boot.
- **New this session: eight Teblor building types are models.** A scene that reads a Teblor building's box sets
  `BuildingViews.UseModels = false` or uses a type that is still a box (Stables & Kennels, Breaker's Hall:
  `ViewHardenVh9Test` P2 uses the Stables). A model's drawn height is the fitted height x the data `scale`. The
  scene loop is **130 scenes**; under load `BenchTest`, `QaM27Test`, `QaH1Test` and `QaV3Test` fail on time and
  pass alone (`QaV3Test` took 899 s under load: run it with `-TimeoutSec 1200`).
- **`tools/import-assets.ps1`'s PASS line is not proof until BUG-1831 is fixed:** after an import check `git
  status` and the file hashes. A Godot `--import` hung 53 minutes under load in QA's run; alone it took 21 s.
- **New this session (0207): an opening may carry `buildOrders`** (no shipped opening does until D38). A test
  that sets an opening's `buildOrder` on a JSON copy must remove `buildOrders` (BUG-1771); a seat's list is
  `AiPlayer.Steps`, never `Opening.BuildOrder`. A scene that plays to a draw reads the notice through
  `SimRunner.Ticked` (`DrawViewTest` is the model); `QaM6V29Test` prints `KNOWN BUG-1790` on purpose.
- **New this session: a two-process row's limit is processor time.** `MapFileTests.RunChild(..., seconds)` ends its child when
  the child and its test host have used `seconds` of processor time (wall ceiling 3x). A child on several cores can
  use more than one second a second: read "used more than N s of processor time" on a small limit as that before
  calling it a hang. A queued-order notice is view-only: `World.DroppedOrders` is emptied every tick, so a scene
  must read it through `SimRunner.Ticked`, never once a frame.
- **New this session: a click is tested against what is drawn.** `SelectionController.BuildingOnRay` /
  `GhostOnRay` replace the box-only picks in `EnemyAt`, `ContextTarget`, `PickResource`, `PickBuilding`; a unit
  wholly behind a model is not click-picked (`HiddenByBuilding`). A scene that clicks beside a Keep or Barracks
  model must pick a pixel on open ground or select the unit directly (`M3PlayableTest`'s seed-6 builder,
  `ReplayPlaybackTest`). Older scenes that pinned the 3 m box were adapted: `BuildingScaleViewTest`, `QaM7V4Test`,
  `QaV3Test`.
- **New this session: `ai.json` steps may carry `throughReserve` / `noReserve`** (no shipped step does). A test
  that forges the Teblor `standard` (`Ai/TeblorSwitchOrders`, `Stress/ReserveSwitchReportTests`, QA's
  `M78ReserveSwitchQaStressTests`) reads the shipped file: when the data track ships switches there, those
  helpers must tolerate them (rule 58). `noReserve` on an expand step = never expands (BUG-1663, by ruling).
- **New this session: `World.DroppedOrders`** is output like `Deaths`: emptied each tick, not hashed, not saved;
  an entry's unit may be dead when read. `SaveRestoreQaTests` lists `_droppedOrders` as not saved.
- **Session length:** 1431 ran about 10 hours with no pin move (sim QA full: default loop 2 h 56 m, Stress
  3 h 35 m under load; the data's QA first ran the wrong filter and lost 2 h: say the filter in a light brief).
- **New this session: a unit type may carry `attack.fromStealthBonus` and a `nearbyOwn` passive.** No shipped
  unit does. A test that forges them uses `Vision/HitBonusScenes` (rule 58). With either anywhere in the data,
  `World.HitBonusInData` is true and every hit by a field type reads `HitFactor.Of` (one status scan; for a pack
  type one grid query into `World.ShoveNeighbors`, so never call it inside movement's phases 8-9).
  `PassiveLoaderTests` pins four triggers; `DataContentHashTests` pins `UnitPassive`'s nine properties.
- **New this session: two building types are models.** A scene that reads a finished Garrison Keep's or
  Barracks' box mesh, material or bar height sets `BuildingViews.UseModels = false` before binding or reads
  `ModelOf` / `BarY` (`BuildingScaleViewTest` and `QaM7V4Test` are the models). Picking follows the drawn shape since M6-VH9
  (BUG-1630). A building's model is found by its id under `game/assets/models/<faction>/<id>/`; the data has no
  `model` field for buildings. `QaM6V26Test` passes with its known-bug rows printed; `--strict` makes them fail.
- **New this session: the Teblor `standard` has the Carver and the Anointing at steps 19 / 20 (10:00).** In
  `ai.json`, research and build steps ignore the reserve while train steps obey it, and a step behind a waiting
  expand step spends only above the reserve (Request 55): **file order is behaviour**, and it changes with the
  sim's next task.
- **The data hash moved (A732F387C2BB7E4D, D34):** every save and recording fixture from before is refused. No
  hash move is planned next session (the sim's AI follow-up moves pins, not the hash, unless it adds an `ai`
  knob to `game/data/common/**`: then it is the session's one hash move and the data track ships no data).
- **Session length:** 0926 ran about 21 hours. The sim developer's last full loop took 6 h 34 m beside the other
  tracks; the view's and data's QA were done hours before the sim's. Perf was evidence this time (214 / 0 / 3
  alone).
- **New this session: a unit type may carry `stillStealth`.** No shipped unit does. A test that forges it uses
  `Vision/StillScenes` (rule 58). With the field anywhere in the data, every world allocates `UnitStore.Still`, the
  save gains a `STIL` part and a held unit's Stealthed entry shows `TicksRemaining` = timed part +
  `StatusSystem.Holders`: a test that reads a Stealthed entry's ticks on forest or stand-still data must subtract the
  holders.
- **New this session: the AI's openings changed (D33).** `cadre_rush` waves at 16 pop, `holy_war` at 14, the Teblor
  `standard` has 29 steps and trains a Breaker, a Rider and a War-dog pack before its expand step. In `ai.json` a
  step after a waiting expand step spends only above the expand's reserve (Request 55): **file order is behaviour**.
  The FFA criterion row is Vathar seed 1; the retrain twin is Raraku seed 5.
- **`ModelViewTest` pins the capsule types as an exact set of six** (the criterion's own list). A new unit type
  without a model (the Shadow roster, a test faction on shipped data) turns it red: the task that lands one hands the
  view that list (rules 54 / 55 rehearsal).
- **Session length:** 0816 ran about 26 hours (three default loops of 1-2.7 h, two Stress runs of 2.6 h and 4.8 h,
  the data's pin runs; QA started per track as builds finished). Rule 72 removes the second Stress run. Perf was
  evidence only in the first inspection (the re-check hit the PC's slow state; base failed the same rows).
- **Smoke can fail once on stale import state** (`Can't find file 'uid://...'` / `Unrecognized UID` lines in a
  worktree's `.godot` cache after another branch's assets were merged): the re-run passes. Two developers saw it this
  session; it is not a finding unless the second run fails.
- **Pinned matches after D33** are listed under "Where things stand"; the older "Pinned matches on `main` after
  M6-S12" bullet below is right except for those rows.
- **The default loop is green on `main`**; the Stress category has no expected red row since 1431 (the BUG-1621 row is skipped with its ids).
  `main` builds **0 errors / 0 warnings**.
- **New this session: the AI's rally point may differ from `hall + rallyDistance`.** A test that stages an AI seat
  and expects its army at the nominal point must keep one-cell lanes (between its own buildings too) more than
  `ai.rallyReach` away, or read `Board.Rally`. A rebalance now goes to the node nearest the worker's nearest finished
  drop-off; a blind seat (phase 2, short of a resource, no live node known, an idle worker) sends a scout on the
  first think of an `ai.scoutAfter` window: a staged seat that must sit still needs a known node of both kinds or no
  idle worker.
- **A construction site is not a drop-off** (M6-SH2). A test or scene that places a drop-off building and expects
  deliveries there must finish it first; a loaded worker with only a site standing waits in state `Returning`. The AI
  retrains a worker when it has none: a test that keeps a seat worker-less must also keep it without the gold
  (`HuntTests.TheRingLeg_*` is the model).
- **Unit models are one surface now.** A scene that pinned a surface count or a team surface index reads
  `UnitModels.AtlasOf` / the one `team` material (`ModelViewTest`, `TeblorModelTest`, `QaM7V5Test`,
  `OverlayLineTest` were updated). A new model type goes through `tools/art/build_unit.py` `build_atlas`; the role
  PNGs no longer exist. Team texels are lit Burley (1-4 levels brighter than before M6-V24): a pixel pin from
  before is stale.
- **Session length:** 2036 ran about 11 hours (Stress 4 h 17 m beside the default loop; the view's default loop
  2 h 43 m under that load). The Perf category was not evidence (the PC's slow state: `ASweepRetarget` 78 us).
- **Session length:** 2136 ran about 22 hours (the Stress category 3.8 h and 4.0 h beside the default loop; the
  default loop 43 m to 2.5 h under load). M6-S12 is QA full with a pin move again: see "At the PLAN" item 4.
- **New this session: a gather loop ends for a queue** (`EconomySystem.EndsForQueue`, only when `QueueCount > 0`). A
  test that parks a worker on a loop with a queued order must put it in reach of its node with a drop-off standing
  (`ViewApi/OrderPathTests.QueuesBehind_*` is the model), or the queue starts at once. The AI never queues.
- **`EndGameHardeningTests.PaleHillsSeed2_FourSeats_*_TwoProcesses_*` has a 1,200 s wall limit** and failed under a
  contended Stress run (3 m 11 s alone): a Stress red there counts only when it fails alone.
- **Scratch clones left behind (permission prompt denied):** `qa1536\m6vh8-qa-clone` and `m6s11-qa-clone` in session
  e9f70028's scratchpad; harmless, on the owner's housekeeping list.
- **`gatherOverlap` 0.5 is in the shipped `rules.json`.** A test that means "the rule off" removes the field from a
  copy (`GatherOverlapScenes` writes or removes it itself; rule 58); "the base's data" in a fixture means the field
  taken out (`D32PreFixtureTests`). Any value above 0 turns the gather floor on.
- **Session length:** 0506 ran about 9.5 hours (the sim's QA full: default loop 1 h 42 m and Stress 2 h 3 m beside
  the other tracks). Keep the data task light next session and tell the view's QA to run timing rows alone.
- **The view's scene loop is 122 scenes** (`ViewHardenVh9Test`, QA's `QaM6VH9Test` and `QaM6VH9FixTest` are new). `BenchTest` (7 s),
  `MinimapTest` (0.5 ms), `DebugOverlayTest`, `QaM25Test`, `QaM27Test` and `QaH1Test` flake under load and pass alone.
- **`ViewApi` source may not name `World` or `UnitStore`** (`TerrainMeshBuilderQaTests.ViewApiSource_HasNoSimMutatorsOrTickCalls`):
  `OrderPath` is built from the store's arrays, handed in by the view (`OrderPathView.NewReader`).
- **Fifteen unit types are models** (the eleven before plus Cadre Mage, Sapper, Priest, Zealot); six are capsules
  (`ModelViewTest` prints "15 models, 6 capsules" and names them: Wickan Lancer, Horse Raider, Catapult, Battering
  Ram, Destrier Rider, War-dog). A count of model types is read from `tools/asset-manifest.json`, never a literal. A
  model file holds the 1.84 m base figure; the data `scale` is applied by the view. Bars and marks sit over the figure
  as drawn (`UnitViews.OverlayLines`); robes, veils and satchels are rigid boxes on a bone (a leg shows through).
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
- **The scene loop is all green: 111 scenes** (`OverlayLineTest` and QA's `QaM6VH8Test` are new; `QaM6V21Shots` and `ModelShots` are windowed tools, not in the loop). `QaM6V20Test`
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

- **Pinned matches on `main` after M6-S12 (every one re-measured once, old values in "was" comments; docs/03
  "Implementation (M6-S12)" item 6 lists old -> new):** the Hard v Normal tally **34 / 7 / 1** (Raraku 13 / 1 / 0,
  Vathar 12 / 2 / 0, Pale Hills 9 / 4 / 1); `MatchCriterionTests` (Vathar **seed 1** 23,244 / Raraku seed 3 27,394 /
  Pale Hills seed 1 33,230 / generated **seed 1** 21,876 / FFA 38,690 / 2v2 61,686; `HalfWay` 11,622);
  `StandardPresetPinQaTests`, `NonHardMatchPinQaTests`, the eight `CounterCompositionTests.TheNonHardPins_*`,
  `ReserveTwinTests`, `SharedVisionFfaPinTests`, `OutcomePinM73bQaTests`, the premap AI replay, `save_ai_seed2`,
  `CliTests.Match_TheLiteralRow_*`; unchanged: `HardKnobsPinTests`, `seed21_playable.replay`, the golden. Stress:
  `AiDeterminismTests` (`HuntSeed` 2: ends 51,218, 2 hunts; its comments still carry M6-SH2's figures),
  `HuntStressTests` (`MidHuntSeed` 2; **the four-seat FFA oracle row seed 2**, 59,951, the same match as
  `FourAiSeats_OnPaleHills_Seed2_KnownStall`: drop that twin at the next pin move), `M6Sh2DeterminismStressTests`
  (the retrain twin on **Raraku seed 3**), `M6Sh2QaStressTests` (the save sweep on **Raraku seed 2**),
  `StarvedSaveTwoProcessQaStressTests` (Raraku seed 1), `ReservePairingsStressTests` (`KnownNoAgeTwo` (2, 0, 1, 1)),
  `MatchRowsM6S10Report` (seeds 1-8 on the three maps: all end). The next sim task that changes Normal AI behaviour
  or gather movement re-measures all of them (rules 43 / 45 / 71).
- **Known stalls / skips with ids on `main`:** the four-seat Pale Hills FFA seed 1 (BUG-1068 / 1116 / 1559: the
  map's gold is gone by 30,000), seeds 3 and 4 (BUG-1536, the owner's question), seeds 6 and 7 (no end by 144,000,
  measured in M6-S12's fix round, same shape); the five `KeepCornerCureQaStressTests` rows (BUG-1463);
  `EndGameHardeningTests.ALoneIdleWorker_*` (BUG-1116), `M5H5HuntAfterDefeatQaTests.A2v2_*` (BUG-1149),
  `SimHardeningM5H5QaTests` rows 5 / 6 (BUG-1150), `CounterMemoryQaTests.AHardSeat_ThatWatched20HorseRaiders_*`
  (BUG-1156), BUG-1161's three rows, `RegenQaTests` rows for BUG-1165 (2) / 1166 (2); the view's `QaM7V1Test`
  zoom-40 1.5 head row; `DebugOverlayTest` and `QaM25Test` 1 ms rows flake under load.
- **Criterion 6 of M5 (the owner wins on Easy, loses to Hard) is owner-only:** M5 is signed off the session the owner's note lands.

---

## Sim track

### Where we are
M8-4 on `main`: BUG-1771 and BUG-1770 fixed, the unit limit (`maxAlive`, Request 56 G4) built on test data; no
hash, pin or format moved by the sim. Counter **3 / 4.** New and closed: BUG-1810 (S4, docs; the Producer).
Carried: BUG-1730, 1731, 1700 (S4), BUG-1660, 1661 (S3; behind BUG-1621 / BUG-1610, whose Stress row stays
skipped), 1555, 1554 / 1556, 1557 (S3), 1558 (S4), 1463, 1537, 1490, 1434, 1433, 1161, 1149, 1152, 1150, 1068,
1116, 1010. The answers to M8-4's Questions 1-4 are in docs/01 (the fourth 2026-11-26 row, (1)): keep all four.

### Next task candidates (after this session)
1. **M6-S14, the AI-play session** ("Proposed briefs" above; feature, QA full, one pin move; 20 bug ids
   1850-1869): BUG-1661 first and alone, then BUG-1660, 1555, 1554 / 1556 (with 1490 and Request 60 if the PLAN
   keeps the format change here), 1558; BUG-1700's comment. Write M8-4's answers into docs/03 (rule 67).
2. The sim's hardening (at 4 / 4): BUG-1730, BUG-1731's decision together with the unit limit's "can still
   train" note, older S3 / S4.
3. Request 56 G5-G8, on test data only.
4. Requests 54, 53 part two. A movement task for BUG-1463 only with a design first.

---

## View track

### Where we are
M6-V30 (eight Teblor building models, the `scale` on a model's height) on `main`. Counter **4 / 4: the next view
session is its hardening.** 130 scenes. New debt: BUG-1830 (S4; the Iron colour on the grey Cairn), BUG-1831 (S4;
the import script's PASS line). Carried: BUG-1790 (S3), BUG-1792, BUG-1710 (S4), the wrong sentence in docs/03
"Implementation (M6-V29)" Question 1. The Producer's answers to M6-V30's Questions 1-5 are in docs/01 (the fourth
2026-11-26 row, (2)): keep the hall; the Cairn stays a grey tower for now; the Whirlwind's buildings are
kit-bashed tents unless the owner supplies a pack; the Hut stays 3.85 m; the two boxes wait for a source.

### Next task candidates (after this session)
1. **M6-VH10, the view's hardening** ("Proposed briefs" above; QA standard; bug ids 1870-1879).
2. **The Whirlwind's buildings as kit-bashed tents** (a `tools/art/` builder feature: box-and-prism tents with
   team-coloured cloth for the Tent, the Holy Camp and the four camps; the pack's market stalls for the Supply
   Cache and its Second Age tower for the Lookout Tower), or the owner's tent pack if "tent pack: ..." is in the
   inbox.
3. The Stone Cairn as stacked rocks (the Stylized Nature MegaKit's rocks are on disk).
4. The two cavalry, the Destrier Rider, the War-dog, the Catapult and the Ram once packs are on disk (owner);
   the Stables & Kennels and the Breaker's Hall with them.
5. An owner item (".NET 10 installed", templates, music, the sound notes, "models: ...", "priest veil: ...",
   "buildings ok", "teblor buildings ok", "roof: also the tower tops", "depot: taller", "whirlwind colours ok",
   "outline hidden units", "order notice: ...", "draw notice: ...").

---

## Data track

### Where we are
D38 on `main`: the three Teblor orders ship; the data hash is B7947DC602B1E11B. Counter **3 / 4.** Closed:
BUG-1612 (S3), BUG-1421 (S4). Open: BUG-1840 (S4; the static checks that read `buildOrder` only), BUG-1611 (S4).
The Producer's answers to D38's Questions 1-2 are in docs/01 (the fourth 2026-11-26 row, (3)): the two D38
Stress rows are handed to the sim's AI-play brief by name (skipped with a bug id if Teblor play moves) and the
data task after it re-measures and re-points them; the stale docs/03 sentences are corrected.

### Next task candidates (after this session)
1. **D39** ("Proposed briefs" above; QA light; no game data; bug ids 1880-1889): shadow.md after M8-4,
   BUG-1840's leftovers, malazan.md's paragraph. Write D38's answers into teblor.md (rule 67).
2. After the sim's AI-play session: the three Teblor orders re-measured on that `main` ("measure and report"),
   the two D38 Stress rows re-pointed at the new table, the hour-long Teblor report classes with it; the owner's
   table again.
3. The Shadow roster as Request 56's schemas reach `main` (G1-G4 are there; G5-G8 are not).
4. The data's hardening at 4 / 4: BUG-1611, older S4.
