# BUG-0226: M4-V3 round-2 nits: the corpse disc's 0.15 m slack lets it up a ramp beside the side wall's foot (~0.5 m); seed-6 own-Billet row lost; the Tent row never has the box behind

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1-5) |
| Found | 2026-10-08-1814, task M4-V3 (QA re-check of fix round 2, bb5451d) |
| System | view: `sim/Rts.Sim/ViewApi/TerrainHeight.cs` (`MaxUnder` / `Joined`, `SeamSlack`); view test scenes `game/tests/AttackOrderViewTest.cs`, `game/tests/QaV6Test.cs` |
| Fixed by | 08dc8e3 (M4-VH1): `TerrainHeight` three step bands (straddle / capped at the step top + 0.08 m / wall); `MaxUnderEdgeWalkQaTests.MaxUnder_RandomPointsAndRadii_HangsUnderAQuarterMetre` un-skipped; AttackOrderViewTest Billet camera fallback as a `Check`; QaV6Test Tent row picks a pixel with the box behind (`Check`); docs/03 and `AttackStage` wording |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~MaxUnderEdgeWalkQaTests"`. The skipped row
   `MaxUnder_RandomPointsAndRadii_HangsUnderAQuarterMetre` (the BUG-0224 row's own 0.25 m bound, but at 8 random
   points and random radii in [0.48, 1.08] per edge cell instead of a 6 x 6 grid with 3 radii, against a 512-step
   reference) fails on all 7 seeds with Skip removed. Worst hangs: 0.27 m (seed 5) to 0.52 m (seed 77). Sinks: 0.
   - Seed 77, (119.934, 147.871), r 1.05. The centre is on level-0 cell (59,73), 0.07 m west of ramp (60,73). That
     ramp rises north from 0 to 1.2 m, so the shared edge is a side wall 0 to 1.2 m high. The NE rim line crosses the
     wall where it is 0.12 m high (under the 0.15 m slack), and that rim sample on the ramp is 0.523 m up. The disc
     is drawn 0.52 m above its own ground.
   - The steep hand-made one-cell ramp (`Slack_AtASteepRampsFootCorner_...`): 1.67 m. Generated maps have no
     one-cell ramps, so this is a note.
2. `EdgeSteps_OnGeneratedMaps_RampToRampEdgesMeetExactly_SmallStepsAreOnlySideWallFeet` (7 seeds, 17 points per edge)
   finds 0 nonzero steps between two ramp cells and 0 edges offset along their whole length. The only steps of at most
   0.15 m are the feet of ramp side walls. The `SeamSlack` comment ("a few generated seams about 0.1 m high, which a
   disc may straddle") and the fix note's "worst hang 0.10 m, at one of those seams" describe side-wall feet, not
   seams.
3. AttackOrderViewTest's own-Billet right-click row (`if (TryBuildingPixel(_billet, ...))`) ran on seeds 1 **and 6**
   before round 2. I instrumented 8b7d654 in a scratch clone and it printed "OLD own Billet row ran" for both seeds.
   Now it runs on seed 1 only. The skip is silent: no Check fails and nothing is printed for seed 6.
4. QaV6Test's Tent row prints `tent row: Raider 23 before the Tent (box behind: False)` on this branch, on the sim
   merge, and on 8b7d654 (pre-existing). Its header says it checks "the unit in front of the enemy Tent is the target
   where it covers the box". The ray through the Raider's pixel never reaches the Tent box: the Raider stands 0.7 m
   out and the ray meets the ground first. So the row only proves "a lone Raider pixel resolves to the Raider".

## Expected
- docs/03 (BUG-0190 / BUG-0224 sentence): "a unit dying against a cliff or beside a ramp's side keeps its disc on its own
  ground".
- Scene rows that guard a rule should either run or say that they did not.

## Actual
- Item 1: the disc hangs up to about 0.5 m in a narrow strip of low ground beside a ramp's side wall near the ramp's
  foot. That is down from 2.16 m (BUG-0224), and about the size of the designed downhill float of a disc on a ramp.
  The dev's un-skipped 0.25 m row passes only because its sample grid misses the strip.
- Items 3 and 4: Attack-order coverage is a little thinner than the scene text says. Own-building right-clicks are also
  covered at the picker level (`UnitPickerQaTests`, "The own Billet nearer than an enemy unit: no target").

## Notes
- Item 1, possible fix: instead of a flat 0.15 m slack, count a crossing as joined only when the step is about 0
  (1e-3 m). The ramp's foot is still joined through its own low edge. Or bound the rim sample's rise beyond a sub-slack
  crossing. In both cases, correct the `SeamSlack` comment.
- Item 3: print a line (or Check) when the Billet has no clean pixel, or move the camera so it has one.
- Item 4: place the Raider so the ray through its pixel reaches the box (nearer the footprint, or test with
  `BuildingPicker.PickRay` first), or reword the header.
- docs/03 and the `AttackStage` summary say "the Tent 10-16 cells behind the Raiders". The code measures 10-16 cells
  from the centre, which is 7-14 cells behind the Raider lines.

## Verification (QA 2026-10-08-2144, M4-VH1)
- Item 1: the flipped row is green on its 7 seeds. QA's `QA/ViewApi/MaxUnderBandsQaTests` covers 16 seeds the fix was
  not measured on, with points only on ramp cells and their neighbours (3-level maps; the real rim radii 0.48 / 0.84 /
  1.08 plus a random one; 1,980-2,520 points a seed). Result: 0 sinks, worst hang over the 512-step oracle 0.132-0.225 m
  (< 0.25). Structurally a capped sample is at most 0.15 + 0.08 = 0.23 m over the step's low side. The straddle term
  copies the oracle's discretisation, an S4 note in BUG-0250 item 2.
- Item 2 (comment/docs): the `SeamSlack` comment is gone; the new comment and docs/03 say there are no seams.
- Item 3: the scene loop prints `seed 6: right-click on the own Billet: no Attack (camera over the Billet)`. The row now
  runs on both seeds and fails loudly if no pixel is found.
- Item 4: QaV6Test prints `tent row: Raider 23 before the Tent (box behind: True) -> unit`, and a `Check` guards it.
- Docs item: docs/03 and the `AttackStage` summary now say "the Tent's anchor 10-16 cells east of the centre (7-14
  cells behind the Raider lines)".
- No silent skips: the full run has 13 skips, none under `ViewApi`.
