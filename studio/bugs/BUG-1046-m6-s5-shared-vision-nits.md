# BUG-1046: M6-S5 shared-vision nits (an untested seam merge, a defeated ally's eyes for up to 3 ticks, mislabelled seats)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-27-0658, task M6-S5 |
| System | Vision (FogStore.Teams), BUG-1042 paperwork |
| Fixed by | M6-S5 (2026-11-05): item 1 `Vision/SharedVisionTests.TheExploreSeam_ExploresEveryCellForTheTeam_BeforeTheNextFogUpdate`; item 2 docs/02 sentence (Producer decision); item 3 labels corrected |

## Repro
1. **The explore seam's merge is untested.** In a scratch clone delete `if (Sharing) MergeTeams();` at the end of
   `FogStore.ExploreAllForTests`: the whole vision / placement / construction filter (256 tests, QA's M6-S5 mutation run)
   stays green. A team scene that calls the seam would read stale team-explored bits until the next fog update. Test seam
   only (a match never calls it), so S4.
2. **A defeated ally's eyes stay shared until the next fog update.** `QA/SharedVisionQaTests.AnAllyDefeatedMidMatch_*`:
   player 1 is out at `TickNumber` 7 (its Billet collapses); its last-stamped cells are still visible to player 0 then and
   until the next fog update (`TickNumber` 10 in that run, at most 3 ticks later), and gone from it on. This is docs/03's rule for any dead unit ("a dead unit's circle is gone at the next
   update", fog every 4 ticks), so not a defect; the M6-S5 QA focus asked for "stops sharing on the collapse tick", and
   docs/02 says nothing either way. Producer's call whether docs/02 should say it.
3. **BUG-1042's fix note mislabels difficulties.** It says "seed 2 seat 2 (Hard)" and "seed 1 seat 0 (Hard)"; with
   difficulty `(p + seed) mod 3` and `AiDifficulty` Easy = 0, Normal = 1, Hard = 2 both seats are **Normal**.

## Expected
1. A test that fails when the seam's merge is removed (e.g. a team scene calling `ExploreAllForTests` then reading
   `IsExplored` of the ally before any fog update).
2. The rule written down (docs/02 "Vision and fog of war": an ally's vision lasts until the next fog update after it is
   out, like a dead unit's circle) or the merge skipped for a defeated player, by the Producer's call.
3. The right difficulty names.

## Actual
As above.

## Notes
- QA's M6-S5 mutation run: of 28 reverted union sites, `RevealBuilding`'s team loop also survived the developer's tests;
  QA added `SharedVisionQaTests.ATowersHighGroundReveal_OnTheAlly_IsSharedWithThePlayer`, which fails on it (not part of
  this bug). `CanSeeBuilding`'s ally clause, the zone union and the ghost-after-merge order are caught only by fuzz rows
  (QA's union fuzz, `PlayerViewOracleFuzzTests.WithTeams_*`).

## Fix (M6-S5, dev, 2026-11-05)
1. `Vision/SharedVisionTests.TheExploreSeam_ExploresEveryCellForTheTeam_BeforeTheNextFogUpdate` (teams and free-for-all
   twin): after `ExploreAllForTests`, with no tick, every cell is explored for every player (`IsExplored` and the byte
   map). Checked: with only the seam's `if (Sharing) MergeTeams();` removed, the team row fails ("player 0 cell 0 not
   explored after the seam"); restored, both pass.
2. Producer decision, no code: docs/02 "Vision and fog of war" now says an ally that is out lends its last sight until the
   next fog update (within 4 ticks), as a dead unit's circle lasts until then.
3. BUG-1042's fix note: seed 2 seat 2 and seed 1 seat 0 are **Normal** (difficulty `(p + seed) mod 3`), corrected.
