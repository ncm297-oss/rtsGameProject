# BUG-1059: The golden's recording-time header is pinned as a literal in a QA test, so every deliberate regen must edit it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-05-2148, task M6-SH1 (Producer, at ACCEPT) |
| System | sim/Rts.Sim.Tests/QA/GoldenDecouplingQaTests.cs (`TheCheckedInHeader_IsTheRecordingTimeHash_NotTheShippedOne`) |
| Fixed by | |

## Repro
1. Read the test: `Assert.Equal(0x30C51BB619A7E650UL, r!.DataHash)` on `ReplayTestRun.GoldenPath`'s header, then
   `Assert.NotEqual(r.DataHash, TestSim.Data.ContentHash())`.
2. Regenerate the golden for a real `k`-line move (`RTS_REGEN_GOLDEN=1`): the header becomes the new shipped hash, the first
   assertion fails, and the second fails too until the shipped hash moves again.

## Expected
M6-SH1's point (docs/03 "Testing strategy"): the golden's `data-hash` line is informational; a regen for a `k`-line move
touches the replay file and nothing else. No test should hold the header's value as a literal.

## Actual
The literal re-couples a regen to a test edit, and the `NotEqual` half makes the test red for the one session in which the
golden was just regenerated (the shipped hash equals the header then). At the 2026-11-05-2148 merge it also forced the
Producer to drop the data track's (harmless, unnecessary) regen: with `742D3E95CF629116` in the header `main` would have been
red.

## Notes
- Fix (next sim hardening, a few lines): keep only what the test is for, "the file plays decoupled and is refused as recorded"
  (already `AJunkDataHashLine_*` and `ReplayGoldenTests.TheGoldenAsRecorded_*`); drop the literal row, or make it "the header
  parses as a 16-hex-digit hash" without naming the value.
- Until then: a deliberate regen moves this literal in the same commit (say so in the commit body, as the old
  `SameGameDataHashes` rule did).
