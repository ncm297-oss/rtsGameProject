# BUG-1136: D-R33 nits (stale test name, field outside the method, a stray line break on the Malazan page)

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-11-07-0236, task D-R33 (QA light) |
| System | content tests / docs: `sim/Rts.Sim.Tests/Content/MapTextContentTests.cs`, `docs/factions/malazan.md` |
| Fixed by | |

## Repro
1. `git diff 052c39e...2500f4d -- sim/Rts.Sim.Tests/Content/MapTextContentTests.cs docs/factions/malazan.md`

## Expected
Names and text say what the code does; the brief's "touch only that method".

## Actual
1. `ThePin_NamesEveryShippedMap_RarakuFirst` no longer requires the pin to name every shipped map (it prints them), so its name now
   states the old, opposite rule. The dev kept it on purpose (bug files cite it); a rename to e.g. `ThePin_PutsRarakuFirst_AndPrintsUnpinnedMaps`
   could land with the next data task that touches this file.
2. The `_out` field and constructor were added just above the method, outside it (the brief said "touch only that method"). Needed for the
   printout, harmless, `Pinned` byte-identical; noted for the conductor's merge with the sim rider that edits the same file.
3. `docs/factions/malazan.md` ~l.553: the edit leaves a short line "Before M5-4g seed 19 read a peak of 8 pop and 6 pop / cap 16 with"
   (re-wrap only; renders the same). The brief said "the paragraph's last sentence"; the dev replaced the allowance sentences mid-paragraph,
   which is clearly the intent.

## Notes
The `DH7MapTextQaTests` decoy-row comment that described the old behavior was updated by QA in this session.
