# BUG-0596: A wave member crowded off the target is sent home out of the wave after its one `Attack`

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-12-1930, task M5-3 (re-check round 1; introduced by the BUG-0591 fix) |
| System | AI `MilitaryManager` (wave orders) |
| Fixed by | 1f46ca9 (M5-3 round 2) |

## Repro
1. Remove the `Skip` on `QA/AiMilitaryQaTests.AWaveThatCanFightTheTarget_KeepsItsMembers_WhileNoneDie`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AWaveThatCanFightTheTarget"`.
2. Scene: the flat AI scene, combat on, player 0 (AI, Normal, 1-pop threshold) sees player 1's hall (4 x 4, 2,400 hp) through a
   billet. Nobody defends. 12 or 24 Heavy Infantry wave in from about 10 cells off; 2,000 ticks.

## Expected
Members that can fight the target stay in the wave while it stands. Only a member that truly can't fight it (BUG-0591: an attack
that hits units only) is given up on.

## Actual
- 12 members: 0 dead, the hall at 1,912 hp, and 2 members sent `Move` home (ticks 481, 501).
- 24 members: 0 dead, the hall at 1,643 hp, and 10 members sent home (ticks 421-601).

The round 1 rule reads "Idle by the seen target after its one `Attack`" as "can't fight it". A Heavy Infantry that finds no room
on the hall's edge gives up its chase when friends already fight the target (`CombatSystem`, the `FriendFightsTarget`
give-up). It stands Idle by the hall and is sent home, and it leaves the wave for good.

## Notes
- Members sent home drop out of the wave's live half-pop. With the shipped `lossFraction` 0.5, a big wave on one building is
  close to "retreating" with no losses. At 24 members, 10 left (42 %). Any real losses on top start the retreat early.
- Before the fix these members got `Attack` again at the next think (BUG-0591's spam). Then they often found room once a
  neighbour moved. The fix needs to tell "can't fight this target" (its attack can't target buildings, or it never struck) apart
  from "crowded out". Two ways: check the unit's `attack.targets` / last strike, or keep crowded members on an `AttackMove` near
  the target instead of sending them home.
- The docs/03 line added in round 1 ("a member Idle there again after its `Attack` can't fight that target") describes the
  intent, not this case.

## Re-check (round 2, 1f46ca9)
- Fixed. The QA row is un-skipped and green: 12 members, 0 sent home, `AttackMove 12, Attack 11`, hall 1,912 / 2,400 at tick
  2,000; 24 members, 0 sent home, `AttackMove 24, Attack 32`, hall 1,553. The wave never ends.
- Mutation: replacing "crowded: stay" by "leave" fails both QA rows and the dev's new
  `MilitaryManagerTests.MembersThatLeaveAlive_AreNotLosses_AndTheRestFightOn`.
- Follow-up filed as BUG-0598: when *every* member is in the "crowded, unordered" state (none can actually strike the target),
  nothing ever ends the wave.
