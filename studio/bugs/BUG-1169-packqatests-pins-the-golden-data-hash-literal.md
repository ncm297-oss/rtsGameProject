# BUG-1169: QA's `PackQaTests.TheShippedDataHash_IsTheGoldenHeaders` pins the golden's data hash as a literal, so the D27 merge turns it red on `main`

| Field | Value |
| --- | --- |
| Severity | S2 (a default-loop row red on the merged `main`; green on the sim branch alone) |
| Status | fixed (the Producer's patch at the 2026-11-10-0146 merge: the row compares the shipped `ContentHash()` with the golden header dynamically, no literal; verified on a scratch merge of sim + view + data, see the session log) |
| Found | 2026-11-10-0146, the Producer's ACCEPT (the merged-tree run of the sim + data branches) |
| System | Tests: `sim/Rts.Sim.Tests/QA/PackQaTests.cs` (QA's M7-2 suite) |
| Fixed by | the merge commit of session 2026-11-10-0146 (patch `scratchpad/accept/BUG-1169-packqatests-hash.patch`, applied by the conductor) |

## Repro
1. Merge `studio/2026-11-10-0146-sim` and `studio/2026-11-10-0146-data` (any order) on 1eb231d.
2. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~PackQaTests.TheShippedDataHash"`: 1 / 1 failed
   (`Assert.Contains("data-hash 64DF327980743173", golden)`: the golden now reads `3E33BCE5D418EC9B`).

## Expected
The golden's header `data-hash` is the hash at recording time, not a pin (M6-SH1's decision), so a test that ties the shipped
data to the golden compares the two values, never a literal. M7-2's own rule ("the header's data hash is unchanged by the
sim this session") was a session-time check for QA's report, not a permanent pin: the brief said D27 would move it.

## Actual
The row asserts both the golden text and `TestSim.Data.ContentHash()` equal the literal `64DF327980743173`. Alone on the sim
branch it passes; after D27's regeneration (`cross_map_seed1.replay` header `3E33BCE5D418EC9B`) it fails. The sim-side
`GatherWedgeQaTests.SameGameDataHashes` already makes the dynamic comparison.

## Notes
- Rule for QA (added to the handoff as rule 49): no literal of the golden's `data-hash` in any test; compare with the header.
- Fix (2 lines): `Assert.Contains("data-hash " + TestSim.Data.ContentHash().ToString("X16"), golden);`.
