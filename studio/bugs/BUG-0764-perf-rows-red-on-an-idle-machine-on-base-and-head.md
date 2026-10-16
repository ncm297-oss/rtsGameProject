# BUG-0764: 17-20 `Category=Perf` rows are red on an idle machine on `main`'s base and on every branch alike: the gates no longer measure this PC

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-16-0847 (filed by the Producer at the ACCEPT from the three QA reports) |
| System | perf gates (`sim/Rts.Sim.Tests`, `[Trait("Category","Perf")]` rows: `Brawl500v500`, `EmptyTick 2,500`, `OneTreeFalls`, `TightBlob2500`, `VisionGate`, the PlayerView enumerate rows, the view's minimap rows, MapGen rows) |
| Fixed by | |

## Repro
1. On an idle machine (no other `dotnet` / Godot process): `dotnet test sim/Rts.Sim.Tests --filter "Category=Perf"`.
2. Do the same on a `git archive` of 8c39c08 (the session's base).

## Expected
Every Perf row green alone on the dev machine (docs/03 "Perf", CLAUDE.md: the benchmarks guard tick cost).

## Actual
2026-10-16-0847: the sim QA (quiet machine, 10 % CPU) head 161 / 175, 14 failed; base 157 / 174, 17 failed; every head failure
also fails on base at the same magnitude (Brawl500v500 5.24 v 5.40 ms, EmptyTick 0.918 v 0.898 ms, minimap 0.274 v 0.267 ms).
The data QA, idle machine, no other process: the identical 18-row set red on head and on a fresh archive of base. The view QA's
interleaved A/B on 4 rows: equal within 2 %. The rows that fail include ones with no AI seat and no data (EmptyTick, DotRefresh,
MapGen). So the machine is slower than the thresholds were calibrated for, on every tree; no regression is shown, and none can be
caught by the gates while they are red for everyone.

## Notes
- Not a regression of any task this session (A/B equal three ways). Filed so the next sim hardening re-baselines rather than
  every session saying "environmental".
- Direction: (1) the owner runs the Perf category once after a fresh boot and reports (STATE "Waiting on you", wanted); (2) if
  still red, re-baseline the failing rows with the measured numbers and the date in each row's comment (the itemised-margin
  policy of `FieldBuildFairnessQaTests`), or gate them relative to a calibration row measured in the same process so a slower
  machine moves every threshold together; (3) check Windows power plan / thermal state (the PC "is slow under three suites" since
  2026-10-15, STATE).
- Standing rule: a Perf failure counts once it fails alone; this bug is about rows that fail alone on base too.
