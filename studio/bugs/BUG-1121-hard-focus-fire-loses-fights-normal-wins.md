# BUG-1121: Hard's focus fire loses a fight Normal wins (20 Heavy Infantry v 20 Raiders: Normal 14 left, Hard 0)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | fixed (re-verified by QA 2026-11-07-0926 round 1) |
| Found | 2026-11-07-0926, task M5-4c-2 |
| System | AI (MilitaryManager.Focus), combat (explicit Attack) |
| Fixed by | 9f7423b (each fighter's own focus reach, about its attack range; `MilitaryManager.Focused` and the free re-send); regression `HardKnobsQaTests.A20HeavyWave_Against20Raiders_WinsOnEveryDifficulty(Hard)` + `HardKnobsTests.AMemberWhoseFocusDied_IsSentOn_WithoutSpendingATry_UnlessItGaveTheFocusUp` |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~HardKnobsQaTests.A20HeavyWave_Against20Raiders_WinsOnEveryDifficulty"`
   (`sim/Rts.Sim.Tests/QA/HardKnobsQaTests.cs`): the flat scene of `HardKnobsScenes`, combat on, 20 Malazan Heavy
   Infantry of the AI seat (a wave starts at the first think) against 20 Whirlwind Raiders of a human seat standing still.

## Expected
docs/02 "AI opponent": Hard is "fair play with tighter decisions (thinks every 0.5 s, focus fire, pulls back wounded
units ...)". A knob that makes Hard stronger must not turn a fight its army wins on Normal into a wipe. M5's criterion
"lose to a Whirlwind AI on Hard" needs Hard at least as strong as Normal in a straight fight.

## Actual
```
Normal: 1123 ticks, Heavy Infantry alive 14, Raiders alive 0
Hard:   1035 ticks, Heavy Infantry alive 0, Raiders alive 13
```
One knob at a time (data copies of the scene, Hard seat, QA scratch run):

| focusFire | pullBackWounded | 20 Heavy v 20 Raiders | 10 v 10 |
| --- | --- | --- | --- |
| off | off | Heavy win, 14 alive | Heavy win, 5 alive |
| off | on | Heavy win, 16 alive | |
| on | off | Raiders win, 14 left | Raiders win, 6 left |
| on | on | Raiders win, 13 left | |

Focus fire alone is the cause; the pull-back helps. Against 20 Desert Archers (ranged, 50 hp) Hard still wins with
focus on, but takes 899 ticks instead of 595 and sends 400 Attack commands (one per member per kill).

## Notes
- Likely mechanism: every member in reach gets an explicit `Attack` on the single lowest-hp enemy (at the first pick
  all are full hp, so the tie goes to the lowest slot: the whole army on one Raider). Under `CombatMode.Ordered` a unit
  holds its target while it lives (`CombatSystem` "no re-pick while it lives"), so 20 melee units crowd one target, most
  can't reach it, and none strikes the Raiders hitting them. Normal's `AttackMove` members each take the nearest enemy.
- Secondary: after a focus dies an `Attack` order ends Idle where it stands (`Settle`), so the wave's `Orders` re-sends
  its `AttackMove` and spends one of the member's `MaxTries`; a member that isn't a cell nearer the target after three
  such re-sends Leaves the wave. 4-seat Pale Hills seed 1, 20,000 ticks: healthy members leaving a wave in progress,
  Hard 8/2/3/3 against Normal 1/1/0/0 (`HardKnobsQaTests.PaleHills_FourSeats_TheFocusIsAlwaysSeen` prints them).
- Matches agree: the developer measured Hard winning 0 of 4 Hard-v-Normal matches with the slice (2 of 4 before), and
  the gather bonus alone made Hard win sooner.
- Possible directions (the developer's call): focus only within a member's own reach (weapon range plus a margin) or only
  for ranged members; keep melee on AttackMove; split the focus over several targets for a big army.

## Re-verify (2026-11-07-0926, round 1, HEAD 9f7423b)
- The controlled repro passes. 20 Heavy Infantry v 20 Raiders: Hard 15 alive, 0 Raiders left in 1,046 ticks; Normal 14
  alive in 1,123. Churn in that fight: 106 unit Attacks, at most 2 per member and target, at most 14 per member.
- The secondary effect is gone. 4-seat Hard Pale Hills seed 1, 20,000 ticks: healthy members leaving a wave in progress
  1/0/0/0 (was 8/2/3/3; Normal 1/1/0/0). Free re-sends 55/62/61/11 per seat, at most 47 Attack + AttackMove for one unit
  over the whole match (Normal: 14). Fog oracle: 2,050 focus picks and 1,210 unit Attacks, 0 faults.
- The flicker attack on the try rule (`QA/HardKnobsRecheckQaTests.AFocusFlickeringInAndOutOfSight_*`): a Sandstorm made
  and removed every 20 / 60 ticks on an immortal Raider. As wave members, Crossbowmen get at most 8 Attacks and 6
  AttackMoves each over 6,000 ticks, and 17 free re-sends in all over 152 re-sightings. As defenders, one Attack per
  re-sighting (148 over 149). The cost is bounded by the flicker: at most one order per member per re-sighting.
- The match-level weakness of Hard against Normal is not closed by this fix. It is filed as BUG-1123: what remains is
  the pull-back plus a residual cost of the other knobs, not the focus crowding this bug was about.
