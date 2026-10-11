# BUG-0511: An own fighter abeam of a stuck caster (not in its way) holds the walk-in's stuck count, so the cast is never dropped

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-2334, task M4-H3 (QA) |
| System | movement / abilities (`MovementSystem.OwnFighterAhead`, BUG-0461's cast walk-in rule) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningH3QaTests.AnOwnFighterBesideTheCaster"` with the
   two `Skip`s removed from `QA/SimHardeningH3QaTests.cs` (`aheadBy` 0.3 and 0.6).
2. Layout (the dev's `CastWalkInTests` pass map): a Cadre Mage in the 2 m pass (row 4) pressed against two enemy holders
   (genuinely stuck), ordered to cast Telas Fire 24 m west. An own Crossbowman stands on the level-1 band beside it (row 3,
   1.5 m across the pass edge, edge-to-edge gap 0.73-0.82 m), shooting a worker on the strip, its centre 0.3 / 0.6 m west of
   the mage's (68-79 degrees off the line of travel).

## Expected
The brief (M4-H3 scope 1, Producer decision): an own unit within 1.5 m **in its direction of travel** holds the count; QA
focus: "an own unit *beside* (not ahead of) the caster must still count stuck ticks". This fighter is not in the caster's way
(its centre is 1.5 m to the side, much more than the two bodies' 0.8 m), and it is across a cliff edge. A genuinely stuck
walk-in drops the cast 60 stuck ticks after its last progress (criterion 1).

## Actual
```
fighter offset <-0.29999924, -1.5> (edge gap 0.73 m, 79 deg off the travel line)
a caster stuck against enemies ... never dropped its cast in 300 ticks: [14 Moving c0 s1 ...] ... [294 Moving c0 s1 ...]
fighter offset <-0.6000004, -1.5> (edge gap 0.82 m, 68 deg off the travel line)   -> same, never dropped
fighter offset <0, -1.5> (exactly abeam, 90 deg)  -> dropped at 67, last progress 6 (correct)
```
The cast stays pending (stuck count 1) for as long as the neighbour fights.

## Notes
`OwnFighterAhead` accepts any own Attacking / Casting unit with `Dot(to, forward) > 0` within reach: the whole forward
half-plane, at any angle up to 90 degrees, on any level. Suggested: require the blocker inside the caster's swept corridor
(lateral offset `|cross(to, forward)| <= r_i + r_j` plus a small slack) or a cone (e.g. within 45 degrees of `forward`),
and on the caster's level / a legal step away. docs/01's row and docs/03 say "its centre in front of the direction of
travel", which describes the half-plane: the docs need the same fix. The hold is not a lost click (the cast still fires if
the way opens), hence S3, but a caster beside its own skirmish line can stand "walking" indefinitely.
