# BUG-0750: D19 nits: a stale Andii Town Hall number, a "Shipped" line in docs/03, two checks that can no longer fail

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-15-2325, task D19 |
| System | design docs, QA content tests, AI ability fuzz |
| Fixed by | |

## Repro
1. `docs/factions/andii.md` line 51 (Eleint-blooded Champion): "115 vs a Town Hall per swing". The note's own numbers
   (40 siege; 40 vs Light = 40 x 0.5 x 2) give, by docs/02's formula with D19's Town Hall armor 2:
   `40 x 3.0 = 120 - 2 = 118` (115 was armor 5). D19 edited this page's Blade line and left this number.
2. `docs/03-technical-design.md` line 1889: "Shipped: M3-2 shipped the Town Hall slot only (... 2,400 hp, armor 5, ...)".
   It is history, but it sits under "Shipped:" and reads as today's value.
3. `QA/Content/D18ReportPreconditionQaTests.D18LeverEdits_ChangeTheLoadedCopy_AndLeaveShippedFilesByteIdentical`
   (lines 42-49, 57, 70-72): the Structure edit (0.7 / 0.35 / 0.6) and `bonusVs.mounted` 1.5 are now the shipped
   values, so "the edits took" cannot tell a mistyped key from a working edit. The kill-time report no longer uses this
   edit (it puts D18's values back in `BuildingKillTimeReportTests.BeforeD19`); QA's new
   `QA/Content/D19QaTests.BeforeD19Copy_LeavesShippedFilesByteIdentical` covers that path. Developer flagged it.
4. `Stress/AbilityManagerFuzzStressTests` line 120 (`Assert.True(ai.Knobs.UseAbilities, ...)`): with every shipped
   difficulty `useAbilities` true, no fuzz scene has a seat with it off, so the check can never fail and the fuzz no
   longer exercises "an off seat sends nothing". That property now rests on one scene,
   `Ai/AbilityManagerTests.ASeatWithUseAbilitiesFalse_NeverCasts_ItsNormalTwinDoes` (a data copy). Developer flagged it.
5. The golden header regen (cabf8c2) carries no reason in the commit message (the brief's criterion 1 asks for one, "as
   D10c did"; D10c's commits also have subject lines only, so the studio's convention may be the
   `GatherWedgeQaTests.SameGameDataHashes` comment, which does give the reason).

## Expected
Docs quote the shipped numbers (or say "then"); every guard in the suite can fail on some input.

## Actual
As above. None changes play: no Andii data exists, and the shipped data is correct.

## Notes
Fixes: 118 on andii.md; "armor 5 then" in docs/03; point item 3's Structure / bonus edit at values off the shipped
ones (e.g. 0.5 / 0.25 / 0.5, 1.4) or drop it; give the fuzz a share of scenes whose Easy block has `useAbilities` false
in a data copy. Item 3 and 4 are test-only (sim / data test folders), items 1-2 data track docs.

**2026-10-16 (merged-tree reprint, BUG-0734's commit):** items 1-2 fixed (andii.md "118 vs a Town Hall per swing (armor 2
since D19)"; docs/03 "armor 5 then (2 since D19)"). Items 3-5 stay open for D-H5.

**2026-10-16 (D-H5, developer; items 3 and 5, proposed fixed; item 4 is the sim track's M5-H2).**
3. `QA/Content/D18ReportPreconditionQaTests.D18LeverEdits_*` now edits to values off the shipped ones (Structure melee
   0.5, pierce 0.25, magic 0.5; `bonusVs.mounted` 1.4, which the test now also asserts) and asserts each edited value
   differs from the shipped one, so the check can't go dead again silently. Mutation: a mistyped damage type id
   (`"meele"`) fails "Expected: 0.5, Actual: 0.699999988" (the old 0.7 edit passed it); a mistyped field key
   (`"structur"`, `"mountd"`) is rejected by the loader ("unknown armor class").
5. Convention written into `studio/bugs/README.md`'s head: a golden header regen's reason lives in the
   `GatherWedgeQaTests.SameGameDataHashes` comment and the commit body.
