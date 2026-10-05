# Autopilot settings

The owner edits this file to control the studio. Every session reads it before doing any work.
Changes take effect at the next session; no restart needed.

| Setting | Value | Meaning |
| --- | --- | --- |
| enabled | yes | `no` = scheduled sessions exit immediately without doing anything (manual `/studio-session` still works) |
| chain_sessions | yes | After a session ends with more work to do, the Producer starts the next one within minutes instead of waiting for the next check-in |
| max_sessions_per_day | 10 | Full work sessions per calendar day (local time). Cheap exits don't count. The studio resumes the next morning when the cap is hit |
| max_fix_rounds | 2 | Dev ↔ QA fix loops per session before the Producer must accept, reject, or escalate |
| hardening_every | 4 | Every Nth full session works the debt backlog (S3/S4 bugs, flaky tests, docs drift) instead of features; plus one hardening session at the end of each milestone |
| max_task_lines | 1500 | Size budget for a task when the design is clear; uncertain work stays around 800 lines |
| stop_at_milestone_end | no | `no` = the Producer signs off milestones itself after verifying them; `yes` = stop for an owner playtest at each milestone |
| open_decisions | producer_default | `producer_default` = the Producer resolves [OPEN] items with the documented recommendation and lists them for review; `owner` = stop and ask |
| stop_at_end_of_plan | yes | When the roadmap (M0-M9) is used up, stop and ask the owner before designing new work |
| push_to_github | yes | Push accepted work to `origin/main` and rejected work to its session branch |
| notify_owner | yes | Send a notification when the studio stops and needs the owner |

Owner authorization, 2026-10-03: "I want to give the producer authorization to start next
sessions whenever ready. Just check with me once we hit the end of the plan before new sessions
are needed to be designed or approved."
