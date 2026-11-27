# BUG-1861: After M6-S14 an AI seat casts onto an ally's building (Vathar Crossing 2v2 seed 6, tick 25,280): a Stress row red and unreported

| Field | Value |
| --- | --- |
| Severity | S2 (a Stress row red on the branch, green on the base, not in the developer's report; an AI team rule broken) |
| Status | open |
| Found | 2026-11-26-1847, task M6-S14 (QA, tier full: the whole Stress category on a fresh clone) |
| System | AI (team play: a cast's point; the hunt / wave / defense together), `Stress/M5H5HuntAfterDefeatQaTests` |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~M5H5HuntAfterDefeatQaTests.FourNormalSeats_To72000"`
   (24 rows, about 15 minutes).
2. Head 0058e09d: 23 / 24, red row `(map: "vathar_crossing", seed: 6, teams: True)`:
   `tick 25280: seat 2 UseAbility onto an ally's building at <182.49141, 188.12477> (wave True, hunting True, defending True)`.
   Base f40cb16e (same command, same machine, Release): 24 / 24 green.
3. The run's state: `over False at 21:04 (25281); first defeat 19:32`; p2 `cadre_rush`, 75 units, army 52, hunting; p0 (its ally)
   `standard`, 25 units; p1 out at 19:32.

## Expected
docs/02 "AI opponent" (team matches) and the M5-H5 / M6-S5 oracles (`TeamAiSoakStressTests`, `AiTeamTests`): an AI never sends
an AttackMove or a cast onto an ally's building. The one allowance is BUG-1149's (the defense's AttackMove to an enemy hitting
an ally's building), which the oracle counts only when the seat defends and is not also hunting in a wave.

## Actual
Seat 2's AI sends a positional `UseAbility` whose point is within 0.5 m of a building of its ally (p0) while its wave is a
hunt and it also defends. The row stops at the first such command.

## Notes
- Not in the brief's pin list nor the developer's re-staged runs: the class is in the Producer's rule-74 grep, the developer's
  rest-of-Stress filter (`m6s14-logs/rest-stress.filter`) did not include it, and its `F2-stress.log` (an earlier tree,
  truncated) never reached this row. docs/03 M6-S14's "every other class in the grep: run ... and green with no edit" does
  not hold for it.
- Probably a latent targeting gap that M6-S14's changed play reaches (the hunt's resume moves where the army stands after a
  find; `AbilityManager`'s cast point is an enemy cluster, which can stand at an ally's building), not a new rule's own output.
  Trace: which ability, whose units at the point, and whether the cast would hurt the ally's building.
- Cure: the cast point check of `AbilityManager` (or the shared "not onto an ally's building" guard the AttackMove has) applied
  to positional casts; or, if the cast is judged harmless, a Producer ruling and a named allowance in the oracle. Rule 45 does not
  apply (not a criterion row); a `Skip` with this id needs a ruling.
