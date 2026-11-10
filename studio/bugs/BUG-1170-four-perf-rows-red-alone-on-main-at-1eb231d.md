# BUG-1170: Four wall-clock Perf rows fail alone (rule 42) on `main` at 1eb231d on this PC, before any M7-2 change

| Field | Value |
| --- | --- |
| Severity | S3 (Perf gates red on `main` on the studio's own machine; no gameplay effect) |
| Status | open (measure first: rerun the four alone after a cold idle at the start of the next sim session; a re-bound only with the regression explained, CLAUDE.md "Testing rules") |
| Found | 2026-11-10-0146, task M7-2 (QA's Perf run on a clean clone of 1eb231d, twice, and on the branch) |
| System | Perf tests: `Ai/AiPerfTests`, `Vision/VisionGatePerfTests`, `QA/PassiveScaleQaTests`, `QA/EndGameH6Round2QaTests` |
| Fixed by | |

## Repro
1. Clean clone of 1eb231d (or of the merged `main` after this session), no other testhost or Godot running, 30 s idle.
2. `dotnet test sim/Rts.Sim.Tests --filter "Category=Perf"`: 4 rows fail (QA, two runs at the base and one at the branch head):
   - `AiPerfTests.EightHardSeats_MidWave_*` 1.056 ms (base 1.126 / 1.040 ms; bound 1 ms; last green avg 0.525 ms, p95 0.863 at M5-H6),
   - `VisionGatePerfTests.Brawl250v250_*` 5.26 ms (base 4.81 / 5.22 ms),
   - `PassiveScaleQaTests.EmptyTick_2500OneOwner_*(zealot, True)` 532 us (base 504 us on run 2),
   - `EndGameH6Round2QaTests.ASweepRetarget_*` 67 us (base 55.8 / 69.1 us; bound 50; measured 16.4 us at M5-H6, 35.2 us worst case).

## Expected
Every Perf row green alone on `main` (rule 42), as at the 2026-11-09-2226 ACCEPT (QA: `AiPerfTests` 6 / 6, `CombatPerfTests` 5 / 5 alone).

## Actual
The same four fail at the diff base and at the M7-2 head, so M7-2 did not cause them; the machine is running slower (the
session ran three tracks' testhosts for hours; the PC's power plan slows it to about 0.6x under a sustained load, BUG-0764, and
the slowdown may outlast the load). The developer measured 203 / 0 Perf earlier in the same session.

## Notes
- Not a code regression as far as the evidence goes: the M7-2 diff adds one `HitPassive < 0` read per direct hit and the
  pack loop only at a spawn; none of the four rows trains packs or bites.
- Next sim session: rerun the four alone first (cold); if green, close as a machine state with the numbers; if red, profile
  before touching a bound (the sweep re-target at 67 us against 50 is the most suspicious: it had 35.2 us of headroom).
