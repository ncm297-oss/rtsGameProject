# BUG-0400: M4-VH2 nits: the hash twin skipped three new ViewApi reads; the BUG-0250 item 3 fix has no test; a reused resource slot can copy an unseen node

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open (item 1 closed by QA's test) |
| Found | 2026-10-10-0624, task M4-VH2 (QA) |
| System | view: `sim/Rts.Sim/ViewApi/SeenResources.cs`, `PropLayout.cs`, `MinimapRaster.cs`; `game/scripts/SelectionController.cs` |
| Fixed by | item 1: QA test `QA/ViewApi/ViewHardening2QaTests.NewViewApiHelpers_VH2_DoNotChangeTheSim_*` (green) |

## Item 1: the hash twin didn't cover every new ViewApi read
Acceptance criterion 5 asks for the hash twin to cover any new helper. The developer extended
`StatusFlashQaTests.NewViewApiHelpers_DoNotChangeTheSim_*` with `PickCaster`'s queue scan, `HasQueuedCast` and
`SentCasts`, but not with `SeenResources.Update` (which reads `FogStore`), the `PropLayout.Refresh` version overload,
or `MinimapRaster.DrawnEnemyDotAt`. QA added `NewViewApiHelpers_VH2_*` (2 seeds x 1,200 ticks, a scout, fells in and out
of sight, every helper called every tick, `--no-fog` mode on odd ticks): the hash equals a bare twin's every tick. Closed
by that test. Noted so the next view brief lists every new ViewApi type in the twin.

## Item 2: the BUG-0250 item 3 fix has no regression test
`SelectionController.ForgetIfNewMatch` clears the double-Cancel guard and the sent-cast memory when the `Simulation`
instance changes. CLAUDE.md asks for a regression test with every bug fix. No game path starts a second `Simulation`
under one `SelectionController` today, so it can't fail in play. A scene row (start a match, Cancel at tick T, restart
the runner with a new `Simulation`, Cancel the same slot at tick T) would pin it once a "restart match" exists.

## Item 3: a reused resource slot can take an unseen node
`SeenResources.Update` takes a pending slot's change when *either* footprint is visible: the remembered node's or the
store's new one. If a slot felled out of sight is reused by a new node elsewhere, also out of sight, seeing the old
footprint copies the new node, which the player has not seen. Not reachable now: resource nodes spawn only at map setup
(`ResourcePlacer`), never mid-match. If regrowth or spawned nodes come later, take the removal when the old footprint is
seen and the new node only when its own footprint is.

## Expected
Every new ViewApi read is in a hash twin; every fix has a test; the last-seen copy shows only what was seen.

## Notes
None of these is reachable in normal play or affects the sim.
