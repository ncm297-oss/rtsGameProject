# BUG-1185: D27 left five Hard-seat digests in QA's StandardPresetPinQaTests un-re-pinned: the Stress suite is red on the branch

| Field | Value |
| --- | --- |
| Severity | S2 (acceptance criterion 3, "Hard-seat pins re-measured in the same commit", not met; a permanent pin red) |
| Status | fixed (D27 fix round 1, 6b72729; verified by QA re-check round 1, 2026-11-10-0146) |
| Found | 2026-11-10-0146, task D27 (QA standard, the pin sweep) |
| System | Tests: `sim/Rts.Sim.Tests/QA/StandardPresetPinQaTests.cs` `AiRows` (`[Trait("Category","Stress")]`) |
| Fixed by | 6b72729 (D27: fix QA findings (round 1)) |

## Repro
1. On 3d25e9d: `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~StandardPresetPinQaTests.EveryAiScene"`.
2. 5 of 7 fail. On the diff base 1eb231d the same filter gives 7 / 7 passed (QA ran both).

## Expected
Every Hard-seat pin re-measured in the D27 commit with the old value in a comment (brief D27, criterion 3 and Scope:
"re-pin the Hard-seat rows ... with the old values in comments"); the default and Stress loops green.

## Actual
The five rows whose scene has a Hard seat (`AiSaveScenes.AiMatch` gives seat p difficulty `(difficulty + p) % 3`) fail; the
two without one (seed 1 Easy / Normal, the 2v2 of Normal AIs) pass, so no Normal-only digest moved:

| Row (seed, players, difficulty, cramped) | Scene | Hard seat | Pinned | Now (3d25e9d) |
| --- | --- | --- | --- | --- |
| 2, 2, 1, false | `ai_seed2_2seats_Normal` | 1 | 0x08DAC14990BB29C5 | 0x947084EC8CD14723 |
| 3, 3, 2, false | `ai_seed3_3seats_Hard` | 0 | 0xFCD8F85C768FCED4 | 0x79FE6C459CD45DF7 |
| 4, 4, 1, false | `ai_seed4_4seats_Normal` | 1 | 0xFB6A7E60CAB35D32 | 0x7F27FD9426E1139D |
| 5, 2, 2, false | `ai_seed5_2seats_Hard` | 0 | 0x19A8D59E908C2B31 | 0xC0BAEAB03FDA4C71 |
| 3, 3, 0, true | `ai_seed3_3seats_Easy_cramped` | 2 | 0x3257C07089F858CA | 0x86C5700D13A97656 |

All five still have 12,000 distinct hashes (the scenes don't stand still).

## Notes
- Cause: Hard's `gatherBonus` 0.25 -> 0.4 changes every Hard seat's economy; these rows are `Stress`, so the default loop
  (`Category!=Perf&Category!=Stress&Category!=Soak`) doesn't run them, and the developer re-ran only the classes it changed.
  The same scene `ai_seed4_4seats_Normal` was re-pinned in `Vision/SharedVisionFfaPinTests` but not here.
- Fix: re-pin the five rows to the "Now" values with the old ones in the comments (the file's pattern), then run every
  `Stress` class that pins a match with a Hard seat (QA's pin sweep this session: `NonHardMatchPinQaTests`,
  `MatchCriterionTests`, `MapSlice2StressTests`, `HuntStressTests`, `CounterCompositionTests`, `HardKnobsMatchTests`,
  `D21OpeningsQaTests`, `DH6ThresholdQaTests`, `MapFileTests` were green; the full Stress + Soak run is the re-check's).
- Test file is QA's (`QA/`), outside the data track's folders; the brief's Scope asks for the Hard re-pins wherever they are.

## Re-check round 1 (2026-11-10-0146, QA)
- The five Hard-seat rows in `AiRows` now hold exactly the "Now" values of the table above, each with the old value in a
  "D27 (BUG-1163/BUG-1185, Hard gatherBonus 0.4): was ..." comment; the two Normal-only rows are byte-for-byte unchanged.
- Full suite on 6b72729 (no filter, Stress + Soak + Perf): `StandardPresetPinQaTests.EveryAiScene_AtStandard_12000Ticks_PlaysAs5368cfe`
  7 / 7 passed; every other Hard-seat pin class green (`SharedVisionFfaPinTests`, `ReserveTwinTests`, `ScoutManagerTests`,
  `ReplayFormatTests`, `HuntTests`, `HardVersusNormalTallyQaStressTests`, `EndGameH6ProbeQaStressTests`, `TeamAiSoakStressTests`).
  No other Stress / Soak digest failed, so no Hard-seat pin is left un-re-pinned.
