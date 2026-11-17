# BUG-1520: Workers deliver resources at a drop-off building that is still under construction

| Field | Value |
| --- | --- |
| Severity | S3 (a rule the player sees broken; no crash, no desync; first item of the sim's task by the owner's request) |
| Status | fixed (M6-SH2, session 2026-11-15-2136; on `main` with that session's sim merge) |
| Found | 2026-11-15, owner's playtest (inbox note of 2026-11-15, 21:42); confirmed in the code at the 2026-11-15-2136 PLAN |
| System | sim: economy (`EconomySystem.InReachOfDropOff`, `NearestDropOff`) |
| Fixed by | M6-SH2 (`EconomySystem.InReachOfDropOff` / `NearestDropOff` skip sites); `Economy/DropOffSiteTests` (20 of 22 rows red on the base), QA's `QA/DropOffSiteQaTests` (six drop-off buildings, three factions, an 8-seed fuzz with conservation) |

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

## Fixed (M6-SH2, 2026-11-15 / 16)
A loaded worker beside its own site banks nothing: with a finished drop-off it walks there; with none finished but a
site standing it waits with its cargo and looks again every `RetryTicks`, delivering once one is finished (the site
included); with no drop-off and no site the loop ends as before (Idle, cargo kept). A worker already walking its load
to a site (an older save) re-picks on its next tick. `EndsForQueue` reads the finished rule. Readers checked: `Deposit`,
the AI's (already finished-only; `FarExpansion.OwnDropOffNear` counts sites on purpose), ViewApi (none), training /
research / rally (already finished-only). QA: correct for every drop-off building of all three factions, cancelled and
destroyed sites, 30 waiters (all deliver 119 ticks after the site finishes), conservation on every tick of an 8-seed
fuzz. Effect on AI matches: a fresh match's wood at 2:00 is 10-20 % lower, within about 5 % by 10:00; every AI match
pin moved (docs/03 "Implementation (M6-SH2)"); the golden did not.
