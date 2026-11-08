# BUG-1059: The golden's recording-time header is pinned as a literal in a QA test, so every deliberate regen must edit it

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (M5-H5, 2026-11-08) |
| Found | 2026-11-05-2148, task M6-SH1 (Producer, at ACCEPT) |
| System | sim/Rts.Sim.Tests/QA/GoldenDecouplingQaTests.cs (`TheCheckedInHeader_IsTheRecordingTimeHash_NotTheShippedOne`) |
| Fixed by | M5-H5 cd211f2 (fix round 1: 6c3639a); `QA/GoldenDecouplingQaTests.TheCheckedInHeader_ParsesAsADataHash_WhateverItsValue` |

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

## Fix (M5-H5, 2026-11-08)
`TheCheckedInHeader_IsTheRecordingTimeHash_NotTheShippedOne` is replaced by `TheCheckedInHeader_ParsesAsADataHash_WhateverItsValue`
(QA-owned file, changed under the brief): the golden reads, its header is a non-zero hash, and it plays every checkpoint
decoupled. No literal and no inequality with the shipped hash, so a regen touches the replay file alone. Fail-first in
the bug's sense: the old row is red the moment the header is regenerated (its first assertion), which this session did
not do; the golden is byte-for-byte unchanged (header `30C51BB619A7E650`).

