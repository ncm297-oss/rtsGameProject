# BUG-0400: M4-VH2 nits: the hash twin skipped three new ViewApi reads; the BUG-0250 item 3 fix has no test; a reused resource slot can copy an unseen node

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (item 1 by QA test; items 2, 3 in M4-VH3) |
| Found | 2026-10-10-0624, task M4-VH2 (QA) |
| System | view: `sim/Rts.Sim/ViewApi/SeenResources.cs`, `PropLayout.cs`, `MinimapRaster.cs`; `game/scripts/SelectionController.cs` |
| Fixed by | item 1: QA test `QA/ViewApi/ViewHardening2QaTests.NewViewApiHelpers_VH2_DoNotChangeTheSim_*`; item 2: afa2661 `CommandCardTest.RestartMatchCancel` (two sims of one seed under one `SelectionController`; with the reset removed the second press sent none); item 3: afa2661 `SeenResources.Update` takes only the removal when just the old footprint is seen (`SeenResourcesTests.AReusedSlot_SeeingOnlyTheOldFootprint_*`, failed first; QA `ViewHardening3QaTests.ReusedSlots_*`) |

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

## Verification (QA 2026-10-10-2334, M4-VH3)
- Item 2 fixed: `CommandCardTest.RestartMatchCancel` (green in the scene loop) starts two sims in turn under one
  `SelectionController` and cancels the same site (slot, generation, tick) in each; one Cancel each.
- Item 3 fixed: QA `QA/ViewApi/ViewHardening3QaTests.ReusedSlots_TheCopyShowsOnlySeenNodes_*` (4 seeds x 2,500 ticks,
  250 slot reuses at random cells each, a scout walking): every node the copy shows was the store's node in that slot
  when its footprint was visible (or at the first fill); 14-29 removal-only steps per run. The hash twin with slot reuse
  (`SeenResources_WithSlotReuse_*`, 2 seeds x 1,200 ticks, fog on and off) is equal every tick. The reverse case (a seen
  new node drops the old remembered one unseen) is filed as BUG-0521 item 2 (not reachable).
