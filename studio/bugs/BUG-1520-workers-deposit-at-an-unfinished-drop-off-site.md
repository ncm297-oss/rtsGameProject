# BUG-1520: Workers deliver resources at a drop-off building that is still under construction

| Field | Value |
| --- | --- |
| Severity | S3 (a rule the player sees broken; no crash, no desync; first item of the sim's task by the owner's request) |
| Status | open |
| Found | 2026-11-15, owner's playtest (inbox note of 2026-11-15, 21:42); confirmed in the code at the 2026-11-15-2136 PLAN |
| System | sim: economy (`EconomySystem.InReachOfDropOff`, `NearestDropOff`) |
| Fixed by | |

## Repro
Place a woodcutter's hut (any drop-off building) beside trees with workers gathering there: they deliver at the
site before it is built.

## Expected
Only a finished building is a drop-off (the AI already reads it so: its reach uses "finished drop-offs").

## Actual
`InReachOfDropOff` and `NearestDropOff` (`sim/Rts.Sim/Economy/EconomySystem.cs`, about lines 418-450) test `Alive`,
`Owner` and `def.DropOff`, never `UnderConstruction`. Not run by the Producer; read in the code.

## Notes
Planned as item 0 of M6-SH2 (session 2026-11-15-2136): the fix changes gather behaviour, so it rides that task's
one re-measure of the AI match pins. Also to check: `EndsForQueue`, `Deposit`, any other reader that treats a site
as finished.
