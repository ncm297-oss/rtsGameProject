# BUG-0915: `BuildingSightLoaderTests.ABadBuildingSightRule_IsAnErrorAtTheField(raw: "0")` failed once in a full non-Perf run, green alone

| Field | Value |
| --- | --- |
| Severity | S4 (flake; S3 if it fails a second full run) |
| Status | fixed |
| Found | 2026-10-17-1624, the Producer's full non-Perf run on the merged tree (sim c91926f + D-R31 ba56348) at ACCEPT |
| System | tests: `Data/BuildingSightLoaderTests` over `TestDataDir.CopyOfShipped()` (a per-test copy of `game/data` under `%TEMP%\rts-data-tests\<guid>`) |
| Fixed by | M5-H4, session 2026-10-26-2108 |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter Category!=Perf` on the merged 1624 tree: 6,714 passed / **1 failed** / 13 skipped of 6,728 in
   32 m 29 s; the one red row is this theory's `raw: "0"` case (240 ms). The view track's scene loop ran on the same disk for part of the run.
2. `dotnet test sim/Rts.Sim.Tests --no-build --filter "FullyQualifiedName~BuildingSightLoaderTests"` right after: 19 / 19 green.
3. The failure message was not captured (the Producer's run grepped the summary lines only).

## Expected
A loader test over its own temp copy is deterministic. The row asserts `!r.Ok` and an error at `common/rules.json` / `buildingSight`
after `EditJson` sets the field to `0`.

## Actual
One failure in one full run; two other full runs of the same tree (the data dev's, QA's on the sim branch) and the rerun alone are
green. No diff this session touches the loader, `rules.json`, `TestDataDir` or `EditJson`.

## Notes
- Likely a transient I/O fault in the copy or the edit under disk contention (an indexer or antivirus holding a just-written file; the
  temp root `rts-data-tests` is shared, each copy's folder is a GUID so not a path collision). Candidates for the fix: `EditJson` /
  `CopyOfShipped` retry once on `IOException`, or assert the edit took (`r.Errors` non-empty before the `Contains`) so the message says
  which step failed.
- Next sim hardening: make the failure message carry `r.Ok`, `r.Errors` and the temp path; if it fails again in a full run, S3 and the
  sim's first item.

## Fix (M5-H4, session 2026-10-26-2108)
- Found a cause that fits "green alone, red once in a full run": `TestDataDir.Dispose` caught only `IOException`, but on
  Windows `Directory.Delete` throws `UnauthorizedAccessException` for a file it may not delete (read-only, or held a moment
  by an indexer or virus scanner after it was written), so the `using` round a passing load failed the row. Dispose now
  catches both, retries once after 50 ms and then leaves the temp folder; the copy and `EditJson` retry a file step once
  on either exception. The row's assertion message now carries `r.Ok` and every error.
- Regression test `TestDataDirTests.Dispose_AFileItMayNotDelete_LeavesTheFolderBehind_WithoutThrowing` (threw
  `UnauthorizedAccessException` before the fix) and `Dispose_RemovesTheCopy`.
- Proof: 10 / 10 green runs of a 395-test batch (every `Data/` loader test, `QA/DataLoaderQaTests`, `QA/AiLoaderQaTests`,
  `TestDataDirTests`, the H3 probe: the copies of `game/data` in parallel) with the 27-run pairing stress test running beside
  it as load; and the full non-Perf suite on the M5-H4 tree (7,443 passed, the 5 failures BUG-1027's data-owned rows).
  Ten full-suite runs (27 min each) were not done.
