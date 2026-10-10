# BUG-0360: Sandstorm's vision blocker and its statuses disagree on who is inside (cell centre vs unit center)

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | fixed |
| Found | 2026-10-10-0215, task M4-4b-2 |
| System | vision / zones (`FogStore.MarkBlocked`, `VisionSystem.ZoneHides`, `ZoneSystem.Apply`) |
| Fixed by | 5c2de85 (M4-H2): `Vision/ZoneVisionTests.AtTheRim_TheUnitsOwnCentreDecides_ForTheFogAndForCombat`, un-skipped `QA/ZoneQaTests.TheBlockerAndTheStatuses_AgreeOnWhoIsInside` |

## Repro
1. Un-skip `QA/ZoneQaTests.TheBlockerAndTheStatuses_AgreeOnWhoIsInside` and run
   `dotnet test sim/Rts.Sim.Tests --filter "FullyQualifiedName~ZoneQaTests"`.
2. The current behaviour is pinned by `QA/ZoneQaTests.TheRadiusEdge_CurrentBehaviour_StatusesByCenter_BlockerByCell` (passes).

Scene: a flat 48 x 48 map, a Whirlwind Sandstorm (6 m) centred at cell (24, 24) = (49, 49) m, and a Malazan Crossbowman 10 m west.
- A Raider at (+5.1, +1.1) m from the centre is 5.2 m out, inside the radius. It stands in cell (27, 25), whose centre is 6.3 m out.
- A Raider at (0, -6.01) m is outside the radius. It stands in cell (24, 21), whose centre is exactly 6.0 m out, so the cell counts as in.

## Expected
docs/02 "Zones" (added by this change): "A zone that hides its contents is seen into only by its owner and by an enemy's own
units standing inside it." The statuses decide "inside" by the unit's center (within the radius, the edge counts). By that rule,
the Raider 5.2 m from the centre should be hidden from the Crossbowman outside, and the one 6.01 m out should not be.

## Actual
The test fails with `a Raider inside the storm (5.2 m) is visible from outside`: `Fog.CanSeeUnit(0, inside)` is true. Combat's
`ZoneHides` also tests the target's cell centre, so it lets the Crossbowman take that Raider. Meanwhile the Raider 6.01 m out,
outside the storm and not Blinded, is hidden. With 2 m cells, the two rules disagree in a band up to about 1.4 m wide around
the rim. For a Malazan unit the same thing happens the other way: one standing inside by its center (so Blinded) can be in a
cell the blocker treats as outside.

## Notes
docs/03 "Implementation (M4-4b-2)" documents "a cell is in the zone when its centre is", so this is the documented rule. But it
contradicts the docs/02 sentence that players will read, and the statuses' own inside test. The QA focus asked for 6.0 m in /
6.01 m out "for statuses and for the mask": the statuses are exact, the mask is not.

Options for the Producer:
- (a) Hide by the unit's center in `ZoneHides` and in `CanSeeUnit`, and keep the cell rule for ground.
- (b) Mark every cell the circle touches: a conservative mask that hides a little more ground but never shows a unit inside.
- (c) Accept it and reword docs/02.

## Producer triage (2026-10-10-0215 ACCEPT)

S3 stays. Planned for the sim hardening next session (first item): option (a), hide a unit by its own centre in `ZoneHides` and `CanSeeUnit` (so the blocker and the statuses agree on who is inside) and keep the cell rule for ground; docs/02 and docs/03 to say the same thing afterwards. Un-skip `TheBlockerAndTheStatuses_AgreeOnWhoIsInside` and retire the current-behaviour pin.

## QA verification (2026-10-10-0624, M4-H2)
Verified by QA 2026-10-10-0624 (M4-H2). Option (a): a unit is hidden by its own centre (edge counts) in `FogStore.CanSeeUnit` and `VisionSystem.ZoneHides`; the ground keeps the cell rule. The un-skipped row failed on the base 63efab2 and passes on 5c2de85. Rim checked: 5.2 m in hidden + never acquired, 6.01 m out seen + shot, exactly 6.0 m hidden; a Blinded Laborer inside by its centre in an "outside" cell sees from inside. QA added `QA/SimHardeningH2QaTests` rows: three players (the storm hides a third player's unit by its centre from everyone but its owner; failed on the base), overlapping storms (a unit in both needs a viewer inside each), the map's four corners (clipped box records). `Stress/ZoneFogOracleFuzzStressTests` (re-derived unit oracle) green on 7 seeds (3 added by QA): 0 mismatches. docs/02 "Zones" and docs/03 "Implementation (M4-4b-2)" agree.
