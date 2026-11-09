# BUG-1159: M5-9b far expansion nits: the leg ignores the reach; a start cut short is walked again; a save-refusal doc claim

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-09-0426, task M5-9b (QA inspection) |
| System | AI: the far expansion (`Ai/FarExpansion.cs`), its save fields (`Blackboard.Save.cs`), `SaveRefusalTests` |
| Fixed by | |

## Repro
1. **The leg ignores `ai.farExpandReach`.** `FarExpandQaTests.TheLeg_DoesNotWalkToAStartOutsideTheReach` (skipped with this
   id; remove the `Skip`): with `farExpandReach` 20 m the seat starts its leg at tick 1 toward start 2, about 65 m off, whose mine
   can never be a candidate (`Candidate` filters by the reach, `NextStart` does not). Output: `reach 20 m: armed 0, first
   leg at 1, looked 0`. Shipped 384 m covers every start on the three map files, so this only bites at a smaller reach
   (or a larger map); a value like 0.001 m loads as "on" and walks legs that can never expand.
2. **A start whose leg ended on a candidate is not marked looked.** `EndLeg` clears `FarStart` without setting its
   `FarLooked` bit, so when that mine is spent and the seat arms again, the leg walks back to its own expansion's start
   (the nearest unlooked one) before the next. Code reading (`FarExpansion.Run`: `EndLeg(ai, stop: false)` on a candidate;
   only `Leg` sets the bit, after the fifth look point). One extra walk; harmless otherwise.
3. **A doc claim the code doesn't make.** `SaveRefusalTests.AFormat8Save_WithAForgedFarExpansion_IsMalformed`'s summary says
   a leg worker "with a generation that doesn't match its slot" is `Malformed`; `ReadFarFrom` checks only that the
   generation is above 0 (QA's byte fuzz, `FarExpandQaTests.TheFarBytes_ByteFuzzed_RefuseOrLoadAndRun`, loads such saves;
   they run fine because `OwnUnit` checks the generation at use). Either drop the claim or check it at load.

## Expected
1. The leg walks only to starts within the reach of the hall (the rule's own knob), or the docs say the reach bounds only the
   depot.
2. A start is marked looked once the leg has seen it, however the leg ended.
3. Test comments say what the code checks.

## Actual
As above.

## Notes
None of the three moves a pinned row (shipped reach, two expansions at most per seat in every measured match).
