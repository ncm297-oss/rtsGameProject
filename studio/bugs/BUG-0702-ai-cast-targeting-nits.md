# BUG-0702: AI cast targeting nits: two groups either side get no cast, casters stack on one point, the reach edge never casts

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (item 1 in M5-4f, Producer, 2026-10-16-2036 ACCEPT; items 2-3 in M5-H2) |
| Found | 2026-10-13-1616, task M5-4c |
| System | AI (`Ai/AbilityManager.Target`) |
| Fixed by | item 1: 8ac7b43 (M5-4f): the ground cast's group is seeded from the enemy in reach **nearest the caster** (slot order on ties), then grown by the radius, instead of the centroid of every enemy in reach; `QA/AbilityManagerQaTests.TwoGroupsEitherSide_OneIsCastOn` un-skipped and green, `Ai/AbilityManagerTests.TwoGroupsEitherSide_TheNearerIsCastOn`. Not built: the owner's "densest cluster" rule (docs/01 2026-10-14; a lone enemy nearest the caster with a group behind it still gives no ground cast): listed for the owner under For your review (M5-4f), sim feature queue. Items 2-3: 47ca4a2 + ac80818 (M5-H2): the no-stacking rule (`AbilityManager.Stacks` / `SentNear` / `ForgetEnded`, the Blackboard's 64-entry recent-cast list; BUG-0760's retry fix in the round), `QA/AbilityManagerQaTests.ThreePriests_OneStormOnOnePoint_TheOthersHoldTheirs` + QA's `SimHardeningM5H2QaTests` rows and 60-scene fuzz; item 3 a docs/03 line ("`range + radius` is a search ring"). Item 1 goes with M5-4f (the densest cluster) |

## Repro
1. **Split groups.** `QA/AbilityManagerQaTests.TwoGroupsEitherSide_OneIsCastOn` (skipped, BUG-0702): a Cadre Mage with two
   enemy Raiders 8 m east and two 8 m west, `castMinTargets` 2. Each pair alone qualifies.
2. **Stacking.** `QA/AbilityManagerQaTests.ThreePriests_StackTheirStorms_OnePoint_OneCastEach` (green, pins today's rule):
   three Priests beside one group of four all cast Sandstorm on the same point in the same think.
3. **Reach edge.** `QA/AbilityManagerQaTests.AGroup_AtTheEdgeOfRangePlusRadius`: a tight group exactly at `range + radius`
   is counted (it is within the reach), but the point pulled back to `range - 0.05` leaves it outside the radius, so there is
   no cast. In practice the ring the AI can hit ends 0.05 m or more inside `range + radius`.

## Expected
docs/02 "AI opponent": Normal "uses abilities", Hard "good ability use". The brief's rule (one centroid of every enemy in
reach) is followed to the letter. These cases are about whether that rule is good use, so they are for the Producer.

## Actual
1. The centroid of all four enemies is the caster's own spot, and no enemy is within 3 m of it, so the mage casts nothing,
   at either pair, for as long as they stand there.
2. Three cooldowns are spent where one storm does the same job (Blinded and Slowed refresh rather than stack). For Telas
   Fire, Burning likewise refreshes.
3. As above. Harmless, but docs/03 says the group is sought "within `range + radius`".

## Notes
Possible rules (owner / Producer call): seed the group from the enemy nearest the caster, or from the densest one, instead
of the centroid of all of them; skip a point another caster was sent to this think (within the radius) for abilities whose
effect doesn't stack; in docs/03 say that the reach is a search ring and that the cast needs the group within the radius of
the pulled-back point.

## QA note (2026-10-16-0847, M5-H2)
Item 2 (no stacking) is in on the branch (`QA/AbilityManagerQaTests.ThreePriests_OneStormOnOnePoint_TheOthersHoldTheirs`), but it broke the judging rule for non-stacking abilities: a storm that never lands is sent once instead of up to MaxTries times (BUG-0760). Item 3 became a docs/03 line ("a search ring"). Item 1 (two groups either side) still skipped (M5-4f).
