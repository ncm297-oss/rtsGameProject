# BUG-1862: `SaveHeader.TryRead`'s doc comment still says only format 10 reaches it (format 11 since M6-S14)

| Field | Value |
| --- | --- |
| Severity | S4 (a stale `///` comment; no behavior change) |
| Status | fixed |
| Found | 2026-11-26-1847, task M6-S14 (QA re-check, fix round 1) |
| System | save format (`sim/Rts.Sim/Save/SaveHeader.cs`) |
| Fixed by | M6-SH3 D (the summary names `SaveFormat.CurrentVersion`) |

## Repro
1. Read `sim/Rts.Sim/Save/SaveHeader.cs`, the `///` summary on `TryRead` (around line 136):
   "M7-1: only <paramref name="version"/> 10 reaches here, whose map id is always there".
2. Read `SaveFormat.CurrentVersion` (`sim/Rts.Sim/Save/SaveFormat.cs`): 11 since M6-S14, and every older format is refused
   `FormatVersionMismatch` before `TryRead`.

## Expected
The comment names the version that actually reaches `TryRead` (`SaveFormat.CurrentVersion`, 11 since M6-S14). M6-S14
rewrote `SaveFormat`'s own summary for the bump, so the header reader's comment should have moved with it.

## Actual
It says 10, which no build since M6-S14 lets through. The `version` parameter is now always 11 there.

## Notes
Found while checking that BUG-1859's relabelled fixtures reach the data-hash check (they do: magic, version, length and
checksum come first, then `dataHash != data.ContentHash()` returns `DataMismatch`). A one-line fix, best with the next
sim change that touches the save format. Better: refer to `SaveFormat.CurrentVersion` rather than a number.
