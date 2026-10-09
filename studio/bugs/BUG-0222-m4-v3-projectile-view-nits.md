# BUG-0222: M4-V3 nits: a skipping tracker misses a same-target lob reuse; a lob slides its last 0.6 m along the ground; the mark ring replaces live marks while free slots exist

| Field | Value |
| --- | --- |
| Severity | S4 |
| Status | fixed (items 1-3; item 2 is an M6 docs note by design) |
| Found | 2026-10-08-1435, task M4-V3 |
| System | view: `sim/Rts.Sim/ViewApi/ProjectileTracker.cs`, `ImpactMarks.cs`, `game/scripts/ProjectileViews.cs` |
| Fixed by | 08dc8e3 (M4-VH1): item 1 `ProjectileTracker` off-line test + `ProjectileViewQaTests.SkippingObserver_SameTargetLobFromAnotherLaunch_IsANewShot` un-skipped; item 2 docs/03 M6 note; item 3 docs (`ImpactMarks` summary and docs/03 say the ring replaces the next slot, even with others expired) |

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

## Verification (QA 2026-10-08-2144, M4-VH1)
- Item 1: the un-skipped row passes: `Reused` 1, the launch is the new shot's, and the arc is not flat. QA's
  `QA/ViewApi/LobReuseHostileQaTests` uses three slots, one owner, one type and two impact points, with random launches
  and an observer on 30 % of ticks. 0 stale arcs in about 3,400 observations on each of four seeds. An every-tick
  observer never restarts a real flight, including at (1020, 1010) on a 1024 m map. Residual: a reuse on the old
  shot's own line is still missed (5 per 3,300 observations; skipping observer only), filed as BUG-0250 item 1.
- Item 2: docs/03 now carries the M6 note (the stone slides its last 0.6 m).
- Item 3: closed by changing the docs, which the bug offered as an option. The code is unchanged and the docs now say
  what it does.
