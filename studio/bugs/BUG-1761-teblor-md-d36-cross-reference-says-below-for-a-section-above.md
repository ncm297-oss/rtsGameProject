# BUG-1761: teblor.md's answer to D-H10 Question 1 points "below" to D36's section, which sits above it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (D37, verified by QA 2026-11-26-0207) |
| Found | 2026-11-25-1437, task D36 (QA) |
| System | docs: `docs/factions/teblor.md` |
| Fixed by | D37 (2026-11-26-0207): "below" -> "above" in D-H10 Question 1's Producer's answer; QA checked every "<section>" above / below reference on the page against the heading order. |

## Repro
1. Open `docs/factions/teblor.md`, "Questions for the Producer (D-H10, 2026-11-25)", Question 1's "Producer's answer"
   (line 806 on D36's head baff7d9): "D36's result is in "The unit mix, second search" below."
2. That section ("## The unit mix, second search (BUG-1612, BUG-1721, D36, 2026-11-25)") is at line 692, above the
   question, followed by "Questions for the Producer (D36, 2026-11-25)" and then D-H10's questions.

## Expected
"above" (or no direction), as the correction at line 609 correctly says "below" from its own position.

## Actual
"below": a reader following it scrolls into the D33 questions.

## Notes
Text only; no pin reads the word. Fix in the data track's next teblor.md edit (D37).
