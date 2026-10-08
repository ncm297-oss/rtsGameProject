# BUG-0222: M4-V3 nits: a skipping tracker misses a same-target lob reuse; a lob slides its last 0.6 m along the ground; the mark ring replaces live marks while free slots exist

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | open |
| Found | 2026-10-08-1435, task M4-V3 |
| System | view: `sim/Rts.Sim/ViewApi/ProjectileTracker.cs`, `ImpactMarks.cs`, `game/scripts/ProjectileViews.cs` |
| Fixed by | |

## Item 1: a tracker that skips ticks misses a reuse by a lob of the same type, owner and target
Repro: `ProjectileViewQaTests.SkippingObserver_SameTargetLobFromAnotherLaunch_IsANewShot` (skipped until fixed). A
sharper from (22, 20) to (30, 20) is observed mid-flight. Then, before the next observation, it lands and a sharper of
the same owner from (30, 12) to the same (30, 20) takes the slot and flies two steps. Observed at tick 20: `Reused 0`,
launch still (22, 20), and the new lob's arc is drawn flat (`ArcHeight` 0) for its whole flight. None of the five checks
fire: it is not standing still, the type, owner and target are the same, the last observation was not the previous tick,
and it is farther from the old launch than before. Not reachable in the Match, which observes on every
`SimRunner.Ticked`. docs/03 says the skipping observer is handled. A cheap extra check for a lob: its position must lie
on the line from the recorded launch point to the target.

## Item 2: the drawn lob stops one step short and slides along the ground
The arc ends one step before the last drawn position (by design, docs/03), and the sim frees the slot on the landing
tick. So the stone touches down, slides one step (0.6 m at 12 m/s) along the ground, and disappears 0.6 m short of
where its burst appears (`QaV7Test`: `longest gap from a lob's last drawn point to its landing 0.60 m`). This is the
placeholder curve, and it's barely visible at RTS zoom. Note it for the M6 projectile pass.

## Item 3: the ring replaces a live mark while other slots are free
`ImpactMarks.Add` always writes at `_head`. If the head slot is still live (an 8-tick burst, or a mark not drawn yet), it
is replaced (`Replaced++`) even when other slots have already expired. The summary says replacement happens "when every
mark is in use". It is still the oldest mark, and 512 slots make this rare, so either the doc or the code should change.
