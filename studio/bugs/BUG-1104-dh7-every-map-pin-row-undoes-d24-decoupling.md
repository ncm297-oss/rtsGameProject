# BUG-1104: D-H7's `ThePin_NamesEveryShippedMap_RarakuFirst` turns red whenever the sim adds a map, undoing D24's id-based decoupling

| Field | Value |
| --- | --- |
| Severity | S3 (test-only; no shipped behavior is wrong, but it reopens the cross-track merge hazard BUG-1103 was about) |
| Status | fixed |
| Found | 2026-11-06-0926, task D-H7 (QA light) |
| System | `sim/Rts.Sim.Tests/Content/MapTextContentTests.cs` (data track's content tests) |
| Fixed by | D-R33 (2500f4d, 2026-11-07-0236): set equality dropped, unpinned maps printed; Raraku-first assert kept. QA-verified (DR33QaTests). The row is named `ThePin_HasRarakuFirst_AndPrintsUnpinnedMaps` since D-R34 (BUG-1136) |

## Repro
1. Copy the shipped data dir and add a fourth map file (`aa_decoy`, clean text), as the sim's next map task would.
2. `QA/Content/DH7MapTextQaTests.AShippedButUnpinnedDecoy_PassesThePin_ButNotTheEveryMapRule` shows it: `PinProblems` is empty
   (D24's rule: an unpinned shipped map is allowed), but the set comparison `ThePin_NamesEveryShippedMap_RarakuFirst` makes
   (`Assert.Equal(shipped ids, pinned ids)`) differs, so on real data that row would fail.

## Expected
The class's own summary (unchanged by D-H7): "a shipped map the pin doesn't name yet still runs the checker, so the sim can add a
map with provisional text and the data task pins it later." BUG-1103's Expected: "Every D24 row green with any number of shipped
maps: the whole point of D24's id-based pin is that the sim can add maps."

## Actual
The new row requires the pin to name every shipped map. A sim task that adds a map (sim owns `game/data/maps/**` and cannot edit
the data track's `Content/` tests) goes red on its own branch or at the conductor's merge, the same hazard as BUG-1103.
`ThePin_AllowsAnUnpinnedShippedMap_...` (also in this file) asserts the opposite policy, so the file now holds two contradictory rules.

## Fix (suggestion, the Producer's call)
Drop the set-equality half and keep `Assert.Equal("raraku", Pinned[0].Id)` (the only thing the rule test needs), or turn the
"every shipped map is pinned" check into a printed reminder rather than an assert.

## Notes
- Found with the decoy row in `sim/Rts.Sim.Tests/QA/Content/DH7MapTextQaTests.cs`.
