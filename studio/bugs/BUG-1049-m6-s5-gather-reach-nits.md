# BUG-1049: M6-S5 gather-reach nits (an over-broad FFA claim in docs/03, two reach anchors, a narrow soak margin, one untested clause)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-0805, task M6-S5 (QA) |
| System | docs/03 "Implementation (M6-S5)", AI (`InGatherReach` / `FoundEnough`), `Stress/TeamAiSoakStressTests` |
| Fixed by | |

## Repro
1. **docs/03 says no free-for-all AI scene moved; two do.** docs/03 "Free-for-all after the reach rule": "no free-for-all
   scene moved, the AI ones included (their starts see their own forest and mines within reach before the scout
   leaves)". True of the five pinned scenes (QA re-ran them on 424a5e8: same five digests). But of six free-for-all AI
   scenes run 12,000 ticks on 424a5e8 and on head (`AiSaveScenes.AiMatch` seeds 4/5/6/7 and `HumanVsAi` 1/3), two move:
   `AiMatch(5, 4, Hard)` (seat 3) and `AiMatch(7, 2, Normal)` (seat 1), both from the commands of tick 420 (hash from
   tick 421). On 424a5e8 the seat, knowing the next seat's forest through its raiders, released its scout at tick 300
   and sent three gatherers into that seat's base (seat 3 of seed 5: hall (188, 218), Gathers at (28, 174) and (21, 191),
   about 166 m; seat 1 of seed 7: hall (218, 196), Gathers at (42, 42) and (29, 45), about 235 m); on head `FoundEnough` holds back, the scout goes out (phase 1
   at 421) and the gatherers stay home. Head with `ai.gatherReach` 512 equals 424a5e8 on all six scenes x 12,000 ticks,
   so the reach rule is the only cause, and it is the intended effect (BUG-1045's shape in a free-for-all, via raiders).
   Only the parenthetical generalises too far.
2. **Two anchors.** `ScoutManager.FoundEnough` measures the reach from the hall's centre; `AiPlayer.InGatherReach` from
   every own finished drop-off. Today a seat has only its hall as a drop-off while the scout is out, so they agree; a
   faction or opening with an early second drop-off (a lumber camp) would let gatherers use a forest the scout still
   counts as not found. Worth one sentence in docs/03 or one shared anchor.
3. **The soak's "starved" margin is narrow.** `TeamAiSoakStressTests.RaidedTicksIfStarved` is 2,000; docs/03 records the
   exempt seats at 2,570-7,168 raided ticks and the reach-rule-off failures at 1,687 and 1,957. A small change in raid
   behaviour moves a row across the line either way (a legitimately starved seat raided 1,950 ticks fails; a
   gather-starved seat raided 2,050 ticks passes). Not wrong today; note it so the next move of the line is deliberate.

4. **Reach-rule test gaps (QA's mutation run, 36 reverted union / reach sites).** Before this QA session two reach
   mutants passed every test in the vision / team / AI filters, soak included: `InGatherReach` without its
   `!HasDropOff` clause, and `WalkForNode` (`KnowsNode`) without its `InGatherReach` check. `NearestNode` without its
   reach check failed only one soak row (seed 3, adjacent). QA's `QA/GatherReachQaTests` now fails on all three. One
   still survives: `InGatherReach` counting an unfinished drop-off **site** as an anchor (dropping
   `OwnBuildingSites[k] ||`). No test builds a second drop-off while the scout is out.

## Expected
1. docs/03 says the five pinned scenes are unchanged and that AI seats whose raiders see an enemy's nodes play
   differently from tick ~420 (the reach rule), naming the two scenes.
2. One anchor, or the difference documented.
3. A comment by the constant with the measured band.
4. A row where a drop-off site is placed before the scout is done and a node near the site (beyond the hall's reach)
   is not sent to until the site finishes.

## Actual
As above.

## Notes
QA's evidence: per-tick hash and AI command logs of both scenes on 424a5e8 and head, and head at reach 512 (session
scratch, not committed); the five pins re-run on 424a5e8 with `SharedVisionFfaPinTests.cs` copied there.
