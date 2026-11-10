# BUG-1164: M5-H6 criterion 4: six of the eight pre-M5-9a digests no longer reproduce at strength 0 + knobs 0

| Field | Value |
| --- | --- |
| Severity | S2 (an acceptance criterion not met as written; a brief-level conflict the Producer must rule on) |
| Status | open |
| Found | 2026-11-09-1336, task M5-H6 (developer's finding, QA confirmed) |
| System | AI: `BuildOrderExecutor.ReopenLost` (the house rule), `MilitaryManager.NextSweep` (the hunt's free starts); `CounterCompositionTests.TheNonHardPins_WithNormalStrengthZero_PlayAsBeforeThisTask` |
| Fixed by | |

## Repro
1. `git diff a7345af...8d1a9e3 -- sim/Rts.Sim.Tests/Ai/CounterCompositionTests.cs`: six of the eight `InlineData` digests of
   `TheNonHardPins_WithNormalStrengthZero_PlayAsBeforeThisTask` are replaced (Raraku 1 Normal, Pale Hills 1 Normal / Easy,
   Pale Hills 3 Normal / Easy, Vathar 1 Normal), each with the pre-M5-9a digest kept in a comment.
2. QA's Stress run on a fresh clone of 8d1a9e3 passes the row with the new digests; the sim is deterministic, so the old
   digests cannot also hold.

## Expected
Brief M5-H6, acceptance criterion 4: "The 8 pre-M5-9a digests still reproduce at strength 0 + knobs 0."

## Actual
Six move: Raraku 1 Normal `E0612240C3E713D2 28083 1476` -> `4765275972CD8BD8 28193 1531`; Pale Hills 1 Normal
`69C4358E67DF7FC3 34219 3144` -> `A0884E7E95EEA8AB 36000 3483`; Pale Hills 1 Easy -> `07EC9909C8DDF1C3 36000 2486`; Pale Hills
3 Normal -> `9753F06856D81476 28517 2459`; Pale Hills 3 Easy -> `BC6868F49655A4DE 33508 3033`; Vathar 1 Normal ->
`72BC8BD819A35B0F 23219 1495`. Raraku and Vathar Easy reproduce.

## Notes
- The cause is by design: the house rule and the hunt's free starts change Normal (and Easy) play and have no knob, and
  the brief's out-of-scope list allowed no new knob beyond (b)'s. Shipping lever (iii), which the brief proposes, makes this
  clause unmeetable. The developer reported it as a finding (docs/03 "Implementation (M5-H6)", "Finding (criterion 4's ...)").
- What the row still proves: with the three M5-9a / M5-9b knobs at 0 the rows play the M5-H6 rules alone (it pins them now).
- The Producer's call: waive the clause for this task (the rows then pin "M5-9a / M5-9b off"), or ask for a knob (a
  `rules.json` switch for the house rule and the hunt's starts) so the old digests come back.
