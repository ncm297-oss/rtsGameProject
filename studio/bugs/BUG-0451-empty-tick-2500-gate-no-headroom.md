# BUG-0451: `EmptyTick_WithManyUnits_IsCheap(2500)` sits on its 500 µs gate; the base commit itself fails it in paired runs

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-1314, task M4-4b-3b (pre-existing; not caused by this change) |
| System | perf tests (`Stress/SimCoreStressTests.EmptyTick_WithManyUnits_IsCheap`) |
| Fixed by | |

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
