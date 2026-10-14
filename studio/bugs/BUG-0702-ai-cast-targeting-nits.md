# BUG-0702: AI cast targeting nits: two groups either side get no cast, casters stack on one point, the reach edge never casts

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-13-1616, task M5-4c |
| System | AI (`Ai/AbilityManager.Target`) |
| Fixed by | |

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
