# BUG-1164: M5-H6 criterion 4: six of the eight pre-M5-9a digests no longer reproduce at strength 0 + knobs 0

| Field | Value |
| --- | --- |
| Severity | S2 → closed (the Producer at the 2026-11-09-1336 ACCEPT: a brief conflict, not a defect) |
| Status | closed (Producer ruling 2026-11-09-1336: the clause is waived. The brief forbade a new knob and the two shipped rules are bug fixes (BUG-1116 / BUG-1157), which get no off-switch; the eight rows now pin "M5-9a / M5-9b off, M5-H6 on" with the pre-M5-9a digests kept in the comments, which is what the rows are for: proof that the three knobs turn their levers off) |
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

## M5-H6 fix round 1
Measured the one in-brief switch there is: gating both rules (the house rule and the hunt's starts) on
`ai.farExpandReach` > 0, the far expansion's knob that the digest rows already set to 0. All six old digests came back
exactly (69C4358E67DF7FC3 34219 3144, 62F16C733AF6D9C8 36000 2601, C9CB142CF3C97C88 29562 3103, 4FF13AE84024D0BB
33524 3075, E0612240C3E713D2 28083 1476, 4FA98DA390936D9B 22794 1395), but
`FarExpandTests.AWholeMatch_WithNoSeatArmed_SendsTheSameCommands_AtKnobZero` (M5-9b: knob 0 changes nothing but the far
expansion) failed on Vathar seed 1 (end 23,322 shipped against 23,043 at knob 0: the house rule). Raraku has two
starts, so its 1v1 never uses the hunt's starts: the Raraku move is the house rule's alone, which needs its own switch.
The gate was reverted; meeting the clause needs a new knob, which the brief rules out. Left for the Producer.
