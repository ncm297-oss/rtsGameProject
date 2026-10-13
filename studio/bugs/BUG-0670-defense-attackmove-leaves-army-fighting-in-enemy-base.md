# BUG-0670: The defense's AttackMove leaves an army that is away fighting in the enemy base: it never comes home

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-10-13-0916, task M5-4b |
| System | AI (DefenseManager, MilitaryManager) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~DefenseQaTests.TheArmyAwayInTheEnemyBase_ComesHomeToDefend"`
   (flat AI scene, combat on: player 0's three Heavy Infantry are attacking player 1's Town Hall 90 m from home when a
   player 1 Raider holds 20 m from player 0's hall; checked 60 s later).
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AiWaveStressTests.TwoWavingAiSeats"` (seeds 1, 6, 11:
   each seat's three line units start in the other's base).

## Expected
docs/02 "AI opponent": defending the base when attacked comes first. The M5-4b brief: while the threat holds "the wave
ends like a retreat (members home, ...)" and every free army unit goes to the defense point. An army that is away from
home when the base is attacked comes home.

## Actual
1. `2 of 3 soldiers not home after 60 s of threat: Attacking 89 m from home; Attacking 19 m from home; Attacking 90 m from home`.
   The threat holds the whole minute.
2. Seeds 1 / 6 / 11: every seat's army stands `Attacking` the other seat's hall from about tick 100 to tick 6,000
   (seed 6: hall hp 2400 -> 1878), while its own base is `Defending` the whole run. No wave starts (`no wave in the run`).

## Notes
Cause: `DefenseManager.Army` sends one `AttackMove` to the defense point to every free army unit, wherever it is. A unit
standing in the enemy base acquires the nearest enemy (the hall it was hitting, the workers) on its first step and fights
on there. It is never re-sent (`DefendedVersion` matches). `MilitaryManager.End` sends a wave member its `Move` home, but
the next think finds the member free (the Move applied) and replaces it with that same `AttackMove`, so the retreat lasts
one think. Possible fixes (Producer's call): a plain `Move` home for units farther than some distance from the point,
then the `AttackMove` once they are near; or leave wave members on their retreat `Move` until they arrive.

With BUG-0671 this is a mutual deadlock in AI-vs-AI play: each side's army sits in the other's base, each side defends
forever, and nobody gathers or waves. `Stress/DefeatAiStressTests` measured the same in 4-seat runs (weak sets 0110, 1010,
1001): a strong seat defends from early on to tick 6,000 and never waves. The developer reported this ("defenders already
fighting in the enemy base don't come back") and left it for QA to judge.
