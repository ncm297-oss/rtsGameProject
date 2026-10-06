# BUG-0057: M1-8 CLI nits: timings include recorder hashing despite docs, unwritable --record found only after the run, 0-checkpoint replays "pass"

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed |
| Found | 2026-10-06-0655, task M1-8 |
| System | tools/Rts.Cli, docs/03 "Debug tooling" |
| Fixed by | 6d1cbfd (M1-9); see the M1-9 re-check below |

Four small findings, grouped. None breaks a criterion.

## 1. docs/03 says hashing is outside the timed region, but with `--record` it isn't
docs/03 "Headless CLI": "a `Stopwatch` around each `Tick()` in the CLI ... hashing and printing are
outside it". With `--record`, `ReplayRecorder.OnTicked` runs inside `Simulation.Tick()` and hashes
the full state at every checkpoint, so those ticks are timed with the hash in them. The developer's
report says this, but the docs don't.

Repro (Debug, 2,500 units, checkpoint every tick, two runs each):
```
run --seed 1 --units 2500 --ticks 600 --checkpoint 1                     avg 5.82 / 5.83 ms
run --seed 1 --units 2500 --ticks 600 --checkpoint 1 --record t.replay   avg 6.99 / 6.60 ms
```
Fix: one sentence in docs/03 ("with `--record`, checkpoint ticks also include the recorder's hash").
**Done by the Producer at the 2026-10-06-0655 ACCEPT** (docs/03 "Headless CLI" now says so). Items
2-4 stay open for the M1 end-of-milestone hardening session.

## 2. An unwritable `--record` path is reported only after the whole run
`run ... --ticks 1728000 --record C:/no/such/dir/x.replay` runs all 1.7 M ticks (about 45 s with one
unit, far longer with an army) and then exits 1 with `error: cannot write replay ...`. Checking that
the directory exists (or opening the file) before ticking would fail fast. Exit code and message are
right; it only wastes time.

## 3. A replay with 0 checkpoints "passes" `play`
`run --seed 1 --units 5 --ticks 50 --record r.replay` (default checkpoint 100) writes a valid replay
with no checkpoints; `play r.replay` prints `ok: 0 checkpoints matched over 50 ticks` and exits 0
although nothing was compared. This is legal per `Replay.Validate` (the recorder writes such files),
so it's a wording issue: say `ok: 0 checkpoints (nothing compared)` or have `run` warn when
`--checkpoint` > `--ticks` with `--record`.

## 4. Cosmetic error text
- `--data ""` or a file path: `error: data in '' did not load (1 errors), first: : : data directory '' does not exist`
  (empty file and path fields give `: : `; "1 errors").
- A broken data dir with several errors shows only the first (by design for the one-line rule, but
  the QA focus asked for the error list; the count is printed, so the user knows there are more).

## Re-check M1-9 (2026-10-06-0905, commit 6d1cbfd): fixed (item 2 has a leftover, BUG-0072)
Item 2: a missing folder, a directory, or an empty path fail before the first tick (`--ticks 1728000`
with a missing folder: 0.05 s). An invalid file name (`a<b>.replay`) or an access-denied folder still
fail only after the run: filed as BUG-0072 (S4). Item 3: `ok: 0 checkpoints (nothing compared) over 50 ticks`.
Item 4: `error: data in '' did not load (1 error), first: data directory '' does not exist`. Regression
tests (`Cli/CliTests`, `DataConversionTests.DataError_ToString_LeavesOutEmptyFields`) fail on base.
