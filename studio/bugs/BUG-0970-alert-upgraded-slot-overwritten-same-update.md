# BUG-0970: `AttackAlerts.Upgraded(k)` can name a slot that a later raise in the same Update overwrote

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-18-0516, task M6-V13 |
| System | HUD / attack alerts (`sim/Rts.Sim/ViewApi/AttackAlerts.cs`) |
| Fixed by | |

## Repro
1. `dotnet test sim/Rts.Sim.Tests --filter FullyQualifiedName~M6V13QaTests.UpgradedSlot_OverwrittenInTheSameUpdate_StillReadsBase`
   with the `Skip` taken off.
2. The test uses a ring of capacity 2. It raises Units alerts A at (0, 0) and B at (500, 0), so the ring is full. In the
   next Update a building hit at (10, 0) joins A and upgrades it, and a unit hit at (1000, 0) raises C.

## Expected
Every index that `Upgraded(k)` returns names an alert that this Update turned Base, so `Kind(Upgraded(k)) == Base` and
`Position(Upgraded(k))` is that alert's position. The doc says "an index as New's".

## Actual
`Kind(Upgraded(0))` is `Units`. Buildings are scanned before units, so C's raise takes the oldest slot, which is A's,
after A was recorded as upgraded. `AlertHud.ShowUpgrades` then checks C's position (1000, 0) to decide whether the
"base under attack" text goes up. It can show the base text for an alert at a units-only spot, or skip it for the
real upgraded alert when C is on screen.

## Notes
- This needs a full ring and an upgraded alert that is the oldest one, still inside its 10 s. At the default capacity
  of 32 that means 32 alerts within 10 s, all 30 m apart. That is rare in play, so this is S4.
- `New(k)` has the same documented shape (BUG-0954: "the set is right, the order is not"). `Upgraded` is worse because
  it can name an alert that is not the one upgraded at all.
- Possible fix: when a raise takes a slot, drop any `_upgraded` entry for that slot from the same Update. The raise
  already knows the slot, so this is cheap.
