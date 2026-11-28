# BUG-1932: A hunt follows raid sightings at its own base, so a side raided every few minutes never finishes a sweep leg

| Field | Value |
| --- | --- |
| Severity | S3 (the original BUG-1855 loop; the traced match now ends through M6-S15 C) |
| Status | open (triaged at the 2026-11-27-1857 ACCEPT; Producer decision in docs/01: a hunt does not chase a sighting the defense is answering; the sim's next AI task, a pin move) |
| Found | 2026-11-27-1857, task M6-S15 (D's measurement; filed from the developer's patch, confirmed by QA) |
| System | AI hunt (`MilitaryManager.Hunt`, `Fresh`) |
| Fixed by | |

## Repro
`Stress/PaleHillsStallTraceTests` on d26338d3 or on d26338d3 + D: seat 0's hunt (hunts 2, never recalled) re-targets
each raid's sighting at its own base and, when that is forgotten, restarts the leg to sweep point 200 (about 240 m
east) from home: 14 times between 49,701 and 70,341.

## Expected
The defense answers the raid; the hunt keeps its leg (or resumes it) instead of walking home after every raid.

## Notes
- M6-S15 D's rule (a hunt the defense recalls is resumed) does not touch this match: the hunt is never recalled.
- QA's note on D: the cut mark is measured from the latest defense's end, so on a base raided often a later hunt can
  keep the old marks longer than `ai.huntMemory` after the cut; harmless (marks only grow), to be worded or tightened
  with this bug.
- docs/03 "Implementation (M6-S15)" D and question 1.
