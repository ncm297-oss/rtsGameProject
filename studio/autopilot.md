# Autopilot settings

The owner edits this file to control scheduled studio sessions. Every session reads it before
doing any work. Changes take effect at the next session; no restart needed.

| Setting | Value | Meaning |
| --- | --- | --- |
| enabled | yes | `no` = scheduled sessions exit immediately without doing anything (manual `/studio-session` still works) |
| max_sessions_per_day | 4 | Full work sessions per calendar day (local time). Quick "nothing to do" exits don't count |
| max_fix_rounds | 2 | Dev ↔ QA fix loops per session before the Producer must accept, reject, or escalate |
| stop_at_milestone_end | yes | When a milestone's criteria are all met, stop and wait for the owner's playtest sign-off |
| push_to_github | yes | Push accepted work to `origin/main` and rejected work to its session branch |
| notify_owner | yes | Send a push notification when the studio stops and is waiting on the owner |
