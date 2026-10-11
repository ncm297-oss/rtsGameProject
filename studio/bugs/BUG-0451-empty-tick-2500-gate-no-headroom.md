# BUG-0451: `EmptyTick_WithManyUnits_IsCheap(2500)` sits on its 500 µs gate; the base commit itself fails it in paired runs

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed (re-baselined: the 2,500 row's gate is 550 us, the Producer's fallback) |
| Found | 2026-10-10-1314, task M4-4b-3b (pre-existing; not caused by this change) |
| System | perf tests (`Stress/SimCoreStressTests.EmptyTick_WithManyUnits_IsCheap`) |
| Fixed by | M4-H3 (sim): `Stress/SimCoreStressTests.EmptyTick_WithManyUnits_IsCheap` budget 0.55 ms for 2,500 units, trend itemised in the row |

## Repro
1. Clone `d980ff0` (the M4-4b-3b base) and the M4-4b-3b head; build both (Debug).
2. Alternate `dotnet test sim/Rts.Sim.Tests --no-build --filter FullyQualifiedName~EmptyTick_WithManyUnits_IsCheap`
   three times each on an otherwise quiet machine.

## Expected
A Perf gate with enough headroom that an unchanged tree passes it repeatably (docs/03 perf thresholds; testing rules:
perf benchmarks guard tick cost).

## Actual
2500 units, avg tick (µs): base 501.7 / 497.6 / 501.3, head 499.7 / 490.9 / 494.3. The gate is `avgMs < 0.5`, so the
**base fails** 2 of 3 runs and the head passes by 0.3-9 µs. In the full Perf run alone the head read 476.1 µs. Previous
sessions: ~481 µs of 500 (handoff), 485-487 µs (the M4-4b-3b dev report).

## Notes
M4-4b-3b adds no cost here (head is not above base in the pairing). The row has been trending up with every per-unit system
added since M4-4a (statuses, passives, now the summon counter test) and is now inside run-to-run noise of its gate, so any
session can see it go red without a regression. Producer's call: re-baseline with an explanation, or buy back
per-tick cost (the counter-gated passes are already ~3 ns each; the remainder is the movement / scan / hash work of 2,500
idle units). QA must not loosen it.

## Producer triage (2026-10-10-1314 ACCEPT)
S3, not blocking (M4-4b-3b's head is not above the base in the pairing). End-of-M4 sim hardening: first try to buy back
per-tick cost in the idle path (the movement / scan / hash work of 2,500 idle units); if nothing cheap is found, re-baseline
the gate to 550 µs with the trend itemised in the row's comment (statuses, passives, summons each added a counter-gated
pass). Until then a failure of this row counts only when rerun alone and above the base in a paired run.

## Measurement and decision (M4-H3, 2026-10-11)
One profiling pass on the measured ticks (a scratch copy of `Simulation.Tick`'s phases with a stopwatch round each, 2,000
ticks after the row's 102 warm-up ticks, Debug; not committed): spatial rebuild 51 us, economy 11, construction 10, order 6,
**target acquisition 305-327 us (75 %)**, movement 15, swings 36, resolve 11, vision 34; summons, passives, statuses, zones,
stealth and abilities 0.5 us together (the counter-gated passes are not the trend). Inside acquisition, `PickTarget` took
244 us a tick for 149 scans that each found no enemy: 2,244 of the scene's 2,500 units spawn off the 256 m map and share the
spatial hash's corner bucket, so each of their scans walks it (filed as **BUG-0510**). No buy-back of 5 % in about 200 lines
that isn't that artifact (the scan path is already owner-coded per bucket and counter-gated), so the Producer's fallback:
the 2,500 row's gate is 550 us (500 and 1,000 keep 500 us), the trend itemised in the row's comment and docs/03 "Testing
strategy". Wall-clock numbers this session were taken with the other two tracks' suites running on the same machine:
`EmptyTick(2500)` alone read 566 / 569 / 620 us on this head and 546 / 571 / 661 us on the base 3584a3a in alternating runs
(head not above base); the scratch profile's own `Tick()` loop read 406-420 us. The full Perf suite alone is in the M4-H3
report.
