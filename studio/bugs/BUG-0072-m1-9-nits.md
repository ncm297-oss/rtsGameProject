# BUG-0072: M1-9 nits: an invalid --record file name still fails only after the run

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-06-0905, task M1-9 |
| System | tools/Rts.Cli, docs/03 "Headless CLI" |
| Fixed by | |

## Repro
```
dotnet tools/Rts.Cli/bin/Debug/net8.0/Rts.Cli.dll run --seed 1 --units 1 --ticks 5 --record "<tmp>/a<b>.replay"
dotnet tools/Rts.Cli/bin/Debug/net8.0/Rts.Cli.dll run --seed 1 --units 1 --ticks 5 --record "C:/Windows/System32/x.replay"
```

## Expected
docs/03 "Headless CLI" (M1-9, BUG-0057 item 2): "The `--record` path is checked before the first tick: a
path that isn't valid, names a directory, or sits in a directory that doesn't exist fails at once".

## Actual
Both run every tick, print the timing line, and only then fail:
```
seed 1 units 1 players 1 ticks 5 checkpoint 100
ticks 5 avg 1.120 ms p99 2.793 ms worst 2.793 ms
error: cannot write replay '.../a<b>.replay': The filename, directory name, or volume label syntax is incorrect. : '...\a<b>.replay'
```
`Path.GetFullPath` accepts `<` / `>` on .NET 8 (Windows), so `RecordPathError` passes an invalid file
name. Access denied (second command) is not claimed by the docs, but has the same cost. Missing folder,
a directory, and an empty path are caught up front (checked by hand: 0.05 s for `--ticks 1728000`).

## Notes
- Opening the file for writing before ticking (`File.Create` then delete, or keep the stream) would catch
  every case at once; or check `Path.GetInvalidFileNameChars()` and fix the docs' wording.
- Exit code and message are right either way; it only wastes the run (BUG-0057's original complaint).
