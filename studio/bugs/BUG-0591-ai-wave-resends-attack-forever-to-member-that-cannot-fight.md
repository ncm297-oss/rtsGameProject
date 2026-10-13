# BUG-0591: A wave member standing Idle by a seen target it can't hurt gets `Attack` every think, and the wave never ends

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-12-1930, task M5-3 |
| System | AI `MilitaryManager` (wave orders) |
| Fixed by | |

## Repro
1. Remove the `Skip` on `QA/AiMilitaryQaTests.AMemberThatCantHarmTheTarget_IsNotSentAttackEveryThinkForever`, then
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~AMemberThatCantHarmTheTarget"`.
2. Scene: the flat AI scene, combat on, player 0 (AI, Normal) with a 1-pop wave threshold and the test's own units.json where
   Heavy Infantry's attack `targets` is `units`. Two Heavy Infantry stand beside player 1's hall (seen). 40 thinks.
3. In play: `Stress/AiWaveStressTests` seed 6 (Malazan v Whirlwind, early waves): player 1's wave sends 278 `Attack`s to one
   raider pushed against the Malazan keep (BUG-0590) and stays active for the rest of the 6,000 ticks.

## Expected
The brief's QA focus: no infinite re-send. A member that is sent `Attack` and is Idle again at the next think without having
fought is given up on after a bounded number of tries (sent home, dropped from the wave, or the wave re-targeted / ended).

## Actual
`wave active True, waves 1; over 40 thinks: 44 Attacks, 2 AttackMoves`. The order rule ("a member Idle within reach of the
target while it is seen gets `Attack`") fires at every think; nothing counts tries. The wave has no other exit while the
target stands and no member dies (no loss, no re-target), so no new wave can start either: in seed 6 the Whirlwind AI's
offence is frozen from tick ~460 to the end of the run.

## Notes
- Per think it is bounded (one command per unit), so the queue never floods. The cost is a wasted command per stuck member per
  think for the rest of the match, and an AI that stops attacking.
- The rule follows the brief's wording, so this is a gap in the wave state machine rather than a departure from the brief.
  Any member that can't hurt the target triggers it: BUG-0590's overlap, a unit whose attack targets units only, a target it
  can't reach. A per-member try count (the `WaveFlag` byte has room) or a wave timeout would bound it.
