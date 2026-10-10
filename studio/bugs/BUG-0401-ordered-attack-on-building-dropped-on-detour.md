# BUG-0401: A player's Attack on a reachable building is silently dropped after 2 s when the path first leads away

| Field | Value |
| --- | --- |
| Severity | S3 |
| Status | open |
| Found | 2026-10-10-0624, task M4-VH2 (fix round 1 re-check) |
| System | combat chase give-up (`CombatSystem` scan block, `CombatConstants.GiveUpScans`); sim track |
| Fixed by | |

## Repro
1. Merged sim+view state at cc85fdf. `QaGhostViewTest`, seed 6, `GoneGhostAttack`: three spawned army units (slots
   13, 14, 15) get the player's right-click Attack on the House ghost (a building target, footprint on level 1).
2. Instrumented with a `Describe(...)` print every 4 ticks (scratch copy only, not committed). Slot 15, on level 1, takes the order
   (`mode Ordered state Moving tgt {2,1}`). It then follows its path *away* from the footprint (rect distance 46.2 ->
   46.3 -> ... -> 48.6 m), `ChaseStall` rises 1..9 on the scan ticks, and at the 10th scan (about tick 127, roughly 2 s
   after the order) it goes `mode None state Idle tgt default` at 48.64 m. It never walks again. Slots 13 and 14
   start on level 2 at the same distance, walk down and reach it, so the footprint is reachable on foot.
3. The same on the pre-M4-H2 rule (the old BUG-0390 log shows the same slot idle at 48.64 m, as BUG-0390's notes say).
   That note calls it "never took the order", but it did take it and then gave it up.

## Expected
A *player-issued* Attack on a reachable target should walk the route, even when the route first leads away (a ramp
round a cliff). The documented give-up rule (docs/03 "Giving up a chase", BUG-0137) was written for auto-acquired
targets: retaliators and attack-movers dithering on another plateau. It makes no exception for an explicit order. Nor
does it make the FriendFightsTarget exception for a building target, so a building target is always given up after 10
stalled scans.

## Actual
The order is dropped with no feedback after 10 scans (2 s) of non-closing walk. One of the three selected units stands
Idle 48 m away while the others attack. The scene's checks don't catch it: they look at the nearest unit only, and
`accepted == selected.Count` is sampled 2 ticks after the click.

## Notes
- This is a rules question as much as a bug. Options for the Producer: exempt `Mode == Ordered` chases whose flow field
  reaches the target (finite cost at the unit's cell) from the stall count; measure stall progress as path distance
  rather than straight-line gap; or document that explicit orders give up too.
- It is not caused by the M4-VH2 fix. The fix's re-selection (spawned units only) just made it visible: before the fix
  this unit was one of four, and the mean-distance check hid it.
