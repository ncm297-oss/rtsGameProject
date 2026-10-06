# BUG-0040: Replay header has no tick-count limit; phase-14 checkpoints can't match commands enqueued during a tick

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (part 1 fixed) |
| Found | 2026-10-05-1446, task M1-6 |
| System | replays (`Rts.Sim.Replays`) |
| Fixed by | Part 1: M1-4d-3 (0a71412): `QA/ReplayQaTests.AbsurdTickCount_IsRefusedAtRead` (un-skipped, passes), `ReplayFormatTests.TickCountAndInterval_HaveAFormatLimit` |

Two small replay findings, grouped.

## 1. No format limit on `ticks` / `checkpoint-interval`

### Repro
`QA/ReplayQaTests.AbsurdTickCount_IsRefusedAtRead` (skipped under this bug): a valid replay edited to
`ticks 2147483647`, `checkpoint-interval 2147483647`, one checkpoint `k 2147483647 ...`, resealed.

### Expected
Players (16) and capacities (1,000,000) have format limits "that bound what a file can make the
reader allocate"; the tick count should have one too (e.g. 24 h = 1,728,000 ticks), so a 1 KB file
can't declare an unbounded playback.

### Actual
`TryRead` returns `None`; `ReplayPlayer.Run` would then tick an empty sim 2^31 times (hours) before
returning. No crash or allocation blow-up (the checkpoint buffer is `ticks / interval + 1` = 2).

## 2. Latent (M5): commands enqueued during a tick vs the phase-14 checkpoint

`Simulation.Enqueue` documents that the AI may enqueue *during* a tick, and `StateHash()` includes
pending commands. The recorder hashes at phase 14, inside the tick, so a recording would include
in-tick commands in the checkpoint hash, while `ReplayPlayer` feeds every logged command between
ticks, after the checkpoint. Playback would then mismatch at the first checkpoint that follows an
in-tick enqueue. (Also open: if the AI runs inside the sim during playback, its commands would be
both re-generated and fed from the log.) Not reproducible today: nothing enqueues during a tick yet.
Settle how AI commands are recorded before M5.

## Notes
Neither affects M1 criteria. Found by QA reading `Replay.Validate` and `Simulation.Tick`.

## Update (QA verified at 2026-10-05-1609 (M1-4d-3, commit 0a71412)): part 1 fixed
`ticks` and `checkpoint-interval` are capped at `Replay.MaxTickCount` = 1,728,000 (24 h). Deviation
from the brief (interval not capped at the tick count) is legitimate: the recorder writes replays
shorter than one interval. A recording longer than 24 h is written but refused on read (BUG-0047 item 4).
Part 2 (in-tick AI enqueue) stays open for M5.
