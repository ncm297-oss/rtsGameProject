# BUG-0302: M4-4a nits: stale BUG-0280 index row, under-itemized memory re-baseline, perf row double-runs the phases, fractional DoT first pulse

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | partly fixed (d by M4-4a fix round 1: pulse clock now starts at the apply, docs/03 states the trailing-fraction rule; a by QA; b, c open) |
| Found | 2026-10-09-0724, task M4-4a |
| System | studio records, QA memory bound, `AbilityPerfTests`, statuses |
| Fixed by | |

## Repro / Actual
a. `studio/bugs/README.md` index still lists BUG-0280 as "open (sim track)" while its file says "fixed (M4-4a)". (QA has
   updated the row in this session after verifying the fix; listed so the Producer knows it was changed by QA.)
b. `QA/FieldBuildFairnessQaTests.World_1024Map_CacheStays32_MemoryBounded`: the bound moved 230,765,000 -> 231,731,000. The
   comment itemizes 671,744 (per-slot ability state) + 229,376 (event list) = 901,120 bytes "plus array headers", but the
   measured growth is 966,040: about 65 KB is not explained (eleven new arrays' headers are a few hundred bytes). Not a
   regression of note (0.03 % of the world), but the "explain every loosening" rule wants the last 65 KB named (first-use
   type loading inside the probe, or a structure not listed).
c. `Abilities/AbilityPerfTests` times `StatusSystem.Run` + `AbilitySystem.Run` by calling them a second time after each
   `sim.Tick()`, so in the measured window every status ages two ticks a tick and casts count down twice. The load it
   measures is therefore not the one it describes (it still holds; QA's `Stress/AbilityScalePerfTests` does the same for
   comparability). A stopwatch around the real phases (or a seam in `Simulation.Tick`) would measure the actual tick.
d. A `damageOverTime` effect whose `duration` is not a whole number of seconds pulses first after the fraction (2.5 s:
   first pulse 10 ticks after the apply), not "a second after the apply" as docs/03 "Implementation (M4-4a)" says. Shipped
   Telas Fire (4 s) is unaffected; the loader accepts any duration.

## Expected
a. Index matches the file. b. Every byte of a loosened bound accounted for. c. The perf row measures one tick's phases.
d. Either the loader requires whole seconds for a DoT duration, or docs/03 states the rule as it is.

## Notes
d shares its cause with BUG-0301 (the pulse clock is the remaining count).

## QA re-check (2026-10-09-0724, round 1)
(d) fixed: the pulse clock starts at the apply and docs/03 states the trailing-fraction rule (pinned by `QA/StatusPulseClockQaTests`). (b) still open for the original ~65 KB; the round-1 raise (+131,072 itemized, +130,848 measured) is fully explained. (c) open.
