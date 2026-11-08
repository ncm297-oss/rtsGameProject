# BUG-1148: A lost hall is never rebuilt while any enemy unit stands on its old anchor (BUG-1118 item 1's judge reads CanPlace, which ignores units)

| Field | Value |
| --- | --- |
| Severity | S2 |
| Status | open |
| Found | 2026-11-07-2306, task M5-H5 (QA full) |
| System | AI (`BuildOrderExecutor.HallFirst`, the hall rebuild's judge) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~SimHardeningM5H5QaTests.AnEnemyUnitParkedOnTheLostHallsAnchor_TheHallIsRebuiltOffIt"`
   (`sim/Rts.Sim.Tests/QA/SimHardeningM5H5QaTests.cs`, two rows: the parked unit seen by the seat, and hidden).
2. The scene: RebuildTests' flat scene (player 0 a Malazan AI with 2,000 gold / wood, combat off), 10 thinks, the Keep
   razed, one enemy Heavy Infantry placed at the centre of the old footprint and left there, 20 thinks.

## Expected
docs/03 "Implementation (M5-4d slice 2)": the hall's Build is judged as a house Build: "a refused anchor gets a retry mark
and the next search moves on". At `cdbe6ca` (the diff base) both rows pass: 10 Keep Builds, 1 at the old anchor, the anchor
marked bad, a Keep site at cell 1798, a few cells off.

## Actual
At `cd211f2` both rows fail: **20 Keep Builds after the raze, all 20 at the occupied old anchor; no site; the anchor never
marked bad.** Every Build is refused at apply (`PlacementError.UnitInTheWay`), but the new M5-H5 judge skips the retry mark
whenever `PlayerView.CanPlace` still accepts a hall at `LostHallCell`, and `PlayerView.CanPlace` by its own summary does not
check units in the way or the never-seal rule ("Not checked: units in the way and the never-seal rule (the Build applies
them)"). So the next think sends the same refused Build again, for as long as the unit stays: one enemy unit parked on the
ruins (a human can do it deliberately; a wave that razed the hall often idles there) keeps the AI from ever rebuilding its
hall. The same loop follows from a never-seal refusal at the old anchor.

```
hidden False: 20 Keep Builds after the raze, 20 at the old anchor; site none; anchor bad False; parked alive True
hidden True: 20 Keep Builds after the raze, 20 at the old anchor; site none; anchor bad False; parked alive True
```

## Notes
- Cause: `BuildOrderExecutor.HallFirst`, the M5-H5 condition
  `!(pending.TypeId == ai.TownHallType && pending.Cell == ai.LostHallCell && ai.View.CanPlace(pending.TypeId, pending.Cell, out _))`.
  The brief's direction for BUG-1118 item 1 was narrower: judge the hall Build against "a Keep stood at the anchor since it
  was sent" (the site appeared, then was razed). The developer's report calls `CanPlace` a "simpler check"; it also
  excuses every refusal `CanPlace` cannot see.
- docs/03 (slice 2, the BUG-1118 sentence) says the skip applies when "the Build was not refused for its ground, e.g. its
  site was razed before the judge"; a unit in the way is a refusal the ground check cannot tell apart.
- The developer's regression row `RebuildTests.AHallSite_RazedBeforeItsBuildIsJudged_IsRebuiltAtTheSameAnchor` stays valid;
  the fix must keep both it and the two QA rows green.
