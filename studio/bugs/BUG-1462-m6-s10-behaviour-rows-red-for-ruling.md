# BUG-1462: M6-S10 rows that assert behaviour and went red on the final tree (listed for the ACCEPT's ruling, rule 57)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open (**Stress rows only**, the sim's hardening; the five default-loop rows are green since session 2026-11-15-1536: `AHardSeat_*` by M6-S11 outcome A, the four content rows by D-R39; the Producer's scratch clone of the three-way tree confirms) |
| Found | 2026-11-15-0506, task M6-S10 (developer) |
| System | tests: AI behaviour rows over the changed economy |
| Fixed by | |

Each row diagnosed in docs/03 "Implementation (M6-S10)" (stage (b) / (c)); none is a held worker or an unplaceable building.

- `CounterCompositionTests.AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat` (default loop): the scene's seat was
  wood-bound (seen and unseen trained the same 10 / 8; red with the cure alone too). Fix round 1 re-staged the criterion-2
  run on a gold-bound seat (wood topped up, the delivered gold replaced by a trickle at the gold-bound boundary):
  `ANormalSeat_ThatSawShock_*` is green (6 / 3 seen v 5 / 4 unseen); this row stays red, Normal and Hard both shift
  0.111 (nine units in the 180 s memory window cannot tell x 1.5 from x 2).
- `Content/AiOpeningPairsReportTests.NormalAiVersusAi_*(0, 2)` (holy_war wiped before its first wave, defeated 12:29) and
  `(2, 0)` (the Whirlwind beaten, Town Hall razed, four steps due and undone: "DEAD END") (default loop). No honest next seed: the row's seed is the pair's smallest, its cell the pages'
  1st-seed column, and the 2nd and 3rd seeds are `QA/Content/D21OpeningsQaTests`' runs.
- `Content/AiOpeningPairsReportTests.Bug0982_*` and `QA/Content/DH6ThresholdQaTests.Bug0982_*`: BUG-0982's rule now picks 14
  pop (cadre_rush ships 18): a data measurement for the balance pass (D33).
- `QA/Content/D21OpeningsQaTests` rows that still fail after their cells were re-printed (wave rule): see the report.
- `Stress/StarvedSaveTwoProcessQaStressTests.PaleHillsSeed1_*`: Pale Hills seed 1 now ends at 42,384 before any starved wave.
- `Stress/StartingResourcesAiStressTests.EachOpening_AtHigh_*` (openings 1, 2): high's mean bank over 1.5 x standard's.
- `Stress/FarExpandProbeQaStressTests.AProbedMatch_*`: (vathar_crossing, 2, 4 seats) p3 silent 7,000 ticks (BUG-1152 family).
  (raraku, 1) is green, not red: its `Known` entry (BUG-1460) makes the probe print, not assert, its oracles.
- `Stress/MapSlice2StressTests.NormalVersusNormal_OnANewMap_OtherSeeds_EndWithin72000` Raraku 3 / 4 / 6: BUG-1460 (no end in
  72,000; a finding, not skipped).
- `QA/ScoutCapQaTests.OneScoutKilled_At0_200_2000_OnAFarForestStart_Seeds1To10(seed: 5)` (Stress): 12 workers left at 200 and
  2,000 (bound 14); 11 losses on the 157 m forest walk.
- `QA/Content/DR35ProsePinQaTests.AgeTwoProse_MatchesTheAgeTwoTables` and `Malazan_Bug0982Prose_MatchesTheThresholdTable`
  (default loop): the Age II cells were re-printed (rule 40); the prose sentences they pin (malazan seed 32 / 45, the
  cadre_rush v raider_rush seeds 8 / 14, whirlwind seed 9 / 19) are now false. Criterion 12 allows the sim cells and
  quoted report sentences only, so these cannot go green inside it: for the data track's next task, not rewritten.
- Sentences on `docs/factions/teblor.md` made false by re-printed cells (BUG-1464 item 3), for the data track:
  "the 11th Hut step (step 15) is skipped and the 12th is never reached on this seed" (the cell beside it: step 16 done
  at a cap of 100, steps 15 and 16 skipped); the first waves "at 340 s" / "at 362 s" against a Normal Whirlwind
  (`TeblorOpeningQaTests` now 323 s / 316 s, Age II 448 / 429 s, beaten at 1,022 s, not 910 s).

## Producer ruling (ACCEPT of 2026-11-15-0506)
Severity S3 (QA filed criterion 14 as S2). **Five default-loop rows reach `main` red by this ruling (rule 57):**
`CounterCompositionTests.AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat`,
`Content/AiOpeningPairsReportTests.NormalAiVersusAi_*` (0, 2) and (2, 0),
`QA/Content/DR35ProsePinQaTests.AgeTwoProse_MatchesTheAgeTwoTables` and `Malazan_Bug0982Prose_MatchesTheThresholdTable`.
Why a ruling and not a REJECT: the brief's criterion 14 ("default loop green") could not hold together with its own
criteria 10 ("a row with no honest re-staging is listed for the ACCEPT's ruling, not edited to pass") and 12 ("a page
sentence a re-printed cell makes false is listed for the data track, not rewritten"): the brief's error (rule 66),
corrected in the session log. None of the five is a held worker, an unplaced building or a broken rule.
- **Owners and order:** the four `Content` / `QA/Content` rows are the whole first item of the data track's next
  task (the prose on malazan.md / whirlwind.md / teblor.md rewritten to the re-printed cells; the two opening-pair
  rows diagnosed as balance or as a report gap, then fixed in the data or re-stated with the reason).
  `AHardSeat_*` is the first item of the sim's next task (a staging that can tell x 1.5 from x 2, or the finding that
  Hard's counter strength does not show at this army size).
- **Until then every default loop's expected result is "5 failed, exactly these".** Any other red row is a finding.
- **Two sessions red = the fix becomes that track's whole task.**
- The Stress rows listed above stay red under the same ruling with their ids (BUG-1460, BUG-1152's family, BUG-0982's
  14 v 18 pop for D33, `StarvedSaveTwoProcess`, `StartingResourcesAi` 1 / 2, `ScoutCap.OneScoutKilled(5)`); they go to
  the sim's hardening (counter 4 / 4 after the next feature session).

## 2026-11-15-1536 (ACCEPT)
The five default-loop rows ruled red at 0506 are green: `CounterCompositionTests.AHardSeat_ShiftsItsLineShareFurther_ThanANormalSeat` (M6-S11, the two-producer-pair staging `RunWide`; Normal 11 / 6 seen v 10 / 8 unseen, Hard 12 / 5; strength-0 controls read 0), `AiOpeningPairsReportTests` (0, 2) and (2, 0) and `DR35ProsePinQaTests` x2 (D-R39). Still red by the ruling, exactly as listed above and re-confirmed by M6-S11's QA on a47318f (16 rows): the Stress rows. They are two sessions red after this one, so they are the sim's next task (the hardening), each green or re-stated with its id. Note for D33: `Bug0982_*` x2 stay red on their pick check (the rule picks 14, the file ships 18); the page's threshold table was re-printed by D-R39.
